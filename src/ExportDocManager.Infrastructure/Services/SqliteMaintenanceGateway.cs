using Microsoft.Data.Sqlite;

namespace ExportDocManager.Services.Infrastructure
{
    internal static class SqliteMaintenanceGateway
    {
        private const string QuickCheckCommandText = "PRAGMA quick_check;";

        public static async Task<string> RunQuickCheckAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = QuickCheckCommandText;
            return (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString()
                ?? string.Empty;
        }

        public static string RunQuickCheck(SqliteConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            using var command = connection.CreateCommand();
            command.CommandText = QuickCheckCommandText;
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }
    }
}
