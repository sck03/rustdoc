#nullable enable

using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Data;

internal static class ScopedExistingNameLoader
{
    // Keep each IN predicate below SQLite's default parameter ceiling while
    // still avoiding a full-table name scan for imports.
    private const int QueryBatchSize = 400;

    public static async Task<HashSet<string>> LoadAsync(
        IQueryable<string> scopedNames,
        IEnumerable<string> candidateNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopedNames);
        ArgumentNullException.ThrowIfNull(candidateNames);

        string[] normalizedCandidates = candidateNames
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existingNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (string[] batch in normalizedCandidates.Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] matches = await scopedNames
                .Where(name => batch.Contains(name.ToUpper()))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (string match in matches)
            {
                existingNames.Add(Normalize(match));
            }
        }

        return existingNames;
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormC).ToUpperInvariant();
}
