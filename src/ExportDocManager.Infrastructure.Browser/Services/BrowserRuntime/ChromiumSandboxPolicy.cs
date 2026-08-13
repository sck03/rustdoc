namespace ExportDocManager.Services.BrowserRuntime
{
    internal static class ChromiumSandboxPolicy
    {
        public const string NoSandboxEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_NO_SANDBOX";
        private const int Windows10Version2004Build = 19041;

        public static bool ResolveNoSandboxSetting()
        {
            string? configured = Environment.GetEnvironmentVariable(NoSandboxEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return RequiresLegacyWindowsCompatibilityMode(
                    OperatingSystem.IsWindows(),
                    Environment.OSVersion.Version);
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

        internal static bool RequiresLegacyWindowsCompatibilityMode(bool isWindows, Version version)
        {
            ArgumentNullException.ThrowIfNull(version);

            // Chromium 149 sandboxed child processes can fail before CDP initialization on
            // legacy Windows 10/Server builds (STATUS_ACCESS_DENIED, sometimes surfaced by
            // Windows as a misleading version.dll dialog). Avoid spawning the broken sandboxed
            // process there; administrators can still force sandboxing with an explicit false.
            return isWindows &&
                   version.Major == 10 &&
                   version.Build >= 0 &&
                   version.Build < Windows10Version2004Build;
        }
    }
}
