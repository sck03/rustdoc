using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.BrowserRuntime
{
    public sealed class BrowserExecutableResolver
    {
        public const string ChromiumExecutableEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE";

        private static readonly string[] PreferredRelativeExecutables =
        {
            Path.Combine("Browsers", "chrome-headless-shell.exe"),
            Path.Combine("Browsers", "chrome-headless-shell"),
            Path.Combine("Browsers", "ChromeHeadlessShell", "chrome-headless-shell.exe"),
            Path.Combine("Browsers", "ChromeHeadlessShell", "chrome-headless-shell"),
            Path.Combine("Browsers", "chrome.exe"), Path.Combine("Browsers", "chromium.exe"),
            Path.Combine("Browsers", "Chromium", "chrome.exe"), Path.Combine("Browsers", "Chromium", "chrome-win", "chrome.exe"),
            Path.Combine("Browsers", "chrome-win", "chrome.exe"), Path.Combine("Browsers", "chrome"),
            Path.Combine("Browsers", "chromium"), Path.Combine("Browsers", "Chromium", "chrome"),
            Path.Combine("Browsers", "Chromium", "chrome-linux", "chrome"), Path.Combine("Browsers", "chrome-linux", "chrome"),
            Path.Combine("Browsers", "Chromium.app", "Contents", "MacOS", "Chromium"),
            Path.Combine("Browsers", "Google Chrome for Testing.app", "Contents", "MacOS", "Google Chrome for Testing")
        };

        private static readonly string[] RecursiveExecutableNames =
        {
            "chrome-headless-shell.exe", "chrome-headless-shell", "chrome.exe", "chromium.exe",
            "chrome", "chromium", "Chromium", "Google Chrome for Testing"
        };

        private readonly IAppPathProvider _pathProvider;

        public BrowserExecutableResolver(IAppPathProvider pathProvider) =>
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));

        public string Resolve()
        {
            string configuredPath = Environment.GetEnvironmentVariable(ChromiumExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullPath = Path.GetFullPath(configuredPath.Trim().Trim('"'));
                if (File.Exists(fullPath)) return fullPath;
                throw new InvalidOperationException($"{ChromiumExecutableEnvironmentVariable} 指向的 Chromium 可执行文件不存在：{fullPath}");
            }

            foreach (string relativePath in PreferredRelativeExecutables)
            {
                string candidate = Path.Combine(_pathProvider.AppRoot, relativePath);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }

            if (Directory.Exists(_pathProvider.BrowserRoot))
            {
                foreach (string name in RecursiveExecutableNames)
                {
                    string candidate = Directory.EnumerateFiles(_pathProvider.BrowserRoot, name, SearchOption.AllDirectories)
                        .OrderBy(file => file.Length).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(candidate)) return Path.GetFullPath(candidate);
                }
            }

            throw new InvalidOperationException(
                "未找到可用 Chromium。请把 Chrome Headless Shell、Chromium 或 Chrome for Testing 放在程序运行目录 Browsers/，或显式设置 EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE。程序不会自动下载到系统盘。");
        }
    }
}
