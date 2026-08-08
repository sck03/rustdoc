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
            bool? sourcePathCaseSensitive,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(targetRoot))
            {
                return;
            }
            PathRootModel source = CreateRootModel(sourceRoot, sourcePathCaseSensitive);
            PathRootModel target = CreateRootModel(targetRoot, null);
            sourceRoot = source.Normalized;
            targetRoot = target.Normalized;
            StringComparison sourceComparison = source.CaseSensitive
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
                    string sourcePrefixPredicate = source.CaseSensitive
                        ? $"left({identifier}, char_length(@source)) = @source"
                        : $"lower(left({identifier}, char_length(@source))) = lower(@source)";
                    string sourceBoundaryPredicate = sourceRoot.EndsWith("/", StringComparison.Ordinal)
                        ? "TRUE"
                        : $"""
char_length({identifier}) = char_length(@source)
    OR substring({identifier} from char_length(@source) + 1 for 1) = '/'
    OR ascii(nullif(substring({identifier} from char_length(@source) + 1 for 1), '')) = 92
""";
                    update.CommandText = $"""
UPDATE {QuoteIdentifier(table)}
SET {identifier} = @target ||
    CASE
      WHEN {relative} = '' THEN ''
      ELSE @separator || replace(replace({relative}, chr(92), @separator), '/', @separator)
    END
WHERE {identifier} IS NOT NULL
  AND {sourcePrefixPredicate}
  AND ({sourceBoundaryPredicate});
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
            char targetSeparator,
            bool? sourcePathCaseSensitive = null)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(sourceRoot) ||
                string.IsNullOrWhiteSpace(targetRoot))
            {
                return value;
            }

            PathRootModel source = CreateRootModel(sourceRoot, sourcePathCaseSensitive);
            PathRootModel target = CreateRootModel(targetRoot, null);
            string normalizedSource = source.Normalized;
            string normalizedValue = NormalizeSeparators(value);
            StringComparison sourceComparison = source.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            if (!normalizedValue.StartsWith(normalizedSource, sourceComparison) ||
                !normalizedSource.EndsWith("/", StringComparison.Ordinal) &&
                normalizedValue.Length > normalizedSource.Length &&
                normalizedValue[normalizedSource.Length] != '/')
            {
                return value;
            }

            string relative = normalizedValue[normalizedSource.Length..].TrimStart('/');
            string normalizedTarget = target.Normalized;
            string combined = string.IsNullOrEmpty(relative)
                ? normalizedTarget
                : normalizedTarget.EndsWith("/", StringComparison.Ordinal)
                    ? $"{normalizedTarget}{relative}"
                    : $"{normalizedTarget}/{relative}";
            return combined.Replace('/', targetSeparator);
        }

        private static string NormalizeSeparators(string value) =>
            value.Replace('\\', '/');

        private static PathRootModel CreateRootModel(string value, bool? caseSensitive)
        {
            string normalized = NormalizeSeparators(value?.Trim() ?? string.Empty);
            if (normalized == "/")
            {
                return new PathRootModel("/", caseSensitive ?? true);
            }

            bool isWindowsDrivePath = normalized.Length >= 3 &&
                char.IsAsciiLetter(normalized[0]) &&
                normalized[1] == ':' &&
                normalized[2] == '/';
            bool isWindowsUncPath = normalized.StartsWith("//", StringComparison.Ordinal);
            int minimumLength = isWindowsDrivePath ? 3 : isWindowsUncPath ? GetUncRootLength(normalized) : 1;
            while (normalized.Length > minimumLength && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            bool inferredCaseSensitive = !isWindowsDrivePath &&
                                         !isWindowsUncPath &&
                                         !LooksLikeDefaultMacPath(normalized);
            return new PathRootModel(normalized, caseSensitive ?? inferredCaseSensitive);
        }

        private static int GetUncRootLength(string normalized)
        {
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length < 2
                ? 2
                : 2 + segments[0].Length + 1 + segments[1].Length;
        }

        private static bool LooksLikeDefaultMacPath(string normalized) =>
            normalized.Equals("/Users", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/Users/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("/Volumes", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/Volumes/", StringComparison.OrdinalIgnoreCase);

        private sealed record PathRootModel(string Normalized, bool CaseSensitive);

        private static string QuoteIdentifier(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
