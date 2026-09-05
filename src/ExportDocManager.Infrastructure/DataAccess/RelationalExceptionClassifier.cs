using Microsoft.Data.Sqlite;
using Npgsql;

namespace ExportDocManager.DataAccess
{
    internal static class RelationalExceptionClassifier
    {
        public static bool IsUniqueConstraintViolation(Exception exception) =>
            Contains(exception, current =>
                current is PostgresException postgres &&
                    postgres.SqlState == PostgresErrorCodes.UniqueViolation ||
                current is SqliteException sqlite && sqlite.SqliteErrorCode == 19);

        public static bool IsWriteContention(Exception exception) =>
            Contains(exception, current =>
                current is PostgresException postgres &&
                    postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected ||
                current is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6);

        private static bool Contains(Exception exception, Func<Exception, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(exception);
            ArgumentNullException.ThrowIfNull(predicate);

            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (predicate(current))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
