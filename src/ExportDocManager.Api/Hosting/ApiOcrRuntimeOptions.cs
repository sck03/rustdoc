using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Api.Hosting
{
    internal static class ApiOcrRuntimeOptions
    {
        public const string RuntimeEnvironmentVariable = OcrRuntimeAvailabilityInspector.RuntimeEnvironmentVariable;

        public static bool ShouldUsePaddleOcr(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            return OcrRuntimeAvailabilityInspector.Inspect(pathProvider).UsePaddleOcr;
        }
    }
}
