using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private static async Task CopyFilesAsync(
            IReadOnlyList<string> sourceFiles,
            string sourceRoot,
            string targetRoot,
            bool overwrite,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken,
            string statusText,
            int startPercent,
            int endPercent)
        {
            Directory.CreateDirectory(targetRoot);

            var files = sourceFiles ?? Array.Empty<string>();
            if (files.Count == 0)
            {
                OperationProgressReporter.Report(progress, statusText, "当前没有需要复制的文件。", endPercent);
                return;
            }

            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string file = files[index];
                string relativePath = Path.GetRelativePath(sourceRoot, file);
                string targetFile = Path.Combine(targetRoot, relativePath);
                string? targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                if (!overwrite && File.Exists(targetFile))
                {
                    OperationProgressReporter.Report(
                        progress,
                        statusText,
                        $"已跳过现有文件：{relativePath}",
                        OperationProgressReporter.Calculate(index + 1, files.Count, startPercent, endPercent));
                    continue;
                }

                try
                {
                    await FileCopyHelper.CopyAsync(
                        file,
                        targetFile,
                        overwrite,
                        cancellationToken).ConfigureAwait(false);
                    OperationProgressReporter.Report(
                        progress,
                        statusText,
                        $"正在处理：{relativePath}",
                        OperationProgressReporter.Calculate(index + 1, files.Count, startPercent, endPercent));
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
            }
        }

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

                var manifest = JsonSerializer.Deserialize<TemplatePackageManifest>(json, JsonOptions)
                               ?? throw new InvalidDataException("模板包 config.json 配置清单为空。");
                ValidateManifest(manifest);
                return manifest;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("模板包配置文件已损坏或不符合 1.1 清单结构。", ex);
            }
        }

        private static void ValidateManifest(TemplatePackageManifest manifest)
        {
            if (!string.Equals(manifest.PackageVersion, PackageSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"模板包版本无效；当前仅接受 {PackageSchemaVersion} 清单。开发期旧格式请重新导出。");
            }

            if (manifest.Templates == null || manifest.ExportTemplates == null || manifest.InternalTemplates == null)
            {
                throw new InvalidDataException("模板包 1.1 清单必须包含 Templates、ExportTemplates 和 InternalTemplates 数组。");
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
            public List<BatchExportItemManifest> ExportTemplates { get; set; } = new();

            [JsonRequired]
            public List<PaymentTemplateItemManifest> InternalTemplates { get; set; } = new();
        }

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
    }
}
