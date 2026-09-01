using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private async Task<List<ReportTemplateConfig>> LoadTemplateRowsAsync(CancellationToken cancellationToken)
        {
            string templatesRoot = _pathResolver.GetUserTemplatesBaseDirectory();
            var configs = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);
            return configs
                .Where(config =>
                    config != null &&
                    !string.IsNullOrWhiteSpace(config.FileName) &&
                    ReportTemplatePathResolver.IsPathWithinDirectory(config.FileName, templatesRoot))
                .Select(config => new ReportTemplateConfig
                {
                    Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(null, config.FileName),
                    Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(config.Name, config.FileName),
                    FileName = _pathResolver.ToStoredPath(config.FileName),
                    WithSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(config.Type, config.FileName) ==
                        ReportDocumentType.PaymentVoucher
                        ? null
                        : config.WithSeal ?? true
                })
                .ToList();
        }

        private async Task SaveTemplateRowsAsync(
            IEnumerable<ReportTemplateConfig> rows,
            CancellationToken cancellationToken)
        {
            string configPath = _pathResolver.GetUserConfigPath();
            var normalizedRows = (rows ?? Enumerable.Empty<ReportTemplateConfig>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.FileName))
                .Select(NormalizeTemplateRowForStorage)
                .ToList();
            var root = new ReportTemplateConfigRoot { Reports = normalizedRows };
            string json = JsonSerializer.Serialize(root, ReportTemplateCatalogLoader.JsonOptions);

            await AtomicFileHelper.WriteAllTextAtomicAsync(
                    configPath,
                    json,
                    System.Text.Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private ReportTemplateConfig NormalizeTemplateRowForStorage(ReportTemplateConfig row)
        {
            string absolutePath = _pathResolver.ToAbsolutePath(row.FileName);
            string templatesRoot = _pathResolver.GetUserTemplatesBaseDirectory();
            if (!ReportTemplatePathResolver.IsPathWithinDirectory(absolutePath, templatesRoot))
            {
                absolutePath = Path.Combine(
                    _pathResolver.EnsureTemplateDirectory(ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(row.Type, row.FileName)),
                    Path.GetFileName(row.FileName));
            }

            var reportType = ReportTemplateCatalogLoader.ResolveCatalogReportType(row.Type, absolutePath);
            return new ReportTemplateConfig
            {
                Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(null, absolutePath),
                Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(row.Name, absolutePath),
                FileName = _pathResolver.ToStoredPath(absolutePath),
                WithSeal = reportType == ReportDocumentType.PaymentVoucher ? null : row.WithSeal ?? true
            };
        }

        private string NormalizeExportPackagePath(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("模板包路径不能为空。", nameof(packagePath));
            }

            string normalized = packagePath.Trim();
            if (!normalized.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            {
                normalized = Path.ChangeExtension(normalized, PackageExtension.TrimStart('.'));
            }

            return ResolvePackagePath(normalized);
        }

        private string NormalizeImportPackagePath(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("模板包路径不能为空。", nameof(packagePath));
            }

            return ResolvePackagePath(packagePath.Trim());
        }

        private string ResolvePackagePath(string packagePath)
        {
            string resolved = Path.IsPathRooted(packagePath)
                ? Path.GetFullPath(packagePath)
                : Path.GetFullPath(Path.Combine(_pathProvider.DataRoot, "TemplatePackages", packagePath));
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                resolved,
                "模板包路径不能经过符号链接、目录联接或其他重解析点。");
            string? directory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    directory,
                    "模板包目录不能经过符号链接、目录联接或其他重解析点。");
                Directory.CreateDirectory(directory);
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    directory,
                    "模板包目录不能经过符号链接、目录联接或其他重解析点。");
            }

            return resolved;
        }
    }
}
