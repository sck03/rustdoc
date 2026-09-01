using System.Security.Cryptography;
using System.Text;
using System.Xml;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    internal static class SingleWindowPackageIntegrity
    {
        public const string CurrentSchemaVersion = "4.0";

        public const string AuthenticationAlgorithm = "HMAC-SHA256";

        public static async Task<SingleWindowPackageFile> DescribeFileAsync(
            string fullPath,
            string relativePath,
            string mediaType,
            string description,
            CancellationToken cancellationToken)
        {
            string digest = await ComputeFileSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
            return new SingleWindowPackageFile
            {
                RelativePath = relativePath,
                MediaType = mediaType ?? string.Empty,
                Description = description ?? string.Empty,
                SizeBytes = new FileInfo(fullPath).Length,
                Sha256 = digest
            };
        }

        public static string ComputeContentDigest(SingleWindowPackageManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            var builder = new StringBuilder();
            Append(builder, manifest.SchemaVersion);
            Append(builder, manifest.PackageId);
            Append(builder, manifest.PackageType.ToString());
            Append(builder, manifest.BusinessType.ToString());
            Append(builder, manifest.BatchReference);
            Append(builder, manifest.SourceInvoiceId);
            Append(builder, manifest.SourceDocumentId);
            Append(builder, manifest.SourceDocumentType);
            Append(builder, manifest.SubmissionVersion);
            Append(builder, manifest.DraftRevision);
            Append(builder, manifest.SourceBaselineHash);
            Append(builder, manifest.InvoiceNo);
            Append(builder, manifest.ContractNo);
            Append(builder, manifest.CompanyScope);
            Append(builder, manifest.SnapshotSha256);
            Append(builder, manifest.SourcePackageDigest);
            Append(builder, manifest.ReceiptReferenceNo);
            Append(builder, manifest.StationKey);
            Append(builder, manifest.CardIdentifier);
            Append(builder, manifest.ClientProfileKey);
            Append(builder, manifest.ClientProfileName);
            Append(builder, manifest.AssignmentNonce);
            Append(builder, manifest.AuthenticationAlgorithm);
            Append(builder, manifest.CreatedAt.ToUniversalTime().ToString("O"));
            Append(builder, manifest.CreatedOnMachine);
            AppendFiles(builder, manifest.PayloadFiles);
            AppendFiles(builder, manifest.AttachmentFiles);
            foreach (string warning in (manifest.Warnings ?? []).OrderBy(item => item, StringComparer.Ordinal))
            {
                Append(builder, warning);
            }

            return ComputeSha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        public static async Task ValidateAsync(
            string workingDirectory,
            SingleWindowPackageManifest manifest,
            SingleWindowPackageType expectedPackageType,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            if (!string.Equals(manifest.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"不支持的单一窗口交接包版本：{manifest.SchemaVersion}。");
            }

            if (manifest.PackageType != expectedPackageType ||
                manifest.BusinessType is not SingleWindowBusinessType.CustomsCoo and
                    not SingleWindowBusinessType.AgentConsignment ||
                !IsHexIdentifier(manifest.PackageId, 32) ||
                !IsSafeProtocolToken(manifest.BatchReference, 40) ||
                manifest.SourceInvoiceId <= 0 ||
                manifest.SourceDocumentId <= 0 ||
                manifest.SubmissionVersion <= 0 ||
                manifest.DraftRevision <= 0 ||
                string.IsNullOrWhiteSpace(manifest.SourceDocumentType) ||
                manifest.SourceDocumentType.Length > 80 ||
                (manifest.InvoiceNo?.Length ?? 0) > 80 ||
                (manifest.ContractNo?.Length ?? 0) > 80 ||
                 string.IsNullOrWhiteSpace(manifest.CompanyScope) ||
                 manifest.CompanyScope.Length > 120 ||
                 !IsStationKey(manifest.StationKey) ||
                 string.IsNullOrWhiteSpace(manifest.CardIdentifier) ||
                 manifest.CardIdentifier.Length > 120 ||
                 !IsClientProfileKey(manifest.ClientProfileKey) ||
                 string.IsNullOrWhiteSpace(manifest.ClientProfileName) ||
                 manifest.ClientProfileName.Length > 80 ||
                 !IsHexIdentifier(manifest.AssignmentNonce, 32) ||
                 !string.Equals(
                     manifest.AuthenticationAlgorithm,
                     AuthenticationAlgorithm,
                     StringComparison.Ordinal) ||
                 !IsSha256(manifest.AuthenticationTag) ||
                 manifest.PayloadFiles == null ||
                manifest.PayloadFiles.Count == 0 ||
                manifest.AttachmentFiles == null ||
                manifest.Warnings == null)
            {
                throw new InvalidDataException("单一窗口交接包 manifest 缺少必要的批次绑定信息。");
            }

            if (expectedPackageType == SingleWindowPackageType.ReceiptPackage &&
                (!IsSha256(manifest.SourcePackageDigest) ||
                 !string.IsNullOrWhiteSpace(manifest.SnapshotSha256) ||
                 (manifest.AttachmentFiles?.Count ?? 0) != 0 ||
                   (manifest.ReceiptReferenceNo?.Length ?? 0) > 120 ||
                   (manifest.ReceiptReferenceNo ?? string.Empty).Any(char.IsControl) ||
                   string.IsNullOrWhiteSpace(manifest.SourcePackageDigest)))
            {
                throw new InvalidDataException("单一窗口回执包的原提交摘要、持卡机绑定、快照或附件清单无效。");
            }

            if (expectedPackageType == SingleWindowPackageType.SubmitPackage &&
                !string.IsNullOrWhiteSpace(manifest.ReceiptReferenceNo))
            {
                throw new InvalidDataException("提交包不能携带官方回执业务编号。");
            }

            IReadOnlyList<SingleWindowPackageFile> payloadFiles = manifest.PayloadFiles
                ?? throw new InvalidDataException("单一窗口交接包缺少业务载荷清单。");
            IReadOnlyList<SingleWindowPackageFile> attachmentFiles = manifest.AttachmentFiles
                ?? throw new InvalidDataException("单一窗口交接包缺少附件清单。");
            EnsureExpectedPathPrefix(payloadFiles, expectedPackageType == SingleWindowPackageType.SubmitPackage
                ? "payloads/"
                : "receipts/");
            EnsureExpectedPathPrefix(attachmentFiles, "attachments/");
            EnsureNoDuplicatePaths(payloadFiles, attachmentFiles);

            await ValidateFilesAsync(workingDirectory, payloadFiles, cancellationToken).ConfigureAwait(false);
            await ValidateFilesAsync(workingDirectory, attachmentFiles, cancellationToken).ConfigureAwait(false);

            if (expectedPackageType == SingleWindowPackageType.SubmitPackage)
            {
                string snapshotPath = PathBoundaryHelper.ResolveProtocolRelativePath(
                    workingDirectory,
                    "snapshot.json",
                    "单一窗口提交包快照路径无效。");
                if (!File.Exists(snapshotPath) || !IsSha256(manifest.SnapshotSha256) ||
                    !string.IsNullOrWhiteSpace(manifest.SourcePackageDigest))
                {
                    throw new InvalidDataException("单一窗口提交包缺少快照或快照摘要。");
                }

                string snapshotDigest = await ComputeFileSha256Async(snapshotPath, cancellationToken).ConfigureAwait(false);
                EnsureDigestEquals(snapshotDigest, manifest.SnapshotSha256, "单一窗口提交包快照摘要不匹配。");
            }

            EnsureOnlyDeclaredFiles(workingDirectory, manifest, expectedPackageType);

            string contentDigest = ComputeContentDigest(manifest);
            EnsureDigestEquals(contentDigest, manifest.ContentDigest, "单一窗口交接包内容摘要不匹配。");

            foreach (var payload in manifest.PayloadFiles)
            {
                string payloadPath = PathBoundaryHelper.ResolveProtocolRelativePath(
                    workingDirectory,
                    payload.RelativePath,
                    "单一窗口交接包 XML 路径越界。");
                await ValidateXmlFileAsync(payloadPath, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task<string> ComputeFileSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }

        public static string ComputeTextSha256(string content)
        {
            return ComputeSha256Hex(Encoding.UTF8.GetBytes(content ?? string.Empty));
        }

        public static string ComputeAuthenticationTag(
            SingleWindowPackageManifest manifest,
            string authenticationSecret)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            byte[] key = DecodeAuthenticationSecret(authenticationSecret);
            byte[] message = Encoding.UTF8.GetBytes(BuildAuthenticationPayload(manifest));
            try
            {
                using var hmac = new HMACSHA256(key);
                return Convert.ToHexString(hmac.ComputeHash(message));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(message);
            }
        }

        public static void ValidateAuthentication(
            SingleWindowPackageManifest manifest,
            string authenticationSecret,
            string failureMessage)
        {
            string expected = ComputeAuthenticationTag(manifest, authenticationSecret);
            byte[] expectedBytes = Convert.FromHexString(expected);
            byte[] actualBytes;
            try
            {
                actualBytes = Convert.FromHexString(manifest?.AuthenticationTag ?? string.Empty);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(failureMessage, ex);
            }

            try
            {
                if (expectedBytes.Length != actualBytes.Length ||
                    !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
                {
                    throw new InvalidDataException(failureMessage);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedBytes);
                CryptographicOperations.ZeroMemory(actualBytes);
            }
        }

        public static async Task ValidateReceiptSourceFileAsync(
            string path,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0)
            {
                throw new InvalidDataException($"单一窗口回执文件为空或不存在：{info.Name}");
            }

            if (info.Length > 10 * 1024 * 1024)
            {
                throw new InvalidDataException($"单一窗口回执文件超过 10 MB 限制：{info.Name}");
            }

            string extension = info.Extension;
            if (!string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".acd", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"不支持的单一窗口回执文件类型：{info.Name}");
            }

            await using var stream = new FileStream(
                info.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 10 * 1024 * 1024
            };
            using var reader = XmlReader.Create(stream, settings);
            try
            {
                while (await reader.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false))
                {
                }
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"单一窗口回执文件不是有效 XML：{info.Name}", ex);
            }
        }

        private static async Task ValidateFilesAsync(
            string workingDirectory,
            IReadOnlyList<SingleWindowPackageFile> files,
            CancellationToken cancellationToken)
        {
            long totalSize = 0;
            foreach (SingleWindowPackageFile file in files ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file == null || string.IsNullOrWhiteSpace(file.RelativePath) || string.IsNullOrWhiteSpace(file.Sha256))
                {
                    throw new InvalidDataException("单一窗口交接包文件清单无效。");
                }

                string fullPath = PathBoundaryHelper.ResolveProtocolRelativePath(
                    workingDirectory,
                    file.RelativePath,
                    "单一窗口交接包文件路径越界。");
                if (!File.Exists(fullPath))
                {
                    throw new InvalidDataException($"单一窗口交接包缺少文件：{file.RelativePath}");
                }

                var info = new FileInfo(fullPath);
                if (file.SizeBytes <= 0 || info.Length != file.SizeBytes)
                {
                    throw new InvalidDataException($"单一窗口交接包文件大小不匹配：{file.RelativePath}");
                }

                string digest = await ComputeFileSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
                EnsureDigestEquals(digest, file.Sha256, $"单一窗口交接包文件摘要不匹配：{file.RelativePath}");
                totalSize = checked(totalSize + info.Length);
                if (totalSize > 50L * 1024L * 1024L)
                {
                    throw new InvalidDataException("单一窗口交接包清单文件总大小超过 50 MB 限制。");
                }
            }
        }

        private static async Task ValidateXmlFileAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 50L * 1024L * 1024L
            };
            using var reader = XmlReader.Create(stream, settings);
            try
            {
                while (await reader.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false))
                {
                }
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"单一窗口交接包包含无效 XML：{Path.GetFileName(path)}", ex);
            }
        }

        private static void EnsureExpectedPathPrefix(
            IReadOnlyList<SingleWindowPackageFile> files,
            string expectedPrefix)
        {
            if ((files ?? []).Any(file =>
                    file == null ||
                    string.IsNullOrWhiteSpace(file.RelativePath) ||
                    !file.RelativePath.Replace('\\', '/').StartsWith(expectedPrefix, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"单一窗口交接包文件必须位于 {expectedPrefix} 目录。");
            }
        }

        private static void EnsureNoDuplicatePaths(params IReadOnlyList<SingleWindowPackageFile>[] fileGroups)
        {
            var paths = new HashSet<string>(PortablePathKey.Comparer);
            foreach (var file in fileGroups.SelectMany(group => group ?? []))
            {
                string path = file?.RelativePath?.Replace('\\', '/') ?? string.Empty;
                if (!paths.Add(path))
                {
                    throw new InvalidDataException($"单一窗口交接包包含重复文件路径：{path}");
                }
            }
        }

        private static void EnsureOnlyDeclaredFiles(
            string workingDirectory,
            SingleWindowPackageManifest manifest,
            SingleWindowPackageType packageType)
        {
            string root = Path.GetFullPath(workingDirectory);
            var declaredPaths = new HashSet<string>(PortablePathKey.Comparer)
            {
                "manifest.json"
            };
            if (packageType == SingleWindowPackageType.SubmitPackage)
            {
                declaredPaths.Add("snapshot.json");
            }

            foreach (var file in manifest.PayloadFiles.Concat(manifest.AttachmentFiles))
            {
                declaredPaths.Add(file.RelativePath.Replace('\\', '/'));
            }

            // Never use Directory.EnumerateFiles(..., AllDirectories) for an
            // untrusted package.  It can follow a junction/symlink between the
            // attribute check and the recursive walk.  The controlled walker
            // validates every directory entry and refuses link-like paths.
            foreach (string filePath in ControlledFileSystemEnumerator.EnumerateFiles(
                         root,
                         errorMessage: "单一窗口交接包不能包含符号链接、目录联接或其他重解析点。"))
            {
                string relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
                if (!declaredPaths.Contains(relativePath))
                {
                    throw new InvalidDataException($"单一窗口交接包包含未在 manifest 声明的文件：{relativePath}");
                }
            }
        }

        private static string BuildAuthenticationPayload(SingleWindowPackageManifest manifest)
        {
            var builder = new StringBuilder();
            Append(builder, manifest.SchemaVersion);
            Append(builder, manifest.PackageId);
            Append(builder, manifest.PackageType.ToString());
            Append(builder, manifest.BusinessType.ToString());
            Append(builder, manifest.BatchReference);
            Append(builder, manifest.ContentDigest);
            Append(builder, manifest.SourcePackageDigest);
            Append(builder, manifest.StationKey);
            Append(builder, manifest.ClientProfileKey);
            Append(builder, manifest.CardIdentifier);
            Append(builder, manifest.CompanyScope);
            Append(builder, manifest.AssignmentNonce);
            Append(builder, manifest.AuthenticationAlgorithm);
            return builder.ToString();
        }

        private static byte[] DecodeAuthenticationSecret(string authenticationSecret)
        {
            try
            {
                byte[] key = Convert.FromBase64String(authenticationSecret?.Trim() ?? string.Empty);
                if (key.Length != 32)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new InvalidDataException("单一窗口交接认证密钥长度无效。");
                }

                return key;
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("单一窗口交接认证密钥格式无效。", ex);
            }
        }

        private static bool IsSafeProtocolToken(string value, int maxLength)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length is > 0 && normalized.Length <= maxLength &&
                   normalized.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');
        }

        private static bool IsHexIdentifier(string value, int exactLength)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == exactLength && normalized.All(Uri.IsHexDigit);
        }

        private static bool IsSha256(string value)
        {
            return IsHexIdentifier(value, 64);
        }

        private static bool IsStationKey(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 36 &&
                   normalized.StartsWith("SWS-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(normalized[4..], "N", out _);
        }

        private static bool IsClientProfileKey(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 36 &&
                   normalized.StartsWith("SWP-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(normalized[4..], "N", out _);
        }

        private static void AppendFiles(StringBuilder builder, IReadOnlyList<SingleWindowPackageFile> files)
        {
            foreach (SingleWindowPackageFile file in (files ?? [])
                         .OrderBy(item => item.RelativePath, StringComparer.Ordinal))
            {
                Append(builder, file.RelativePath);
                Append(builder, file.MediaType);
                Append(builder, file.Description);
                Append(builder, file.SizeBytes);
                Append(builder, file.Sha256);
            }
        }

        private static void Append(StringBuilder builder, object value)
        {
            string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append(text.Length).Append(':').Append(text).Append('|');
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static void EnsureDigestEquals(string actual, string expected, string message)
        {
            if (string.IsNullOrWhiteSpace(expected) ||
                !string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
