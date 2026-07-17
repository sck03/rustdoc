using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportHtmlService : IReportHtmlService
    {
        private readonly ReportEntityLoader _entityLoader;
        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplateCatalogLoader _catalogLoader;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        private readonly SemaphoreSlim _templateConfigSemaphore = new(1, 1);
        private readonly object _configLock = new();
        private Dictionary<ReportDocumentType, string> _templatePathCache;
        private List<ReportTemplateConfig> _templateConfigs;
        private bool _templateConfigLoaded;

        public ReportHtmlService(
            IDbContextFactory<AppDbContext> contextFactory,
            ISettingsService settingsService,
            IAppPathProvider pathProvider)
            : this(contextFactory, settingsService, pathProvider, new BusinessDataAccessScope(new DatabaseConnectionSettings()))
        {
        }

        public ReportHtmlService(
            IDbContextFactory<AppDbContext> contextFactory,
            ISettingsService settingsService,
            IAppPathProvider pathProvider,
            BusinessDataAccessScope accessScope)
        {
            ArgumentNullException.ThrowIfNull(contextFactory);
            ArgumentNullException.ThrowIfNull(settingsService);
            _entityLoader = new ReportEntityLoader(contextFactory);
            _contextFactory = contextFactory;
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver);
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

                if (!result.Any(r => r.TemplatePath.Equals(templatePath, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new ReportTemplateDescriptor
                    {
                        ReportType = reportType,
                        DisplayName = displayName,
                        TemplatePath = templatePath,
                        WithSealDefault = cfg.WithSeal ?? true
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
                        WithSealDefault = true
                    });
                }
            }

            return result;
        }

        public async Task<ReportHtmlRenderResult> RenderInvoiceReportAsync(
            int invoiceId,
            ReportDocumentType reportType,
            string templatePath = null,
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
                throw new KeyNotFoundException($"未找到发票：{invoiceId}");
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
            string templatePath = null,
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
            string templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default)
        {
            if (paymentId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(paymentId), "付款ID必须大于0。");
            }

            var payment = await _entityLoader.LoadPaymentAsync(paymentId, cancellationToken).ConfigureAwait(false);
            if (payment == null)
            {
                throw new KeyNotFoundException($"未找到付款单：{paymentId}");
            }

            var (resolvedTemplatePath, templateContent) = await LoadTemplateAsync(
                    ReportDocumentType.PaymentVoucher,
                    templatePath,
                    cancellationToken)
                .ConfigureAwait(false);
            string html = await GeneratePaymentVoucherHtmlAsync(payment, templateContent, withSeal, cancellationToken).ConfigureAwait(false);

            return new ReportHtmlRenderResult
            {
                ReportType = ReportDocumentType.PaymentVoucher,
                SourceId = paymentId,
                TemplatePath = resolvedTemplatePath,
                WithSeal = withSeal,
                Html = html
            };
        }

        public async Task<ReportHtmlRenderResult> RenderPaymentVoucherDraftAsync(
            Payment payment,
            string templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(payment);

            var (resolvedTemplatePath, templateContent) = await LoadTemplateAsync(
                    ReportDocumentType.PaymentVoucher,
                    templatePath,
                    cancellationToken)
                .ConfigureAwait(false);
            string html = await GeneratePaymentVoucherHtmlAsync(payment, templateContent, withSeal, cancellationToken).ConfigureAwait(false);

            return new ReportHtmlRenderResult
            {
                ReportType = ReportDocumentType.PaymentVoucher,
                SourceId = payment.Id,
                TemplatePath = resolvedTemplatePath,
                WithSeal = withSeal,
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
                templateContent = ScribanReportTemplateRenderer.PreprocessHtmlTemplate(templateContent);

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

                var globals = ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(invoice, customer, exporter, withSeal);
                return ScribanReportTemplateRenderer.Render(templateContent, globals);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "生成报表失败, 类型 {ReportType}, 发票 {InvoiceId}", reportType, invoice?.Id);
                throw new InvalidOperationException($"Failed to generate report: {ex.Message}", ex);
            }
        }

        private async Task<string> GeneratePaymentVoucherHtmlAsync(
            Payment payment,
            string templateContent,
            bool withSeal,
            CancellationToken cancellationToken)
        {
            if (payment == null)
            {
                throw new ArgumentNullException(nameof(payment));
            }

            try
            {
                var (exporter, payee) = await _entityLoader
                    .LoadPaymentVoucherEntitiesAsync(payment, cancellationToken)
                    .ConfigureAwait(false);

                var globals = ReportTemplateGlobalsBuilder.BuildPaymentVoucherGlobals(exporter, payment, payee, withSeal);
                return ScribanReportTemplateRenderer.Render(templateContent, globals);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "生成付款单失败, 付款单 {PaymentId}", payment?.Id);
                throw new InvalidOperationException($"Failed to generate payment voucher: {ex.Message}", ex);
            }
        }

        private async Task<string> ResolveTemplatePathAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken)
        {
            var resolvedTemplatePath = string.IsNullOrWhiteSpace(templatePath)
                ? string.Empty
                : _pathResolver.ToAbsolutePath(templatePath);

            if (string.IsNullOrWhiteSpace(resolvedTemplatePath))
            {
                resolvedTemplatePath = await GetTemplatePathAsync(reportType, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(resolvedTemplatePath) || !File.Exists(resolvedTemplatePath))
            {
                throw new FileNotFoundException($"Template not found: {resolvedTemplatePath}");
            }

            return resolvedTemplatePath;
        }

        private async Task<(string Path, string Content)> LoadTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
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
                    throw new FileNotFoundException("用户报表模板不存在、已停用或无权访问。", templatePath);
                }

                return (templatePath, template.ContentHtml ?? string.Empty);
            }

            string resolvedPath = await ResolveTemplatePathAsync(reportType, templatePath, cancellationToken).ConfigureAwait(false);
            string content = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            return (resolvedPath, content);
        }

        private static bool TryParseUserTemplateId(string templatePath, out int id)
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
                catch (Exception ex)
                {
                    Log.Error(ex, "加载报表模板配置失败");
                    lock (_configLock)
                    {
                        _templatePathCache = new Dictionary<ReportDocumentType, string>();
                        _templateConfigs = new List<ReportTemplateConfig>();
                        _templateConfigLoaded = true;
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

            string configuredPath = null;
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
            string basePath = _pathResolver.EnsureTemplateDirectory(subFolder);

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
