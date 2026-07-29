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
    public void RawSql_ShouldRemainConfinedToReviewedInfrastructureGateways()
    {
        string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Infrastructure");
        var allowedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Services/DatabaseInitializationService.cs",
            "Services/DatabaseSchemaBaseline.cs",
            "Services/SqliteMaintenanceGateway.cs"
        };
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new
            {
                Path = path,
                RelativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                Content = File.ReadAllText(path)
            })
            .Where(file => !allowedRelativePaths.Contains(file.RelativePath))
            .SelectMany(file => RawSqlTokens
                .Where(token => file.Content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{file.RelativePath}: contains unreviewed raw SQL token `{token}`"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Raw SQL is restricted to the reviewed schema-initialization and SQLite maintenance gateways."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));

        string initialization = File.ReadAllText(Path.Combine(sourceRoot, "Services", "DatabaseSchemaBaseline.cs"));
        Assert.Contains("MetadataTableName", initialization, StringComparison.Ordinal);
        Assert.Contains("ExecuteSqlRawAsync", initialization, StringComparison.Ordinal);
        Assert.Contains("CreatePerformanceIndexesAsync", initialization, StringComparison.Ordinal);

        string sqliteMaintenance = File.ReadAllText(Path.Combine(sourceRoot, "Services", "SqliteMaintenanceGateway.cs"));
        Assert.Contains("QuickCheckCommandText", sqliteMaintenance, StringComparison.Ordinal);
        Assert.Contains("PRAGMA quick_check;", sqliteMaintenance, StringComparison.Ordinal);
        Assert.Contains("RunQuickCheckAsync", sqliteMaintenance, StringComparison.Ordinal);
        Assert.Contains("RunQuickCheck", sqliteMaintenance, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSqlHsSearchIndexes_ShouldKeepPrefixAndOptionalTrigramFallbackContracts()
    {
        string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Infrastructure");
        string initialization = File.ReadAllText(
            Path.Combine(sourceRoot, "Services", "DatabaseSchemaBaseline.cs"));

        foreach (string requiredContract in new[]
        {
            "IX_HsCodes_Status_NormalizedCode_Prefix",
            "IX_HsCodeDeclarationExamples_RawCode_Prefix",
            "IX_HsCodeRemoteCandidates_Status_RawCode_Prefix",
            "IX_Products_HSCode_Prefix",
            "IX_Items_HSCode_Prefix",
            "CREATE EXTENSION IF NOT EXISTS pg_trgm",
            "IX_HsCodes_TextSearch_Trgm",
            "IX_HsCodeDeclarationExamples_TextSearch_Trgm",
            "IX_HsCodeRemoteCandidates_TextSearch_Trgm",
            "IX_Items_HistorySearch_Trgm",
            "PostgreSQL pg_trgm indexes were not installed"
        })
        {
            Assert.Contains(requiredContract, initialization, StringComparison.Ordinal);
        }

        Assert.Contains("ex.SqlState is \"42501\" or \"0A000\" or \"58P01\"", initialization, StringComparison.Ordinal);
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
