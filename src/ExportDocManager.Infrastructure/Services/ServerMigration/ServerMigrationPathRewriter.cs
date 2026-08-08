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
            ("SwSubmissionBatches", "ClientDispatchPath"),
            ("ApiBackgroundJobs", "OutputPath"),
            ("SwClientProfiles", "CustomsCooClientRootPath"),
            ("SwClientProfiles", "AgentConsignmentClientRootPath")
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
            bool sourcePathCaseSensitive = IsSourcePathCaseSensitive(sourceRoot);
            StringComparison sourceComparison = sourcePathCaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            if (string.Equals(sourceRoot, targetRoot, sourceComparison))
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
                    string sourcePrefixPredicate = sourcePathCaseSensitive
                        ? $"left({identifier}, char_length(@source)) = @source"
                        : $"lower(left({identifier}, char_length(@source))) = lower(@source)";
                    update.CommandText = $"""
UPDATE {QuoteIdentifier(table)}
SET {identifier} = @target ||
    CASE
      WHEN {relative} = '' THEN ''
      ELSE @separator || replace(replace({relative}, chr(92), @separator), '/', @separator)
    END
WHERE {identifier} IS NOT NULL
  AND {sourcePrefixPredicate}
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
            StringComparison sourceComparison = IsSourcePathCaseSensitive(normalizedSource)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            if (!normalizedValue.StartsWith(normalizedSource, sourceComparison) ||
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

        private static bool IsSourcePathCaseSensitive(string sourceRoot)
        {
            string normalized = NormalizeSeparators(sourceRoot ?? string.Empty);
            bool isWindowsDrivePath = normalized.Length >= 3 &&
                char.IsAsciiLetter(normalized[0]) &&
                normalized[1] == ':' &&
                normalized[2] == '/';
            bool isWindowsUncPath = normalized.StartsWith("//", StringComparison.Ordinal);
            return !isWindowsDrivePath && !isWindowsUncPath;
        }

        private static string QuoteIdentifier(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
