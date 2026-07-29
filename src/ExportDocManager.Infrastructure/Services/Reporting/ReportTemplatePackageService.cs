using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportTemplatePackageService : IReportTemplatePackageService
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
                    ExportedAt = DateTime.Now,
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
                    _settingsService.Settings.BatchExport ??= new BatchExportSettings();
                    _settingsService.Settings.BatchExport.Items ??= new List<BatchExportItem>();
                    _settingsService.Settings.PaymentTemplates ??= new List<PaymentTemplateItem>();
                    _settingsService.Settings.BatchExport.Items = MergeBatchExportItems(
                        _settingsService.Settings.BatchExport.Items,
                        exportTemplates,
                        strategy);
                    _settingsService.Settings.PaymentTemplates = MergePaymentTemplateItems(
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

        private static List<PaymentTemplateItem> MergePaymentTemplateItems(
            List<PaymentTemplateItem> existing,
            List<PaymentTemplateItem> incoming,
            ReportTemplateImportStrategy strategy)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite)
            {
                return incoming.Select(ClonePaymentItem).ToList();
            }

            var result = existing?.Select(ClonePaymentItem).ToList() ?? new List<PaymentTemplateItem>();
            var map = result.ToDictionary(BuildTemplateItemKey, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var item in incoming)
            {
                string key = BuildTemplateItemKey(item);
                if (!map.ContainsKey(key))
                {
                    var added = ClonePaymentItem(item);
                    result.Add(added);
                    map[key] = added;
                    continue;
                }

                if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    map[key].Name = item.Name;
                    map[key].TemplatePath = item.TemplatePath;
                    map[key].ReportType = ReportDocumentType.PaymentVoucher.ToString();
                    map[key].IsEnabled = item.IsEnabled;
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
            return BuildTemplateItemKey(item);
        }

        private static string BuildTemplateItemKey(TemplateItemBase item)
        {
            return $"{item?.ReportType}|{item?.TemplatePath}|{item?.Name}";
        }

        private static ReportTemplateConfig CloneRow(ReportTemplateConfig row)
        {
            bool supportsSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(row?.Type, row?.FileName) !=
                ReportDocumentType.PaymentVoucher;
            return new ReportTemplateConfig
            {
                Type = row?.Type ?? string.Empty,
                Name = row?.Name ?? string.Empty,
                FileName = row?.FileName ?? string.Empty,
                WithSeal = supportsSeal ? row?.WithSeal ?? true : null
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

        private static PaymentTemplateItem ClonePaymentItem(PaymentTemplateItem item)
        {
            return new PaymentTemplateItem
            {
                Name = item?.Name ?? string.Empty,
                TemplatePath = item?.TemplatePath ?? string.Empty,
                ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                IsEnabled = item?.IsEnabled ?? true
            };
        }

        private List<BatchExportItemManifest> BuildExportManifestItems(IEnumerable<BatchExportItem> items)
        {
            return (items ?? Enumerable.Empty<BatchExportItem>())
                .Select(item => TryNormalizeTemplateReference(
                    item?.TemplatePath,
                    ReportDocumentType.ExportDocument,
                    out string templatePath)
                    ? new BatchExportItemManifest
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.ExportDocument.ToString(),
                        IsEnabled = item?.IsEnabled ?? true,
                        ShowSeal = item?.ShowSeal ?? true
                    }
                    : null)
                .OfType<BatchExportItemManifest>()
                .ToList();
        }

        private List<PaymentTemplateItemManifest> BuildPaymentManifestItems(IEnumerable<PaymentTemplateItem> items)
        {
            return (items ?? Enumerable.Empty<PaymentTemplateItem>())
                .Select(item => TryNormalizeTemplateReference(
                    item?.TemplatePath,
                    ReportDocumentType.PaymentVoucher,
                    out string templatePath)
                    ? new PaymentTemplateItemManifest
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                        IsEnabled = item?.IsEnabled ?? true
                    }
                    : null)
                .OfType<PaymentTemplateItemManifest>()
                .ToList();
        }

        private List<BatchExportItem> BuildImportedExportItems(IEnumerable<BatchExportItemManifest> items)
        {
            return (items ?? Enumerable.Empty<BatchExportItemManifest>())
                .Select(item => TryNormalizeTemplateReference(
                    item?.TemplatePath,
                    ReportDocumentType.ExportDocument,
                    out string templatePath)
                    ? new BatchExportItem
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.ExportDocument.ToString(),
                        IsEnabled = item?.IsEnabled ?? true,
                        ShowSeal = item?.ShowSeal ?? true
                    }
                    : null)
                .OfType<BatchExportItem>()
                .ToList();
        }

        private List<PaymentTemplateItem> BuildImportedPaymentItems(IEnumerable<PaymentTemplateItemManifest> items)
        {
            return (items ?? Enumerable.Empty<PaymentTemplateItemManifest>())
                .Select(item => TryNormalizeTemplateReference(
                    item?.TemplatePath,
                    ReportDocumentType.PaymentVoucher,
                    out string templatePath)
                    ? new PaymentTemplateItem
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                        IsEnabled = item?.IsEnabled ?? true
                    }
                    : null)
                .OfType<PaymentTemplateItem>()
                .ToList();
        }

        private bool TryNormalizeTemplateReference(
            string templatePath,
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
            [JsonRequired]
            public string PackageVersion { get; set; } = string.Empty;

            [JsonRequired]
            public DateTime ExportedAt { get; set; } = DateTime.Now;

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
