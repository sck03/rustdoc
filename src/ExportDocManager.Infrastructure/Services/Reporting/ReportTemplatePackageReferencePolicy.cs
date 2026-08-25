using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using System.Text.Json.Serialization;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportTemplatePackageReferencePolicy
    {
        private readonly ReportTemplatePathResolver _pathResolver;

        public ReportTemplatePackageReferencePolicy(ReportTemplatePathResolver pathResolver)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public bool TryNormalize(
            string? templatePath,
            ReportDocumentType reportType,
            out string normalizedPath)
        {
            normalizedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return false;
            }

            try
            {
                string absolutePath = Path.GetFullPath(_pathResolver.ToAbsolutePath(templatePath));
                bool managed = _pathResolver.IsBuiltInTemplatePath(absolutePath) ||
                               _pathResolver.IsUserTemplatePath(absolutePath);
                if (!managed || !File.Exists(absolutePath) ||
                    ReportTemplateCatalogLoader.ResolveCatalogReportType(null, absolutePath) != reportType)
                {
                    return false;
                }

                normalizedPath = _pathResolver.ToStoredPath(absolutePath);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public string NormalizeDefault(string? templatePath, ReportDocumentType reportType)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            if (templatePath.Trim().StartsWith("user-template:", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return TryNormalize(templatePath, reportType, out string normalizedPath)
                ? normalizedPath
                : throw new InvalidDataException($"默认 {reportType} 模板不存在或不属于受管模板目录。");
        }

        public static string MergeDefault(
            string existing,
            string incoming,
            ReportTemplateImportStrategy strategy)
        {
            return strategy switch
            {
                ReportTemplateImportStrategy.Overwrite => incoming,
                ReportTemplateImportStrategy.AddOnly => string.IsNullOrWhiteSpace(existing) ? incoming : existing,
                _ => string.IsNullOrWhiteSpace(incoming) ? existing : incoming
            };
        }
    }

    internal sealed class ReportTemplateDefaultsManifest
    {
        [JsonRequired]
        public string ExportDocumentTemplatePath { get; set; } = string.Empty;

        [JsonRequired]
        public string PaymentVoucherTemplatePath { get; set; } = string.Empty;
    }
}
