using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportHtmlService : IReportHtmlService
    {
        private readonly ReportEntityLoader _entityLoader;
        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplateCatalogLoader _catalogLoader;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;
        private readonly ISettingsService _settingsService;
        private readonly IAppPathProvider _pathProvider;
        private readonly ILogger<ReportHtmlService> _logger;

        private readonly SemaphoreSlim _templateConfigSemaphore = new(1, 1);
        private readonly Lock _configLock = new();
        private Dictionary<ReportDocumentType, string> _templatePathCache = [];
        private List<ReportTemplateConfig> _templateConfigs = [];
        private bool _templateConfigLoaded;

        public ReportHtmlService(
            IDbContextFactory<AppDbContext> contextFactory,
            ISettingsService settingsService,
            IAppPathProvider pathProvider,
            BusinessDataAccessScope accessScope,
            ILogger<ReportHtmlService>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(contextFactory);
            ArgumentNullException.ThrowIfNull(settingsService);
            _contextFactory = contextFactory;
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _settingsService = settingsService;
            _logger = logger ?? NullLogger<ReportHtmlService>.Instance;
            _entityLoader = new ReportEntityLoader(contextFactory, _accessScope);
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver, _logger);
        }

        public async Task<IReadOnlyList<ReportTemplateDescriptor>> GetAvailableTemplatesAsync(
            ReportDocumentType reportType,
            CancellationToken cancellationToken = default)
        {
            await EnsureTemplateConfigLoadedAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<ReportTemplateDescriptor>();

            List<ReportTemplateConfig> configs;
            lock (_configLock)
            {
                configs = _templateConfigs?.Select(CloneTemplateConfig).ToList() ?? new List<ReportTemplateConfig>();
            }

            foreach (var cfg in configs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (cfg == null || string.IsNullOrWhiteSpace(cfg.FileName))
                {
                    continue;
                }

                if (ReportTemplateCatalogLoader.ResolveCatalogReportType(cfg.Type, cfg.FileName) != reportType)
                {
                    continue;
                }

                var templatePath = Path.GetFullPath(cfg.FileName);
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(cfg.Name)
                    ? Path.GetFileNameWithoutExtension(templatePath)
                    : cfg.Name;

                if (!result.Any(r => PhysicalPathComparison.AreSamePath(r.TemplatePath, templatePath)))
                {
                    result.Add(new ReportTemplateDescriptor
                    {
                        ReportType = reportType,
                        DisplayName = displayName,
                        TemplatePath = templatePath,
                        WithSealDefault = reportType == ReportDocumentType.PaymentVoucher
                            ? null
                            : cfg.WithSeal ?? true
                    });
                }
            }

            if (result.Count == 0)
            {
                string defaultPath = await GetTemplatePathAsync(reportType, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(defaultPath) && File.Exists(defaultPath))
                {
                    result.Add(new ReportTemplateDescriptor
                    {
                        ReportType = reportType,
                        DisplayName = "默认模板 (Default)",
                        TemplatePath = defaultPath,
                        WithSealDefault = reportType == ReportDocumentType.PaymentVoucher ? null : true
                    });
                }
            }

            return result;
        }

        public async Task<ReportHtmlRenderResult> RenderInvoiceReportAsync(
            int invoiceId,
            ReportDocumentType reportType,
            string? templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default)
        {
            if (invoiceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(invoiceId), "发票ID必须大于0。");
            }

            if (reportType != ReportDocumentType.ExportDocument)
            {
                throw new ArgumentException("发票 HTML 预览目前仅支持出口单据模板。", nameof(reportType));
            }

            var invoice = await _entityLoader.LoadInvoiceAsync(invoiceId, cancellationToken).ConfigureAwait(false);
            if (invoice == null)
            {
                throw new ResourceNotFoundException($"未找到发票：{invoiceId}");
            }

            var (resolvedTemplatePath, templateContent) = await LoadTemplateAsync(reportType, templatePath, cancellationToken).ConfigureAwait(false);
            string html = await GenerateHtmlReportAsync(reportType, invoice, templateContent, withSeal, false, cancellationToken).ConfigureAwait(false);

            return new ReportHtmlRenderResult
            {
                ReportType = reportType,
                SourceId = invoiceId,
                TemplatePath = resolvedTemplatePath,
                WithSeal = withSeal,
                Html = html
            };
        }

        public async Task<ReportHtmlRenderResult> RenderInvoiceReportDraftAsync(
            Invoice invoice,
            ReportDocumentType reportType,
            string? templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            if (reportType != ReportDocumentType.ExportDocument)
            {
                throw new ArgumentException("发票草稿 HTML 预览目前仅支持出口单据模板。", nameof(reportType));
            }

            var (resolvedTemplatePath, templateContent) = await LoadTemplateAsync(reportType, templatePath, cancellationToken).ConfigureAwait(false);
            string html = await GenerateHtmlReportAsync(reportType, invoice, templateContent, withSeal, true, cancellationToken).ConfigureAwait(false);

            return new ReportHtmlRenderResult
            {
                ReportType = reportType,
                SourceId = invoice.Id,
                TemplatePath = resolvedTemplatePath,
                WithSeal = withSeal,
                Html = html
            };
        }

        public async Task<ReportHtmlRenderResult> RenderPaymentVoucherAsync(
            int paymentId,
            string? templatePath = null,
            CancellationToken cancellationToken = default)
        {
            if (paymentId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(paymentId), "付款ID必须大于0。");
            }

            var payment = await _entityLoader.LoadPaymentAsync(paymentId, cancellationToken).ConfigureAwait(false);
            if (payment == null)
            {
                throw new ResourceNotFoundException($"未找到付款单：{paymentId}");
            }

            var (resolvedTemplatePath, templateContent) = await LoadTemplateAsync(
                    ReportDocumentType.PaymentVoucher,
                    templatePath,
                    cancellationToken)
                .ConfigureAwait(false);
            string html = await GeneratePaymentVoucherHtmlAsync(payment, templateContent, cancellationToken).ConfigureAwait(false);

            return new ReportHtmlRenderResult
            {
                ReportType = ReportDocumentType.PaymentVoucher,
                SourceId = paymentId,
                TemplatePath = resolvedTemplatePath,
                WithSeal = null,
                Html = html
            };
        }

        public async Task<ReportHtmlRenderResult> RenderPaymentVoucherDraftAsync(
            Payment payment,
            string? templatePath = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(payment);

            var (resolvedTemplatePath, templateContent) = await LoadTemplateAsync(
                    ReportDocumentType.PaymentVoucher,
                    templatePath,
                    cancellationToken)
                .ConfigureAwait(false);
            string html = await GeneratePaymentVoucherHtmlAsync(payment, templateContent, cancellationToken).ConfigureAwait(false);

            return new ReportHtmlRenderResult
            {
                ReportType = ReportDocumentType.PaymentVoucher,
                SourceId = payment.Id,
                TemplatePath = resolvedTemplatePath,
                WithSeal = null,
                Html = html
            };
        }

        private async Task<string> GenerateHtmlReportAsync(
            ReportDocumentType reportType,
            Invoice invoice,
            string templateContent,
            bool withSeal,
            bool isPreview,
            CancellationToken cancellationToken)
        {
            try
            {
                ReportTemplateContentPolicy.Validate(reportType, templateContent);
                templateContent = ScribanReportTemplateRenderer.PreprocessHtmlTemplate(templateContent, _logger);

                if (!isPreview)
                {
                    EnsureInvoiceValid(invoice);
                }
                else if (invoice == null)
                {
                    invoice = new Invoice();
                }

                var (customer, exporter) = await _entityLoader
                    .LoadInvoiceEntitiesAsync(invoice, isPreview, cancellationToken)
                    .ConfigureAwait(false);

                var globals = ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(
                    invoice,
                    customer,
                    exporter,
                    withSeal,
                    _pathProvider,
                    _logger);
                return ScribanReportTemplateRenderer.Render(templateContent, globals);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "报表模板或内容校验失败, 类型 {ReportType}, 发票 {InvoiceId}", reportType, invoice?.Id);
                throw new ServiceValidationException($"报表模板或内容无效：{ex.Message}", ex);
            }
            catch (Exception ex) when (ex is ServiceException or KeyNotFoundException or
                                       FileNotFoundException or UnauthorizedAccessException)
            {
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "报表模板或渲染内容校验失败, 类型 {ReportType}, 发票 {InvoiceId}", reportType, invoice?.Id);
                throw new ServiceValidationException($"报表模板或内容无效：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成报表失败, 类型 {ReportType}, 发票 {InvoiceId}", reportType, invoice?.Id);
                throw new InfrastructureServiceException("报表生成服务暂时不可用，请稍后重试。", ex);
            }
        }

        private async Task<string> GeneratePaymentVoucherHtmlAsync(
            Payment payment,
            string templateContent,
            CancellationToken cancellationToken)
        {
            if (payment == null)
            {
                throw new ArgumentNullException(nameof(payment));
            }

            try
            {
                ReportTemplateContentPolicy.Validate(ReportDocumentType.PaymentVoucher, templateContent);
                templateContent = ScribanReportTemplateRenderer.PreprocessHtmlTemplate(templateContent, _logger);
                var payee = await _entityLoader
                    .LoadPaymentVoucherEntitiesAsync(payment, cancellationToken)
                    .ConfigureAwait(false);

                var globals = ReportTemplateGlobalsBuilder.BuildPaymentVoucherGlobals(payment, payee);
                return ScribanReportTemplateRenderer.Render(templateContent, globals);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "付款单模板或渲染内容校验失败, 付款单 {PaymentId}", payment?.Id);
                throw new ServiceValidationException($"付款单模板或内容无效：{ex.Message}", ex);
            }
            catch (Exception ex) when (ex is ServiceException or KeyNotFoundException or
                                       FileNotFoundException or UnauthorizedAccessException)
            {
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "付款单模板或渲染内容校验失败, 付款单 {PaymentId}", payment?.Id);
                throw new ServiceValidationException($"付款单模板或内容无效：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成付款单失败, 付款单 {PaymentId}", payment?.Id);
                throw new InfrastructureServiceException("付款单生成服务暂时不可用，请稍后重试。", ex);
            }
        }

        private async Task<string> ResolveTemplatePathAsync(
            ReportDocumentType reportType,
            string? templatePath,
            CancellationToken cancellationToken)
        {
            var resolvedTemplatePath = string.IsNullOrWhiteSpace(templatePath)
                ? string.Empty
                : _pathResolver.ToAbsolutePath(templatePath);

            if (string.IsNullOrWhiteSpace(resolvedTemplatePath))
            {
                resolvedTemplatePath = await GetTemplatePathAsync(reportType, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(resolvedTemplatePath) &&
                !_pathResolver.IsBuiltInTemplatePath(resolvedTemplatePath) &&
                !_pathResolver.IsUserTemplatePath(resolvedTemplatePath))
            {
                throw new PermissionDeniedException("报表模板必须位于内置模板目录或运行数据根用户模板目录。");
            }

            if (string.IsNullOrWhiteSpace(resolvedTemplatePath) || !File.Exists(resolvedTemplatePath))
            {
                throw new ResourceNotFoundException("报表模板不存在或已被移除。");
            }

            var resolvedReportType = ReportTemplateCatalogLoader.ResolveCatalogReportType(null, resolvedTemplatePath);
            if (resolvedReportType != reportType)
            {
                throw new ArgumentException(
                    "模板类型与当前业务单据不匹配。出口单证模板和付款报销模板不能交叉使用。",
                    nameof(templatePath));
            }

            return resolvedTemplatePath;
        }

        private async Task<(string Path, string Content)> LoadTemplateAsync(
            ReportDocumentType reportType,
            string? templatePath,
            CancellationToken cancellationToken)
        {
            if (TryParseUserTemplateId(templatePath, out int userTemplateId))
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var template = await _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                    .Where(item => item.Id == userTemplateId && item.IsActive && item.ReportType == reportType.ToString())
                    .Select(item => new { item.Id, item.ContentHtml })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (template == null)
                {
                    throw new ResourceNotFoundException("用户报表模板不存在、已停用或无权访问。");
                }

                return ($"user-template:{userTemplateId}", template.ContentHtml ?? string.Empty);
            }

            string resolvedPath = await ResolveTemplatePathAsync(reportType, templatePath, cancellationToken).ConfigureAwait(false);
            string content = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            return (resolvedPath, content);
        }

        private static bool TryParseUserTemplateId(string? templatePath, out int id)
        {
            const string prefix = "user-template:";
            id = 0;
            return !string.IsNullOrWhiteSpace(templatePath) &&
                   templatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(templatePath[prefix.Length..], out id) &&
                   id > 0;
        }

        private async Task EnsureTemplateConfigLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (_templateConfigLoaded)
            {
                return;
            }

            await _templateConfigSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_templateConfigLoaded)
                {
                    return;
                }

                try
                {
                    var configs = (await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false)).ToList();
                    var cache = _catalogLoader.BuildTemplatePathCache(configs);

                    lock (_configLock)
                    {
                        _templatePathCache = cache;
                        _templateConfigs = configs;
                        _templateConfigLoaded = true;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "加载报表模板配置失败，本次使用内置模板并在下次请求时重试");
                    lock (_configLock)
                    {
                        _templatePathCache = new Dictionary<ReportDocumentType, string>();
                        _templateConfigs = new List<ReportTemplateConfig>();
                        _templateConfigLoaded = false;
                    }
                }
            }
            finally
            {
                _templateConfigSemaphore.Release();
            }
        }

        private async Task<string> GetTemplatePathAsync(
            ReportDocumentType reportType,
            CancellationToken cancellationToken = default)
        {
            await EnsureTemplateConfigLoadedAsync(cancellationToken).ConfigureAwait(false);

            await _settingsService.LoadAsync().ConfigureAwait(false);
            string defaultTemplatePath = reportType == ReportDocumentType.PaymentVoucher
                ? _settingsService.Settings.ReportTemplateDefaults.PaymentVoucherTemplatePath
                : _settingsService.Settings.ReportTemplateDefaults.ExportDocumentTemplatePath;
            if (!string.IsNullOrWhiteSpace(defaultTemplatePath))
            {
                string resolvedDefaultPath = Path.GetFullPath(_pathResolver.ToAbsolutePath(defaultTemplatePath));
                ReportTemplateConfig? configuredDefault;
                lock (_configLock)
                {
                    configuredDefault = _templateConfigs.FirstOrDefault(config =>
                        PhysicalPathComparison.AreSamePath(config.FileName, resolvedDefaultPath));
                }

                if (configuredDefault != null &&
                    ReportTemplateCatalogLoader.ResolveCatalogReportType(
                        configuredDefault.Type,
                        configuredDefault.FileName) == reportType &&
                    File.Exists(resolvedDefaultPath))
                {
                    return resolvedDefaultPath;
                }
            }

            string? configuredPath = null;
            lock (_configLock)
            {
                if (_templatePathCache != null && _templatePathCache.TryGetValue(reportType, out var path))
                {
                    configuredPath = path;
                }
            }

            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            string subFolder = reportType == ReportDocumentType.PaymentVoucher
                ? ReportTemplateCatalogLoader.InternalTemplateCatalogType
                : ReportTemplateCatalogLoader.ExportTemplateCatalogType;
            string basePath = _pathResolver.GetBuiltInTemplateDirectory(subFolder);

            string htmlPath = reportType == ReportDocumentType.PaymentVoucher
                ? "payment_voucher_template.html"
                : "invoice_template.html";
            htmlPath = Path.Combine(basePath, htmlPath);

            return File.Exists(htmlPath) ? htmlPath : string.Empty;
        }

        private static void EnsureInvoiceValid(Invoice invoice)
        {
            if (invoice == null || invoice.Id <= 0)
            {
                throw new ArgumentException("发票数据无效 / Invalid invoice data");
            }
        }

        private static ReportTemplateConfig CloneTemplateConfig(ReportTemplateConfig config)
        {
            return new ReportTemplateConfig
            {
                Type = config.Type,
                FileName = config.FileName,
                Name = config.Name,
                WithSeal = config.WithSeal
            };
        }
    }
}
