using System.Text;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplateService : IReportTemplateService
    {
        private const string StoragePolicy =
            "内置模板从程序根 Templates/ 只读加载；新建、编辑副本、重命名、删除和模板包导入统一写入运行数据根 Templates/。不会改写已安装程序资源，也不会使用系统用户配置目录或系统级共享数据目录。";

        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplateCatalogLoader _catalogLoader;
        private readonly ISettingsService _settingsService;
        private readonly IBusinessClock _clock;
        private readonly ILogger<ReportTemplateService> _logger;
        private readonly ReportTemplateV3ImageResourceHydrator _imageResourceHydrator;
        private readonly ReportTemplateStorageCoordinator _storageCoordinator;
        private readonly IDbContextFactory<AppDbContext>? _contextFactory;
        private readonly BusinessDataAccessScope? _accessScope;

        public ReportTemplateService(
            IAppPathProvider pathProvider,
            ISettingsService settingsService,
            IBusinessClock? clock = null,
            ILogger<ReportTemplateService>? logger = null,
            IDbContextFactory<AppDbContext>? contextFactory = null,
            BusinessDataAccessScope? accessScope = null,
            IReportTemplateImageResourceService? imageResourceService = null)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(settingsService);
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _logger = logger ?? NullLogger<ReportTemplateService>.Instance;
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver, _logger);
            _settingsService = settingsService;
            _clock = clock ?? BusinessClock.CreateSystem();
            _contextFactory = contextFactory;
            _accessScope = accessScope;
            _storageCoordinator = new ReportTemplateStorageCoordinator(pathProvider, settingsService, _logger);
            _imageResourceHydrator = new ReportTemplateV3ImageResourceHydrator(
                imageResourceService ?? new ReportTemplateImageResourceService(pathProvider));
        }

        public async Task<ReportTemplateContentResult> CreateTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            string? displayName = null,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            return await _storageCoordinator.ExecuteMutationAsync(async transaction =>
            {
                string resolvedPath = ResolveTemplateLifecycleTargetPath(
                    reportType,
                    templatePath,
                    BuildDefaultTemplateFileName(reportType));

                ReportTemplateFilePolicy.EnsureNoPortableCollision(resolvedPath);

                if (File.Exists(resolvedPath))
                {
                    throw new ResourceConflictException("目标模板已存在。");
                }

                string title = string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(resolvedPath)
                    : displayName.Trim();
                string content = ReportTemplateStarterFactory.Create(reportType, title, resolvedPath);
                ReportTemplateContentPolicy.Validate(reportType, content);

                await AtomicFileHelper.WriteAllTextAtomicAsync(
                        resolvedPath,
                        content,
                        Encoding.UTF8,
                        cancellationToken)
                    .ConfigureAwait(false);
                transaction.MarkTemplatesChanged();

                transaction.MarkSettingsChanged();
                await SyncTemplateStateAsync(reportType, string.Empty, resolvedPath, title, cancellationToken).ConfigureAwait(false);
                return ToContentResult(CreateResolvedTemplate(reportType, resolvedPath, title), content);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplateContentResult> GetTemplateContentAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            return await _storageCoordinator.ExecuteReadAsync(async () =>
            {
                var resolved = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                    .ConfigureAwait(false);
                string content = await File.ReadAllTextAsync(resolved.TemplatePath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);

                return ToContentResult(resolved, content);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplateContentResult> SaveTemplateContentAsync(
            ReportDocumentType reportType,
            string templatePath,
            string content,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            ReportTemplateContentPolicy.Validate(reportType, content ?? string.Empty);
            return await _storageCoordinator.ExecuteMutationAsync(async transaction =>
            {
                var resolved = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: false, cancellationToken)
                    .ConfigureAwait(false);
                string previousPath = resolved.TemplatePath;
                if (_pathResolver.IsBuiltInTemplatePath(previousPath))
                {
                    string userCopyPath = _pathResolver.GetUserCopyPath(previousPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(userCopyPath)!);
                    resolved = CreateResolvedTemplate(reportType, userCopyPath, resolved.DisplayName);
                }

                await AtomicFileHelper.WriteAllTextAtomicAsync(
                        resolved.TemplatePath,
                        content ?? string.Empty,
                        Encoding.UTF8,
                        cancellationToken)
                    .ConfigureAwait(false);
                transaction.MarkTemplatesChanged();

                transaction.MarkSettingsChanged();
                await SyncTemplateStateAsync(
                        reportType,
                        previousPath,
                        resolved.TemplatePath,
                        resolved.DisplayName,
                        cancellationToken)
                    .ConfigureAwait(false);

                return ToContentResult(resolved, content ?? string.Empty);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplateContentResult> RenameTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            string newTemplatePath,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            return await _storageCoordinator.ExecuteMutationAsync(async transaction =>
            {
                var current = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                    .ConfigureAwait(false);
                EnsureTemplateLifecyclePath(current.TemplatePath);
                var catalogSnapshot = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);

                string resolvedNewPath = ResolveTemplateLifecycleTargetPath(
                    reportType,
                    newTemplatePath,
                    Path.GetFileName(current.TemplatePath));
                if (PhysicalPathComparison.AreSamePath(current.TemplatePath, resolvedNewPath))
                {
                    string unchangedContent = await File.ReadAllTextAsync(current.TemplatePath, Encoding.UTF8, cancellationToken)
                        .ConfigureAwait(false);
                    return ToContentResult(current, unchangedContent);
                }

                ReportTemplateFilePolicy.EnsureNoPortableCollision(resolvedNewPath, current.TemplatePath);
                if (File.Exists(resolvedNewPath))
                {
                    throw new ResourceConflictException("目标模板已存在。");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(resolvedNewPath)!);
                File.Move(current.TemplatePath, resolvedNewPath, overwrite: false);
                transaction.MarkTemplatesChanged();
                transaction.MarkSettingsChanged();
                await SyncTemplateStateAsync(
                        reportType,
                        current.TemplatePath,
                        resolvedNewPath,
                        current.DisplayName,
                        cancellationToken,
                        catalogSnapshot,
                        current.TemplatePath)
                    .ConfigureAwait(false);

                string content = await File.ReadAllTextAsync(resolvedNewPath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                return ToContentResult(CreateResolvedTemplate(reportType, resolvedNewPath, current.DisplayName), content);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplateContentResult> UpdateTemplateDisplayNameAsync(
            ReportDocumentType reportType,
            string templatePath,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            return await _storageCoordinator.ExecuteMutationAsync(async transaction =>
            {
                var current = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                    .ConfigureAwait(false);
                string normalizedDisplayName = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(displayName, current.TemplatePath);
                transaction.MarkTemplatesChanged();
                await RefreshTemplateCatalogAsync(current.TemplatePath, normalizedDisplayName, cancellationToken).ConfigureAwait(false);

                string content = await File.ReadAllTextAsync(current.TemplatePath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                return ToContentResult(CreateResolvedTemplate(reportType, current.TemplatePath, normalizedDisplayName), content);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplateCommandResult> SetDefaultTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            return await _storageCoordinator.ExecuteMutationAsync(async transaction =>
            {
                if (TryParseUserTemplateId(templatePath, out int userTemplateId))
                {
                    if (_contextFactory == null || _accessScope == null)
                    {
                        throw new InvalidOperationException("数据库报表模板服务尚未配置。");
                    }

                    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                    var userTemplate = await _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                        .Where(item => item.Id == userTemplateId &&
                                       item.Status == TemplateLifecycleStatusCatalog.Published &&
                                       item.ReportType == reportType.ToString())
                        .Select(item => new { item.Id, item.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (userTemplate == null)
                    {
                        throw new ResourceNotFoundException("用户报表模板不存在、已停用或无权访问。");
                    }

                    await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
                    string userStoredPath = $"user-template:{userTemplate.Id}";
                    transaction.MarkSettingsChanged();
                    await _settingsService.UpdateAsync(settings =>
                    {
                        if (reportType == ReportDocumentType.PaymentVoucher)
                        {
                            if (string.Equals(settings.ReportTemplateDefaults.PaymentVoucherTemplatePath, userStoredPath, StringComparison.Ordinal)) return false;
                            settings.ReportTemplateDefaults.PaymentVoucherTemplatePath = userStoredPath;
                        }
                        else
                        {
                            if (string.Equals(settings.ReportTemplateDefaults.ExportDocumentTemplatePath, userStoredPath, StringComparison.Ordinal)) return false;
                            settings.ReportTemplateDefaults.ExportDocumentTemplatePath = userStoredPath;
                        }
                        return true;
                    }, cancellationToken).ConfigureAwait(false);

                    return new ReportTemplateCommandResult
                    {
                        ReportType = reportType,
                        TemplatePath = userStoredPath,
                        StoragePolicy = StoragePolicy,
                        Message = $"已将“{userTemplate.Name}”设为默认模板。"
                    };
                }

                var current = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                    .ConfigureAwait(false);
                string storedPath = _pathResolver.ToStoredPath(current.TemplatePath);

                await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
                transaction.MarkSettingsChanged();
                await _settingsService.UpdateAsync(settings =>
                {
                    string configuredPath = reportType == ReportDocumentType.PaymentVoucher
                        ? settings.ReportTemplateDefaults.PaymentVoucherTemplatePath
                        : settings.ReportTemplateDefaults.ExportDocumentTemplatePath;
                    if (string.Equals(configuredPath, storedPath, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (reportType == ReportDocumentType.PaymentVoucher)
                    {
                        settings.ReportTemplateDefaults.PaymentVoucherTemplatePath = storedPath;
                    }
                    else
                    {
                        settings.ReportTemplateDefaults.ExportDocumentTemplatePath = storedPath;
                    }

                    return true;
                }, cancellationToken).ConfigureAwait(false);

                return new ReportTemplateCommandResult
                {
                    ReportType = reportType,
                    TemplatePath = current.TemplatePath,
                    StoragePolicy = StoragePolicy,
                    Message = $"已将“{current.DisplayName}”设为默认模板。"
                };
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplateCommandResult> DeleteTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            return await _storageCoordinator.ExecuteMutationAsync(async transaction =>
            {
                var current = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                    .ConfigureAwait(false);
                EnsureTemplateLifecyclePath(current.TemplatePath);
                var catalogSnapshot = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);

                File.Delete(current.TemplatePath);
                transaction.MarkTemplatesChanged();
                transaction.MarkSettingsChanged();
                await SyncTemplateStateAsync(
                        reportType,
                        current.TemplatePath,
                        string.Empty,
                        null,
                        cancellationToken,
                        catalogSnapshot,
                        current.TemplatePath)
                    .ConfigureAwait(false);

                return new ReportTemplateCommandResult
                {
                    ReportType = reportType,
                    TemplatePath = current.TemplatePath,
                    StoragePolicy = StoragePolicy,
                    Message = "模板已删除。"
                };
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportTemplatePreviewResult> PreviewTemplateContentAsync(
            ReportDocumentType reportType,
            string content,
            bool withSeal = true,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            cancellationToken.ThrowIfCancellationRequested();
            ReportTemplateContentPolicy.Validate(reportType, content ?? string.Empty);
            string templateContent = ScribanReportTemplateRenderer.PreprocessHtmlTemplate(
                content ?? string.Empty,
                _logger);
            bool effectiveWithSeal = reportType != ReportDocumentType.PaymentVoucher && withSeal;
            string html = reportType == ReportDocumentType.PaymentVoucher
                ? RenderPaymentVoucherPreview(templateContent)
                : RenderInvoicePreview(templateContent, effectiveWithSeal);
            html = await _imageResourceHydrator.HydrateAsync(html, cancellationToken).ConfigureAwait(false);

            return new ReportTemplatePreviewResult
            {
                ReportType = reportType,
                WithSeal = reportType == ReportDocumentType.PaymentVoucher ? null : effectiveWithSeal,
                Html = html
            };
        }

        private string ResolveTemplateLifecycleTargetPath(
            ReportDocumentType reportType,
            string selectedTemplatePath,
            string fallbackFileName)
        {
            string category = GetTemplateCategory(reportType);
            string categoryDirectory = _pathResolver.EnsureTemplateDirectory(category);
            string candidatePath;

            if (string.IsNullOrWhiteSpace(selectedTemplatePath))
            {
                candidatePath = Path.Combine(categoryDirectory, fallbackFileName);
            }
            else
            {
                string selected = selectedTemplatePath.Trim()
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                candidatePath = _pathResolver.ToAbsolutePath(selected);

                if (!ReportTemplatePathResolver.IsPathWithinDirectory(candidatePath, categoryDirectory))
                {
                    string fileName = Path.GetFileName(candidatePath);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = fallbackFileName;
                    }

                    candidatePath = Path.GetFullPath(Path.Combine(categoryDirectory, fileName));
                }
            }

            candidatePath = ReportTemplateFilePolicy.NormalizeNewTemplatePath(candidatePath);
            if (!ReportTemplatePathResolver.IsPathWithinDirectory(candidatePath, categoryDirectory))
            {
                throw new PermissionDeniedException("只能在当前模板分类目录下新建或重命名模板。");
            }

            return candidatePath;
        }

        private async Task<ResolvedReportTemplate> ResolveEditableTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            bool mustExist,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                throw new ArgumentException("模板路径不能为空。", nameof(templatePath));
            }

            string resolvedPath = Path.GetFullPath(_pathResolver.ToAbsolutePath(templatePath.Trim()));
            ReportTemplateFilePolicy.ValidateExistingTemplatePath(resolvedPath);

            var configs = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);
            var matched = configs.FirstOrDefault(config =>
                PhysicalPathComparison.AreSamePath(config.FileName, resolvedPath));

            bool withinManagedTemplateRoots = _pathResolver.IsBuiltInTemplatePath(resolvedPath) ||
                                              _pathResolver.IsUserTemplatePath(resolvedPath);
            if (!withinManagedTemplateRoots || matched == null && !_pathResolver.IsUserTemplatePath(resolvedPath) && !File.Exists(resolvedPath))
            {
                throw new PermissionDeniedException("只能读取内置模板，或维护运行数据根 Templates/ 下的用户模板。");
            }

            var effectiveReportType = matched != null
                ? ReportTemplateCatalogLoader.ResolveCatalogReportType(matched.Type, matched.FileName)
                : ReportTemplateCatalogLoader.ResolveCatalogReportType(null, resolvedPath);
            if (effectiveReportType != reportType)
            {
                throw new ArgumentException("模板类型与请求的报表类型不匹配。", nameof(reportType));
            }

            if (mustExist && !File.Exists(resolvedPath))
            {
                throw new ResourceNotFoundException("报表模板不存在。");
            }

            string? directory = Path.GetDirectoryName(resolvedPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("无法解析模板所在目录。", nameof(templatePath));
            }

            if (_pathResolver.IsUserTemplatePath(resolvedPath))
            {
                Directory.CreateDirectory(directory);
            }
            return new ResolvedReportTemplate
            {
                ReportType = reportType,
                DisplayName = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(matched?.Name, resolvedPath),
                TemplatePath = resolvedPath,
                WithSealDefault = reportType == ReportDocumentType.PaymentVoucher
                    ? null
                    : matched?.WithSeal ?? true
            };
        }

        private async Task SyncTemplateStateAsync(
            ReportDocumentType reportType,
            string previousTemplatePath,
            string currentTemplatePath,
            string? currentDisplayName,
            CancellationToken cancellationToken,
            IReadOnlyList<ReportTemplateConfig>? catalogSnapshot = null,
            string? removedTemplatePath = null)
        {
            await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

            string normalizedPreviousPath = _catalogLoader.NormalizeStoredTemplatePath(previousTemplatePath);
            string normalizedPreviousAbsolutePath = _catalogLoader.NormalizeAbsoluteTemplatePath(previousTemplatePath);
            string normalizedCurrentPath = _catalogLoader.NormalizeStoredTemplatePath(currentTemplatePath);
            await _settingsService.UpdateAsync(settings =>
            {
                bool changed = UpdateDefaultTemplateReference(
                    settings.ReportTemplateDefaults,
                    reportType,
                    normalizedPreviousPath,
                    normalizedPreviousAbsolutePath,
                    normalizedCurrentPath);
                if (reportType == ReportDocumentType.PaymentVoucher)
                {
                    return UpdateTemplateReferences(
                        settings.PaymentTemplates,
                        normalizedPreviousPath,
                        normalizedPreviousAbsolutePath,
                        normalizedCurrentPath) || changed;
                }

                return UpdateTemplateReferences(
                    settings.BatchExport.Items,
                    normalizedPreviousPath,
                    normalizedPreviousAbsolutePath,
                    normalizedCurrentPath) || changed;
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await RefreshTemplateCatalogAsync(
                    currentTemplatePath,
                    currentDisplayName,
                    cancellationToken,
                    catalogSnapshot,
                    removedTemplatePath)
                .ConfigureAwait(false);
        }

        private async Task RefreshTemplateCatalogAsync(
            string currentTemplatePath,
            string? currentDisplayName,
            CancellationToken cancellationToken,
            IReadOnlyList<ReportTemplateConfig>? catalogSnapshot = null,
            string? removedTemplatePath = null)
        {
            string configPath = _pathResolver.GetUserConfigPath();
            var configs = catalogSnapshot ??
                await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);
            string normalizedCurrentPath = string.IsNullOrWhiteSpace(currentTemplatePath)
                ? string.Empty
                : Path.GetFullPath(currentTemplatePath);
            string normalizedRemovedPath = string.IsNullOrWhiteSpace(removedTemplatePath)
                ? string.Empty
                : Path.GetFullPath(removedTemplatePath);
            var rows = configs
                .Where(config =>
                    config != null &&
                    !string.IsNullOrWhiteSpace(config.FileName) &&
                    (normalizedRemovedPath.Length == 0 ||
                     !PhysicalPathComparison.AreSamePath(config.FileName, normalizedRemovedPath)) &&
                    (_pathResolver.IsBuiltInTemplatePath(config.FileName) || _pathResolver.IsUserTemplatePath(config.FileName)))
                .Select(config => new ReportTemplateConfig
                {
                    Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(config.Type, config.FileName),
                    FileName = _pathResolver.ToStoredPath(config.FileName),
                    Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(
                        !string.IsNullOrWhiteSpace(normalizedCurrentPath) &&
                        PhysicalPathComparison.AreSamePath(config.FileName, normalizedCurrentPath)
                            ? currentDisplayName
                            : config.Name,
                        config.FileName),
                    WithSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(config.Type, config.FileName) == ReportDocumentType.PaymentVoucher
                        ? null
                        : config.WithSeal ?? true
                })
                .ToList();
            if (normalizedCurrentPath.Length > 0 &&
                !rows.Any(config => PhysicalPathComparison.AreSamePath(
                    _pathResolver.ToAbsolutePath(config.FileName),
                    normalizedCurrentPath)))
            {
                rows.Add(new ReportTemplateConfig
                {
                    Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(null, normalizedCurrentPath),
                    FileName = _pathResolver.ToStoredPath(normalizedCurrentPath),
                    Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(
                        currentDisplayName,
                        normalizedCurrentPath),
                    WithSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(null, normalizedCurrentPath) ==
                               ReportDocumentType.PaymentVoucher
                        ? null
                        : true
                });
            }
            rows = rows
                .OrderBy(config => config.Type, StringComparer.Ordinal)
                .ThenBy(config => config.FileName, StringComparer.Ordinal)
                .ToList();
            var root = new ReportTemplateConfigRoot { Reports = rows };
            string json = JsonSerializer.Serialize(root, ReportTemplateCatalogLoader.JsonOptions);

            await AtomicFileHelper.WriteAllTextAtomicAsync(
                    configPath,
                    json,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private void DemandReportTypeAccess(ReportDocumentType reportType)
        {
            if (_accessScope?.CurrentUser != null)
            {
                _accessScope.DemandPermission(
                    ReportDocumentAccessCatalog.GetSourceResource(reportType),
                    PermissionAction.View);
            }
        }

        private bool UpdateTemplateReferences(
            IEnumerable<TemplateItemBase> items,
            string previousTemplatePath,
            string previousAbsoluteTemplatePath,
            string currentTemplatePath)
        {
            if (items == null ||
                (string.IsNullOrWhiteSpace(previousTemplatePath) &&
                 string.IsNullOrWhiteSpace(previousAbsoluteTemplatePath)))
            {
                return false;
            }

            bool changed = false;
            foreach (var item in items.Where(item => item != null))
            {
                if (!TemplateReferenceMatches(item.TemplatePath, previousTemplatePath, previousAbsoluteTemplatePath))
                {
                    continue;
                }

                string nextPath = currentTemplatePath ?? string.Empty;
                if (string.Equals(item.TemplatePath, nextPath, StringComparison.Ordinal))
                {
                    continue;
                }

                item.TemplatePath = nextPath;
                changed = true;
            }

            return changed;
        }

        private bool UpdateDefaultTemplateReference(
            ReportTemplateDefaults defaults,
            ReportDocumentType reportType,
            string previousTemplatePath,
            string previousAbsoluteTemplatePath,
            string currentTemplatePath)
        {
            string configuredPath = reportType == ReportDocumentType.PaymentVoucher
                ? defaults.PaymentVoucherTemplatePath
                : defaults.ExportDocumentTemplatePath;
            if (!TemplateReferenceMatches(configuredPath, previousTemplatePath, previousAbsoluteTemplatePath))
            {
                return false;
            }

            if (reportType == ReportDocumentType.PaymentVoucher)
            {
                defaults.PaymentVoucherTemplatePath = currentTemplatePath;
            }
            else
            {
                defaults.ExportDocumentTemplatePath = currentTemplatePath;
            }

            return true;
        }

        private bool TemplateReferenceMatches(
            string templatePath,
            string previousTemplatePath,
            string previousAbsoluteTemplatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return false;
            }

            string normalizedPath = _catalogLoader.NormalizeStoredTemplatePath(templatePath);
            string normalizedAbsolutePath = _catalogLoader.NormalizeAbsoluteTemplatePath(templatePath);
            return
                (!string.IsNullOrWhiteSpace(previousTemplatePath) &&
                 string.Equals(normalizedPath, previousTemplatePath, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(previousAbsoluteTemplatePath) &&
                 PhysicalPathComparison.Comparer.Equals(normalizedAbsolutePath, previousAbsoluteTemplatePath));
        }

        private static ReportTemplateContentResult ToContentResult(ResolvedReportTemplate template, string content)
        {
            return new ReportTemplateContentResult
            {
                ReportType = template.ReportType,
                DisplayName = template.DisplayName,
                TemplatePath = template.TemplatePath,
                WithSealDefault = template.WithSealDefault,
                Content = content ?? string.Empty,
                StoragePolicy = StoragePolicy
            };
        }

        private static bool TryParseUserTemplateId(string? templatePath, out int id)
        {
            const string prefix = "user-template:";
            id = 0;
            return !string.IsNullOrWhiteSpace(templatePath) &&
                   templatePath.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(templatePath.Trim()[prefix.Length..], out id) &&
                   id > 0;
        }

        private static ResolvedReportTemplate CreateResolvedTemplate(
            ReportDocumentType reportType,
            string templatePath,
            string? displayName = null)
        {
            return new ResolvedReportTemplate
            {
                ReportType = reportType,
                DisplayName = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(displayName, templatePath),
                TemplatePath = Path.GetFullPath(templatePath),
                WithSealDefault = reportType == ReportDocumentType.PaymentVoucher ? null : true
            };
        }

        private void EnsureTemplateLifecyclePath(string templatePath)
        {
            if (!_pathResolver.IsUserTemplatePath(templatePath))
            {
                throw new PermissionDeniedException("内置模板为只读资源；请先保存为用户模板副本，再执行重命名或删除。");
            }
        }

        private static string GetTemplateCategory(ReportDocumentType reportType)
        {
            return reportType == ReportDocumentType.PaymentVoucher
                ? ReportTemplateCatalogLoader.InternalTemplateCatalogType
                : ReportTemplateCatalogLoader.ExportTemplateCatalogType;
        }

        private string BuildDefaultTemplateFileName(ReportDocumentType reportType)
        {
            string prefix = reportType == ReportDocumentType.PaymentVoucher
                ? "internal_template"
                : "export_template";
            return $"{prefix}_{_clock.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.html";
        }

        private sealed class ResolvedReportTemplate
        {
            public ReportDocumentType ReportType { get; init; }

            public string DisplayName { get; init; } = string.Empty;

            public string TemplatePath { get; init; } = string.Empty;

            public bool? WithSealDefault { get; init; }
        }
    }
}
