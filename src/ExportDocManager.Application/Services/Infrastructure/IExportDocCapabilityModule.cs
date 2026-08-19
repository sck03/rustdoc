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
        IExportDocCapabilityRegistry services,
        IAppPathProvider pathProvider);
}

/// <summary>
/// Small composition contract exposed to optional capability assemblies. The application
/// layer stays independent from ASP.NET Core and the API composition root owns the concrete
/// dependency-injection container.
/// </summary>
public interface IExportDocCapabilityRegistry
{
    void AddScoped<TService>() where TService : class;

    void AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    void AddScoped<TService>(Func<IExportDocCapabilityServiceProvider, TService> factory)
        where TService : class;

    void AddSingleton<TService>() where TService : class;

    void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    void AddSingleton<TService>(Func<IExportDocCapabilityServiceProvider, TService> factory)
        where TService : class;
}

public interface IExportDocCapabilityServiceProvider
{
    TService GetRequiredService<TService>() where TService : notnull;
}
