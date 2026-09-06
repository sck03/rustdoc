using ExportDocManager.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.Infrastructure.Tests;

internal sealed class InMemoryTestDatabase : IDbContextFactory<AppDbContext>, IDisposable, IAsyncDisposable
{
    private readonly DbContextOptions<AppDbContext> _options;

    public InMemoryTestDatabase(bool ignoreTransactions = false)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
        if (ignoreTransactions) builder.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        _options = builder.Options;
    }

    public AppDbContext CreateDbContext() => new(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }

    public void Dispose()
    {
        using var context = CreateDbContext();
        context.Database.EnsureDeleted();
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
    }
}

internal sealed class SqliteTestDatabase : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Pooling=False");
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteTestDatabase(params IInterceptor[] interceptors)
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)
            .AddInterceptors(interceptors).Options;
        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
    public void Dispose() => _connection.Dispose();
}
