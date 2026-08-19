using ExportDocManager.Services.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Hosting;

internal sealed class ServiceCollectionCapabilityRegistry(IServiceCollection services)
    : IExportDocCapabilityRegistry
{
    public void AddScoped<TService>() where TService : class =>
        services.AddScoped<TService>();

    public void AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService =>
        services.AddScoped<TService, TImplementation>();

    public void AddScoped<TService>(Func<IExportDocCapabilityServiceProvider, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.AddScoped(provider => factory(new CapabilityServiceProvider(provider)));
    }

    public void AddSingleton<TService>() where TService : class =>
        services.AddSingleton<TService>();

    public void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService =>
        services.AddSingleton<TService, TImplementation>();

    public void AddSingleton<TService>(Func<IExportDocCapabilityServiceProvider, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton(provider => factory(new CapabilityServiceProvider(provider)));
    }

    private sealed class CapabilityServiceProvider(IServiceProvider provider)
        : IExportDocCapabilityServiceProvider
    {
        public TService GetRequiredService<TService>() where TService : notnull =>
            provider.GetRequiredService<TService>();
    }
}
