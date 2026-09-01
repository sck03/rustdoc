using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Services.SingleWindow;

public sealed partial class SingleWindowDocumentPersistenceService
{
    private async Task<int> ReadCurrentCustomsCooRevisionAsync(
        int sourceInvoiceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            return await context.CustomsCooDocuments
                .AsNoTracking()
                .Where(document => document.SourceInvoiceId == sourceInvoiceId)
                .Select(document => (int?)document.DraftRevision)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Never replace the original write conflict with a best-effort
            // diagnostic query failure.  Zero explicitly means "unknown".
            return 0;
        }
    }

    private async Task<int> ReadCurrentAgentConsignmentRevisionAsync(
        int sourceInvoiceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            return await context.AgentConsignmentDocuments
                .AsNoTracking()
                .Where(document => document.SourceInvoiceId == sourceInvoiceId)
                .Select(document => (int?)document.DraftRevision)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsSourceInvoiceUniqueConflict(
        Exception exception,
        string tableName)
    {
        foreach (Exception current in EnumerateExceptions(exception))
        {
            if (current is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                ContainsSourceInvoiceMarker(
                    tableName,
                    postgres.ConstraintName,
                    postgres.MessageText,
                    postgres.Detail))
            {
                return true;
            }

            if (current is SqliteException sqlite &&
                sqlite.SqliteErrorCode == 19 &&
                ContainsSourceInvoiceMarker(tableName, sqlite.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSourceInvoiceMarker(
        string tableName,
        params string?[] values) =>
        values.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
            value.Contains(nameof(Models.Entities.CustomsCooDocument.SourceInvoiceId), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Exception> EnumerateExceptions(Exception root)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.TryPop(out Exception? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions.Reverse())
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException != null)
            {
                pending.Push(current.InnerException);
            }
        }
    }
}
