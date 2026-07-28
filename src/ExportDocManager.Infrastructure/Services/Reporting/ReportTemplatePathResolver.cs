using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportTemplatePathResolver
    {
        private const string BuiltInPathPrefix = "builtin:";
        private const string UserPathPrefix = "user:";

        private readonly IAppPathProvider _pathProvider;

        public ReportTemplatePathResolver(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public string GetBuiltInTemplatesBaseDirectory() => Path.GetFullPath(_pathProvider.TemplateRoot);

        public string GetUserTemplatesBaseDirectory() => Path.GetFullPath(_pathProvider.UserTemplateRoot);

        public string GetBuiltInTemplateDirectory(string category) =>
            Path.Combine(GetBuiltInTemplatesBaseDirectory(), NormalizeTemplateCategory(category));

        public string EnsureTemplateDirectory(string category)
        {
            string directory = Path.Combine(GetUserTemplatesBaseDirectory(), NormalizeTemplateCategory(category));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public string GetUserConfigPath() =>
            Path.Combine(GetUserTemplatesBaseDirectory(), "report_templates.json");

        public string ToStoredPath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            string selectedFullPath = Path.GetFullPath(templatePath);
            string userRoot = GetUserTemplatesBaseDirectory();
            if (IsPathWithinDirectory(selectedFullPath, userRoot))
            {
                return UserPathPrefix + NormalizeStoredRelativePath(Path.GetRelativePath(userRoot, selectedFullPath));
            }

            string builtInRoot = GetBuiltInTemplatesBaseDirectory();
            if (IsPathWithinDirectory(selectedFullPath, builtInRoot))
            {
                return BuiltInPathPrefix + NormalizeStoredRelativePath(Path.GetRelativePath(builtInRoot, selectedFullPath));
            }

            return selectedFullPath;
        }

        public string ToAbsolutePath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            string normalizedPath = templatePath.Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (TryResolvePrefixedPath(normalizedPath, UserPathPrefix, GetUserTemplatesBaseDirectory(), out string userPath) ||
                TryResolvePrefixedPath(normalizedPath, BuiltInPathPrefix, GetBuiltInTemplatesBaseDirectory(), out userPath))
            {
                return userPath;
            }

            if (Path.IsPathRooted(normalizedPath))
            {
                return Path.GetFullPath(normalizedPath);
            }

            normalizedPath = StripTemplatesPrefix(normalizedPath);
            string userCandidate = Path.GetFullPath(Path.Combine(GetUserTemplatesBaseDirectory(), normalizedPath));
            if (File.Exists(userCandidate))
            {
                return userCandidate;
            }

            string builtInCandidate = Path.GetFullPath(Path.Combine(GetBuiltInTemplatesBaseDirectory(), normalizedPath));
            return File.Exists(builtInCandidate) ? builtInCandidate : userCandidate;
        }

        public bool IsBuiltInTemplatePath(string path) =>
            IsPathWithinDirectory(path, GetBuiltInTemplatesBaseDirectory());

        public bool IsUserTemplatePath(string path) =>
            IsPathWithinDirectory(path, GetUserTemplatesBaseDirectory());

        public string GetUserCopyPath(string builtInTemplatePath)
        {
            string fullPath = Path.GetFullPath(builtInTemplatePath);
            if (!IsBuiltInTemplatePath(fullPath))
            {
                throw new ArgumentException("指定路径不是内置模板。", nameof(builtInTemplatePath));
            }

            string relativePath = Path.GetRelativePath(GetBuiltInTemplatesBaseDirectory(), fullPath);
            string targetPath = Path.GetFullPath(Path.Combine(GetUserTemplatesBaseDirectory(), relativePath));
            if (!IsUserTemplatePath(targetPath))
            {
                throw new UnauthorizedAccessException("无法为内置模板创建安全的用户副本路径。");
            }

            return targetPath;
        }

        public string GetCatalogIdentity(string templatePath)
        {
            string fullPath = Path.GetFullPath(templatePath);
            if (IsUserTemplatePath(fullPath))
            {
                return NormalizeStoredRelativePath(Path.GetRelativePath(GetUserTemplatesBaseDirectory(), fullPath));
            }

            if (IsBuiltInTemplatePath(fullPath))
            {
                return NormalizeStoredRelativePath(Path.GetRelativePath(GetBuiltInTemplatesBaseDirectory(), fullPath));
            }

            return fullPath;
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

            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolvePrefixedPath(
            string path,
            string prefix,
            string root,
            out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relativePath = path[prefix.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!IsPathWithinDirectory(candidate, root))
            {
                throw new UnauthorizedAccessException("模板路径不能离开受管模板目录。");
            }

            resolvedPath = candidate;
            return true;
        }

        private static string StripTemplatesPrefix(string path)
        {
            string prefix = "Templates" + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path[prefix.Length..]
                : path;
        }

        private static string NormalizeStoredRelativePath(string path) =>
            path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
