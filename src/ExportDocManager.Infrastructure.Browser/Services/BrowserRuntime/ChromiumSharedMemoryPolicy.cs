namespace ExportDocManager.Services.BrowserRuntime
{
    internal static class ChromiumSharedMemoryPolicy
    {
        public const string DisableDevShmUsageEnvironmentVariable =
            "EXPORTDOCMANAGER_CHROMIUM_DISABLE_DEV_SHM_USAGE";

        public static bool ResolveDisableDevShmUsageSetting()
        {
            string? configured = Environment.GetEnvironmentVariable(DisableDevShmUsageEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return true;
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

            if (bool.TryParse(normalized, out bool disabled))
            {
                return disabled;
            }

            throw new InvalidOperationException(
                $"{DisableDevShmUsageEnvironmentVariable} 只能配置为 0、1、false 或 true。");
        }
    }
}
