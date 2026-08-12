using ExportDocManager.DataAccess;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 配置与业务文件的原子切换边界。保留旧事务类型作为测试和旧调用点的兼容 façade。
/// </summary>
internal static class ServerMigrationFileSwitcher
{
    public static ServerMigrationFileTransactionState Prepare(
        IAppPathProvider paths,
        string stagingRoot,
        string safetyRoot,
        PendingServerMigrationRestore marker,
        DatabaseConnectionSettings databaseSettings) =>
        ServerMigrationFileTransaction.Prepare(paths, stagingRoot, safetyRoot, marker, databaseSettings);

    public static void Apply(ServerMigrationFileTransactionState state) =>
        ServerMigrationFileTransaction.Apply(state);

    public static void Rollback(string safetyRoot) =>
        ServerMigrationFileTransaction.Rollback(safetyRoot);

    public static void CleanupPrepared(ServerMigrationFileTransactionState state) =>
        ServerMigrationFileTransaction.CleanupPrepared(state);

    public static void CleanupSnapshots(ServerMigrationFileTransactionState state) =>
        ServerMigrationFileTransaction.CleanupSnapshots(state);

    public static ServerMigrationFileTransactionState? ReadState(string safetyRoot) =>
        ServerMigrationFileTransaction.ReadState(safetyRoot);
}
