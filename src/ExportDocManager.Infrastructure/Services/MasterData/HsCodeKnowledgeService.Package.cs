using System.IO.Compression;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        public async Task ExportPackageAsync(
            Stream destination,
            DateTimeOffset? since = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
            {
                throw new ArgumentException("HS知识包导出目标必须可写。", nameof(destination));
            }

            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            DateTime? sinceUtc = since?.UtcDateTime;

            await using (var boundedOutput = new MaximumLengthWriteStream(
                             destination,
                             HsCodeKnowledgePackagePolicy.MaximumPackageBytes,
                             leaveOpen: true))
            {
                using var archive = new ZipArchive(boundedOutput, ZipArchiveMode.Create, leaveOpen: true);
                long expandedBytes = 0;
                var checksums = new Dictionary<string, string>(StringComparer.Ordinal);

                async Task WriteJsonEntryAsync<T>(string name, IQueryable<T> query)
                {
                    var zipEntry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    await using var entryStream = zipEntry.Open();
                    using var hashingStream = new HashingQuotaWriteStream(
                        entryStream,
                        HsCodeKnowledgePackagePolicy.MaximumEntryBytes,
                        HsCodeKnowledgePackagePolicy.MaximumExpandedBytes,
                        () => expandedBytes);
                    await JsonSerializer.SerializeAsync(
                        hashingStream,
                        query.AsAsyncEnumerable(),
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    await hashingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    expandedBytes += hashingStream.BytesWritten;
                    checksums[name] = hashingStream.GetHashHex();
                }

                await WriteJsonEntryAsync(
                    "hs-codes.json",
                    context.HsCodes.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdateTime >= sinceUtc));
                await WriteJsonEntryAsync(
                    "declaration-examples.json",
                    context.HsCodeDeclarationExamples.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc));
                await WriteJsonEntryAsync(
                    "replacement-relations.json",
                    context.HsCodeReplacementRelations.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc));
                await WriteJsonEntryAsync(
                    "search-feedback.json",
                    context.HsCodeSearchFeedback.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc));

                var manifest = new KnowledgeManifest(
                    PackageSchemaVersion,
                    DateTimeOffset.UtcNow,
                    since,
                    checksums);
                byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await manifestStream.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<HsCodeKnowledgePackagePreview> PreviewPackageAsync(string packagePath, CancellationToken cancellationToken = default)
        {
            var info = new FileInfo(packagePath ?? string.Empty);
            if (!info.Exists) throw new FileNotFoundException("HS知识库文件不存在。", packagePath);
            if (info.Length <= 0 || info.Length > HsCodeKnowledgePackagePolicy.MaximumPackageBytes) throw new InvalidDataException("HS知识库文件为空或超过100MB限制。");
            using var archive = ZipFile.OpenRead(info.FullName);
            var knownNames = new HashSet<string>(["manifest.json", "hs-codes.json", "declaration-examples.json", "replacement-relations.json", "search-feedback.json"], StringComparer.OrdinalIgnoreCase);
            var packageEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToList();
            bool hasDuplicateEntry = packageEntries
                .GroupBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            if (packageEntries.Count != knownNames.Count ||
                hasDuplicateEntry ||
                packageEntries.Any(entry => !knownNames.Contains(entry.FullName) || entry.Length > HsCodeKnowledgePackagePolicy.MaximumEntryBytes) ||
                packageEntries.Sum(entry => entry.Length) > HsCodeKnowledgePackagePolicy.MaximumExpandedBytes)
                throw new InvalidDataException("HS知识库包含未知或过大的文件。");
            byte[] manifestBytes = await ReadEntryAsync(
                archive,
                "manifest.json",
                HsCodeKnowledgePackagePolicy.MaximumManifestBytes,
                cancellationToken);
            var manifest = JsonSerializer.Deserialize<KnowledgeManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("HS知识库清单无效。");
            if (!string.Equals(manifest.SchemaVersion, PackageSchemaVersion, StringComparison.Ordinal))
                throw new InvalidDataException($"不支持的HS知识库版本：{manifest.SchemaVersion}。");
            async Task<T> ReadAndVerifyAsync<T>(string name)
            {
                var entry = GetRequiredKnowledgeEntry(archive, name);
                string actualChecksum = await ComputeEntrySha256Async(entry, cancellationToken);
                if (!manifest.Checksums.TryGetValue(name, out string? expected) ||
                    !string.Equals(expected, actualChecksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"HS知识库文件校验失败：{name}。");

                await using var stream = entry.Open();
                return await JsonSerializer.DeserializeAsync<T>(
                           stream,
                           JsonOptions,
                           cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException($"HS知识库内容无效：{name}。");
            }
            var codes = await ReadAndVerifyAsync<List<HsCode>>("hs-codes.json");
            var examples = await ReadAndVerifyAsync<List<HsCodeDeclarationExample>>("declaration-examples.json");
            var replacements = await ReadAndVerifyAsync<List<HsCodeReplacementRelation>>("replacement-relations.json");
            var feedback = await ReadAndVerifyAsync<List<HsCodeSearchFeedback>>("search-feedback.json");
            ValidatePackageContent(codes, examples, replacements, feedback);
            return new HsCodeKnowledgePackagePreview(info.Name, manifest.SchemaVersion, manifest.ExportedAt,
                codes.Count, examples.Count, replacements.Count, feedback.Count,
                codes, examples, replacements, feedback,
                ["导入只合并HS知识库，不包含发票、客户、付款、账号、授权或其他业务数据。"]);
        }

        public async Task<HsCodeKnowledgeImportResult> ImportPackageAsync(
            HsCodeKnowledgePackagePreview preview, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _dbContextFactory,
                async (context, token) =>
                {
                    int addedCodes = 0, updatedCodes = 0, addedExamples = 0, updatedExamples = 0, addedRelations = 0, addedFeedback = 0;

                    var preparedCodes = preview.HsCodes
                        .Select(source => (Source: CloneHsCode(source), Code: HsCodeTextHelper.NormalizeCode(source.Code)))
                        .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                        .ToList();
                    var existingCodes = new Dictionary<string, HsCode>(StringComparer.OrdinalIgnoreCase);
                    foreach (var batch in preparedCodes.Select(item => item.Code)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Chunk(DatabaseInClauseBatchSize))
                    {
                        string[] keys = batch.ToArray();
                        var rows = await context.HsCodes
                            .Where(item => keys.Contains(item.NormalizedCode))
                            .ToListAsync(token);
                        foreach (var row in rows)
                        {
                            existingCodes.TryAdd(row.NormalizedCode, row);
                        }
                    }

                    foreach (var (source, code) in preparedCodes)
                    {
                        source.Code = code;
                        if (existingCodes.TryGetValue(code, out var target))
                        {
                            MergeHsCode(source, target);
                            updatedCodes++;
                        }
                        else
                        {
                            source.Id = 0;
                            source.RowVersion = null;
                            await context.HsCodes.AddAsync(source, token);
                            existingCodes[code] = source;
                            addedCodes++;
                        }
                    }

                    var preparedExamples = new List<(HsCodeDeclarationExample Source, string Fingerprint)>(preview.Examples.Count);
                    foreach (var source in preview.Examples.Select(CloneHsCodeDeclarationExample))
                    {
                        source.RawReportedHsCode = HsCodeTextHelper.NormalizeCode(source.RawReportedHsCode);
                        source.ResolvedCurrentHsCode = HsCodeTextHelper.NormalizeCode(source.ResolvedCurrentHsCode);
                        source.ProductName = source.ProductName.Trim();
                        source.Specification = (source.Specification ?? string.Empty).Trim();
                        source.SearchText = NormalizeSearchText($"{source.ProductName} {source.Specification}");
                        source.Fingerprint = BuildFingerprint(source.RawReportedHsCode, source.ProductName, source.Specification);
                        source.Source = string.IsNullOrWhiteSpace(source.Source) ? "KnowledgePackage" : source.Source.Trim();
                        source.ResolutionStatus = string.IsNullOrWhiteSpace(source.ResolutionStatus)
                            ? "Unresolved"
                            : source.ResolutionStatus.Trim();
                        preparedExamples.Add((source, source.Fingerprint));
                    }

                    var existingExamples = new Dictionary<string, HsCodeDeclarationExample>(StringComparer.OrdinalIgnoreCase);
                    foreach (var batch in preparedExamples.Select(item => item.Fingerprint)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Chunk(DatabaseInClauseBatchSize))
                    {
                        string[] keys = batch.ToArray();
                        var rows = await context.HsCodeDeclarationExamples
                            .Where(item => keys.Contains(item.Fingerprint))
                            .ToListAsync(token);
                        foreach (var row in rows)
                        {
                            existingExamples.TryAdd(row.Fingerprint, row);
                        }
                    }

                    foreach (var (source, fingerprint) in preparedExamples)
                    {
                        if (existingExamples.TryGetValue(fingerprint, out var target))
                        {
                            MergeExample(source, target);
                            updatedExamples++;
                        }
                        else
                        {
                            source.Id = 0;
                            await context.HsCodeDeclarationExamples.AddAsync(source, token);
                            existingExamples[fingerprint] = source;
                            addedExamples++;
                        }
                    }

                    var preparedRelations = new List<(HsCodeReplacementRelation Source, ReplacementRelationKey Key)>(preview.Replacements.Count);
                    foreach (var source in preview.Replacements.Select(CloneHsCodeReplacementRelation))
                    {
                        source.OldCode = HsCodeTextHelper.NormalizeCode(source.OldCode);
                        source.NewCode = HsCodeTextHelper.NormalizeCode(source.NewCode);
                        source.Source = string.IsNullOrWhiteSpace(source.Source) ? "KnowledgePackage" : source.Source.Trim();
                        preparedRelations.Add((source, new ReplacementRelationKey(
                            source.OldCode,
                            source.NewCode,
                            source.EffectiveYear)));
                    }

                    var existingRelations = new HashSet<ReplacementRelationKey>();
                    foreach (var batch in preparedRelations.Select(item => item.Key.OldCode)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Chunk(DatabaseInClauseBatchSize))
                    {
                        string[] oldCodes = batch.ToArray();
                        var rows = await context.HsCodeReplacementRelations
                            .AsNoTracking()
                            .Where(item => oldCodes.Contains(item.OldCode))
                            .Select(item => new { item.OldCode, item.NewCode, item.EffectiveYear })
                            .ToListAsync(token);
                        foreach (var row in rows)
                        {
                            existingRelations.Add(new ReplacementRelationKey(
                                row.OldCode,
                                row.NewCode,
                                row.EffectiveYear));
                        }
                    }

                    foreach (var (source, key) in preparedRelations)
                    {
                        if (existingRelations.Add(key))
                        {
                            source.Id = 0;
                            await context.HsCodeReplacementRelations.AddAsync(source, token);
                            addedRelations++;
                        }
                    }

                    var preparedFeedback = new List<(HsCodeSearchFeedback Source, string Fingerprint)>(preview.Feedback.Count);
                    foreach (var source in preview.Feedback.Select(CloneHsCodeSearchFeedback))
                    {
                        source.QueryText = (source.QueryText ?? string.Empty).Trim();
                        source.ProductName = string.IsNullOrWhiteSpace(source.ProductName) ? null : source.ProductName.Trim();
                        source.Specification = string.IsNullOrWhiteSpace(source.Specification) ? null : source.Specification.Trim();
                        source.CandidateCode = HsCodeTextHelper.NormalizeCode(source.CandidateCode);
                        source.Fingerprint = BuildFingerprint(
                            NormalizeSearchText(source.QueryText),
                            source.CandidateCode,
                            source.ProductName,
                            source.Specification);
                        preparedFeedback.Add((source, source.Fingerprint));
                    }

                    var existingFeedback = new Dictionary<string, HsCodeSearchFeedback>(StringComparer.OrdinalIgnoreCase);
                    foreach (var batch in preparedFeedback.Select(item => item.Fingerprint)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Chunk(DatabaseInClauseBatchSize))
                    {
                        string[] keys = batch.ToArray();
                        var rows = await context.HsCodeSearchFeedback
                            .Where(item => keys.Contains(item.Fingerprint))
                            .ToListAsync(token);
                        foreach (var row in rows)
                        {
                            existingFeedback.TryAdd(row.Fingerprint, row);
                        }
                    }

                    foreach (var (source, fingerprint) in preparedFeedback)
                    {
                        if (existingFeedback.TryGetValue(fingerprint, out var target))
                        {
                            target.AcceptedCount = Math.Max(target.AcceptedCount, source.AcceptedCount);
                            target.RejectedCount = Math.Max(target.RejectedCount, source.RejectedCount);
                            target.LastConfirmedAt = Max(target.LastConfirmedAt, source.LastConfirmedAt);
                            target.UpdatedAt = Max(target.UpdatedAt, source.UpdatedAt);
                        }
                        else
                        {
                            source.Id = 0;
                            await context.HsCodeSearchFeedback.AddAsync(source, token);
                            existingFeedback[fingerprint] = source;
                            addedFeedback++;
                        }
                    }

                    await context.SaveChangesAsync(token);
                    return new HsCodeKnowledgeImportResult(addedCodes, updatedCodes, addedExamples, updatedExamples, addedRelations, addedFeedback,
                        $"HS知识库合并完成：编码新增{addedCodes}/更新{updatedCodes}，实例新增{addedExamples}/更新{updatedExamples}，替代关系新增{addedRelations}，学习记录新增{addedFeedback}。");
                },
                cancellationToken).ConfigureAwait(false);
        }

    }
}
