namespace ExportDocManager.Infrastructure.Tests
{
    public class InfrastructurePathPolicyTests
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTokensByRelativePath =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Paths/RuntimeAppPathProvider.cs"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "AppContext.BaseDirectory"
                },
                ["Services/SharedDatabaseMaintenanceService.cs"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "ProgramData"
                }
            };

        private static readonly string[] ForbiddenPathTokens =
        [
            "Path.GetTempPath",
            "Directory.GetCurrentDirectory",
            "Environment.GetFolderPath",
            "SpecialFolder",
            "CommonApplicationData",
            "LocalApplicationData",
            "ApplicationData",
            "ProgramData",
            @"C:\",
            "AppContext.BaseDirectory"
        ];

        [Fact]
        public void InfrastructureSource_ShouldNotIntroduceUnreviewedSystemDiskOrTempPathDefaults()
        {
            string sourceRoot = ResolveInfrastructureSourceRoot();
            var violations = Directory
                .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .SelectMany(path => FindViolations(sourceRoot, path))
                .ToList();

            Assert.True(
                violations.Count == 0,
                "Infrastructure path policy violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        private static IEnumerable<string> FindViolations(string sourceRoot, string path)
        {
            string relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            string content = File.ReadAllText(path);
            AllowedTokensByRelativePath.TryGetValue(relativePath, out var allowedTokens);

            foreach (string token in ForbiddenPathTokens)
            {
                if (!content.Contains(token, StringComparison.Ordinal))
                {
                    continue;
                }

                if (allowedTokens?.Contains(token) == true)
                {
                    continue;
                }

                yield return $"{relativePath}: contains unreviewed token `{token}`";
            }
        }

        private static bool IsBuildOutput(string path)
        {
            string normalizedPath = path.Replace('\\', '/');
            return normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveInfrastructureSourceRoot()
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(directory, "src", "ExportDocManager.Infrastructure");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate src/ExportDocManager.Infrastructure from test output.");
        }
    }
}
