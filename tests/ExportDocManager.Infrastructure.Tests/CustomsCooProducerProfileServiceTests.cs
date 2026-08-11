using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using ExportDocManager.Services.SingleWindow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class CustomsCooProducerProfileServiceTests
{
    [Fact]
    public async Task RememberProfilesAsync_ShouldBatchLookupAndSaveOnce()
    {
        using var factory = new CountingDbContextFactory();
        await using (AppDbContext seedContext = await factory.CreateDbContextAsync())
        {
            seedContext.CustomsCooProducerProfiles.Add(new CustomsCooProducerProfile
            {
                CiqRegNo = "EXISTING-001",
                PrdcEtpsName = "Existing Producer",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                LastUsedAt = DateTime.Now
            });
            await seedContext.SaveChangesAsync();
        }
        factory.ResetCounters();

        var service = new CustomsCooProducerProfileService(factory);
        int remembered = await service.RememberProfilesAsync(
        [
            new CustomsCooProducerProfileInput
            {
                CiqRegNo = " existing-001 ",
                PrdcEtpsName = "Existing Producer",
                Producer = "Updated Contact"
            },
            new CustomsCooProducerProfileInput
            {
                CiqRegNo = "EXISTING-001",
                PrdcEtpsName = "Duplicate Input"
            },
            new CustomsCooProducerProfileInput
            {
                PrdcEtpsName = "New Producer",
                ProducerEmail = "new@example.test"
            }
        ]);

        Assert.Equal(2, remembered);
        Assert.Equal(1, factory.CreatedContextCount);
        Assert.Equal(1, factory.SaveChangesCount);

        await using AppDbContext verificationContext = await factory.CreateDbContextAsync();
        Assert.Equal(2, await verificationContext.CustomsCooProducerProfiles.CountAsync());
        Assert.Equal(
            "Updated Contact",
            (await verificationContext.CustomsCooProducerProfiles.SingleAsync(
                item => item.CiqRegNo == "EXISTING-001")).Producer);
    }

    private sealed class CountingDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly CountingSaveChangesInterceptor _interceptor = new();
        private readonly DbContextOptions<AppDbContext> _options;

        public CountingDbContextFactory()
        {
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(_interceptor)
                .Options;
        }

        public int CreatedContextCount { get; private set; }

        public int SaveChangesCount => _interceptor.SaveChangesCount;

        public AppDbContext CreateDbContext()
        {
            CreatedContextCount++;
            return new AppDbContext(_options);
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public void ResetCounters()
        {
            CreatedContextCount = 0;
            _interceptor.Reset();
        }

        public void Dispose()
        {
            using var context = new AppDbContext(_options);
            context.Database.EnsureDeleted();
        }
    }

    private sealed class CountingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int SaveChangesCount { get; private set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            SaveChangesCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return ValueTask.FromResult(result);
        }

        public void Reset() => SaveChangesCount = 0;
    }
}
