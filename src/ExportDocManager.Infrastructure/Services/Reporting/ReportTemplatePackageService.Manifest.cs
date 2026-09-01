using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Models;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private static async Task<TemplatePackageManifest> ReadManifestAsync(
            string manifestPath,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException("模板包缺少 config.json 配置清单。");
            }

            try
            {
                string json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidDataException("模板包 config.json 配置清单为空。");
                }

                // Read the version before strict deserialization so a package from
                // an intentionally unsupported generation gets a precise contract
                // error instead of being reported as a generic malformed JSON file.
                using (var envelope = JsonDocument.Parse(json))
                {
                    if (envelope.RootElement.ValueKind == JsonValueKind.Object &&
                        envelope.RootElement.TryGetProperty("PackageVersion", out var versionElement) &&
                        versionElement.ValueKind == JsonValueKind.String &&
                        !string.Equals(versionElement.GetString(), PackageSchemaVersion, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"模板包版本无效；当前仅接受 {PackageSchemaVersion} 清单。开发期旧格式请重新导出。");
                    }
                }

                var manifest = JsonSerializer.Deserialize<TemplatePackageManifest>(json, JsonOptions)
                               ?? throw new InvalidDataException("模板包 config.json 配置清单为空。");
                ValidateManifest(manifest);
                return manifest;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"模板包配置文件已损坏或不符合 {PackageSchemaVersion} 清单结构。", ex);
            }
        }

        private static void ValidateManifest(TemplatePackageManifest manifest)
        {
            if (!string.Equals(manifest.PackageVersion, PackageSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"模板包版本无效；当前仅接受 {PackageSchemaVersion} 清单。开发期旧格式请重新导出。");
            }

            if (manifest.Templates == null || manifest.TemplateDefaults == null ||
                manifest.ExportTemplates == null || manifest.InternalTemplates == null ||
                manifest.Files == null)
            {
                throw new InvalidDataException(
                    $"模板包 {PackageSchemaVersion} 清单必须包含 Templates、TemplateDefaults、ExportTemplates 和 InternalTemplates。");
            }

            for (int index = 0; index < manifest.Templates.Count; index++)
            {
                var row = manifest.Templates[index]
                          ?? throw new InvalidDataException($"模板包 Templates[{index}] 不能为空。");
                if (string.IsNullOrWhiteSpace(row.Type) || string.IsNullOrWhiteSpace(row.FileName))
                {
                    throw new InvalidDataException($"模板包 Templates[{index}] 缺少 Type 或 FileName。");
                }

                bool isExport = string.Equals(
                    row.Type,
                    ReportTemplateCatalogLoader.ExportTemplateCatalogType,
                    StringComparison.OrdinalIgnoreCase);
                bool isPayment = string.Equals(
                    row.Type,
                    ReportTemplateCatalogLoader.InternalTemplateCatalogType,
                    StringComparison.OrdinalIgnoreCase);
                if (!isExport && !isPayment)
                {
                    throw new InvalidDataException($"模板包 Templates[{index}] 的 Type 只能是 Export 或 Internal。");
                }

                if (isPayment && row.WithSeal.HasValue)
                {
                    throw new InvalidDataException($"模板包 Templates[{index}] 是付款报销模板，不得包含 WithSeal 印章配置。");
                }

                if (isExport && !row.WithSeal.HasValue)
                {
                    throw new InvalidDataException($"模板包 Templates[{index}] 是报关单证模板，缺少 WithSeal 配置。");
                }
            }

            if (manifest.FileCount != manifest.Files.Count || manifest.TotalBytes < 0 ||
                string.IsNullOrWhiteSpace(manifest.FilesDigest))
            {
                throw new InvalidDataException("模板包文件摘要清单无效。");
            }

            foreach (var file in manifest.Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path) ||
                    file.SizeBytes < 0 || !IsSha256(file.Sha256))
                {
                    throw new InvalidDataException("模板包文件摘要清单包含无效条目。");
                }

                _ = PortablePathKey.NormalizeRelativePath(file.Path);
            }

            ValidateTemplateItems(
                manifest.ExportTemplates,
                ReportDocumentType.ExportDocument,
                "ExportTemplates");
            ValidateTemplateItems(
                manifest.InternalTemplates,
                ReportDocumentType.PaymentVoucher,
                "InternalTemplates");
        }

        private static void ValidateTemplateItems<T>(
            IReadOnlyList<T> items,
            ReportDocumentType expectedReportType,
            string propertyName)
            where T : TemplateItemManifestBase
        {
            for (int index = 0; index < items.Count; index++)
            {
                var item = items[index]
                           ?? throw new InvalidDataException($"模板包 {propertyName}[{index}] 不能为空。");
                if (string.IsNullOrWhiteSpace(item.TemplatePath))
                {
                    throw new InvalidDataException($"模板包 {propertyName}[{index}] 缺少 TemplatePath。");
                }

                if (!string.Equals(item.ReportType, expectedReportType.ToString(), StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"模板包 {propertyName}[{index}] 的 ReportType 必须是 {expectedReportType}。");
                }
            }
        }

        private sealed class TemplatePackageManifest
        {
            [JsonRequired]
            public string PackageVersion { get; set; } = string.Empty;

            [JsonRequired]
            public DateTimeOffset ExportedAt { get; set; }

            [JsonRequired]
            public List<TemplateRowManifest> Templates { get; set; } = new();

            [JsonRequired]
            public ReportTemplateDefaultsManifest TemplateDefaults { get; set; } = new();

            [JsonRequired]
            public List<BatchExportItemManifest> ExportTemplates { get; set; } = new();

            [JsonRequired]
            public List<PaymentTemplateItemManifest> InternalTemplates { get; set; } = new();

            [JsonRequired]
            public List<TemplateFileManifest> Files { get; set; } = new();

            [JsonRequired]
            public int FileCount { get; set; }

            [JsonRequired]
            public long TotalBytes { get; set; }

            [JsonRequired]
            public string FilesDigest { get; set; } = string.Empty;
        }

        private sealed class TemplateFileManifest
        {
            [JsonRequired]
            public string Path { get; set; } = string.Empty;

            [JsonRequired]
            public long SizeBytes { get; set; }

            [JsonRequired]
            public string Sha256 { get; set; } = string.Empty;

            public TemplateFileManifest()
            {
            }

            public TemplateFileManifest(string path, long sizeBytes, string sha256)
            {
                Path = path;
                SizeBytes = sizeBytes;
                Sha256 = sha256;
            }
        }

        private sealed record TemplateFileManifestSummary(
            IReadOnlyList<TemplateFileManifest> Files,
            int FileCount,
            long TotalBytes,
            string FilesDigest);

        private sealed class TemplateRowManifest
        {
            [JsonRequired]
            public string Type { get; set; } = string.Empty;

            [JsonRequired]
            public string Name { get; set; } = string.Empty;

            [JsonRequired]
            public string FileName { get; set; } = string.Empty;

            public bool? WithSeal { get; set; }
        }

        private abstract class TemplateItemManifestBase
        {
            [JsonRequired]
            public string Name { get; set; } = string.Empty;

            [JsonRequired]
            public string TemplatePath { get; set; } = string.Empty;

            [JsonRequired]
            public string ReportType { get; set; } = string.Empty;

            [JsonRequired]
            public bool IsEnabled { get; set; } = true;
        }

        private sealed class BatchExportItemManifest : TemplateItemManifestBase
        {
            [JsonRequired]
            public bool ShowSeal { get; set; } = true;
        }

        private sealed class PaymentTemplateItemManifest : TemplateItemManifestBase
        {
        }

        private static bool IsSha256(string value) =>
            value.Length == 64 && value.All(character => Uri.IsHexDigit(character));
    }
}
