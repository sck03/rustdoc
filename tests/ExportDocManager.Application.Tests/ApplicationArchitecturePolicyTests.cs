namespace ExportDocManager.Application.Tests
{
    public class ApplicationArchitecturePolicyTests
    {
        private static readonly string[] ForbiddenTokens =
        [
            "System.Drawing",
            "System.Windows.Forms",
            "Windows.Forms",
            "Microsoft.Web.WebView2",
            "AppPaths",
            "SessionManager",
            "BusinessDataAccessPolicy",
            "Path.GetTempPath",
            "Directory.GetCurrentDirectory",
            "Environment.GetFolderPath",
            "SpecialFolder",
            "AppContext.BaseDirectory",
            @"C:\"
        ];

        [Fact]
        public void ApplicationSource_ShouldRemainPlatformAndPathIndependent()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Application");
            var violations = Directory
                .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .SelectMany(path => FindViolations(sourceRoot, path))
                .ToList();

            Assert.True(
                violations.Count == 0,
                "Application architecture policy violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        private static IEnumerable<string> FindViolations(string sourceRoot, string path)
        {
            string relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            string content = File.ReadAllText(path);

            foreach (string token in ForbiddenTokens)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    yield return $"{relativePath}: contains forbidden token `{token}`";
                }
            }
        }

        private static bool IsBuildOutput(string path)
        {
            string normalizedPath = path.Replace('\\', '/');
            return normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSourceRoot(params string[] segments)
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException($"Could not locate {string.Join("/", segments)} from test output.");
        }
    }
}
