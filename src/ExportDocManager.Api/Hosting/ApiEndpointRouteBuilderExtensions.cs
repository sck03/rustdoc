using ExportDocManager.DataAccess;
using ExportDocManager.Services.Security;

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
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentInvoices).MapInvoiceDataMaintenanceEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentDashboard).MapDashboardEndpoints();
            var invoices = api.MapPermissionGroup(PermissionModuleCatalog.DocumentInvoices);
            invoices.MapInvoiceEndpoints();
            invoices.MapInvoiceShippingMarkEndpoints();
            invoices.MapInvoiceTransferEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentQuery).MapQueryEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentPayments).MapPaymentEndpoints();
            api.MapAuditLogEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentJobs).MapJobEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentCustomOptions).MapCustomOptionEndpoints();
            api.MapToolEndpoints();
            api.MapReportEndpoints();
            api.MapMasterDataEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesCrm).MapCrmEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesSuppliers).MapSupplierEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesEmailTemplates).MapEmailTemplateEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesOpportunities).MapSalesOpportunityEndpoints();
            api.MapSingleWindowEndpoints();

            return endpoints;
        }
    }
}
