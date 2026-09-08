using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Worklist;

public sealed class WorklistCapabilityModule : IExportDocCapabilityModule
{
    public string Key => CapabilityModuleKeys.Worklist;
    public void RegisterServices(IExportDocCapabilityRegistry services, IAppPathProvider pathProvider)
    {
        services.AddScoped<IWorklistService, WorklistService>();
        services.AddScoped<IWorklistSource, InvoiceWorklistSource>();
        services.AddScoped<IWorklistSource, FollowUpWorklistSource>();
        services.AddScoped<IWorklistSource, OfficeWorklistSource>();
    }
}
