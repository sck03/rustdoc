using ExportDocManager.DataAccess;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Dashboard;
using ExportDocManager.Services.EmailTemplates;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Opportunities;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Suppliers;
using ExportDocManager.Services.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiServiceCollectionExtensions
    {
        public static IServiceCollection AddExportDocManagerApiServices(
            this IServiceCollection services,
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            ApiRuntimeOptions runtimeOptions = null)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            runtimeOptions ??= new ApiRuntimeOptions();

            services.AddSingleton(pathProvider);
            services.AddSingleton(databaseSettings);
            services.AddSingleton(runtimeOptions);
            services.AddSingleton(ApiDesktopAccessOptions.FromRuntimeOptions(runtimeOptions));
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddCors(options =>
            {
                options.AddPolicy(
                    ApiCorsPolicy.LocalFrontendPolicyName,
                    policy => policy
                        .SetIsOriginAllowed(origin => ApiCorsPolicy.IsAllowedOrigin(origin, runtimeOptions))
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            });
            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                services.AddSingleton<IApiSessionTokenService, DatabaseApiSessionTokenService>();
            }
            else
            {
                services.AddSingleton<IApiSessionTokenService, InMemoryApiSessionTokenService>();
            }
            services.AddSingleton<ApiCurrentUserResolver>();
            services.AddSingleton<ApiAuthorizationService>();
            services.AddSingleton(_ => ApiBackgroundJobConcurrencyOptions.FromEnvironment());
            services.AddSingleton<ApiBackgroundJobService>();
            services.AddSingleton<ApiBackgroundJobRunner>();
            services.AddSingleton<ApiBackgroundJobRetryDispatcher>();
            services.AddHostedService<SqliteSingleInstanceHostedService>();
            services.AddHostedService<PostgreSqlAutomaticBackupHostedService>();
            services.AddSingleton<IBackgroundJobService>(provider =>
                provider.GetRequiredService<ApiBackgroundJobService>());
            services.AddSingleton<ICurrentUserContext, ApiCurrentUserContext>();
            services.AddSingleton<IAuditUserProvider, ApiAuditUserProvider>();
            services.AddSingleton<AuditInterceptor>();
            services.AddSingleton<ISettingsService>(_ => new SettingsService(pathProvider));
            services.AddSingleton<IRuntimeDependencyDiagnosticsService, RuntimeDependencyDiagnosticsService>();
            services.TryAddSingleton<IRuntimeLicenseAnchorStore>(_ =>
                RuntimeLicenseAnchorStoreFactory.CreateDefault(pathProvider));
            services.TryAddSingleton<ILicenseSignatureVerifier, EcdsaLicenseSignatureVerifier>();
            services.AddSingleton<ILicenseService, RuntimeLicenseService>();
            services.AddScoped<IBackupService>(_ => new BackupService(databaseSettings, pathProvider));
            services.AddScoped<ICloudSyncService, WebDavCloudSyncService>();
            services.AddScoped<ISharedDatabaseMaintenanceService, SharedDatabaseMaintenanceService>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IShutdownMaintenanceService, ShutdownMaintenanceService>();
            services.AddScoped<ISystemLogCleanupService, SystemLogCleanupService>();
            services.AddHttpClient("ExchangeRates");
            services.AddHttpClient("AI");
            services.AddSingleton<IExchangeRateService>(provider =>
                new BocExchangeRateService(
                    provider.GetRequiredService<ISettingsService>(),
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("ExchangeRates")));
            services.AddScoped<IDatabaseInitializationService, DatabaseInitializationService>();
            services.AddSingleton<DatabaseInitializationCoordinator>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IPermissionTemplateService, PermissionTemplateService>();
            services.AddScoped<BusinessDataAccessScope>();
            services.AddScoped<IItemService, ItemService>();
            services.AddScoped<IInvoicePartyResolver, InvoicePartyResolver>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.AddScoped<IInvoiceTransferService, InvoiceTransferService>();
            services.AddScoped<IShippingMarkImageService, ShippingMarkImageService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IPaymentService, PaymentService>();
            services.AddScoped<ICustomOptionService, CustomOptionService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<ICrmService, CrmService>();
            services.AddScoped<ICrmCustomerImportService, CrmCustomerImportService>();
            services.AddScoped<ICrmCustomerExportService, CrmCustomerExportService>();
            services.AddScoped<ISupplierDirectoryService, SupplierDirectoryService>();
            services.AddScoped<ISupplierAssessmentService, SupplierAssessmentService>();
            services.AddScoped<ISupplierFileService, SupplierFileService>();
            services.AddScoped<IEmailTemplateService, EmailTemplateService>();
            services.AddScoped<ISalesOpportunityService, SalesOpportunityService>();
            services.AddScoped<IExporterService, ExporterService>();
            services.AddScoped<IPayeeService, PayeeService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IAuxiliaryService, AuxiliaryService>();
            services.AddSingleton<BrowserRuntimeManager>();
            services.AddSingleton<BrowserExecutableResolver>();
            services.AddSingleton<ManagedPlaywrightBrowserHost>();
            services.AddSingleton<IHsCodeRemoteProvider, I5a6HsCodeProvider>();
            services.AddScoped<IHsCodeKnowledgeService, HsCodeKnowledgeService>();
            services.AddScoped<IHsCodeService, HsCodeService>();
            services.AddScoped<IPdfMergeService, PdfMergeService>();
            services.AddScoped<IEmailService, SmtpEmailService>();
            services.AddScoped<BuiltInExcelImportAnalyzer>();
            services.AddScoped<IExcelImportAnalyzer, HybridExcelImportAnalyzer>();
            services.AddScoped<IExcelImportService, ExcelImportService>();
            services.AddScoped<IExcelImportTemplateService, ExcelImportTemplateService>();
            services.AddScoped<IContainerLoadingService, ContainerLoadingService>();
            services.AddSingleton<IContainerPackingEngine, ContainerPackingEngine>();
            services.AddScoped<IOcrService, UnsupportedOcrService>();
            services.AddSingleton<RustOcrSidecarHost>();
            if (File.Exists(RustOcrSidecarHost.FindExecutable(pathProvider)))
            {
                services.AddScoped<IOcrService, RustOcrService>();
            }
            else if (ApiOcrRuntimeOptions.ShouldUsePaddleOcr(pathProvider))
            {
                services.AddScoped<IOcrService, PaddleOcrService>();
            }

            services.AddScoped<IAIService>(provider =>
                new OpenAiCompatibleService(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("AI"),
                    provider.GetRequiredService<ISettingsService>()));
            services.AddScoped<ILetterOfCreditComplianceReviewService, LetterOfCreditComplianceReviewService>();
            services.AddScoped<ILetterOfCreditDocumentService, LetterOfCreditDocumentService>();
            services.AddScoped<IReportHtmlService, ReportHtmlService>();
            services.AddSingleton<IInvoiceProfitAnalysisService, InvoiceProfitAnalysisService>();
            services.AddScoped<IReportTemplateService, ReportTemplateService>();
            services.AddScoped<IUserReportTemplateService, UserReportTemplateService>();
            services.AddScoped<IReportTemplateStorageDiagnosticsService, ReportTemplateStorageDiagnosticsService>();
            services.AddScoped<IReportTemplatePackageService, ReportTemplatePackageService>();
            services.AddSingleton<IReportTemplateFieldCatalogService, ReportTemplateFieldCatalogService>();
            services.AddScoped<IHtmlToPdfService, ChromiumHtmlToPdfService>();
            services.AddScoped<IReportPdfRenderService, ReportPdfRenderService>();
            services.AddScoped<SingleWindowTrackingService>();
            services.AddScoped<ISingleWindowTrackingService>(provider =>
                provider.GetRequiredService<SingleWindowTrackingService>());
            services.AddScoped<ISingleWindowOperationCenterService>(provider =>
                provider.GetRequiredService<SingleWindowTrackingService>());
            services.AddScoped<ISingleWindowWorkstationRegistryService, SingleWindowWorkstationRegistryService>();
            services.AddScoped<ISingleWindowCollaborationDataSource, LocalSingleWindowCollaborationDataSource>();
            services.AddScoped<ICustomsCooSourceAssembler, CustomsCooSourceAssembler>();
            services.AddScoped<IAgentConsignmentSourceAssembler, AgentConsignmentSourceAssembler>();
            services.AddScoped<ICustomsCooFieldMapper, CustomsCooFieldMapper>();
            services.AddScoped<IAgentConsignmentFieldMapper, AgentConsignmentFieldMapper>();
            services.AddScoped<ISingleWindowXmlValidator, SingleWindowXmlValidator>();
            services.AddScoped<ICustomsCooPayloadGenerator, CustomsCooXmlPayloadGenerator>();
            services.AddScoped<IAgentConsignmentPayloadGenerator, AgentConsignmentXmlPayloadGenerator>();
            services.AddScoped<ISingleWindowReceiptParser, SingleWindowReceiptParser>();
            services.AddScoped<ManualImportClientBridge>();
            services.AddScoped<ISingleWindowClientProfileService>(provider =>
                provider.GetRequiredService<ManualImportClientBridge>());
            services.AddScoped<ISingleWindowClientBridge>(provider =>
                provider.GetRequiredService<ManualImportClientBridge>());
            services.AddScoped<ICustomsCooProducerProfileService, CustomsCooProducerProfileService>();
            services.AddScoped<SingleWindowDocumentPersistenceService>();
            services.AddScoped<ISingleWindowDocumentPersistenceService>(provider =>
                provider.GetRequiredService<SingleWindowDocumentPersistenceService>());
            services.AddScoped<ICustomsCooDocumentService>(provider =>
                provider.GetRequiredService<SingleWindowDocumentPersistenceService>());
            services.AddScoped<IAgentConsignmentDocumentService>(provider =>
                provider.GetRequiredService<SingleWindowDocumentPersistenceService>());
            services.AddScoped<ISingleWindowExportReviewService, SingleWindowExportReviewService>();
            services.AddScoped<ISingleWindowHandoffPackageService, SingleWindowHandoffPackageService>();
            services.AddScoped<ISingleWindowReferenceCatalogExcelImportService, SingleWindowReferenceCatalogExcelImportService>();
            services.AddSingleton<ISingleWindowReferenceCatalogService>(_ =>
                new SingleWindowReferenceCatalogService(pathProvider));
            ConfigureSingleWindowReferenceCatalogLoaders(pathProvider);
            services.AddMasterDataReadRepositories();
            services.AddSharedReadRepositories();
            services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
            {
                DbHelper.ConfigureDbContextOptions(options, databaseSettings);
                options.AddInterceptors(serviceProvider.GetRequiredService<AuditInterceptor>());
            });

            return services;
        }

        private static void ConfigureSingleWindowReferenceCatalogLoaders(IAppPathProvider pathProvider)
        {
            CustomsCooIssuingAuthorityCatalog.ConfigureEntrySnapshotLoader(
                () => CustomsCooIssuingAuthorityFileLoader.LoadEntriesFromFiles(
                    Path.Combine(pathProvider.ResourceRoot, "SingleWindow", "customs_coo_issuing_authorities.json"),
                    Path.Combine(pathProvider.ResourceRoot, "SingleWindow", "customs_coo_issuing_authorities.address_overrides.json"),
                    Path.Combine(pathProvider.SingleWindowRoot, "customs_coo_issuing_authorities.override.json")));
            SingleWindowFieldMapperHelpers.ConfigureReferenceCatalogSnapshotLoader(
                () => SingleWindowReferenceCatalogService.LoadEffectiveCatalogSnapshot(pathProvider));
            SingleWindowReferenceCatalogs.ConfigureReferenceCatalogSnapshotLoader(
                () => SingleWindowReferenceCatalogService.LoadEffectiveCatalogSnapshot(pathProvider));
            SingleWindowReferenceCatalogService.ReferenceCatalogChanged -= SingleWindowFieldMapperHelpers.ReloadReferenceCatalog;
            SingleWindowReferenceCatalogService.ReferenceCatalogChanged += SingleWindowFieldMapperHelpers.ReloadReferenceCatalog;
            SingleWindowReferenceCatalogService.ReferenceCatalogChanged -= SingleWindowReferenceCatalogs.Reload;
            SingleWindowReferenceCatalogService.ReferenceCatalogChanged += SingleWindowReferenceCatalogs.Reload;
        }

        private static IServiceCollection AddMasterDataReadRepositories(this IServiceCollection services)
        {
            services.AddScoped<LocalMasterDataReadRepository>();
            services.AddScoped<ICustomerReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());
            services.AddScoped<IExporterReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());
            services.AddScoped<IPayeeReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());
            services.AddScoped<IProductReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());
            services.AddScoped<IPortReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());
            services.AddScoped<IUnitReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());
            services.AddScoped<IHsCodeReadRepository>(provider =>
                provider.GetRequiredService<LocalMasterDataReadRepository>());

            return services;
        }

        private static IServiceCollection AddSharedReadRepositories(this IServiceCollection services)
        {
            services.AddScoped<LocalSharedReadRepository>();
            services.AddScoped<IInvoiceListReadRepository>(provider =>
                provider.GetRequiredService<LocalSharedReadRepository>());
            services.AddScoped<IPaymentReadRepository>(provider =>
                provider.GetRequiredService<LocalSharedReadRepository>());
            services.AddScoped<IPaymentDetailReadRepository>(provider =>
                provider.GetRequiredService<LocalSharedReadRepository>());
            services.AddScoped<IQueryReadRepository>(provider =>
                provider.GetRequiredService<LocalSharedReadRepository>());
            services.AddScoped<IAuditLogReadRepository>(provider =>
                provider.GetRequiredService<LocalSharedReadRepository>());
            services.AddScoped<IQueryResultExportService, QueryResultExportService>();

            return services;
        }
    }
}
