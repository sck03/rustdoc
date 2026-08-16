using System.IO;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class RuntimeAppPathProvider : IAppPathProvider
    {
        public const string DataRootEnvironmentVariable = "EXPORTDOCMANAGER_DATA_ROOT";

        private readonly string _appRoot;
        private readonly string _dataRoot;

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

        public string DataRoot => _dataRoot;

        public string DatabaseRoot => GetDataPath("Database");

        public string TemplateRoot => GetAppPath("Templates");

        public string UserTemplateRoot => GetDataPath("Templates");

        public string ResourceRoot => GetAppPath("Resources");

        public string BrowserRoot => GetAppPath("Browsers");

        public string ToolRoot => GetAppPath("Tools");

        public string FileRoot => GetDataPath("Files");

        public string ExportRoot => GetDataPath("Exports");

        public string BackupRoot => GetDataPath("Backups");

        public string SingleWindowRoot => GetDataPath("SingleWindow");

        public string OcrModelRoot => GetAppPath("OcrModels");

        public string LogRoot => GetDataPath("Logs");

        public string CacheRoot => GetDataPath("Cache");

        public string ConfigRoot => GetDataPath("Config");

        public string SecurityRoot => GetDataPath("Security");

        public string WebViewRoot => GetDataPath("WebView");

        private string GetDataPath(string name)
        {
            return Path.Combine(DataRoot, name);
        }

        private string GetAppPath(string name)
        {
            return Path.Combine(AppRoot, name);
        }

        public static string NormalizeRoot(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            string fullPath = Path.GetFullPath(path);
            int rootLength = Path.GetPathRoot(fullPath)?.Length ?? 0;
            return fullPath.Length > rootLength
                ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
        }

        private static string ResolveDefaultDataRoot(string appRoot)
        {
            var configuredDataRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configuredDataRoot)
                ? Path.Combine(appRoot, "App_Data")
                : configuredDataRoot;
        }

    }
}
