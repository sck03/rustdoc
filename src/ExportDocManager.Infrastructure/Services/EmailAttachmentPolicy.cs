using System.Collections.Frozen;
using System.IO.Compression;
using System.Text;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public static class EmailAttachmentPolicy
    {
        public const int MaximumAttachmentCount = 10;
        public const long MaximumSingleAttachmentBytes = 10L * 1024L * 1024L;
        public const long MaximumTotalAttachmentBytes = 18L * 1024L * 1024L;

        private static readonly FrozenSet<string> AllowedExtensions = new[]
        {
            ".csv", ".doc", ".docx", ".jpeg", ".jpg", ".pdf", ".png", ".txt", ".xls", ".xlsx", ".zip"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> ValidateAndNormalize(IEnumerable<string> attachmentPaths)
        {
            var paths = (attachmentPaths ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Distinct(PhysicalPathComparison.Comparer)
                .ToList();
            if (paths.Count > MaximumAttachmentCount)
            {
                throw new ServiceValidationException(
                    $"邮件附件不能超过 {MaximumAttachmentCount} 个。");
            }

            long totalBytes = 0;
            foreach (string path in paths)
            {
                if (!File.Exists(path))
                {
                    throw new ResourceNotFoundException($"附件文件不存在：{path}");
                }

                ValidateFileType(path);

                long length = new FileInfo(path).Length;
                if (length > MaximumSingleAttachmentBytes)
                {
                    throw new PayloadLimitExceededException(MaximumSingleAttachmentBytes);
                }
                if (totalBytes > MaximumTotalAttachmentBytes - length)
                {
                    throw new PayloadLimitExceededException(MaximumTotalAttachmentBytes);
                }
                totalBytes += length;
            }

            return paths;
        }

        private static void ValidateFileType(string path)
        {
            string extension = Path.GetExtension(path);
            if (!AllowedExtensions.Contains(extension))
            {
                throw new ServiceValidationException($"邮件附件类型不受支持：{extension}");
            }

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);
                bool valid = extension.ToLowerInvariant() switch
                {
                    ".pdf" => StartsWith(stream, "%PDF-"u8),
                    ".png" => StartsWith(stream, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
                    ".jpg" or ".jpeg" => StartsWith(stream, [0xFF, 0xD8, 0xFF]),
                    ".doc" or ".xls" => StartsWith(stream, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
                    ".docx" => IsOpenXmlPackage(stream, "word/"),
                    ".xlsx" => IsOpenXmlPackage(stream, "xl/"),
                    ".zip" => IsZipPackage(stream),
                    ".csv" or ".txt" => IsText(stream),
                    _ => false
                };
                if (!valid)
                {
                    throw new ServiceValidationException($"邮件附件内容与扩展名不匹配：{Path.GetFileName(path)}");
                }
            }
            catch (ServiceValidationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new InfrastructureServiceException($"无法安全读取邮件附件：{Path.GetFileName(path)}", exception);
            }
        }

        private static bool StartsWith(Stream stream, ReadOnlySpan<byte> signature)
        {
            Span<byte> buffer = stackalloc byte[signature.Length];
            stream.Position = 0;
            return stream.Read(buffer) == buffer.Length && buffer.SequenceEqual(signature);
        }

        private static bool IsZipPackage(Stream stream)
        {
            if (!StartsWith(stream, [0x50, 0x4B]))
            {
                return false;
            }

            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return archive.Entries.Count > 0;
        }

        private static bool IsOpenXmlPackage(Stream stream, string contentRoot)
        {
            if (!StartsWith(stream, [0x50, 0x4B]))
            {
                return false;
            }

            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            bool hasContentTypes = archive.Entries.Any(entry =>
                string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
            bool hasExpectedRoot = archive.Entries.Any(entry =>
                entry.FullName.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase));
            return hasContentTypes && hasExpectedRoot;
        }

        private static bool IsText(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[4096];
            stream.Position = 0;
            int read = stream.Read(buffer);
            if (read == 0)
            {
                return true;
            }

            ReadOnlySpan<byte> content = buffer[..read];
            if (content.Contains((byte)0))
            {
                return false;
            }

            try
            {
                _ = new UTF8Encoding(false, true).GetCharCount(content);
                return true;
            }
            catch (DecoderFallbackException)
            {
                // Existing Chinese exports may use a legacy single-byte code
                // page.  Reject binary control-heavy input while allowing
                // those text files without adding a second conversion path.
                int controls = 0;
                foreach (byte value in content)
                {
                    if (value < 0x20 && value is not (0x09 or 0x0A or 0x0D))
                    {
                        controls++;
                    }
                }
                return controls <= Math.Max(1, content.Length / 100);
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ServiceValidationException($"附件路径无效：{ex.Message}", ex);
            }
        }
    }
}
