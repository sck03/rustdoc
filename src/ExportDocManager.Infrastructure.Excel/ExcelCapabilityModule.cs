using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Suppliers;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Infrastructure.Excel;

public sealed class ExcelCapabilityModule : IExportDocCapabilityModule
{
    public string Key => "excel";

    public void RegisterServices(
        IServiceCollection services,
        IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pathProvider);

        services.AddScoped<BuiltInExcelImportAnalyzer>();
        services.AddScoped<IExcelImportAnalyzer, HybridExcelImportAnalyzer>();
        services.AddScoped<IExcelImportService, ExcelImportService>();
        services.AddScoped<IExcelImportTemplateService, ExcelImportTemplateService>();
        services.AddScoped<ICrmCustomerImportService, CrmCustomerImportService>();
        services.AddScoped<ICrmCustomerExportService, CrmCustomerExportService>();
        services.AddScoped<ISupplierFileService, SupplierFileService>();
        services.AddScoped<IQueryResultExportService, QueryResultExportService>();
        services.AddScoped<ISingleWindowReferenceCatalogExcelImportService, SingleWindowReferenceCatalogExcelImportService>();
        services.AddScoped<IHsCodeImportService, HsCodeImportService>();
        services.AddSingleton<IAuditLogExcelExporter, AuditLogExcelExporter>();
    }
}
