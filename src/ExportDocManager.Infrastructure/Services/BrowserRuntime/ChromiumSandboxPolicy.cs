namespace ExportDocManager.Services.BrowserRuntime
{
    internal static class ChromiumSandboxPolicy
    {
        public const string NoSandboxEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_NO_SANDBOX";

        public static bool ResolveNoSandboxSetting()
        {
            string configured = Environment.GetEnvironmentVariable(NoSandboxEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            string normalized = configured.Trim();
            if (normalized == "1")
            {
                return true;
            }

            if (normalized == "0")
            {
                return false;
            }

            if (bool.TryParse(normalized, out bool enabled))
            {
                return enabled;
            }

            throw new InvalidOperationException($"{NoSandboxEnvironmentVariable} 只能配置为 0、1、false 或 true。");
        }
    }
}
