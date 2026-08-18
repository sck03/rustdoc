using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Utils;

internal static class SqliteFtsSearch
{
    public static IQueryable<int> QueryIds(
        AppDbContext context,
        string tableName,
        string idColumn,
        string? keyword,
        IReadOnlyList<string> containsColumns,
        IReadOnlyList<string>? numericPrefixColumns = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateIdentifier(tableName);
        ValidateIdentifier(idColumn);
        foreach (string column in containsColumns)
        {
            ValidateIdentifier(column);
        }
        if (numericPrefixColumns != null)
        {
            foreach (string column in numericPrefixColumns)
            {
                ValidateIdentifier(column);
            }
        }

        string[] tokens = TextSearchHelper.Tokenize(keyword);
        if (tokens.Length == 0)
        {
            string selectAllSql =
                $"SELECT CAST(\"{idColumn}\" AS INTEGER) AS \"Value\" FROM \"{tableName}\"";
            return context.Database.SqlQueryRaw<int>(selectAllSql);
        }

        var parameters = new object[tokens.Length];
        var tokenClauses = new string[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            bool usePrefix = numericPrefixColumns != null && token.Any(char.IsDigit);
            IReadOnlyList<string> columns = usePrefix ? numericPrefixColumns! : containsColumns;
            string escapedToken = EfTextSearchExtensions.EscapeLikePattern(token);
            parameters[index] = usePrefix ? $"{escapedToken}%" : $"%{escapedToken}%";
            string escapeClause = token.IndexOfAny(['%', '_', '\\']) >= 0 ? " ESCAPE '\\'" : string.Empty;
            tokenClauses[index] = "(" + string.Join(
                " OR ",
                columns.Select(column => $"\"{column}\" LIKE {{{index}}}{escapeClause}")) + ")";
        }

        string sql =
            $"SELECT CAST(\"{idColumn}\" AS INTEGER) AS \"Value\" " +
            $"FROM \"{tableName}\" WHERE {string.Join(" AND ", tokenClauses)}";
        return context.Database.SqlQueryRaw<int>(sql, parameters);
    }

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !char.IsAsciiLetter(value[0]) ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("SQLite FTS identifier is invalid.", nameof(value));
        }
    }
}
