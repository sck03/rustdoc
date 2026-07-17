namespace ExportDocManager.DataAccess
{
    public static class DatabaseModeHelper
    {
        public const string SqliteProvider = DatabaseConnectionSettings.SqliteProvider;
        public const string PostgreSqlProvider = DatabaseConnectionSettings.PostgreSqlProvider;

        public static string NormalizeProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return SqliteProvider;
            }

            var trimmed = provider.Trim();
            if (string.Equals(trimmed, SqliteProvider, StringComparison.OrdinalIgnoreCase))
            {
                return SqliteProvider;
            }

            if (string.Equals(trimmed, PostgreSqlProvider, StringComparison.OrdinalIgnoreCase))
            {
                return PostgreSqlProvider;
            }

            throw new ArgumentException(
                $"不支持的数据库类型: {provider}。可选值为 {SqliteProvider} 或 {PostgreSqlProvider}。",
                nameof(provider));
        }

        public static bool UsesPostgreSql(DatabaseConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            return string.Equals(
                NormalizeProvider(settings.Provider),
                PostgreSqlProvider,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesSharedDatabase(DatabaseConnectionSettings settings)
        {
            return UsesPostgreSql(settings) &&
                   HasCompletePostgreSqlConfiguration(settings);
        }

        public static string GetCurrentModeText(DatabaseConnectionSettings settings)
        {
            if (UsesSharedDatabase(settings))
            {
                return "当前是数据库共享模式（PostgreSQL）";
            }

            if (UsesPostgreSql(settings))
            {
                return "当前已选择数据库共享模式（PostgreSQL），但服务器、数据库名或账号尚未配置完成";
            }

            return "当前是单机模式（SQLite）";
        }

        public static string Validate(DatabaseConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (UsesPostgreSql(settings) &&
                !HasCompletePostgreSqlConfiguration(settings))
            {
                return "当前已切换到数据库共享模式，但 PostgreSQL 服务器、数据库名或账号尚未配置完整。请到系统设置补全后再保存并重启程序。";
            }

            return string.Empty;
        }

        public static bool HasCompletePostgreSqlConfiguration(DatabaseConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (!UsesPostgreSql(settings))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(NormalizePostgreSqlText(settings.PostgreSqlHost)) &&
                   !string.IsNullOrWhiteSpace(NormalizePostgreSqlText(settings.PostgreSqlDatabase)) &&
                   !string.IsNullOrWhiteSpace(NormalizePostgreSqlText(settings.PostgreSqlUsername));
        }

        private static string NormalizePostgreSqlText(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
