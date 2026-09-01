using System.Xml;
using System.Text;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        public async Task<SingleWindowReceiptCollectionResult> CollectReceiptFilesAsync(
            int batchId,
            CancellationToken cancellationToken = default)
        {
            EnsureSqliteStation();
            var profile = await _clientProfileService.GetActiveAsync(cancellationToken);
            string stationKey = await _stationIdentity
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches.AsNoTracking(), context)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken)
                ?? throw new ResourceNotFoundException("未找到要收集回执的单一窗口批次。");
            if (!Enum.TryParse<SingleWindowBusinessType>(batch.BusinessType, true, out var businessType))
            {
                throw new ServiceValidationException("单一窗口批次业务类型无效。");
            }

            EnsureBatchBelongsToCurrentStation(batch, profile, stationKey, businessType);
            if (batch.Status is SingleWindowBatchStatusCatalog.Preparing or
                SingleWindowBatchStatusCatalog.SubmitPackageExported or
                SingleWindowBatchStatusCatalog.SubmitPackageImported or
                SingleWindowBatchStatusCatalog.ClientDispatching or
                SingleWindowBatchStatusCatalog.ClientDispatchFailed)
            {
                throw new ResourceConflictException("请先成功把该批次发送到官方客户端，再收集回执。");
            }

            string receiptRootPath = EnsureReceiptRootForRead(
                ResolveConfiguredRoot(profile, businessType));

            var receiptFiles = await CollectMatchingReceiptFilesAsync(receiptRootPath, batch, cancellationToken);

            return new SingleWindowReceiptCollectionResult
            {
                BatchId = batch.Id,
                BatchReference = batch.BatchReference,
                ReceiptRootPath = receiptRootPath,
                ReceiptFiles = receiptFiles
            };
        }

        private const int MaximumReceiptScanDepth = 4;
        private const int MaximumReceiptFileCount = 2_000;
        private const int MaximumReceiptEntryCount = 20_000;
        private const long MaximumReceiptFileBytes = 32L * 1024L * 1024L;
        private const long MaximumReceiptScanBytes = 200L * 1024L * 1024L;
        private const string ReceiptPathError = "官方回执目录无效或包含符号链接、目录联接及其他重解析点。";
        private static readonly Encoding StrictUtf8Encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private static readonly Encoding StrictUtf16LittleEndianEncoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: false,
            throwOnInvalidBytes: true);
        private static readonly Encoding StrictUtf16BigEndianEncoding = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true);
        private static readonly Encoding StrictUtf32LittleEndianEncoding = new UTF32Encoding(
            false,
            false,
            true);
        private static readonly Encoding StrictUtf32BigEndianEncoding = new UTF32Encoding(
            true,
            false,
            true);

        private static string EnsureReceiptRootForRead(string configuredRoot)
        {
            string normalizedRoot = SingleWindowClientProfilePathResolver.NormalizeClientRootPath(configuredRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                throw new ServiceValidationException("本机操作卡尚未配置官方回执目录。");
            }

            EnsureReceiptPathComponentsAreSafe(normalizedRoot, ReceiptPathError);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(normalizedRoot);
            }
            catch (FileNotFoundException)
            {
                throw new ResourceNotFoundException("本机操作卡配置的官方回执目录不存在。");
            }
            catch (DirectoryNotFoundException)
            {
                throw new ResourceNotFoundException("本机操作卡配置的官方回执目录不存在。");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InfrastructureServiceException("官方回执目录暂时不可访问，请检查目录权限。", ex);
            }
            catch (IOException ex)
            {
                throw new InfrastructureServiceException("官方回执目录暂时不可用，请稍后重试。", ex);
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new ServiceValidationException("本机操作卡配置的官方回执路径必须是目录。");
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ServiceValidationException(ReceiptPathError);
            }

            return normalizedRoot;
        }

        private IReadOnlyList<string> EnumerateSupportedReceiptFiles(
            string rootPath,
            CancellationToken cancellationToken)
        {
            string normalizedRoot = Path.GetFullPath(rootPath);
            var files = new List<string>();
            var pending = new Queue<(string Path, int Depth)>();
            var budget = new ReceiptScanBudget();
            pending.Enqueue((normalizedRoot, 0));

            while (pending.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExistingReceiptDirectory(current.Path, normalizedRoot);

                FileSystemInfo[] entries;
                try
                {
                    entries = new DirectoryInfo(current.Path)
                        .EnumerateFileSystemInfos(
                            "*",
                            new EnumerationOptions
                            {
                                RecurseSubdirectories = false,
                                IgnoreInaccessible = false,
                                ReturnSpecialDirectories = false,
                                AttributesToSkip = 0
                            })
                        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(entry => entry.FullName, PhysicalPathComparison.Comparer)
                        .ToArray();
                }
                catch (FileNotFoundException ex)
                {
                    throw new InfrastructureServiceException("官方回执目录在扫描期间消失，请稍后重试。", ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new InfrastructureServiceException("官方回执目录在扫描期间消失，请稍后重试。", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InfrastructureServiceException("官方回执目录暂时不可访问，请检查目录权限。", ex);
                }
                catch (IOException ex)
                {
                    throw new InfrastructureServiceException("官方回执目录暂时不可用，请稍后重试。", ex);
                }

                foreach (FileSystemInfo entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++budget.EntryCount > MaximumReceiptEntryCount)
                    {
                        throw new InvalidDataException("官方回执目录条目数量超过单次扫描上限，请缩小目录范围后重试。");
                    }

                    string fullPath = Path.GetFullPath(entry.FullName);
                    if (!PathBoundaryHelper.IsWithinRoot(fullPath, normalizedRoot))
                    {
                        throw new ServiceValidationException(ReceiptPathError);
                    }

                    FileAttributes attributes = ReadReceiptAttributes(fullPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new ServiceValidationException(ReceiptPathError);
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (current.Depth < MaximumReceiptScanDepth)
                        {
                            pending.Enqueue((fullPath, current.Depth + 1));
                        }

                        continue;
                    }

                    string extension = Path.GetExtension(fullPath);
                    if (!string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(extension, ".acd", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long length;
                    try
                    {
                        length = new FileInfo(fullPath).Length;
                    }
                    catch (FileNotFoundException)
                    {
                        _logger.LogDebug("Receipt file disappeared during scan: {Path}", fullPath);
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        _logger.LogDebug("Receipt file directory disappeared during scan: {Path}", fullPath);
                        continue;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw new InfrastructureServiceException("官方回执文件暂时不可访问，请检查目录权限。", ex);
                    }
                    catch (IOException ex)
                    {
                        throw new InfrastructureServiceException("官方回执文件暂时不可用，请稍后重试。", ex);
                    }

                    if (length < 0 || length > MaximumReceiptFileBytes ||
                        budget.TotalBytes > MaximumReceiptScanBytes - length)
                    {
                        throw new InvalidDataException("官方回执目录超过单次扫描资源上限，请缩小目录范围后重试。");
                    }

                    budget.TotalBytes += length;
                    if (++budget.FileCount > MaximumReceiptFileCount)
                    {
                        throw new InvalidDataException("官方回执目录文件数量超过单次扫描上限，请缩小目录范围后重试。");
                    }

                    files.Add(fullPath);
                }
            }

            return files;
        }

        private static void EnsureExistingReceiptDirectory(string path, string rootPath)
        {
            if (!PathBoundaryHelper.IsWithinRoot(path, rootPath))
            {
                throw new ServiceValidationException(ReceiptPathError);
            }

            FileAttributes attributes = ReadReceiptAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new ServiceValidationException("官方回执扫描路径必须是目录。");
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ServiceValidationException(ReceiptPathError);
            }
        }

        private static FileAttributes ReadReceiptAttributes(string path)
        {
            try
            {
                return File.GetAttributes(path);
            }
            catch (FileNotFoundException ex)
            {
                throw new InfrastructureServiceException("官方回执目录或文件在扫描期间消失，请稍后重试。", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new InfrastructureServiceException("官方回执目录或文件在扫描期间消失，请稍后重试。", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InfrastructureServiceException("官方回执目录或文件暂时不可访问，请检查目录权限。", ex);
            }
            catch (IOException ex)
            {
                throw new InfrastructureServiceException("官方回执目录或文件暂时不可用，请稍后重试。", ex);
            }
        }

        private static void EnsureReceiptPathComponentsAreSafe(string path, string errorMessage)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new ServiceValidationException(errorMessage);
                    }
                }
                catch (FileNotFoundException)
                {
                    // A missing candidate leaf is allowed; its existing ancestors
                    // are still checked on the way to the volume root.
                }
                catch (DirectoryNotFoundException)
                {
                    // See the FileNotFoundException case above.
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InfrastructureServiceException("官方回执目录暂时不可访问，请检查目录权限。", ex);
                }
                catch (IOException ex)
                {
                    throw new InfrastructureServiceException("官方回执目录暂时不可用，请稍后重试。", ex);
                }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) ||
                    string.Equals(parent, current, PathBoundaryHelper.PathComparison))
                {
                    break;
                }

                current = parent;
            }
        }

        private sealed class ReceiptScanBudget
        {
            public int EntryCount { get; set; }

            public int FileCount { get; set; }

            public long TotalBytes { get; set; }
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
                .Select(path => NormalizeReceiptCandidateDirectory(path, receiptRootPath))
                .Distinct(PhysicalPathComparison.Comparer)
                .ToList();
            var candidateFiles = EnumerateSupportedReceiptFiles(receiptRootPath, cancellationToken);

            var matches = new List<(string Path, int Score)>();
            foreach (var path in candidateFiles.Distinct(PhysicalPathComparison.Comparer))
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
                .ThenByDescending(item => SafeLastWriteTimeUtc(item.Path))
                .Select(item => item.Path)
                .ToList();
        }

        private static string NormalizeReceiptCandidateDirectory(string path, string receiptRootPath)
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ServiceValidationException("官方回执候选目录路径无效。", ex);
            }

            if (!PathBoundaryHelper.IsWithinRoot(candidate, receiptRootPath))
            {
                throw new ServiceValidationException(ReceiptPathError);
            }

            EnsureReceiptPathComponentsAreSafe(candidate, ReceiptPathError);
            return candidate;
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

            if (batch.LastClientDispatchAt.HasValue)
            {
                DateTimeOffset earliestExpectedWriteUtc = batch.LastClientDispatchAt.Value
                    .ToUniversalTime()
                    .AddMinutes(-5);
                DateTime lastWriteUtc;
                try
                {
                    lastWriteUtc = File.GetLastWriteTimeUtc(path);
                }
                catch (FileNotFoundException)
                {
                    _logger.LogDebug("Receipt file disappeared while matching: {Path}", path);
                    return 0;
                }
                catch (DirectoryNotFoundException)
                {
                    _logger.LogDebug("Receipt directory disappeared while matching: {Path}", path);
                    return 0;
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InfrastructureServiceException("官方回执文件暂时不可访问，请检查目录权限。", ex);
                }
                catch (IOException ex)
                {
                    throw new InfrastructureServiceException("官方回执文件暂时不可用，请稍后重试。", ex);
                }

                if (lastWriteUtc < earliestExpectedWriteUtc.UtcDateTime)
                {
                    return 0;
                }
            }

            string exactBatchDirectory = Path.Combine(candidateDirectories.FirstOrDefault() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(exactBatchDirectory) &&
                PathBoundaryHelper.IsWithinRoot(directory, exactBatchDirectory))
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
            if (parsedReceipt == null)
            {
                // A filename/directory token alone is not sufficient evidence.  In
                // particular, malformed XML must never receive the high exact-directory
                // score and be offered to the operator as a real receipt.
                return 0;
            }
            if (!string.IsNullOrWhiteSpace(batch.ReferenceNo) &&
                !string.IsNullOrWhiteSpace(parsedReceipt.ReferenceNo) &&
                !string.Equals(parsedReceipt.ReferenceNo, batch.ReferenceNo, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

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

            if (!exactBatchDirectoryMatch && !tokenMatch && !parsedReferenceMatch)
            {
                return 0;
            }

            return score;
        }

        private async Task<SingleWindowReceiptParseResult?> TryParseReceiptAsync(
            SwSubmissionBatch batch,
            string path,
            CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<SingleWindowBusinessType>(batch.BusinessType, true, out var businessType))
            {
                return null;
            }

            string content;
            try
            {
                content = await ReadReceiptContentBoundedAsync(path, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                _logger.LogDebug("Receipt file disappeared while parsing: {Path}", path);
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogDebug("Receipt directory disappeared while parsing: {Path}", path);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InfrastructureServiceException("官方回执文件暂时不可访问，请检查目录权限。", ex);
            }
            catch (IOException ex)
            {
                throw new InfrastructureServiceException("官方回执文件暂时不可用，请稍后重试。", ex);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogDebug(ex, "Ignoring oversized or malformed receipt bytes: {Path}", path);
                return null;
            }
            catch (DecoderFallbackException ex)
            {
                _logger.LogDebug(ex, "Ignoring receipt with invalid text encoding: {Path}", path);
                return null;
            }

            if (content.Length > MaximumReceiptFileBytes)
            {
                _logger.LogDebug("Receipt file exceeded parser limit: {Path}", path);
                return null;
            }

            try
            {
                var parsedReceipt = _singleWindowReceiptParser.Parse(businessType, content, Path.GetFileName(path));
                return parsedReceipt?.ReceiptKind == SingleWindowReceiptKind.Unknown
                    ? null
                    : parsedReceipt;
            }
            catch (XmlException ex)
            {
                _logger.LogDebug(ex, "Ignoring malformed receipt XML: {Path}", path);
                return null;
            }
            catch (FormatException ex)
            {
                _logger.LogDebug(ex, "Ignoring malformed receipt content: {Path}", path);
                return null;
            }
            catch (ArgumentException ex)
            {
                _logger.LogDebug(ex, "Ignoring malformed receipt arguments: {Path}", path);
                return null;
            }
        }

        private static async Task<string> ReadReceiptContentBoundedAsync(
            string path,
            CancellationToken cancellationToken)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException("官方回执文件路径无效。");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var buffer = new MemoryStream(capacity: (int)Math.Min(stream.Length, MaximumReceiptFileBytes));
            byte[] chunk = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (total > MaximumReceiptFileBytes - read)
                {
                    throw new InvalidDataException("官方回执文件超过允许大小。");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
            }

            buffer.Position = 0;
            if (!buffer.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array is not byte[] bytes)
            {
                throw new InvalidDataException("官方回执文件无法读取为连续字节缓冲区。");
            }

            int byteCount = checked((int)buffer.Length);
            return DecodeReceiptContent(bytes.AsSpan(segment.Offset, byteCount));
        }

        internal static string DecodeReceiptContent(ReadOnlySpan<byte> bytes)
        {
            Encoding encoding;
            int preambleLength;

            // Check UTF-32 before UTF-16 because the UTF-32 little-endian
            // preamble starts with the UTF-16 little-endian preamble bytes.
            if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
            {
                encoding = StrictUtf32LittleEndianEncoding;
                preambleLength = 4;
            }
            else if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
            {
                encoding = StrictUtf32BigEndianEncoding;
                preambleLength = 4;
            }
            else if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
            {
                encoding = StrictUtf8Encoding;
                preambleLength = 3;
            }
            else if (HasPrefix(bytes, 0xFF, 0xFE))
            {
                encoding = StrictUtf16LittleEndianEncoding;
                preambleLength = 2;
            }
            else if (HasPrefix(bytes, 0xFE, 0xFF))
            {
                encoding = StrictUtf16BigEndianEncoding;
                preambleLength = 2;
            }
            else if (LooksLikeUnmarkedUtf16Xml(bytes, bigEndian: false))
            {
                encoding = StrictUtf16LittleEndianEncoding;
                preambleLength = 0;
            }
            else if (LooksLikeUnmarkedUtf16Xml(bytes, bigEndian: true))
            {
                encoding = StrictUtf16BigEndianEncoding;
                preambleLength = 0;
            }
            else
            {
                encoding = StrictUtf8Encoding;
                preambleLength = 0;
            }

            return encoding.GetString(bytes[preambleLength..]);
        }

        private static bool HasPrefix(ReadOnlySpan<byte> bytes, byte first, byte second)
        {
            return bytes.Length >= 2 && bytes[0] == first && bytes[1] == second;
        }

        private static bool HasPrefix(
            ReadOnlySpan<byte> bytes,
            byte first,
            byte second,
            byte third,
            byte fourth)
        {
            return bytes.Length >= 4 &&
                bytes[0] == first &&
                bytes[1] == second &&
                bytes[2] == third &&
                bytes[3] == fourth;
        }

        private static bool HasPrefix(ReadOnlySpan<byte> bytes, byte first, byte second, byte third)
        {
            return bytes.Length >= 3 &&
                bytes[0] == first &&
                bytes[1] == second &&
                bytes[2] == third;
        }

        private static bool LooksLikeUnmarkedUtf16Xml(ReadOnlySpan<byte> bytes, bool bigEndian)
        {
            if (bytes.Length < 4)
            {
                return false;
            }

            int sampleLength = Math.Min(bytes.Length - (bytes.Length % 2), 512);
            for (int offset = 0; offset + 1 < sampleLength; offset += 2)
            {
                ushort value = bigEndian
                    ? (ushort)((bytes[offset] << 8) | bytes[offset + 1])
                    : (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

                if (value is 0x20 or 0x09 or 0x0A or 0x0D)
                {
                    continue;
                }

                // XML documents must begin with '<' (apart from an optional
                // BOM, which was handled above).  Requiring that first
                // non-whitespace UTF-16 code unit prevents ordinary UTF-8
                // payloads from being misclassified merely because they have
                // an occasional zero byte.
                return value == '<';
            }

            return false;
        }

        private static bool MatchesBatch(string text, SwSubmissionBatch batch)
        {
            return ContainsToken(text, batch.BatchReference) ||
                ContainsToken(text, batch.InvoiceNo) ||
                ContainsToken(text, batch.ReferenceNo);
        }

        internal static bool ContainsToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalizedToken = token.Trim();
            int searchIndex = 0;
            while (searchIndex <= text.Length - normalizedToken.Length)
            {
                int matchIndex = text.IndexOf(
                    normalizedToken,
                    searchIndex,
                    StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    return false;
                }

                int matchEnd = matchIndex + normalizedToken.Length;
                bool startsAtBoundary = matchIndex == 0 || !char.IsLetterOrDigit(text[matchIndex - 1]);
                bool endsAtBoundary = matchEnd == text.Length || !char.IsLetterOrDigit(text[matchEnd]);
                if (startsAtBoundary && endsAtBoundary)
                {
                    return true;
                }

                searchIndex = matchIndex + 1;
            }

            return false;
        }

        private static bool StartsWithAny(string text, IReadOnlyList<string> prefixes)
        {
            return prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private DateTime SafeLastWriteTimeUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (FileNotFoundException)
            {
                _logger.LogDebug("Receipt file disappeared while sorting: {Path}", path);
                return DateTime.MinValue;
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogDebug("Receipt directory disappeared while sorting: {Path}", path);
                return DateTime.MinValue;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InfrastructureServiceException("官方回执文件暂时不可访问，请检查目录权限。", ex);
            }
            catch (IOException ex)
            {
                throw new InfrastructureServiceException("官方回执文件暂时不可用，请稍后重试。", ex);
            }
        }
    }
}
