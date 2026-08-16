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

            var api = endpoints.MapGroup(string.Empty)
                .WithMetadata(new ApiEndpointAccessMetadata(true, true, true))
                .WithApiResourceProfile(ApiResourceProfile.Interactive);
            api.MapSystemEndpoints(runtimeOptions, databaseSettings);
            api.MapGroup(string.Empty)
                .WithApiResourceProfile(ApiResourceProfile.Authentication)
                .MapLicenseEndpoints();
            api.MapGroup(string.Empty)
                .WithApiResourceProfile(ApiResourceProfile.Authentication)
                .MapAuthEndpoints();
            api.MapUserEndpoints();
            api.MapPermissionTemplateEndpoints();
            api.MapSettingsEndpoints();
            var maintenance = api.MapGroup(string.Empty)
                .WithApiResourceProfile(ApiResourceProfile.Maintenance)
                .WithApiSecurityAudit("database-maintenance");
            maintenance.MapBackupEndpoints();
            maintenance.MapSharedDatabaseMaintenanceEndpoints();
            maintenance.MapServerMigrationEndpoints();
            maintenance.MapPermissionGroup(PermissionModuleCatalog.DocumentInvoices)
                .MapInvoiceDataMaintenanceEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentDashboard).MapDashboardEndpoints();
            var invoices = api.MapPermissionGroup(PermissionModuleCatalog.DocumentInvoices);
            invoices.MapInvoiceEndpoints();
            invoices.MapInvoiceShippingMarkEndpoints();
            invoices.MapInvoiceTransferEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentQuery).MapQueryEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentPayments).MapPaymentEndpoints();
            api.MapGroup(string.Empty)
                .WithApiResourceProfile(ApiResourceProfile.Workload)
                .MapAuditLogEndpoints();
            api.MapGroup(string.Empty)
                .WithApiResourceProfile(ApiResourceProfile.Streaming)
                .MapPermissionGroup(PermissionModuleCatalog.DocumentJobs)
                .MapJobEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.DocumentCustomOptions).MapCustomOptionEndpoints();
            var workloads = api.MapGroup(string.Empty)
                .WithApiResourceProfile(ApiResourceProfile.Workload);
            workloads.MapToolEndpoints();
            workloads.MapReportEndpoints();
            api.MapMasterDataEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesCrm).MapCrmEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesSuppliers).MapSupplierEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesEmailTemplates).MapEmailTemplateEndpoints();
            api.MapPermissionGroup(PermissionModuleCatalog.SalesOpportunities).MapSalesOpportunityEndpoints();
            workloads.MapSingleWindowEndpoints();

            return endpoints;
        }
    }
}
