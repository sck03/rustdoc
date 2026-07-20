namespace ExportDocManager.Api.Hosting
{
    internal static class ApiOcrRuntimeOptions
    {
        public const string RuntimeEnvironmentVariable = "EXPORTDOCMANAGER_OCR_RUNTIME";

        public static bool IsEnabled()
        {
            string mode = (Environment.GetEnvironmentVariable(RuntimeEnvironmentVariable) ?? "auto").Trim().ToLowerInvariant();
            return mode is not ("0" or "false" or "disabled" or "off" or "none" or "unsupported");
        }
    }
}
