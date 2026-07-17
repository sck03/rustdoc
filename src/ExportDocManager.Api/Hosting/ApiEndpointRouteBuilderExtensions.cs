using ExportDocManager.DataAccess;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapExportDocManagerApiEndpoints(
            this IEndpointRouteBuilder endpoints,
            ApiRuntimeOptions runtimeOptions,
            DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            ArgumentNullException.ThrowIfNull(databaseSettings);

            endpoints.MapSystemEndpoints(runtimeOptions, databaseSettings);
            endpoints.MapLicenseEndpoints();
            endpoints.MapAuthEndpoints();
            endpoints.MapUserEndpoints();
            endpoints.MapPermissionTemplateEndpoints();
            endpoints.MapSettingsEndpoints();
            endpoints.MapBackupEndpoints();
            endpoints.MapSharedDatabaseMaintenanceEndpoints();
            endpoints.MapDashboardEndpoints();
            endpoints.MapInvoiceEndpoints();
            endpoints.MapInvoiceShippingMarkEndpoints();
            endpoints.MapInvoiceTransferEndpoints();
            endpoints.MapQueryEndpoints();
            endpoints.MapPaymentEndpoints();
            endpoints.MapAuditLogEndpoints();
            endpoints.MapJobEndpoints();
            endpoints.MapCustomOptionEndpoints();
            endpoints.MapToolEndpoints();
            endpoints.MapReportEndpoints();
            endpoints.MapMasterDataEndpoints();
            endpoints.MapCrmEndpoints();
            endpoints.MapSupplierEndpoints();
            endpoints.MapEmailTemplateEndpoints();
            endpoints.MapSalesOpportunityEndpoints();
            endpoints.MapSingleWindowEndpoints();

            return endpoints;
        }
    }
}


