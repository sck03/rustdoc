namespace ExportDocManager.Infrastructure.Tests;

public sealed class InfrastructureSqlSafetyPolicyTests
{
    private static readonly string[] RawSqlTokens =
    [
        "ExecuteSqlRaw",
        "FromSqlRaw",
        "SqlQueryRaw",
        "CommandText ="
    ];

    [Fact]
    public void RawSql_ShouldRemainConfinedToReviewedSchemaInitialization()
    {
        string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Infrastructure");
        string allowedRelativePath = "Services/DatabaseInitializationService.cs";
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new
            {
                Path = path,
                RelativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                Content = File.ReadAllText(path)
            })
            .Where(file => !string.Equals(file.RelativePath, allowedRelativePath, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => RawSqlTokens
                .Where(token => file.Content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{file.RelativePath}: contains unreviewed raw SQL token `{token}`"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Raw SQL is restricted to the reviewed schema-initialization module."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));

        string initialization = File.ReadAllText(Path.Combine(sourceRoot, "Services", "DatabaseInitializationService.cs"));
        Assert.Contains("EscapeSqliteIdentifier", initialization, StringComparison.Ordinal);
        Assert.Contains("EscapePostgreSqlIdentifier", initialization, StringComparison.Ordinal);
        Assert.Contains("ExecuteSqlInterpolatedAsync", initialization, StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        return normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSourceRoot(params string[] segments)
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', segments)} from test output.");
    }
}
