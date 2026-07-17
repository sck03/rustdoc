using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportTemplatePackageService : IReportTemplatePackageService
    {
        private const string PackageExtension = ".edtpl";

        private const string StoragePolicy =
            "模板包导出路径来自用户显式输入；相对路径解析到运行数据根 TemplatePackages/。模板文件只从程序根 Templates/ 打包，导入临时目录使用运行数据根 Cache/TemplatePackages，不写入系统盘用户目录。";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly IAppPathProvider _pathProvider;
        private readonly ISettingsService _settingsService;
        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplateCatalogLoader _catalogLoader;

        public ReportTemplatePackageService(
            IAppPathProvider pathProvider,
            ISettingsService settingsService)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver);
        }

        public async Task<ReportTemplatePackageExportResult> ExportAsync(
            string packagePath,
            IProgress<OperationProgressUpdate> progress = null,
            CancellationToken cancellationToken = default)
        {
            string targetPath = NormalizeExportPackagePath(packagePath);
            string templatesRoot = _pathResolver.GetTemplatesBaseDirectory();
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
                    PackageVersion = "1.0",
                    ExportedAt = DateTime.Now,
                    Templates = rows.Select(row => new TemplateRowManifest
                    {
                        Type = row.Type,
                        Name = row.Name,
                        FileName = row.FileName,
                        WithSeal = row.WithSeal ?? true
                    }).ToList(),
                    ExportTemplates = (_settingsService.Settings.BatchExport?.Items ?? new List<BatchExportItem>())
                        .Select(ToManifestItem)
                        .ToList(),
                    InternalTemplates = (_settingsService.Settings.PaymentTemplates ?? new List<BatchExportItem>())
                        .Select(ToManifestItem)
                        .ToList()
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
            IProgress<OperationProgressUpdate> progress = null,
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
            string templatesRoot = _pathResolver.GetTemplatesBaseDirectory();

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

                Directory.CreateDirectory(templatesRoot);
                var sourceFiles = Directory.GetFiles(sourceTemplates, "*", SearchOption.AllDirectories)
                    .Where(path => !string.Equals(Path.GetFileName(path), "report_templates.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
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
                string manifestPath = Path.Combine(tempRoot, "config.json");
                var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                var incomingRows = (manifest.Templates ?? new List<TemplateRowManifest>())
                    .Where(item => !string.IsNullOrWhiteSpace(item.Type) && !string.IsNullOrWhiteSpace(item.FileName))
                    .Select(item => new ReportTemplateConfig
                    {
                        Type = item.Type,
                        Name = item.Name,
                        FileName = item.FileName,
                        WithSeal = item.WithSeal
                    })
                    .ToList();

                if (incomingRows.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var existingRows = await LoadTemplateRowsAsync(cancellationToken).ConfigureAwait(false);
                    var mergedRows = MergeTemplateRows(existingRows, incomingRows, strategy);
                    await SaveTemplateRowsAsync(mergedRows, cancellationToken).ConfigureAwait(false);
                }

                var exportTemplates = (manifest.ExportTemplates ?? new List<BatchExportItemManifest>())
                    .Select(ToBatchExportItem)
                    .ToList();
                var internalTemplates = (manifest.InternalTemplates ?? new List<BatchExportItemManifest>())
                    .Select(ToBatchExportItem)
                    .ToList();

                if (exportTemplates.Count > 0 || internalTemplates.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _settingsService.Settings.BatchExport ??= new BatchExportSettings();
                    _settingsService.Settings.BatchExport.Items ??= new List<BatchExportItem>();
                    _settingsService.Settings.PaymentTemplates ??= new List<BatchExportItem>();
                    _settingsService.Settings.BatchExport.Items = MergeBatchExportItems(
                        _settingsService.Settings.BatchExport.Items,
                        exportTemplates,
                        strategy);
                    _settingsService.Settings.PaymentTemplates = MergeBatchExportItems(
                        _settingsService.Settings.PaymentTemplates,
                        internalTemplates,
                        strategy);
                    ReportProgress(progress, "正在保存模板配置", "正在写入批量导出和付款模板设置。", 90);
                    await _settingsService.SaveAsync().ConfigureAwait(false);
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

        private async Task<List<ReportTemplateConfig>> LoadTemplateRowsAsync(CancellationToken cancellationToken)
        {
            string templatesRoot = _pathResolver.GetTemplatesBaseDirectory();
            var configs = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);
            return configs
                .Where(config =>
                    config != null &&
                    !string.IsNullOrWhiteSpace(config.FileName) &&
                    ReportTemplatePathResolver.IsPathWithinDirectory(config.FileName, templatesRoot))
                .Select(config => new ReportTemplateConfig
                {
                    Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(config.Type, config.FileName),
                    Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(config.Name, config.FileName),
                    FileName = _pathResolver.ToStoredPath(config.FileName),
                    WithSeal = config.WithSeal ?? true
                })
                .ToList();
        }

        private async Task SaveTemplateRowsAsync(
            IEnumerable<ReportTemplateConfig> rows,
            CancellationToken cancellationToken)
        {
            string configPath = Path.Combine(_pathResolver.GetTemplatesBaseDirectory(), "report_templates.json");
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
            string templatesRoot = _pathResolver.GetTemplatesBaseDirectory();
            if (!ReportTemplatePathResolver.IsPathWithinDirectory(absolutePath, templatesRoot))
            {
                absolutePath = Path.Combine(
                    _pathResolver.EnsureTemplateDirectory(ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(row.Type, row.FileName)),
                    Path.GetFileName(row.FileName));
            }

            return new ReportTemplateConfig
            {
                Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(row.Type, absolutePath),
                Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(row.Name, absolutePath),
                FileName = _pathResolver.ToStoredPath(absolutePath),
                WithSeal = row.WithSeal ?? true
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
            string directory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return resolved;
        }

        private static async Task CopyFilesAsync(
            IReadOnlyList<string> sourceFiles,
            string sourceRoot,
            string targetRoot,
            bool overwrite,
            IProgress<OperationProgressUpdate> progress,
            CancellationToken cancellationToken,
            string statusText,
            int startPercent,
            int endPercent)
        {
            Directory.CreateDirectory(targetRoot);

            var files = sourceFiles ?? Array.Empty<string>();
            if (files.Count == 0)
            {
                ReportProgress(progress, statusText, "当前没有需要复制的文件。", endPercent);
                return;
            }

            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string file = files[index];
                string relativePath = Path.GetRelativePath(sourceRoot, file);
                string targetFile = Path.Combine(targetRoot, relativePath);
                string targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                if (!overwrite && File.Exists(targetFile))
                {
                    ReportProgress(
                        progress,
                        statusText,
                        $"已跳过现有文件：{relativePath}",
                        CalculateProgress(index + 1, files.Count, startPercent, endPercent));
                    continue;
                }

                try
                {
                    await FileCopyHelper.CopyAsync(
                        file,
                        targetFile,
                        overwrite,
                        cancellationToken).ConfigureAwait(false);
                    ReportProgress(
                        progress,
                        statusText,
                        $"正在处理：{relativePath}",
                        CalculateProgress(index + 1, files.Count, startPercent, endPercent));
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
                return new TemplatePackageManifest();
            }

            try
            {
                string json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<TemplatePackageManifest>(json) ?? new TemplatePackageManifest();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("模板包配置文件已损坏。", ex);
            }
        }

        private static List<ReportTemplateConfig> MergeTemplateRows(
            List<ReportTemplateConfig> existing,
            List<ReportTemplateConfig> incoming,
            ReportTemplateImportStrategy strategy)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite)
            {
                return incoming.Select(CloneRow).ToList();
            }

            var result = existing?.Select(CloneRow).ToList() ?? new List<ReportTemplateConfig>();
            var map = result.ToDictionary(BuildTemplateRowKey, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var row in incoming)
            {
                string key = BuildTemplateRowKey(row);
                if (!map.ContainsKey(key))
                {
                    var added = CloneRow(row);
                    result.Add(added);
                    map[key] = added;
                    continue;
                }

                if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    map[key].Name = row.Name;
                    map[key].WithSeal = row.WithSeal;
                }
            }

            return result;
        }

        private static List<BatchExportItem> MergeBatchExportItems(
            List<BatchExportItem> existing,
            List<BatchExportItem> incoming,
            ReportTemplateImportStrategy strategy)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite)
            {
                return incoming.Select(CloneItem).ToList();
            }

            var result = existing?.Select(CloneItem).ToList() ?? new List<BatchExportItem>();
            var map = result.ToDictionary(BuildBatchItemKey, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var item in incoming)
            {
                string key = BuildBatchItemKey(item);
                if (!map.ContainsKey(key))
                {
                    var added = CloneItem(item);
                    result.Add(added);
                    map[key] = added;
                    continue;
                }

                if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    map[key].Name = item.Name;
                    map[key].TemplatePath = item.TemplatePath;
                    map[key].ReportType = item.ReportType;
                    map[key].IsEnabled = item.IsEnabled;
                    map[key].ShowSeal = item.ShowSeal;
                }
            }

            return result;
        }

        private static string BuildTemplateRowKey(ReportTemplateConfig row)
        {
            return $"{row?.Type}|{row?.FileName}";
        }

        private static string BuildBatchItemKey(BatchExportItem item)
        {
            return $"{item?.ReportType}|{item?.TemplatePath}|{item?.Name}";
        }

        private static ReportTemplateConfig CloneRow(ReportTemplateConfig row)
        {
            return new ReportTemplateConfig
            {
                Type = row?.Type ?? string.Empty,
                Name = row?.Name ?? string.Empty,
                FileName = row?.FileName ?? string.Empty,
                WithSeal = row?.WithSeal ?? true
            };
        }

        private static BatchExportItem CloneItem(BatchExportItem item)
        {
            return new BatchExportItem
            {
                Name = item?.Name ?? string.Empty,
                TemplatePath = item?.TemplatePath ?? string.Empty,
                ReportType = item?.ReportType ?? string.Empty,
                IsEnabled = item?.IsEnabled ?? true,
                ShowSeal = item?.ShowSeal ?? true
            };
        }

        private static BatchExportItemManifest ToManifestItem(BatchExportItem item)
        {
            return new BatchExportItemManifest
            {
                Name = item?.Name ?? string.Empty,
                TemplatePath = item?.TemplatePath ?? string.Empty,
                ReportType = item?.ReportType ?? string.Empty,
                IsEnabled = item?.IsEnabled ?? true,
                ShowSeal = item?.ShowSeal ?? true
            };
        }

        private static BatchExportItem ToBatchExportItem(BatchExportItemManifest item)
        {
            return new BatchExportItem
            {
                Name = item?.Name ?? string.Empty,
                TemplatePath = item?.TemplatePath ?? string.Empty,
                ReportType = item?.ReportType ?? string.Empty,
                IsEnabled = item?.IsEnabled ?? true,
                ShowSeal = item?.ShowSeal ?? true
            };
        }

        private static void ReportProgress(
            IProgress<OperationProgressUpdate> progress,
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

        private sealed class TemplatePackageManifest
        {
            public string PackageVersion { get; set; } = "1.0";

            public DateTime ExportedAt { get; set; } = DateTime.Now;

            public List<TemplateRowManifest> Templates { get; set; } = new();

            public List<BatchExportItemManifest> ExportTemplates { get; set; } = new();

            public List<BatchExportItemManifest> InternalTemplates { get; set; } = new();
        }

        private sealed class TemplateRowManifest
        {
            public string Type { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public string FileName { get; set; } = string.Empty;

            public bool WithSeal { get; set; } = true;
        }

        private sealed class BatchExportItemManifest
        {
            public string Name { get; set; } = string.Empty;

            public string TemplatePath { get; set; } = string.Empty;

            public string ReportType { get; set; } = string.Empty;

            public bool IsEnabled { get; set; } = true;

            public bool ShowSeal { get; set; } = true;
        }
    }
}
