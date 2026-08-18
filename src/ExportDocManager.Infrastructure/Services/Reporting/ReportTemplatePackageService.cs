using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService : IReportTemplatePackageService
    {
        private const string PackageExtension = ".edtpl";
        private const string PackageSchemaVersion = "1.1";

        private const string StoragePolicy =
            "模板包导出路径来自用户显式输入；相对路径解析到运行数据根 TemplatePackages/。只打包和导入运行数据根 Templates/ 下的用户模板，内置模板保持只读；临时文件使用运行数据根 Cache/TemplatePackages。";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        private readonly IAppPathProvider _pathProvider;
        private readonly ISettingsService _settingsService;
        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplateCatalogLoader _catalogLoader;
        private readonly ILogger<ReportTemplatePackageService> _logger;
        private readonly IBusinessClock _clock;

        public ReportTemplatePackageService(
            IAppPathProvider pathProvider,
            ISettingsService settingsService,
            ILogger<ReportTemplatePackageService>? logger = null,
            IBusinessClock? clock = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _logger = logger ?? NullLogger<ReportTemplatePackageService>.Instance;
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver, _logger);
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<ReportTemplatePackageExportResult> ExportAsync(
            string packagePath,
            IProgress<OperationProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string targetPath = NormalizeExportPackagePath(packagePath);
            string templatesRoot = _pathResolver.GetUserTemplatesBaseDirectory();
            string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "TemplatePackages",
                "edtpl-export");
            string tempTemplates = Path.Combine(tempRoot, "Templates");

            Directory.CreateDirectory(templatesRoot);
            await _settingsService.LoadAsync().ConfigureAwait(false);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(progress, "正在扫描模板目录", "系统正在整理本次打包的模板文件。", 5);

                var templateFiles = Directory.Exists(templatesRoot)
                    ? Directory.GetFiles(templatesRoot, "*", SearchOption.AllDirectories)
                        .Where(path => !string.Equals(Path.GetFileName(path), "report_templates.json", StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                    : Array.Empty<string>();
                await CopyFilesAsync(
                    templateFiles,
                    templatesRoot,
                    tempTemplates,
                    overwrite: true,
                    progress,
                    cancellationToken,
                    "正在复制模板文件",
                    8,
                    46).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(progress, "正在整理模板清单", "系统正在写入模板包配置清单。", 55);
                var rows = await LoadTemplateRowsAsync(cancellationToken).ConfigureAwait(false);
                var manifest = new TemplatePackageManifest
                {
                    PackageVersion = PackageSchemaVersion,
                    ExportedAt = _clock.UtcNow,
                    Templates = rows.Select(row => new TemplateRowManifest
                    {
                        Type = row.Type,
                        Name = row.Name,
                        FileName = row.FileName,
                        WithSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(row.Type, row.FileName) ==
                            ReportDocumentType.PaymentVoucher
                            ? null
                            : row.WithSeal ?? true
                    }).ToList(),
                    ExportTemplates = BuildExportManifestItems(_settingsService.Settings.BatchExport?.Items),
                    InternalTemplates = BuildPaymentManifestItems(_settingsService.Settings.PaymentTemplates)
                };

                string manifestPath = Path.Combine(tempRoot, "config.json");
                string manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
                await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                await ZipArchiveHelper.CreateFromDirectoryAsync(
                    tempRoot,
                    targetPath,
                    cancellationToken,
                    progress,
                    "正在生成模板包",
                    60,
                    95).ConfigureAwait(false);
                ReportProgress(progress, "模板包导出完成", $"已生成：{Path.GetFileName(targetPath)}", 100);

                return new ReportTemplatePackageExportResult
                {
                    PackagePath = targetPath,
                    TemplateCount = manifest.Templates.Count,
                    StoragePolicy = StoragePolicy
                };
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(tempRoot);
            }
        }

        public async Task<ReportTemplatePackageImportResult> ImportAsync(
            string packagePath,
            ReportTemplateImportStrategy strategy = ReportTemplateImportStrategy.Overwrite,
            IProgress<OperationProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string sourcePackagePath = NormalizeImportPackagePath(packagePath);
            if (!File.Exists(sourcePackagePath))
            {
                throw new FileNotFoundException("模板包文件不存在。", sourcePackagePath);
            }

            string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "TemplatePackages",
                "edtpl-import");
            string templatesRoot = _pathResolver.GetUserTemplatesBaseDirectory();

            await _settingsService.LoadAsync().ConfigureAwait(false);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(
                    sourcePackagePath,
                    tempRoot,
                    cancellationToken,
                    progress,
                    "正在解包模板包",
                    5,
                    35).ConfigureAwait(false);

                string sourceTemplates = Path.Combine(tempRoot, "Templates");
                if (!Directory.Exists(sourceTemplates))
                {
                    throw new InvalidDataException("模板包缺少 Templates 目录。");
                }

                string manifestPath = Path.Combine(tempRoot, "config.json");
                var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);

                Directory.CreateDirectory(templatesRoot);
                var sourceFiles = Directory.GetFiles(sourceTemplates, "*", SearchOption.AllDirectories)
                    .Where(path => !string.Equals(Path.GetFileName(path), "report_templates.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (string sourceFile in sourceFiles.Where(path =>
                             string.Equals(Path.GetExtension(path), ".html", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = Path.GetRelativePath(sourceTemplates, sourceFile);
                    string category = relativePath.Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                    var reportType = string.Equals(
                        category,
                        ReportTemplateCatalogLoader.InternalTemplateCatalogType,
                        StringComparison.OrdinalIgnoreCase)
                        ? ReportDocumentType.PaymentVoucher
                        : ReportDocumentType.ExportDocument;
                    string templateContent = await File.ReadAllTextAsync(sourceFile, cancellationToken).ConfigureAwait(false);
                    ReportTemplateContentPolicy.Validate(reportType, templateContent);
                }
                await CopyFilesAsync(
                    sourceFiles,
                    sourceTemplates,
                    templatesRoot,
                    overwrite: strategy != ReportTemplateImportStrategy.AddOnly,
                    progress,
                    cancellationToken,
                    "正在写入模板文件",
                    40,
                    72).ConfigureAwait(false);

                ReportProgress(progress, "正在读取模板包配置", "系统正在整合模板和列表配置。", 76);
                var incomingRows = (manifest.Templates ?? new List<TemplateRowManifest>())
                    .Where(item => !string.IsNullOrWhiteSpace(item.Type) && !string.IsNullOrWhiteSpace(item.FileName))
                    .Select(item => new ReportTemplateConfig
                    {
                        Type = item.Type,
                        Name = item.Name,
                        FileName = item.FileName,
                        WithSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(item.Type, item.FileName) ==
                            ReportDocumentType.PaymentVoucher
                            ? null
                            : item.WithSeal ?? true
                    })
                    .ToList();

                if (incomingRows.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var existingRows = await LoadTemplateRowsAsync(cancellationToken).ConfigureAwait(false);
                    var mergedRows = MergeTemplateRows(existingRows, incomingRows, strategy);
                    await SaveTemplateRowsAsync(mergedRows, cancellationToken).ConfigureAwait(false);
                }

                var exportTemplates = BuildImportedExportItems(manifest.ExportTemplates);
                var internalTemplates = BuildImportedPaymentItems(manifest.InternalTemplates);

                if (exportTemplates.Count > 0 || internalTemplates.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReportProgress(progress, "正在保存模板配置", "正在写入批量导出和付款模板设置。", 90);
                    await _settingsService.UpdateAsync(settings =>
                    {
                        settings.BatchExport.Items = MergeBatchExportItems(
                            settings.BatchExport.Items,
                            exportTemplates,
                            strategy);
                        settings.PaymentTemplates = MergePaymentTemplateItems(
                            settings.PaymentTemplates,
                            internalTemplates,
                            strategy);
                        return true;
                    }, cancellationToken).ConfigureAwait(false);
                }

                int importedTemplateCount = manifest.Templates?.Count ?? 0;
                ReportProgress(progress, "模板包导入完成", $"共加载 {importedTemplateCount} 个模板配置项。", 100);
                return new ReportTemplatePackageImportResult
                {
                    TemplateCount = importedTemplateCount,
                    PackageVersion = manifest.PackageVersion,
                    StoragePolicy = StoragePolicy
                };
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(tempRoot);
            }
        }

        private static void ReportProgress(
            IProgress<OperationProgressUpdate>? progress,
            string statusText,
            string detailText,
            int? percent = null)
        {
            progress?.Report(new OperationProgressUpdate
            {
                StatusText = statusText ?? string.Empty,
                DetailText = detailText ?? string.Empty,
                ProgressPercent = percent
            });
        }

        private static int CalculateProgress(int completed, int total, int startPercent, int endPercent)
        {
            if (total <= 0)
            {
                return Math.Clamp(endPercent, 0, 100);
            }

            int normalizedCompleted = Math.Clamp(completed, 0, total);
            int normalizedStart = Math.Clamp(startPercent, 0, 100);
            int normalizedEnd = Math.Clamp(endPercent, normalizedStart, 100);
            int progress = normalizedStart + ((normalizedEnd - normalizedStart) * normalizedCompleted / total);
            return Math.Clamp(progress, normalizedStart, normalizedEnd);
        }
    }
}
