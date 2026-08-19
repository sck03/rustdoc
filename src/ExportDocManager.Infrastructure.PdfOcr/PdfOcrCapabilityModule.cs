using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.PdfOcr;

public sealed class PdfOcrCapabilityModule : IExportDocCapabilityModule
{
    public string Key => "pdf-ocr";

    public void RegisterServices(
        IExportDocCapabilityRegistry services,
        IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pathProvider);

        services.AddScoped<IPdfMergeService, PdfMergeService>();
        services.AddScoped<IOcrService, UnsupportedOcrService>();
        services.AddSingleton<RustOcrSidecarHost>();
        if (OcrRuntimeOptions.IsEnabled() && File.Exists(RustOcrSidecarHost.FindExecutable(pathProvider)))
        {
            services.AddScoped<IOcrService, RustOcrService>();
        }
        services.AddScoped<ILetterOfCreditDocumentService, LetterOfCreditDocumentService>();
        services.AddSingleton<IRuntimeDependencyDiagnosticContributor, OcrRuntimeDiagnosticContributor>();
        services.AddSingleton<IOcrRuntimeVerifier, OcrRuntimeVerifier>();
    }
}

internal static class OcrRuntimeOptions
{
    public const string RuntimeEnvironmentVariable = "EXPORTDOCMANAGER_OCR_RUNTIME";

    public static bool IsEnabled()
    {
        string mode = (Environment.GetEnvironmentVariable(RuntimeEnvironmentVariable) ?? "auto")
            .Trim()
            .ToLowerInvariant();
        return mode is not ("0" or "false" or "disabled" or "off" or "none" or "unsupported");
    }
}
