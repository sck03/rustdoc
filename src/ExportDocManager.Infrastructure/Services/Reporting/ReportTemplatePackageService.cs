using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService : IReportTemplatePackageService
    {
        public Task<ReportTemplatePackageExportResult> ExportAsync(
            string packagePath,
            IProgress<OperationProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default) =>
            _storageCoordinator.ExecuteReadAsync(
                () => ExportCoreAsync(packagePath, progress, cancellationToken),
                cancellationToken);

        private async Task<ReportTemplatePackageExportResult> ExportCoreAsync(
            string packagePath,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            string targetPath = NormalizeExportPackagePath(packagePath);
            string templatesRoot = _pathResolver.GetUserTemplatesBaseDirectory();
            string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "TemplatePackages",
                "edtpl-export");
            string tempTemplates = Path.Combine(tempRoot, "Templates");

            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                templatesRoot,
                "模板目录不能经过符号链接、目录联接或其他重解析点。");
            Directory.CreateDirectory(templatesRoot);
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                templatesRoot,
                templatesRoot,
                "模板目录不能包含符号链接、目录联接或其他重解析点。");
            await _settingsService.LoadAsync().ConfigureAwait(false);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                OperationProgressReporter.Report(progress, "正在扫描模板目录", "系统正在整理本次打包的模板文件。", 5);

                var templateFiles = ControlledFileSystemEnumerator
                    .EnumerateFiles(templatesRoot, cancellationToken)
                    .Where(path => !string.Equals(Path.GetFileName(path), "report_templates.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
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

                var fileManifest = await BuildFileManifestAsync(
                    tempTemplates,
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                OperationProgressReporter.Report(progress, "正在整理模板清单", "系统正在写入模板包配置清单。", 55);
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
                    TemplateDefaults = new ReportTemplateDefaultsManifest
                    {
                        ExportDocumentTemplatePath = _referencePolicy.NormalizeDefault(
                            _settingsService.Settings.ReportTemplateDefaults?.ExportDocumentTemplatePath,
                            ReportDocumentType.ExportDocument),
                        PaymentVoucherTemplatePath = _referencePolicy.NormalizeDefault(
                            _settingsService.Settings.ReportTemplateDefaults?.PaymentVoucherTemplatePath,
                            ReportDocumentType.PaymentVoucher)
                    },
                    ExportTemplates = BuildExportManifestItems(_settingsService.Settings.BatchExport?.Items),
                    InternalTemplates = BuildPaymentManifestItems(_settingsService.Settings.PaymentTemplates),
                    Files = fileManifest.Files.ToList(),
                    FileCount = fileManifest.FileCount,
                    TotalBytes = fileManifest.TotalBytes,
                    FilesDigest = fileManifest.FilesDigest
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
                OperationProgressReporter.Report(progress, "模板包导出完成", $"已生成：{Path.GetFileName(targetPath)}", 100);

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

        public Task<ReportTemplatePackageImportResult> ImportAsync(
            string packagePath,
            ReportTemplateImportStrategy strategy = ReportTemplateImportStrategy.Overwrite,
            IProgress<OperationProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default) =>
            _storageCoordinator.ExecuteMutationAsync(
                transaction => ImportCoreAsync(packagePath, strategy, progress, transaction, cancellationToken),
                cancellationToken);

        private async Task<ReportTemplatePackageImportResult> ImportCoreAsync(
            string packagePath,
            ReportTemplateImportStrategy strategy,
            IProgress<OperationProgressUpdate>? progress,
            ReportTemplateStorageCoordinator.ReportTemplateStorageMutation transaction,
            CancellationToken cancellationToken)
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

                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    templatesRoot,
                    "模板目录不能经过符号链接、目录联接或其他重解析点。");
                Directory.CreateDirectory(templatesRoot);
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    templatesRoot,
                    templatesRoot,
                    "模板目录不能包含符号链接、目录联接或其他重解析点。");
                var sourceFiles = ControlledFileSystemEnumerator.EnumerateFiles(sourceTemplates, cancellationToken)
                    .Where(path => !string.Equals(Path.GetFileName(path), "report_templates.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                await ValidateFileManifestAsync(
                    sourceTemplates,
                    sourceFiles,
                    manifest,
                    cancellationToken).ConfigureAwait(false);
                if (sourceFiles.Any(path =>
                        string.Equals(Path.GetExtension(path), ReportTemplateFilePolicy.Extension, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(Path.GetExtension(path), ReportTemplateFilePolicy.Extension, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("模板包中的报表模板扩展名必须使用小写 .html。");
                }

                foreach (string sourceFile in sourceFiles.Where(path =>
                             string.Equals(Path.GetExtension(path), ReportTemplateFilePolicy.Extension, StringComparison.Ordinal)))
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
                    string targetFile = Path.Combine(templatesRoot, relativePath);
                    ReportTemplateFilePolicy.ValidateExistingTemplatePath(sourceFile);
                    ReportTemplateFilePolicy.EnsureNoPortableCollision(targetFile);
                }
                transaction.MarkTemplatesChanged();
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

                OperationProgressReporter.Report(progress, "正在读取模板包配置", "系统正在整合模板和列表配置。", 76);
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
                string exportDefault = _referencePolicy.NormalizeDefault(
                    manifest.TemplateDefaults.ExportDocumentTemplatePath,
                    ReportDocumentType.ExportDocument);
                string paymentDefault = _referencePolicy.NormalizeDefault(
                    manifest.TemplateDefaults.PaymentVoucherTemplatePath,
                    ReportDocumentType.PaymentVoucher);

                cancellationToken.ThrowIfCancellationRequested();
                OperationProgressReporter.Report(progress, "正在保存模板配置", "正在写入默认模板、单据包和付款报表设置。", 90);
                transaction.MarkSettingsChanged();
                await _settingsService.UpdateAsync(settings =>
                {
                    settings.ReportTemplateDefaults.ExportDocumentTemplatePath = ReportTemplatePackageReferencePolicy.MergeDefault(
                        settings.ReportTemplateDefaults.ExportDocumentTemplatePath,
                        exportDefault,
                        strategy);
                    settings.ReportTemplateDefaults.PaymentVoucherTemplatePath = ReportTemplatePackageReferencePolicy.MergeDefault(
                        settings.ReportTemplateDefaults.PaymentVoucherTemplatePath,
                        paymentDefault,
                        strategy);
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

                int importedTemplateCount = manifest.Templates?.Count ?? 0;
                OperationProgressReporter.Report(progress, "模板包导入完成", $"共加载 {importedTemplateCount} 个模板配置项。", 100);
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

    }
}
