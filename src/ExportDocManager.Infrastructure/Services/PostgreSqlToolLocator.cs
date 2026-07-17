using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services
{
    internal sealed record PostgreSqlToolPaths(
        string BinRoot,
        string PgDumpPath,
        string PgRestorePath,
        string PsqlPath)
    {
        public bool ToolsReady =>
            !string.IsNullOrWhiteSpace(PgDumpPath) &&
            !string.IsNullOrWhiteSpace(PgRestorePath) &&
            !string.IsNullOrWhiteSpace(PsqlPath);

        public int AvailableToolCount =>
            CountPath(PgDumpPath) + CountPath(PgRestorePath) + CountPath(PsqlPath);

        private static int CountPath(string path) => string.IsNullOrWhiteSpace(path) ? 0 : 1;
    }

    internal static class PostgreSqlToolLocator
    {
        public const string BinRootEnvironmentVariable = "EXPORTDOCMANAGER_POSTGRES_BIN";

        public static PostgreSqlToolPaths Resolve(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string configuredRoot = Environment.GetEnvironmentVariable(BinRootEnvironmentVariable) ?? string.Empty;
            var candidates = new[]
            {
                configuredRoot,
                Path.Combine(pathProvider.ToolRoot, "PostgreSQL", "bin"),
                Path.Combine(pathProvider.ToolRoot, "PostgreSQL")
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            foreach (string root in candidates)
            {
                string pgDump = ResolveToolPath(root, "pg_dump");
                string pgRestore = ResolveToolPath(root, "pg_restore");
                string psql = ResolveToolPath(root, "psql");
                if (!string.IsNullOrWhiteSpace(pgDump) ||
                    !string.IsNullOrWhiteSpace(pgRestore) ||
                    !string.IsNullOrWhiteSpace(psql))
                {
                    return new PostgreSqlToolPaths(root, pgDump, pgRestore, psql);
                }
            }

            return new PostgreSqlToolPaths(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        private static string ResolveToolPath(string root, string toolName)
        {
            string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            string path = Path.Combine(root, toolName + extension);
            return File.Exists(path) ? Path.GetFullPath(path) : string.Empty;
        }
    }
}
