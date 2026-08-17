namespace ExportDocManager.Services.Infrastructure;

internal static class ServerMigrationManifestBudget
{
    internal static long SumBytes(ServerMigrationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        long total = 0;
        foreach (ServerMigrationFileManifest file in manifest.Files ?? [])
        {
            if (file.SizeBytes < 0)
            {
                throw new InvalidDataException($"迁移清单文件大小无效：{file.RelativePath}");
            }
            total = checked(total + file.SizeBytes);
        }
        return total;
    }
}
