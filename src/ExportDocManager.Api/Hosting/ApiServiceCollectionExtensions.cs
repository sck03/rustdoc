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
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Suppliers;
using ExportDocManager.Services.Tools;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json.Serialization;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiServiceCollectionExtensions
    {
        public static IServiceCollection AddExportDocManagerApiServices(
            this IServiceCollection services,
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            ApiRuntimeOptions? runtimeOptions = null)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            runtimeOptions ??= new ApiRuntimeOptions();

            // A scheduled SQLite restore must be applied before any DbContext,
            // hosted service, or request can open the database.
            SqlitePendingRestoreManager.ApplyPendingRestore(pathProvider, databaseSettings);

            services.AddSingleton(pathProvider);
            services.AddSingleton(databaseSettings);
            services.AddSingleton(runtimeOptions);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IBusinessClock>(provider =>
                new BusinessClock(
                    provider.GetRequiredService<TimeProvider>(),
                    runtimeOptions.BusinessTimeZoneId));
            services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.RespectNullableAnnotations = true;
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            services.AddOpenApi("v1", options =>
            {
                options.AddSchemaTransformer<NullableOpenApiSchemaTransformer>();
                options.AddDocumentTransformer<ApiOpenApiDocumentTransformer>();
                options.AddOperationTransformer<ApiOpenApiDocumentTransformer>();
            });
            services.AddSingleton(ApiDesktopAccessOptions.FromRuntimeOptions(runtimeOptions));
            services.AddLogging();
            services.AddExportDocManagerResourceGovernance();
            services.AddExportDocManagerObservability();
            services.AddSingleton<ApiSecurityAuditWriter>();
            services.AddHttpContextAccessor();
            services.AddSingleton<ApiBackgroundJobExecutionUserAccessor>();
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
            services.AddSingleton<ApiLoginAttemptService>();
            services.AddSingleton<ApiSensitiveOperationTicketService>();
            services.AddSingleton<ApiDownloadTicketService>();
            services.AddSingleton(_ => ApiBackgroundJobConcurrencyOptions.FromEnvironment());
            services.AddSingleton(_ => ApiBackgroundJobRetentionOptions.FromEnvironment());
            services.AddSingleton<ApiBackgroundJobService>();
            services.AddSingleton<ApiBackgroundJobRunner>();
            services.AddHostedService<SqliteSingleInstanceHostedService>();
            services.AddHostedService<PostgreSqlSingleInstanceHostedService>();
            services.AddHostedService(provider => provider.GetRequiredService<ApiBackgroundJobRunner>());
            services.AddSingleton<ApiBackgroundJobRetryDispatcher>();
            services.AddHostedService<PostgreSqlAutomaticBackupHostedService>();
            services.AddSingleton<IBackgroundJobService>(provider =>
                provider.GetRequiredService<ApiBackgroundJobService>());
            services.AddSingleton<ICurrentUserContext, ApiCurrentUserContext>();
            services.AddSingleton<IAuditUserProvider, ApiAuditUserProvider>();
            services.AddSingleton<AuditInterceptor>();
            services.AddSingleton<ISettingsService>(_ => new SettingsService(pathProvider));
            services.AddSingleton<IRuntimeDependencyDiagnosticsService, RuntimeDependencyDiagnosticsService>();
            services.AddSingleton<IApiReadinessProbe, ApiReadinessProbe>();
            services.TryAddSingleton<IRuntimeLicenseAnchorStore>(_ =>
                RuntimeLicenseAnchorStoreFactory.CreateDefault(pathProvider));
            services.TryAddSingleton<ILicenseSignatureVerifier, EcdsaLicenseSignatureVerifier>();
            services.AddSingleton<ILicenseService, RuntimeLicenseService>();
            services.AddScoped<IBackupService>(provider => new BackupService(
                databaseSettings,
                pathProvider,
                clock: provider.GetRequiredService<IBusinessClock>(),
                logger: provider.GetRequiredService<ILogger<BackupService>>()));
            services.AddScoped<ICloudSyncService, WebDavCloudSyncService>();
            services.AddScoped<ISharedDatabaseMaintenanceService, SharedDatabaseMaintenanceService>();
            services.AddScoped<IServerMigrationService, ServerMigrationService>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IShutdownMaintenanceService, ShutdownMaintenanceService>();
            services.AddScoped<ISystemLogCleanupService, SystemLogCleanupService>();
            services.AddHttpClient("ExchangeRates", client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                    ConnectCallback = ExchangeRateEndpointPolicy.ConnectPublicHostAsync,
                    UseProxy = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                });
            services.AddHttpClient("AI");
            services.AddSingleton<IExchangeRateService>(provider =>
                new BocExchangeRateService(
                    provider.GetRequiredService<ISettingsService>(),
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("ExchangeRates"),
                    clock: provider.GetRequiredService<IBusinessClock>()));
            services.AddSingleton<DatabaseInitializationCoordinator>();
            services.AddScoped<IDatabaseInitializationService>(provider =>
                new DatabaseInitializationService(
                    provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                    databaseSettings,
                    provider.GetRequiredService<DatabaseInitializationCoordinator>(),
                    runtimeOptions.NetworkMode && DatabaseModeHelper.UsesPostgreSql(databaseSettings),
                    runtimeOptions.BootstrapToken,
                    pathProvider,
                    provider.GetRequiredService<ILogger<DatabaseInitializationService>>()));
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IPermissionTemplateService, PermissionTemplateService>();
            services.AddScoped<BusinessDataAccessScope>();
            services.AddScoped<IItemService, ItemService>();
            services.AddScoped<IInvoicePartyResolver, InvoicePartyResolver>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.AddScoped<IInvoiceDataMaintenanceService, InvoiceDataMaintenanceService>();
            services.AddScoped<IInvoiceTransferService, InvoiceTransferService>();
            services.AddScoped<IShippingMarkImageService, ShippingMarkImageService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IPaymentService, PaymentService>();
            services.AddScoped<ICustomOptionService, CustomOptionService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<ICrmService, CrmService>();
            services.AddScoped<ISupplierDirectoryService, SupplierDirectoryService>();
            services.AddScoped<ISupplierAssessmentService, SupplierAssessmentService>();
            services.AddScoped<IEmailTemplateService, EmailTemplateService>();
            services.AddScoped<ISalesOpportunityService, SalesOpportunityService>();
            services.AddScoped<IExporterService, ExporterService>();
            services.AddScoped<IExporterSealService, ExporterSealService>();
            services.AddScoped<IPayeeService, PayeeService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IAuxiliaryService, AuxiliaryService>();
            services.AddScoped<IHsCodeKnowledgeService, HsCodeKnowledgeService>();
            services.AddScoped<IHsCodeService, HsCodeService>();
            services.AddScoped<IHtmlToPdfService, UnsupportedHtmlToPdfService>();
            services.AddScoped<IPdfMergeService, UnsupportedPdfMergeService>();
            services.AddScoped<IOcrService, UnsupportedOcrService>();
            services.AddScoped<ILetterOfCreditDocumentService, UnsupportedLetterOfCreditDocumentService>();
            services.AddScoped<IExcelImportAnalyzer, UnsupportedExcelImportAnalyzer>();
            services.AddScoped<IExcelImportService, UnsupportedExcelImportService>();
            services.AddScoped<IExcelImportTemplateService, UnsupportedExcelImportTemplateService>();
            services.AddScoped<ICrmCustomerImportService, UnsupportedCrmCustomerImportService>();
            services.AddScoped<ICrmCustomerExportService, UnsupportedCrmCustomerExportService>();
            services.AddScoped<ISupplierFileService, UnsupportedSupplierFileService>();
            services.AddScoped<IQueryResultExportService, UnsupportedQueryResultExportService>();
            services.AddScoped<ISingleWindowReferenceCatalogExcelImportService, UnsupportedSingleWindowReferenceCatalogExcelImportService>();
            services.AddScoped<IHsCodeImportService, UnsupportedHsCodeImportService>();
            services.AddSingleton<IAuditLogExcelExporter, UnsupportedAuditLogExcelExporter>();
            services.AddScoped<IEmailService, SmtpEmailService>();
            services.AddScoped<IContainerLoadingService, ContainerLoadingService>();
            services.AddSingleton<IContainerPackingEngine, ContainerPackingEngine>();

            services.AddScoped<IAIService>(provider =>
                new OpenAiCompatibleService(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("AI"),
                    provider.GetRequiredService<ISettingsService>()));
            services.AddScoped<ILetterOfCreditComplianceReviewService, LetterOfCreditComplianceReviewService>();
            services.AddScoped<IReportHtmlService, ReportHtmlService>();
            services.AddSingleton<IInvoiceProfitAnalysisService, InvoiceProfitAnalysisService>();
            services.AddScoped<IReportTemplateService, ReportTemplateService>();
            services.AddScoped<IUserReportTemplateService, UserReportTemplateService>();
            services.AddScoped<IReportTemplateStorageDiagnosticsService, ReportTemplateStorageDiagnosticsService>();
            services.AddScoped<IReportTemplatePackageService, ReportTemplatePackageService>();
            services.AddSingleton<IReportTemplateFieldCatalogService, ReportTemplateFieldCatalogService>();
            services.AddScoped<IReportPdfRenderService, ReportPdfRenderService>();
            services.AddScoped<SingleWindowTrackingService>();
            services.AddScoped<ISingleWindowTrackingService>(provider =>
                provider.GetRequiredService<SingleWindowTrackingService>());
            services.AddScoped<ISingleWindowOperationCenterService>(provider =>
                provider.GetRequiredService<SingleWindowTrackingService>());
            services.AddSingleton<ISingleWindowStationIdentityService, SingleWindowStationIdentityService>();
            services.AddScoped<ISingleWindowDisasterRecoveryService, SingleWindowDisasterRecoveryService>();
            services.AddScoped<ICustomsCooSourceAssembler, CustomsCooSourceAssembler>();
            services.AddScoped<IAgentConsignmentSourceAssembler, AgentConsignmentSourceAssembler>();
            services.AddScoped<ICustomsCooFieldMapper, CustomsCooFieldMapper>();
            services.AddScoped<IAgentConsignmentFieldMapper, AgentConsignmentFieldMapper>();
            services.AddScoped<ISingleWindowXmlValidator, SingleWindowXmlValidator>();
            services.AddScoped<ICustomsCooPayloadGenerator, CustomsCooXmlPayloadGenerator>();
            services.AddScoped<IAgentConsignmentPayloadGenerator, AgentConsignmentXmlPayloadGenerator>();
            services.AddScoped<ISingleWindowReceiptParser, SingleWindowReceiptParser>();
            services.AddScoped<ISingleWindowClientProfileService, SingleWindowClientProfileService>();
            services.AddScoped<ISingleWindowClientBridge, ManualImportClientBridge>();
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
            var singleWindowCatalogStore = new SingleWindowReferenceCatalogSnapshotStore(
                SingleWindowReferenceCatalogService.CreateSnapshot(pathProvider));
            services.AddSingleton(singleWindowCatalogStore);
            services.AddSingleton<ISingleWindowReferenceCatalogSnapshotProvider>(singleWindowCatalogStore);
            services.AddSingleton<ISingleWindowReferenceCatalogService>(_ =>
                new SingleWindowReferenceCatalogService(pathProvider, singleWindowCatalogStore));
            services.AddMasterDataReadRepositories();
            services.AddSharedReadRepositories();
            foreach (var module in ExportDocCapabilityModuleLoader.Load(typeof(ApiServiceCollectionExtensions).Assembly.Location))
            {
                module.RegisterServices(services, pathProvider);
            }
            services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
            {
                DbHelper.ConfigureDbContextOptions(options, databaseSettings, pathProvider);
                options.AddInterceptors(serviceProvider.GetRequiredService<AuditInterceptor>());
            });

            return services;
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
            return services;
        }
    }
}
