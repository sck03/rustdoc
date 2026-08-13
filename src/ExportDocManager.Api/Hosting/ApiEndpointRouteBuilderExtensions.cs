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

            var api = endpoints.MapGroup(string.Empty).WithMetadata(
                new ApiEndpointAccessMetadata(true, true, true));
            api.MapSystemEndpoints(runtimeOptions, databaseSettings);
            api.MapLicenseEndpoints();
            api.MapAuthEndpoints();
            api.MapUserEndpoints();
            api.MapPermissionTemplateEndpoints();
            api.MapSettingsEndpoints();
            api.MapBackupEndpoints();
            api.MapSharedDatabaseMaintenanceEndpoints();
            api.MapServerMigrationEndpoints();
            api.MapInvoiceDataMaintenanceEndpoints();
            api.MapDashboardEndpoints();
            api.MapInvoiceEndpoints();
            api.MapInvoiceShippingMarkEndpoints();
            api.MapInvoiceTransferEndpoints();
            api.MapQueryEndpoints();
            api.MapPaymentEndpoints();
            api.MapAuditLogEndpoints();
            api.MapJobEndpoints();
            api.MapCustomOptionEndpoints();
            api.MapToolEndpoints();
            api.MapReportEndpoints();
            api.MapMasterDataEndpoints();
            api.MapCrmEndpoints();
            api.MapSupplierEndpoints();
            api.MapEmailTemplateEndpoints();
            api.MapSalesOpportunityEndpoints();
            api.MapSingleWindowEndpoints();

            return endpoints;
        }
    }
}
