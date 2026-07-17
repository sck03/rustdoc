using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportTemplatePathResolver
    {
        private readonly IAppPathProvider _pathProvider;

        public ReportTemplatePathResolver(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public string GetTemplatesBaseDirectory()
        {
            return Path.GetFullPath(_pathProvider.TemplateRoot);
        }

        public string EnsureTemplateDirectory(string category)
        {
            var directory = Path.Combine(GetTemplatesBaseDirectory(), NormalizeTemplateCategory(category));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public string ToStoredPath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            var templatesBaseDir = GetTemplatesBaseDirectory();
            var selectedFullPath = Path.GetFullPath(templatePath);

            return IsPathWithinDirectory(selectedFullPath, templatesBaseDir)
                ? Path.GetRelativePath(templatesBaseDir, selectedFullPath)
                : selectedFullPath;
        }

        public string ToAbsolutePath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            var normalizedPath = templatePath.Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(Path.Combine(GetTemplatesBaseDirectory(), normalizedPath));
        }

        public static string NormalizeTemplateCategory(string category)
        {
            return string.Equals(category, ReportTemplateCatalogLoader.InternalTemplateCatalogType, StringComparison.OrdinalIgnoreCase)
                ? ReportTemplateCatalogLoader.InternalTemplateCatalogType
                : ReportTemplateCatalogLoader.ExportTemplateCatalogType;
        }

        public static bool IsPathWithinDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
