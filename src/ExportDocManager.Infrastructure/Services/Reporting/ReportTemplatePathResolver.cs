using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

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

        public string GetBuiltInTemplatesBaseDirectory()
        {
            string root = Path.GetFullPath(_pathProvider.TemplateRoot);
            return PathBoundaryHelper.EnsureNoLinkLikeComponents(
                root,
                "内置模板目录不能经过符号链接、目录联接或其他重解析点。");
        }

        public string GetUserTemplatesBaseDirectory()
        {
            string root = Path.GetFullPath(_pathProvider.UserTemplateRoot);
            return PathBoundaryHelper.EnsureNoLinkLikeComponents(
                root,
                "用户模板目录不能经过符号链接、目录联接或其他重解析点。");
        }

        public string GetBuiltInTemplateDirectory(string category) =>
            Path.Combine(GetBuiltInTemplatesBaseDirectory(), NormalizeTemplateCategory(category));

        public string EnsureTemplateDirectory(string category)
        {
            string directory = Path.Combine(GetUserTemplatesBaseDirectory(), NormalizeTemplateCategory(category));
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                directory,
                "用户模板分类目录不能经过符号链接、目录联接或其他重解析点。");
            Directory.CreateDirectory(directory);
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                directory,
                GetUserTemplatesBaseDirectory(),
                "用户模板分类目录不能包含符号链接、目录联接或其他重解析点。");
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
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    selectedFullPath,
                    userRoot,
                    "用户模板路径不能经过符号链接、目录联接或其他重解析点。");
                return UserPathPrefix + NormalizeStoredRelativePath(Path.GetRelativePath(userRoot, selectedFullPath));
            }

            string builtInRoot = GetBuiltInTemplatesBaseDirectory();
            if (IsPathWithinDirectory(selectedFullPath, builtInRoot))
            {
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    selectedFullPath,
                    builtInRoot,
                    "内置模板路径不能经过符号链接、目录联接或其他重解析点。");
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
                string absolutePath = Path.GetFullPath(normalizedPath);
                string userRoot = GetUserTemplatesBaseDirectory();
                if (IsPathWithinDirectory(absolutePath, userRoot))
                {
                    return PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                        absolutePath,
                        userRoot,
                        "用户模板路径不能经过符号链接、目录联接或其他重解析点。");
                }

                string builtInRoot = GetBuiltInTemplatesBaseDirectory();
                if (IsPathWithinDirectory(absolutePath, builtInRoot))
                {
                    return PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                        absolutePath,
                        builtInRoot,
                        "内置模板路径不能经过符号链接、目录联接或其他重解析点。");
                }

                return absolutePath;
            }

            normalizedPath = StripTemplatesPrefix(normalizedPath);
            string userRootCandidate = GetUserTemplatesBaseDirectory();
            string userCandidate = Path.GetFullPath(Path.Combine(userRootCandidate, normalizedPath));
            if (!IsPathWithinDirectory(userCandidate, userRootCandidate))
            {
                throw new PermissionDeniedException("模板路径不能离开受管模板目录。");
            }
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                userCandidate,
                userRootCandidate,
                "用户模板路径不能经过符号链接、目录联接或其他重解析点。");
            if (File.Exists(userCandidate))
            {
                return userCandidate;
            }

            string builtInRootCandidate = GetBuiltInTemplatesBaseDirectory();
            string builtInCandidate = Path.GetFullPath(Path.Combine(builtInRootCandidate, normalizedPath));
            if (!IsPathWithinDirectory(builtInCandidate, builtInRootCandidate))
            {
                throw new PermissionDeniedException("模板路径不能离开受管模板目录。");
            }
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                builtInCandidate,
                builtInRootCandidate,
                "内置模板路径不能经过符号链接、目录联接或其他重解析点。");
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
                throw new PermissionDeniedException("无法为内置模板创建安全的用户副本路径。");
            }

            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                targetPath,
                GetUserTemplatesBaseDirectory(),
                "用户模板副本路径不能经过符号链接、目录联接或其他重解析点。");

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

            return PathBoundaryHelper.IsWithinRoot(path, directory);
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
                throw new PermissionDeniedException("模板路径不能离开受管模板目录。");
            }

            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                candidate,
                root,
                "模板路径不能经过符号链接、目录联接或其他重解析点。");

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
