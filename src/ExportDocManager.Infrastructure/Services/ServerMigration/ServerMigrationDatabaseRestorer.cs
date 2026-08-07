using ExportDocManager.DataAccess;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// PostgreSQL 恢复边界，隔离迁移编排与具体 pg_dump/pg_restore 工具调用。
/// </summary>
internal static class ServerMigrationDatabaseRestorer
{
    public static Task ValidateDumpContainerAsync(
        PostgreSqlToolPaths tools,
        string dumpPath,
        CancellationToken cancellationToken) =>
        ServerMigrationPostgreSql.ValidateDumpContainerAsync(tools, dumpPath, cancellationToken);

    public static Task ValidateProductDumpAsync(
        PostgreSqlToolPaths tools,
        DatabaseConnectionSettings settings,
        string dumpPath,
        string validationDatabaseName,
        CancellationToken cancellationToken) =>
        ServerMigrationPostgreSql.ValidateProductDumpAsync(
            tools,
            settings,
            dumpPath,
            validationDatabaseName,
            cancellationToken);

    public static Task CreateSafetyBackupAsync(
        PostgreSqlToolPaths tools,
        DatabaseConnectionSettings settings,
        string destination,
        CancellationToken cancellationToken) =>
        ServerMigrationPostgreSql.CreateSafetyBackupAsync(tools, settings, destination, cancellationToken);

    public static Task RestoreAsync(
        PostgreSqlToolPaths tools,
        DatabaseConnectionSettings settings,
        string dumpPath,
        CancellationToken cancellationToken) =>
        ServerMigrationPostgreSql.RestoreDatabaseAsync(tools, settings, dumpPath, cancellationToken);

    public static Task TryDropValidationDatabaseAsync(
        DatabaseConnectionSettings settings,
        string databaseName,
        CancellationToken cancellationToken = default) =>
        ServerMigrationPostgreSql.TryDropDatabaseAsync(settings, databaseName, cancellationToken);
}
