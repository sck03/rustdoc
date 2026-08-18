using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        internal static async Task UpsertExampleInContextAsync(
            AppDbContext context,
            HsCodeExampleInput input,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            bool incrementUseCount = true)
        {
            string code = HsCodeTextHelper.NormalizeCode(input.RawReportedHsCode);
            string name = (input.ProductName ?? string.Empty).Trim();
            string fingerprint = BuildFingerprint(code, name, input.Specification);
            var example = await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken);
            if (example == null)
            {
                example = new HsCodeDeclarationExample { Fingerprint = fingerprint, CreatedAt = now };
                await context.HsCodeDeclarationExamples.AddAsync(example, cancellationToken);
            }
            example.RawReportedHsCode = code; example.ResolvedCurrentHsCode = HsCodeTextHelper.NormalizeCode(input.ResolvedCurrentHsCode); example.ProductName = name;
            example.Specification = (input.Specification ?? string.Empty).Trim(); example.SearchText = NormalizeSearchText($"{name} {input.Specification}");
            example.Source = string.IsNullOrWhiteSpace(input.Source) ? "UserConfirmed" : input.Source.Trim();
            example.SourceYear = input.SourceYear;
            example.ResolutionStatus = input.IsManuallyVerified ? "ManuallyVerified" : NormalizeResolutionStatus(input.ResolutionStatus, example.ResolvedCurrentHsCode, code);
            example.IsManuallyVerified = true;
            if (incrementUseCount) example.UseCount++;
            example.LastUsedAt = now; example.UpdatedAt = now;
        }

        private async Task ResolveExamplesAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            int lastId = 0;
            while (true)
            {
                var examples = await context.HsCodeDeclarationExamples
                    .Where(item => item.Id > lastId)
                    .OrderBy(item => item.Id)
                    .Take(KnowledgeResolutionBatchSize)
                    .ToListAsync(cancellationToken);
                if (examples.Count == 0)
                {
                    break;
                }

                var rawCodes = examples
                    .Select(item => HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode))
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var relations = new List<HsCodeReplacementRelation>();
                foreach (var batch in rawCodes.Chunk(DatabaseInClauseBatchSize))
                {
                    string[] batchCodes = batch.ToArray();
                    relations.AddRange(await context.HsCodeReplacementRelations
                        .AsNoTracking()
                        .Where(item => batchCodes.Contains(item.OldCode))
                        .ToListAsync(cancellationToken));
                }

                var lookupCodes = examples
                    .SelectMany(item => new[] { item.RawReportedHsCode, item.ResolvedCurrentHsCode })
                    .Concat(relations.Select(item => item.NewCode))
                    .Select(HsCodeTextHelper.NormalizeCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var map = new Dictionary<string, HsCode>(StringComparer.OrdinalIgnoreCase);
                foreach (var batch in lookupCodes.Chunk(DatabaseInClauseBatchSize))
                {
                    var rows = await context.HsCodes
                        .AsNoTracking()
                        .Where(item => batch.Contains(item.NormalizedCode))
                        .ToListAsync(cancellationToken);
                    foreach (var row in rows)
                    {
                        map[row.NormalizedCode] = row;
                    }
                }

                foreach (var example in examples)
                {
                    var resolution = ResolveCurrentCode(example, map, relations);
                    example.ResolvedCurrentHsCode = resolution.CurrentCode;
                    example.ResolutionStatus = resolution.Status;
                    example.UpdatedAt = _clock.UtcNow;
                }

                await context.SaveChangesAsync(cancellationToken);
                lastId = examples[^1].Id;
                context.ChangeTracker.Clear();
            }
        }
    }
}
