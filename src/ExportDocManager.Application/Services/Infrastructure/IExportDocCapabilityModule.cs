using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// Runtime capability module loaded by the API composition root. Implementations
/// live in optional infrastructure assemblies so the core API does not reference
/// Excel, browser, PDF, OCR, or their transitive packages directly.
/// </summary>
public interface IExportDocCapabilityModule
{
    string Key { get; }

    void RegisterServices(
        IServiceCollection services,
        IAppPathProvider pathProvider);
}
