using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        public async Task<SingleWindowReceiptCollectionResult> CollectReceiptFilesAsync(
            int batchId,
            string receiptRootPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(receiptRootPath) || !Directory.Exists(receiptRootPath))
            {
                throw new InvalidOperationException("默认回执目录不存在。");
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches.AsNoTracking(), context)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken)
                ?? throw new InvalidOperationException("未找到要收集回执的单一窗口批次。");

            var receiptFiles = await CollectMatchingReceiptFilesAsync(receiptRootPath, batch, cancellationToken);

            return new SingleWindowReceiptCollectionResult
            {
                BatchId = batch.Id,
                BatchReference = batch.BatchReference,
                ReceiptRootPath = receiptRootPath,
                ReceiptFiles = receiptFiles
            };
        }

        private static IEnumerable<string> EnumerateSupportedReceiptFiles(string rootPath, SearchOption searchOption)
        {
            return Directory
                .EnumerateFiles(rootPath, "*.*", searchOption)
                .Where(path =>
                {
                    var extension = Path.GetExtension(path);
                    return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".acd", StringComparison.OrdinalIgnoreCase);
                });
        }

        private async Task<IReadOnlyList<string>> CollectMatchingReceiptFilesAsync(
            string receiptRootPath,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            var layout = ResolveBusinessLayout(receiptRootPath, createDirectories: false);
            var candidateDirectories = BuildReceiptCandidateDirectories(
                receiptRootPath,
                layout,
                batch)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var candidateFiles = new List<string>();

            foreach (var directory in candidateDirectories.Where(Directory.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidateFiles.AddRange(EnumerateSupportedReceiptFiles(directory, SearchOption.TopDirectoryOnly));
            }

            if (candidateFiles.Count == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidateFiles.AddRange(EnumerateSupportedReceiptFiles(receiptRootPath, SearchOption.AllDirectories));
            }

            var matches = new List<(string Path, int Score)>();
            foreach (var path in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int score = await CalculateReceiptMatchScoreAsync(path, batch, candidateDirectories, cancellationToken);
                if (score > 0)
                {
                    matches.Add((path, score));
                }
            }

            return matches
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => File.GetLastWriteTime(item.Path))
                .Select(item => item.Path)
                .ToList();
        }

        private IEnumerable<string> BuildReceiptCandidateDirectories(
            string receiptRootPath,
            SingleWindowClientFolderLayout layout,
            SwSubmissionBatch batch)
        {
            yield return Path.Combine(receiptRootPath, batch.BatchReference);
            yield return Path.Combine(layout.InBox, batch.BatchReference);
            yield return Path.Combine(layout.InBox, "Successed");
            yield return Path.Combine(layout.InBox, "Success");
            yield return Path.Combine(layout.InBox, "Failed");
            yield return layout.InBox;
            yield return Path.Combine(layout.BizRoot, "Receipt");
            yield return Path.Combine(layout.BizRoot, "Receipt", batch.BatchReference);
            yield return Path.Combine(layout.BizRoot, "Receipt", "Successed");
            yield return Path.Combine(layout.BizRoot, "Receipt", "Failed");
            yield return Path.Combine(layout.BizRoot, "回执");
            yield return Path.Combine(layout.BizRoot, "回执", batch.BatchReference);
            yield return Path.Combine(layout.BizRoot, "Inbox");
            yield return layout.FailBox;
            yield return layout.BizRoot;
        }

        private async Task<int> CalculateReceiptMatchScoreAsync(
            string path,
            SwSubmissionBatch batch,
            IReadOnlyList<string> candidateDirectories,
            CancellationToken cancellationToken)
        {
            int score = 0;
            string fileName = Path.GetFileName(path);
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            bool exactBatchDirectoryMatch = false;
            bool tokenMatch = false;
            bool parsedReferenceMatch = false;

            string exactBatchDirectory = Path.Combine(candidateDirectories.FirstOrDefault() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(exactBatchDirectory) &&
                directory.StartsWith(exactBatchDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
                exactBatchDirectoryMatch = true;
            }

            if (StartsWithAny(fileName, ["Successed_", "Success_", "Failed_", "Error_", "Receipt_", "Ret_", "Result_"]))
            {
                score += 180;
            }

            if (ContainsToken(fileName, batch.BatchReference) || ContainsToken(directory, batch.BatchReference))
            {
                score += 400;
                tokenMatch = true;
            }

            if (ContainsToken(fileName, batch.InvoiceNo) || ContainsToken(directory, batch.InvoiceNo))
            {
                score += 220;
                tokenMatch = true;
            }

            if (ContainsToken(fileName, batch.ReferenceNo) || ContainsToken(directory, batch.ReferenceNo))
            {
                score += 280;
                tokenMatch = true;
            }

            var parsedReceipt = await TryParseReceiptAsync(batch, path, cancellationToken);
            if (parsedReceipt != null)
            {
                score += 180;

                if (!string.IsNullOrWhiteSpace(batch.ReferenceNo) &&
                    string.Equals(parsedReceipt.ReferenceNo, batch.ReferenceNo, StringComparison.OrdinalIgnoreCase))
                {
                    score += 500;
                    parsedReferenceMatch = true;
                }

                if (MatchesBatch(fileName, batch) || MatchesBatch(parsedReceipt.SourceFileName, batch))
                {
                    score += 120;
                    tokenMatch = true;
                }
            }

            if (!exactBatchDirectoryMatch && !tokenMatch && !parsedReferenceMatch)
            {
                return 0;
            }

            return score;
        }

        private async Task<SingleWindowReceiptParseResult> TryParseReceiptAsync(
            SwSubmissionBatch batch,
            string path,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!Enum.TryParse<SingleWindowBusinessType>(batch.BusinessType, true, out var businessType))
                {
                    return null;
                }

                string content = await File.ReadAllTextAsync(path, cancellationToken);
                var parsedReceipt = _singleWindowReceiptParser.Parse(businessType, content, Path.GetFileName(path));
                return parsedReceipt?.ReceiptKind == SingleWindowReceiptKind.Unknown
                    ? null
                    : parsedReceipt;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return null;
            }
        }

        private static bool MatchesBatch(string text, SwSubmissionBatch batch)
        {
            return ContainsToken(text, batch.BatchReference) ||
                ContainsToken(text, batch.InvoiceNo) ||
                ContainsToken(text, batch.ReferenceNo);
        }

        private static bool ContainsToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return text.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithAny(string text, IReadOnlyList<string> prefixes)
        {
            return prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
