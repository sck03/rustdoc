using System.Text;
using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportTemplateService : IReportTemplateService
    {
        private const string StoragePolicy =
            "内置模板从程序根 Templates/ 只读加载；新建、编辑副本、重命名、删除和模板包导入统一写入运行数据根 Templates/。不会改写已安装程序资源，也不会使用系统用户配置目录或系统级共享数据目录。";

        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplateCatalogLoader _catalogLoader;
        private readonly ISettingsService _settingsService;
        private readonly IBusinessClock _clock;
        private readonly ILogger<ReportTemplateService> _logger;

        public ReportTemplateService(
            IAppPathProvider pathProvider,
            ISettingsService settingsService,
            IBusinessClock? clock = null,
            ILogger<ReportTemplateService>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(settingsService);
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _logger = logger ?? NullLogger<ReportTemplateService>.Instance;
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver, _logger);
            _settingsService = settingsService;
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<ReportTemplateContentResult> CreateTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            string? displayName = null,
            CancellationToken cancellationToken = default)
        {
            string resolvedPath = ResolveTemplateLifecycleTargetPath(
                reportType,
                templatePath,
                BuildDefaultTemplateFileName(reportType));

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

            await SyncTemplateReferencesAsync(reportType, string.Empty, resolvedPath, cancellationToken).ConfigureAwait(false);
            return ToContentResult(CreateResolvedTemplate(reportType, resolvedPath, title), content);
        }

        public async Task<ReportTemplateContentResult> GetTemplateContentAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            var resolved = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                .ConfigureAwait(false);
            string content = await File.ReadAllTextAsync(resolved.TemplatePath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return ToContentResult(resolved, content);
        }

        public async Task<ReportTemplateContentResult> SaveTemplateContentAsync(
            ReportDocumentType reportType,
            string templatePath,
            string content,
            CancellationToken cancellationToken = default)
        {
            ReportTemplateContentPolicy.Validate(reportType, content ?? string.Empty);
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

            await SyncTemplateReferencesAsync(
                    reportType,
                    previousPath,
                    resolved.TemplatePath,
                    cancellationToken)
                .ConfigureAwait(false);

            return ToContentResult(resolved, content ?? string.Empty);
        }

        public async Task<ReportTemplateContentResult> RenameTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            string newTemplatePath,
            CancellationToken cancellationToken = default)
        {
            var current = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                .ConfigureAwait(false);
            EnsureTemplateLifecyclePath(current.TemplatePath);

            string resolvedNewPath = ResolveTemplateLifecycleTargetPath(
                reportType,
                newTemplatePath,
                Path.GetFileName(current.TemplatePath));
            if (string.Equals(current.TemplatePath, resolvedNewPath, PathBoundaryHelper.PathComparison))
            {
                string unchangedContent = await File.ReadAllTextAsync(current.TemplatePath, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                return ToContentResult(current, unchangedContent);
            }

            if (File.Exists(resolvedNewPath))
            {
                throw new ResourceConflictException("目标模板已存在。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(resolvedNewPath)!);
            File.Move(current.TemplatePath, resolvedNewPath, overwrite: false);
            await SyncTemplateReferencesAsync(reportType, current.TemplatePath, resolvedNewPath, cancellationToken).ConfigureAwait(false);

            string content = await File.ReadAllTextAsync(resolvedNewPath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return ToContentResult(CreateResolvedTemplate(reportType, resolvedNewPath), content);
        }

        public async Task<ReportTemplateCommandResult> DeleteTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            var current = await ResolveEditableTemplateAsync(reportType, templatePath, mustExist: true, cancellationToken)
                .ConfigureAwait(false);
            EnsureTemplateLifecyclePath(current.TemplatePath);

            File.Delete(current.TemplatePath);
            await SyncTemplateReferencesAsync(reportType, current.TemplatePath, string.Empty, cancellationToken).ConfigureAwait(false);

            return new ReportTemplateCommandResult
            {
                ReportType = reportType,
                TemplatePath = current.TemplatePath,
                StoragePolicy = StoragePolicy,
                Message = "模板已删除。"
            };
        }

        public Task<ReportTemplatePreviewResult> PreviewTemplateContentAsync(
            ReportDocumentType reportType,
            string content,
            bool withSeal = true,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportTemplateContentPolicy.Validate(reportType, content ?? string.Empty);
            string templateContent = ScribanReportTemplateRenderer.PreprocessHtmlTemplate(
                content ?? string.Empty,
                _logger);
            bool effectiveWithSeal = reportType != ReportDocumentType.PaymentVoucher && withSeal;
            string html = reportType == ReportDocumentType.PaymentVoucher
                ? RenderPaymentVoucherPreview(templateContent)
                : RenderInvoicePreview(templateContent, effectiveWithSeal);

            return Task.FromResult(new ReportTemplatePreviewResult
            {
                ReportType = reportType,
                WithSeal = reportType == ReportDocumentType.PaymentVoucher ? null : effectiveWithSeal,
                Html = html
            });
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

            candidatePath = EnsureHtmlExtension(candidatePath);
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
            if (!string.Equals(Path.GetExtension(resolvedPath), ".html", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("报表模板必须是 .html 文件。", nameof(templatePath));
            }

            var configs = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);
            var matched = configs.FirstOrDefault(config =>
                string.Equals(Path.GetFullPath(config.FileName), resolvedPath, PathBoundaryHelper.PathComparison));

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

        private async Task SyncTemplateReferencesAsync(
            ReportDocumentType reportType,
            string previousTemplatePath,
            string currentTemplatePath,
            CancellationToken cancellationToken)
        {
            await _settingsService.LoadAsync().ConfigureAwait(false);

            string normalizedPreviousPath = _catalogLoader.NormalizeStoredTemplatePath(previousTemplatePath);
            string normalizedPreviousAbsolutePath = _catalogLoader.NormalizeAbsoluteTemplatePath(previousTemplatePath);
            string normalizedCurrentPath = _catalogLoader.NormalizeStoredTemplatePath(currentTemplatePath);
            bool settingsChanged = false;

            if (reportType == ReportDocumentType.PaymentVoucher)
            {
                if (_settingsService.Settings.PaymentTemplates != null)
                {
                    settingsChanged |= UpdateTemplateReferences(
                        _settingsService.Settings.PaymentTemplates,
                        normalizedPreviousPath,
                        normalizedPreviousAbsolutePath,
                        normalizedCurrentPath);
                }
            }
            else if (_settingsService.Settings.BatchExport?.Items != null)
            {
                settingsChanged |= UpdateTemplateReferences(
                    _settingsService.Settings.BatchExport.Items,
                    normalizedPreviousPath,
                    normalizedPreviousAbsolutePath,
                    normalizedCurrentPath);
            }

            if (settingsChanged)
            {
                await _settingsService.SaveAsync().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await RefreshTemplateCatalogAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RefreshTemplateCatalogAsync(CancellationToken cancellationToken)
        {
            string configPath = _pathResolver.GetUserConfigPath();
            var configs = await _catalogLoader.LoadResolvedConfigsAsync(cancellationToken).ConfigureAwait(false);
            var rows = configs
                .Where(config =>
                    config != null &&
                    !string.IsNullOrWhiteSpace(config.FileName) &&
                    _pathResolver.IsUserTemplatePath(config.FileName))
                .Select(config => new ReportTemplateConfig
                {
                    Type = ReportTemplateCatalogLoader.NormalizeTemplateCatalogType(config.Type, config.FileName),
                    FileName = _pathResolver.ToStoredPath(config.FileName),
                    Name = ReportTemplateCatalogLoader.NormalizeTemplateDisplayName(config.Name, config.FileName),
                    WithSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(config.Type, config.FileName) == ReportDocumentType.PaymentVoucher
                        ? null
                        : config.WithSeal ?? true
                })
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
                string normalizedItemPath = _catalogLoader.NormalizeStoredTemplatePath(item.TemplatePath);
                string normalizedItemAbsolutePath = _catalogLoader.NormalizeAbsoluteTemplatePath(item.TemplatePath);
                bool matchesPreviousPath =
                    (!string.IsNullOrWhiteSpace(previousTemplatePath) &&
                     string.Equals(normalizedItemPath, previousTemplatePath, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(previousAbsoluteTemplatePath) &&
                     string.Equals(normalizedItemAbsolutePath, previousAbsoluteTemplatePath, StringComparison.OrdinalIgnoreCase));

                if (!matchesPreviousPath)
                {
                    continue;
                }

                string nextPath = currentTemplatePath ?? string.Empty;
                if (string.Equals(item.TemplatePath, nextPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.TemplatePath = nextPath;
                changed = true;
            }

            return changed;
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

        private static string EnsureHtmlExtension(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                throw new ArgumentException("模板路径不能为空。", nameof(templatePath));
            }

            string extension = Path.GetExtension(templatePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return Path.ChangeExtension(templatePath, ".html");
            }

            if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("报表模板必须是 .html 文件。", nameof(templatePath));
            }

            return Path.GetFullPath(templatePath);
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
            return $"{prefix}_{_clock.Now:yyyyMMddHHmmss}.html";
        }

        private string RenderInvoicePreview(string templateContent, bool withSeal)
        {
            var invoice = BuildSampleInvoice();
            var customer = new Customer
            {
                CustomerNameEN = "SAMPLE CUSTOMER LTD.",
                AddressEN = "88 Sample Road, Hamburg, Germany",
                ContactPerson = "M. Buyer",
                Email = "buyer@example.com",
                Phone = "+49 40 0000 0000"
            };
            var exporter = BuildSampleExporter();
            var globals = ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(
                invoice,
                customer,
                exporter,
                withSeal,
                logger: _logger);
            return ScribanReportTemplateRenderer.Render(templateContent, globals);
        }

        private static string RenderPaymentVoucherPreview(string templateContent)
        {
            var sampleDate = new DateOnly(2026, 6, 15);
            var payee = new Payee
            {
                Name = "Sample Payee",
                BankName = "Sample Bank",
                RMBAccount = "6222 0000 1111 2222",
                Notes = "Sample beneficiary"
            };
            var payment = new Payment
            {
                Id = 1001,
                InvoiceNo = "PREVIEW-INTERNAL-001",
                PaymentDate = sampleDate,
                ShipmentDate = sampleDate.AddDays(-3),
                PayerName = "示例付款单位",
                PayeeName = payee.Name,
                PaymentMethod = "Bank Transfer",
                USDAmount = 100m,
                CNYAmount = 720m,
                Notes = "Sample payment voucher.",
                BankName = payee.BankName,
                AccountNo = payee.RMBAccount
            };

            var globals = ReportTemplateGlobalsBuilder.BuildPaymentVoucherGlobals(payment, payee);
            return ScribanReportTemplateRenderer.Render(templateContent, globals);
        }

        private static Invoice BuildSampleInvoice()
        {
            var sampleDate = new DateOnly(2026, 6, 15);
            var invoice = new Invoice
            {
                InvoiceNo = "PREVIEW-EXPORT-001",
                ContractNo = "CN-2026-001",
                InvoiceDate = sampleDate,
                ShipmentDate = sampleDate.AddDays(10),
                CustomerNameEN = "SAMPLE CUSTOMER LTD.",
                ExporterNameEN = "SAMPLE EXPORTER CO., LTD.",
                PortOfLoading = "NINGBO",
                PortOfDestination = "HAMBURG",
                DestinationCountry = "GERMANY",
                Currency = "USD",
                PaymentTerms = "T/T",
                TradeTerms = "FOB"
            };
            invoice.Items =
            [
                new Item
                {
                    StyleNo = "SKU-001",
                    StyleName = "Sample Jacket",
                    StyleNameCN = "样例夹克",
                    Quantity = 120,
                    UnitEN = "PCS",
                    UnitCN = "件",
                    Cartons = 10,
                    CtnUnitEN = "CTNS",
                    UnitPrice = 12.5m,
                    TotalPrice = 1500m,
                    GWPerCtn = 18m,
                    NWPerCtn = 16m,
                    GWTotal = 180m,
                    NWTotal = 160m,
                    Volume = 2.4m
                }
            ];
            invoice.CalculateTotals();
            return invoice;
        }

        private static Exporter BuildSampleExporter()
        {
            return new Exporter
            {
                ExporterNameEN = "SAMPLE EXPORTER CO., LTD.",
                ExporterNameCN = "样例出口公司",
                AddressEN = "99 Export Avenue, Ningbo, China",
                AddressCN = "宁波市样例出口路 99 号",
                ContactPerson = "Export Team",
                Phone = "+86 574 0000 0000",
                BankName = "Sample Bank Ningbo Branch",
                BankAccount = "1234567890",
                SwiftCode = "SAMPLECNXXX"
            };
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
