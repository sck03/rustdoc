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
            "Services/DatabaseSchemaBaseline.SqliteSearch.cs",
            "Repositories/SqliteFtsSearch.cs",
            "Services/SqliteMaintenanceGateway.cs",
            "Services/ServerMigration/ServerMigrationService.cs",
            "Services/ServerMigration/ServerMigrationPathRewriter.cs",
            "Services/ServerMigration/ServerMigrationPostgreSql.cs"
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
        Assert.Contains("CreateCorePerformanceIndexesAsync", initialization, StringComparison.Ordinal);

        string sqliteMaintenance = File.ReadAllText(Path.Combine(sourceRoot, "Services", "SqliteMaintenanceGateway.cs"));
        Assert.Contains("QuickCheckCommandText", sqliteMaintenance, StringComparison.Ordinal);
        Assert.Contains("PRAGMA quick_check;", sqliteMaintenance, StringComparison.Ordinal);
        Assert.Contains("RunQuickCheckAsync", sqliteMaintenance, StringComparison.Ordinal);
        Assert.Contains("RunQuickCheck", sqliteMaintenance, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessDataServices_ShouldRequireExplicitAccessScope()
    {
        Type scopeType = typeof(ExportDocManager.Services.Security.BusinessDataAccessScope);
        Type[] scopedServices =
        [
            typeof(ExportDocManager.Services.Infrastructure.LocalMasterDataReadRepository),
            typeof(ExportDocManager.Services.MasterData.CustomerService),
            typeof(ExportDocManager.Services.MasterData.ExporterService),
            typeof(ExportDocManager.Services.MasterData.HsCodeKnowledgeService),
            typeof(ExportDocManager.Services.Reporting.ReportHtmlService)
        ];

        var violations = scopedServices
            .SelectMany(type => type
                .GetConstructors(System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.Public |
                                 System.Reflection.BindingFlags.NonPublic)
                .Where(constructor => !constructor.IsPrivate &&
                                      constructor.GetParameters().All(parameter => parameter.ParameterType != scopeType))
                .Select(constructor => $"{type.Name}: {constructor}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Business-data services must fail closed by requiring BusinessDataAccessScope in every callable constructor."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SearchIndexes_ShouldMatchProviderQueriesAndKeepPrefixFallbacks()
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
            "IX_Invoices_TextSearch_Trgm",
            "IX_Payments_TextSearch_Trgm",
            "IX_Customers_TextSearch_Trgm",
            "IX_Exporters_TextSearch_Trgm",
            "IX_Payees_TextSearch_Trgm",
            "IX_CrmCustomers_TextSearch_Trgm",
            "IX_SupplierCompanies_TextSearch_Trgm",
            "IX_CustomsCooProducerProfiles_TextSearch_Trgm",
            "\"Name\" gin_trgm_ops",
            "PostgreSQL pg_trgm indexes were not installed"
        })
        {
            Assert.Contains(requiredContract, initialization, StringComparison.Ordinal);
        }

        Assert.Contains("ex.SqlState is \"42501\" or \"0A000\" or \"58P01\"", initialization, StringComparison.Ordinal);

        string sqliteSearch = File.ReadAllText(
            Path.Combine(sourceRoot, "Services", "DatabaseSchemaBaseline.SqliteSearch.cs"));
        Assert.Contains("CREATE VIRTUAL TABLE \"InvoiceSearch\" USING fts5", sqliteSearch, StringComparison.Ordinal);
        Assert.Contains("CREATE VIRTUAL TABLE \"PaymentSearch\" USING fts5", sqliteSearch, StringComparison.Ordinal);
        Assert.Contains("tokenize='trigram'", sqliteSearch, StringComparison.Ordinal);
        Assert.Contains("TR_Items_Search_Update", sqliteSearch, StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        return normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSourceRoot(params string[] segments)
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', segments)} from test output.");
    }
}
