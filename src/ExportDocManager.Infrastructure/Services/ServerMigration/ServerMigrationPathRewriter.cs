using ExportDocManager.DataAccess;
using Npgsql;

namespace ExportDocManager.Services.Infrastructure
{
    internal static class ServerMigrationPathRewriter
    {
        private static readonly IReadOnlyList<(string Table, string Column)> ManagedColumns =
        [
            ("Exporters", "DocSealPath"),
            ("Exporters", "CustomsSealPath"),
            ("Invoices", "LetterOfCreditSourcePath"),
            ("Invoices", "ShippingMarksImage"),
            ("CustomsCooAttachments", "FilePath"),
            ("SwHandoffPackageRecords", "FilePath"),
            ("SwSubmissionBatches", "SubmitPackagePath"),
            ("SwSubmissionBatches", "WorkingDirectoryPath"),
            ("SwSubmissionBatches", "LastReceiptPackagePath"),
            ("SwSubmissionBatches", "ClientDispatchPath")
        ];

        public static async Task RewriteDatabasePathsAsync(
            DatabaseConnectionSettings settings,
            string sourceRoot,
            string targetRoot,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(targetRoot))
            {
                return;
            }
            sourceRoot = sourceRoot.TrimEnd('/', '\\');
            targetRoot = targetRoot.TrimEnd('/', '\\');
            if (string.Equals(sourceRoot, targetRoot, StringComparison.Ordinal))
            {
                return;
            }

            await using var connection = new NpgsqlConnection(
                DbHelper.BuildPostgreSqlConnectionString(settings));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                string separator = Path.DirectorySeparatorChar.ToString();
                foreach ((string table, string column) in ManagedColumns)
                {
                    await using NpgsqlCommand update = connection.CreateCommand();
                    update.Transaction = transaction;
                    string identifier = QuoteIdentifier(column);
                    string remainder = $"substring({identifier} from char_length(@source) + 1)";
                    string relative = $"ltrim({remainder}, '/' || chr(92))";
                    update.CommandText = $"""
UPDATE {QuoteIdentifier(table)}
SET {identifier} = @target ||
    CASE
      WHEN {relative} = '' THEN ''
      ELSE @separator || replace(replace({relative}, chr(92), @separator), '/', @separator)
    END
WHERE {identifier} IS NOT NULL
  AND lower(left({identifier}, char_length(@source))) = lower(@source)
  AND (
    char_length({identifier}) = char_length(@source)
    OR substring({identifier} from char_length(@source) + 1 for 1) = '/'
    OR ascii(nullif(substring({identifier} from char_length(@source) + 1 for 1), '')) = 92
  );
""";
                    update.Parameters.AddWithValue("source", sourceRoot);
                    update.Parameters.AddWithValue("target", targetRoot);
                    update.Parameters.AddWithValue("separator", separator);
                    await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        internal static string RewriteManagedPath(
            string value,
            string sourceRoot,
            string targetRoot,
            char targetSeparator)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(sourceRoot) ||
                string.IsNullOrWhiteSpace(targetRoot))
            {
                return value;
            }

            string normalizedSource = NormalizeSeparators(sourceRoot).TrimEnd('/');
            string normalizedValue = NormalizeSeparators(value);
            if (!normalizedValue.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                normalizedValue.Length > normalizedSource.Length &&
                normalizedValue[normalizedSource.Length] != '/')
            {
                return value;
            }

            string relative = normalizedValue[normalizedSource.Length..].TrimStart('/');
            string normalizedTarget = NormalizeSeparators(targetRoot).TrimEnd('/');
            string combined = string.IsNullOrEmpty(relative)
                ? normalizedTarget
                : $"{normalizedTarget}/{relative}";
            return combined.Replace('/', targetSeparator);
        }

        private static string NormalizeSeparators(string value) =>
            value.Replace('\\', '/');

        private static string QuoteIdentifier(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
