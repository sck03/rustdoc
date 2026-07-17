using System.IO;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class RuntimeAppPathProvider : IAppPathProvider
    {
        public const string DataRootEnvironmentVariable = "EXPORTDOCMANAGER_DATA_ROOT";

        private readonly string _appRoot;
        private readonly string _dataRoot;

        public RuntimeAppPathProvider()
        {
            _appRoot = NormalizeRoot(AppContext.BaseDirectory);
            _dataRoot = NormalizeRoot(ResolveDefaultDataRoot(_appRoot));
        }

        public RuntimeAppPathProvider(string appRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appRoot);

            _appRoot = NormalizeRoot(appRoot);
            _dataRoot = NormalizeRoot(ResolveDefaultDataRoot(_appRoot));
        }

        public RuntimeAppPathProvider(string appRoot, string dataRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

            _appRoot = NormalizeRoot(appRoot);
            _dataRoot = NormalizeRoot(dataRoot);
        }

        public string AppRoot => _appRoot;

        public string DataRoot => EnsureDirectory(_dataRoot);

        public string DatabaseRoot => GetDataDirectory("Database");

        public string TemplateRoot => GetAppPath("Templates");

        public string ResourceRoot => GetAppPath("Resources");

        public string BrowserRoot => GetAppPath("Browsers");

        public string ToolRoot => GetAppPath("Tools");

        public string FileRoot => GetDataDirectory("Files");

        public string ExportRoot => GetDataDirectory("Exports");

        public string BackupRoot => GetDataDirectory("Backups");

        public string SingleWindowRoot => GetDataDirectory("SingleWindow");

        public string OcrModelRoot => GetAppPath("OcrModels");

        public string LogRoot => GetDataDirectory("Logs");

        public string CacheRoot => GetDataDirectory("Cache");

        public string ConfigRoot => GetDataDirectory("Config");

        public string SecurityRoot => GetDataDirectory("Security");

        public string WebViewRoot => GetDataDirectory("WebView");

        private string GetDataDirectory(string name)
        {
            return EnsureDirectory(Path.Combine(DataRoot, name));
        }

        private string GetAppPath(string name)
        {
            return Path.Combine(AppRoot, name);
        }

        private static string NormalizeRoot(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ResolveDefaultDataRoot(string appRoot)
        {
            var configuredDataRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configuredDataRoot)
                ? Path.Combine(appRoot, "App_Data")
                : configuredDataRoot;
        }

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
