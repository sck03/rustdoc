using System.Xml.Linq;

namespace ExportDocManager.Domain.Tests
{
    public class ProjectReferencePolicyTests
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ExpectedProjectReferences =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/ExportDocManager.Domain/ExportDocManager.Domain.csproj"] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ["src/ExportDocManager.Application/ExportDocManager.Application.csproj"] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "src/ExportDocManager.Domain/ExportDocManager.Domain.csproj"
                    },
                ["src/ExportDocManager.Infrastructure/ExportDocManager.Infrastructure.csproj"] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "src/ExportDocManager.Application/ExportDocManager.Application.csproj",
                        "src/ExportDocManager.Domain/ExportDocManager.Domain.csproj"
                    },
                ["src/ExportDocManager.Api/ExportDocManager.Api.csproj"] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "src/ExportDocManager.Application/ExportDocManager.Application.csproj",
                        "src/ExportDocManager.Infrastructure/ExportDocManager.Infrastructure.csproj"
                    },
                ["tools/ExportDocManager.ApiClientGenerator/ExportDocManager.ApiClientGenerator.csproj"] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "src/ExportDocManager.Api/ExportDocManager.Api.csproj"
                    }
            };

        [Fact]
        public void ProductionProjects_ShouldFollowApprovedDependencyDirection()
        {
            string repositoryRoot = ResolveRepositoryRoot();
            var projectFiles = EnumerateProductionProjectFiles(repositoryRoot)
                .Select(path => NormalizeRelativePath(repositoryRoot, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var violations = new List<string>();
            violations.AddRange(projectFiles
                .Where(path => !ExpectedProjectReferences.ContainsKey(path))
                .Select(path => $"{path}: production project is not classified by the dependency policy."));
            violations.AddRange(ExpectedProjectReferences.Keys
                .Where(path => !projectFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                .Select(path => $"{path}: expected production project was not found."));

            foreach (string projectPath in projectFiles.Where(ExpectedProjectReferences.ContainsKey))
            {
                string fullProjectPath = Path.Combine(repositoryRoot, projectPath.Replace('/', Path.DirectorySeparatorChar));
                var actualReferences = ReadProjectReferences(repositoryRoot, fullProjectPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var expectedReferences = ExpectedProjectReferences[projectPath]
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                violations.AddRange(actualReferences
                    .Except(expectedReferences, StringComparer.OrdinalIgnoreCase)
                    .Select(path => $"{projectPath}: contains unapproved project reference `{path}`."));
                violations.AddRange(expectedReferences
                    .Except(actualReferences, StringComparer.OrdinalIgnoreCase)
                    .Select(path => $"{projectPath}: is missing expected project reference `{path}`."));
            }

            Assert.True(
                violations.Count == 0,
                "Project reference policy violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        private static IEnumerable<string> EnumerateProductionProjectFiles(string repositoryRoot)
        {
            string[] roots =
            [
                Path.Combine(repositoryRoot, "src"),
                Path.Combine(repositoryRoot, "tools")
            ];

            return roots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
                .Where(path => !IsBuildOutput(path));
        }

        private static IEnumerable<string> ReadProjectReferences(string repositoryRoot, string projectPath)
        {
            string projectDirectory = Path.GetDirectoryName(projectPath)
                ?? throw new DirectoryNotFoundException($"Project directory was not found for {projectPath}.");
            XDocument project = XDocument.Load(projectPath);

            return project
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
                .Select(path => NormalizeRelativePath(repositoryRoot, path));
        }

        private static bool IsBuildOutput(string path)
        {
            string normalizedPath = path.Replace('\\', '/');
            return normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRelativePath(string repositoryRoot, string path)
        {
            return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        }

        private static string ResolveRepositoryRoot()
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (File.Exists(Path.Combine(directory, "ExportDocManager.sln")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output.");
        }
    }
}
