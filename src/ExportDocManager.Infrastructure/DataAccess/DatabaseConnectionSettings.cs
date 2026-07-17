namespace ExportDocManager.DataAccess
{
    public sealed class DatabaseConnectionSettings
    {
        public const string SqliteProvider = "Sqlite";
        public const string PostgreSqlProvider = "PostgreSQL";
        public const string DefaultSqliteDatabaseFileName = "data.db";
        public const int DefaultPostgreSqlPort = 5432;

        public string Provider { get; set; } = SqliteProvider;
        public string SqliteDatabaseFileName { get; set; } = DefaultSqliteDatabaseFileName;
        public string PostgreSqlHost { get; set; } = string.Empty;
        public int PostgreSqlPort { get; set; } = DefaultPostgreSqlPort;
        public string PostgreSqlDatabase { get; set; } = string.Empty;
        public string PostgreSqlUsername { get; set; } = string.Empty;
        public string PostgreSqlPassword { get; set; } = string.Empty;
        public string PostgreSqlAdditionalOptions { get; set; } = string.Empty;
    }
}
