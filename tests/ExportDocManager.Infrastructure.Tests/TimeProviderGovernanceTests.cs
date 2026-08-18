using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class TimeProviderGovernanceTests
{
    private static readonly string[] DirectSystemClockPatterns =
    [
        "DateTimeOffset.UtcNow",
        "DateTimeOffset.Now",
        "DateTime.UtcNow",
        "DateTime.Now",
        "DateTime.Today"
    ];

    [Fact]
    public void ProductionSource_ShouldUseTimeProviderInsteadOfDirectSystemClockAccess()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        string[] violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, lineNumber = index + 1 })
                .Where(item => DirectSystemClockPatterns.Any(pattern =>
                    item.line.Contains(pattern, StringComparison.Ordinal)))
                .Select(item => $"{Path.GetRelativePath(sourceRoot, item.path)}:{item.lineNumber}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public async Task AppDbContext_ShouldApplyInjectedTimeAtPersistenceBoundary()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new AppDbContext(options, clock);
        var template = new EmailTemplate { Name = "clock-policy" };
        context.EmailTemplates.Add(template);

        await context.SaveChangesAsync();

        Assert.Equal(clock.GetUtcNow(), template.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), template.UpdatedAt);

        DateTimeOffset createdAt = template.CreatedAt;
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        template.Subject = "updated";
        await context.SaveChangesAsync();

        Assert.Equal(createdAt, template.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), template.UpdatedAt);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate ExportDocManager.sln.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
