using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.DataAccess
{
    public static class AppDbContextExecution
    {
        public static Task ExecuteInTransactionAsync(
            IDbContextFactory<AppDbContext> contextFactory,
            Func<AppDbContext, CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            return ExecuteInTransactionAsync(
                contextFactory,
                async (context, token) =>
                {
                    await operation(context, token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken);
        }

        public static async Task<T> ExecuteInTransactionAsync<T>(
            IDbContextFactory<AppDbContext> contextFactory,
            Func<AppDbContext, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(contextFactory);
            ArgumentNullException.ThrowIfNull(operation);

            await using var strategyContext = await contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var executionStrategy = strategyContext.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var context = await contextFactory
                    .CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var transaction = await context.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    var result = await operation(context, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }).ConfigureAwait(false);
        }
    }
}
