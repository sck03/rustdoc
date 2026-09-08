using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Attachments;

public sealed class AttachmentCapabilityModule : IExportDocCapabilityModule
{
    public string Key => CapabilityModuleKeys.BusinessAttachments;
    public void RegisterServices(IExportDocCapabilityRegistry services, IAppPathProvider pathProvider) =>
        services.AddScoped<IBusinessAttachmentService, BusinessAttachmentService>();
}
