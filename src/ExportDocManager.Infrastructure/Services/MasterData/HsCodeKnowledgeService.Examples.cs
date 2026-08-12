using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        public async Task<IReadOnlyList<HsCodeDeclarationExample>> ListExamplesAsync(
            string? keyword, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var query = BuildExampleQuery(context, keyword);
            int normalizedPageNumber = Math.Max(pageNumber, 1);
            int normalizedPageSize = Math.Clamp(pageSize, 1, 200);
            return await query.OrderByDescending(item => item.IsManuallyVerified).ThenByDescending(item => item.UpdatedAt)
                .Skip(PagingHelper.CalculateOffset(normalizedPageNumber, normalizedPageSize))
                .Take(normalizedPageSize).ToListAsync(cancellationToken);
        }

        public async Task<int> CountExamplesAsync(string? keyword, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await BuildExampleQuery(context, keyword).CountAsync(cancellationToken);
        }

        public async Task<HsCodeDeclarationExample> SaveExampleAsync(HsCodeExampleInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            string rawCode = HsCodeTextHelper.NormalizeCode(input.RawReportedHsCode);
            string currentCode = HsCodeTextHelper.NormalizeCode(input.ResolvedCurrentHsCode);
            string name = ValidateTextLength(input.ProductName, 300, "商品名称");
            string specification = ValidateTextLength(input.Specification, 1500, "规格与申报要素");
            string source = ValidateTextLength(input.Source, 100, "实例来源");
            if (string.IsNullOrWhiteSpace(rawCode) || string.IsNullOrWhiteSpace(name))
                throw new ServiceValidationException("申报实例必须填写历史/原始HS编码和商品名称。");
            if (rawCode.Length > 20 || currentCode.Length > 20)
                throw new ArgumentException("HS 编码不能超过 20 个字符。");
            string fingerprint = BuildFingerprint(rawCode, name, specification);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(currentCode) &&
                !await HasTrustedActiveCodeAsync(context, currentCode, cancellationToken))
                throw new ServiceValidationException("当前有效编码必须来自已验证的本地年度税则，并包含来源、年度和验证时间。");
            var entity = input.Id > 0
                ? await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken)
                : await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken);
            DateTime now = DateTime.UtcNow;
            if (entity == null)
            {
                entity = new HsCodeDeclarationExample { CreatedAt = now };
                await context.HsCodeDeclarationExamples.AddAsync(entity, cancellationToken);
            }
            entity.Fingerprint = fingerprint;
            entity.RawReportedHsCode = rawCode;
            entity.ResolvedCurrentHsCode = string.IsNullOrWhiteSpace(currentCode) ? null : currentCode;
            entity.ProductName = name;
            entity.Specification = specification;
            entity.SearchText = NormalizeSearchText($"{name} {specification}");
            entity.Source = string.IsNullOrWhiteSpace(source) ? "Manual" : source;
            entity.SourceYear = input.SourceYear;
            entity.ResolutionStatus = NormalizeResolutionStatus(input.ResolutionStatus, currentCode, rawCode);
            entity.IsManuallyVerified = input.IsManuallyVerified;
            entity.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public async Task<bool> DeleteExampleAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await context.HsCodeDeclarationExamples.FindAsync([id], cancellationToken);
            if (entity == null) return false;
            context.HsCodeDeclarationExamples.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<int> DeleteExamplesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        {
            var normalizedIds = (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();
            if (normalizedIds.Count == 0) return 0;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entities = await context.HsCodeDeclarationExamples
                .Where(item => normalizedIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
            if (entities.Count == 0) return 0;
            context.HsCodeDeclarationExamples.RemoveRange(entities);
            await context.SaveChangesAsync(cancellationToken);
            return entities.Count;
        }

        public async Task RecordFeedbackAsync(HsCodeKnowledgeFeedbackInput input, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await HsCodeKnowledgeFeedbackWriter.RecordInContextAsync(context, input, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
