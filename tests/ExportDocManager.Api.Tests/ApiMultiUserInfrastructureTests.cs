using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiMultiUserInfrastructureTests
    {
        [Fact]
        public void DatabaseSessionService_ShouldPersistOnlyTokenHashAndHonorRevocation()
        {
            using var database = TestDatabase.Create("sessions");
            int userId;
            int permissionTemplateId;
            using (var context = database.Factory.CreateDbContext())
            {
                var template = new PermissionTemplate
                {
                    Code = "test-session-template",
                    Name = "Session Template",
                    IsActive = true,
                    Modules =
                    [
                        new PermissionTemplateModule
                        {
                            ModuleKey = PermissionModuleCatalog.DocumentInvoices,
                            AccessLevel = PermissionAccessLevel.View
                        }
                    ]
                };
                var user = new User
                {
                    Username = "team-user",
                    PasswordHash = "hash",
                    FullName = "Team User",
                    Role = "User",
                    PermissionTemplate = template,
                    IsActive = true
                };
                context.Users.Add(user);
                context.SaveChanges();
                userId = user.Id;
                permissionTemplateId = template.Id;
            }

            var service = new DatabaseApiSessionTokenService(database.Factory);
            var issued = service.Issue(new User
            {
                Id = userId,
                Username = "team-user",
                PasswordHash = "hash",
                FullName = "Team User",
                Role = "User",
                PermissionTemplateId = permissionTemplateId,
                IsActive = true
            });

            using (var context = database.Factory.CreateDbContext())
            {
                var stored = Assert.Single(context.ApiUserSessions.AsNoTracking());
                Assert.NotEqual(issued.AccessToken, stored.TokenHash);
                Assert.Equal(64, stored.TokenHash.Length);
            }

            var validated = service.Validate(issued.AccessToken);
            Assert.Equal("team-user", validated?.Username);
            Assert.Equal(
                PermissionAccessLevel.View,
                validated?.EffectiveModuleAccess[PermissionModuleCatalog.DocumentInvoices]);
            Assert.Equal(1, service.RevokeUserSessions(userId));
            Assert.Null(service.Validate(issued.AccessToken));
            Assert.Equal(0, service.RevokeUserSessions(userId));
        }

        [Fact]
        public void DatabaseSessionService_ShouldRejectDisabledUser()
        {
            using var database = TestDatabase.Create("disabled-session");
            User user;
            using (var context = database.Factory.CreateDbContext())
            {
                user = new User
                {
                    Username = "disabled-user",
                    PasswordHash = "hash",
                    FullName = "Disabled User",
                    Role = "User",
                    IsActive = true
                };
                context.Users.Add(user);
                context.SaveChanges();
            }

            var service = new DatabaseApiSessionTokenService(database.Factory);
            var issued = service.Issue(user);
            using (var context = database.Factory.CreateDbContext())
            {
                var storedUser = context.Users.Single(item => item.Id == user.Id);
                storedUser.IsActive = false;
                context.SaveChanges();
            }

            Assert.Null(service.Validate(issued.AccessToken));
            using var verificationContext = database.Factory.CreateDbContext();
            Assert.NotNull(verificationContext.ApiUserSessions.Single().RevokedAt);
        }

        [Fact]
        public async Task DatabaseJobStore_ShouldNotDeleteJobsWrittenByAnotherServiceInstance()
        {
            using var database = TestDatabase.Create("job-store");
            var pathProvider = new RuntimeAppPathProvider(database.Root, database.Root);
            var settings = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider
            };
            var first = new ApiBackgroundJobService(pathProvider, settings, database.Factory);
            var second = new ApiBackgroundJobService(pathProvider, settings, database.Factory);

            first.Upsert(CreateTerminalJob("job-from-first", "alice"));
            second.Upsert(CreateTerminalJob("job-from-second", "bob"));

            using (var context = database.Factory.CreateDbContext())
            {
                var ids = context.ApiBackgroundJobs.AsNoTracking()
                    .Select(item => item.JobId)
                    .OrderBy(item => item)
                    .ToArray();
                Assert.Equal(new[] { "job-from-first", "job-from-second" }, ids);
            }

            Assert.True(await second.DeleteAsync("job-from-second"));
            using var verificationContext = database.Factory.CreateDbContext();
            Assert.Equal("job-from-first", Assert.Single(verificationContext.ApiBackgroundJobs).JobId);
        }

        [Fact]
        public async Task BackgroundJobRunner_ShouldEnforcePerUserLimit()
        {
            await AssertConcurrencyLimitAsync(
                new ApiBackgroundJobConcurrencyOptions
                {
                    GlobalLimit = 6,
                    PerUserLimit = 2,
                    BrowserLimit = 6
                },
                Enumerable.Range(0, 4).Select(_ => (Kind: "Test", User: "alice")).ToArray(),
                expectedMaximum: 2);
        }

        [Fact]
        public async Task BackgroundJobRunner_ShouldEnforceBrowserLimit()
        {
            await AssertConcurrencyLimitAsync(
                new ApiBackgroundJobConcurrencyOptions
                {
                    GlobalLimit = 6,
                    PerUserLimit = 2,
                    BrowserLimit = 2
                },
                Enumerable.Range(0, 4).Select(index => (Kind: "ReportDocument", User: $"user-{index}")).ToArray(),
                expectedMaximum: 2);
        }

        [Fact]
        public async Task BackgroundJobRunner_ShouldEnforceGlobalLimit()
        {
            await AssertConcurrencyLimitAsync(
                new ApiBackgroundJobConcurrencyOptions
                {
                    GlobalLimit = 4,
                    PerUserLimit = 2,
                    BrowserLimit = 6
                },
                Enumerable.Range(0, 6).Select(index => (Kind: "Test", User: $"user-{index}")).ToArray(),
                expectedMaximum: 4);
        }

        [Fact]
        public async Task BackgroundJobRunner_ShouldRejectExcessPerUserQueueAndRecoverCapacity()
        {
            var jobs = new ApiBackgroundJobService();
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobs,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance,
                new ApiBackgroundJobConcurrencyOptions
                {
                    GlobalLimit = 1,
                    PerUserLimit = 1,
                    BrowserLimit = 1,
                    GlobalQueueLimit = 4,
                    PerUserQueueLimit = 2
                });
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var running = runner.Enqueue("Test", "running", "alice", async (_, context) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(context.CancellationToken);
                return string.Empty;
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = runner.Enqueue("Test", "queued", "alice", (_, _) => Task.FromResult(string.Empty));
            var rejected = runner.Enqueue("Test", "rejected", "alice", (_, _) => Task.FromResult(string.Empty));

            Assert.Equal(BackgroundJobStatusCatalog.Failed, rejected.Status);
            Assert.Equal(ApiBackgroundJobQueueStatusCatalog.Rejected, rejected.StatusText);
            Assert.Contains("当前用户任务队列", rejected.ErrorMessage, StringComparison.Ordinal);

            release.TrySetResult(true);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, (await WaitForTerminalJobAsync(jobs, running.JobId)).Status);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, (await WaitForTerminalJobAsync(jobs, queued.JobId)).Status);

            var afterRelease = runner.Enqueue("Test", "after-release", "alice", (_, _) => Task.FromResult(string.Empty));
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, (await WaitForTerminalJobAsync(jobs, afterRelease.JobId)).Status);
        }

        [Fact]
        public async Task BackgroundJobService_ShouldBoundTerminalHistoryPerUser()
        {
            using var database = TestDatabase.Create("job-retention");
            var pathProvider = new RuntimeAppPathProvider(database.Root, database.Root);
            var settings = new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.PostgreSqlProvider };
            var service = new ApiBackgroundJobService(
                pathProvider,
                settings,
                database.Factory,
                new ApiBackgroundJobRetentionOptions
                {
                    RetentionDays = 30,
                    GlobalLimit = 100,
                    PerUserLimit = 20
                });

            for (int index = 0; index < 25; index++)
            {
                var timestamp = DateTimeOffset.UtcNow.AddMinutes(index);
                service.Upsert(new BackgroundJobSnapshot
                {
                    JobId = $"retained-{index:00}",
                    Kind = "Test",
                    Title = $"retained-{index:00}",
                    Status = BackgroundJobStatusCatalog.Succeeded,
                    ProgressPercent = 100,
                    RequestedBy = "alice",
                    CreatedAt = timestamp,
                    CompletedAt = timestamp,
                    CanCancel = false
                });
            }

            var page = await service.QueryAsync(new BackgroundJobQuery
            {
                RequestedBy = "alice",
                PageNumber = 1,
                PageSize = 100
            });
            Assert.Equal(20, page.TotalCount);
            Assert.DoesNotContain(page.Items, item => item.JobId == "retained-00");
            using var verificationContext = database.Factory.CreateDbContext();
            Assert.Equal(20, await verificationContext.ApiBackgroundJobs.CountAsync());
        }

        [Fact]
        public async Task SqliteSingleInstanceHostedService_ShouldRejectSecondLeaseAndReleaseOnStop()
        {
            using var database = TestDatabase.Create("sqlite-single-instance");
            var paths = new RuntimeAppPathProvider(database.Root, database.Root);
            var settings = new DatabaseConnectionSettings();
            using var first = new SqliteSingleInstanceHostedService(paths, settings);
            using var second = new SqliteSingleInstanceHostedService(paths, settings);

            await first.StartAsync(CancellationToken.None);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => second.StartAsync(CancellationToken.None));
            Assert.Contains("SQLite 单机版已经", exception.Message, StringComparison.Ordinal);
            second.Dispose();
            using var stillBlocked = new SqliteSingleInstanceHostedService(paths, settings);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => stillBlocked.StartAsync(CancellationToken.None));

            await first.StopAsync(CancellationToken.None);
            using var third = new SqliteSingleInstanceHostedService(paths, settings);
            await third.StartAsync(CancellationToken.None);
            await third.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task DatabaseInitializationCoordinator_ShouldRunOnceAndRetryAfterFailure()
        {
            var coordinator = new DatabaseInitializationCoordinator();
            int successfulCalls = 0;
            var concurrent = Enumerable.Range(0, 8).Select(_ => coordinator.InitializeOnceAsync(async () =>
            {
                Interlocked.Increment(ref successfulCalls);
                await Task.Delay(25);
                return DatabaseInitializationResult.Success();
            }));

            var results = await Task.WhenAll(concurrent);
            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.Equal(1, successfulCalls);

            var retryCoordinator = new DatabaseInitializationCoordinator();
            int retryCalls = 0;
            var failed = await retryCoordinator.InitializeOnceAsync(() =>
            {
                Interlocked.Increment(ref retryCalls);
                return Task.FromResult(DatabaseInitializationResult.Fail("temporary", false));
            });
            var succeeded = await retryCoordinator.InitializeOnceAsync(() =>
            {
                Interlocked.Increment(ref retryCalls);
                return Task.FromResult(DatabaseInitializationResult.Success());
            });

            Assert.False(failed.IsSuccess);
            Assert.True(succeeded.IsSuccess);
            Assert.Equal(2, retryCalls);
        }

        [Fact]
        public async Task BackgroundJobRunner_ShouldBlockQueuedJobAfterSubmittingUserIsDisabled()
        {
            var user = new User { Id = 42, Username = "queued-user", Role = "User", IsActive = true };
            var userService = new MutableUserService(user);
            var services = new ServiceCollection();
            services.AddSingleton<IUserService>(userService);
            using var provider = services.BuildServiceProvider();
            var jobs = new ApiBackgroundJobService();
            var runner = new ApiBackgroundJobRunner(
                jobs,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance,
                new FixedCurrentUserContext(user),
                new ApiBackgroundJobConcurrencyOptions { GlobalLimit = 2, PerUserLimit = 1, BrowserLimit = 2 });
            var blockerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBlocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool queuedDelegateExecuted = false;

            var blocker = runner.Enqueue("Test", "blocker", user.Username, async (_, context) =>
            {
                blockerStarted.TrySetResult(true);
                await releaseBlocker.Task.WaitAsync(context.CancellationToken);
                return string.Empty;
            });
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            userService.IsActive = false;
            var queued = runner.Enqueue("Test", "disabled", user.Username, (_, _) =>
            {
                queuedDelegateExecuted = true;
                return Task.FromResult(string.Empty);
            });
            releaseBlocker.TrySetResult(true);

            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, (await WaitForTerminalJobAsync(jobs, blocker.JobId)).Status);
            var blocked = await WaitForTerminalJobAsync(jobs, queued.JobId);
            Assert.Equal(BackgroundJobStatusCatalog.Failed, blocked.Status);
            Assert.Contains("账号已停用或不存在", blocked.ErrorMessage, StringComparison.Ordinal);
            Assert.False(queuedDelegateExecuted);
        }

        private static async Task AssertConcurrencyLimitAsync(
            ApiBackgroundJobConcurrencyOptions options,
            IReadOnlyList<(string Kind, string User)> requests,
            int expectedMaximum)
        {
            var jobs = new ApiBackgroundJobService();
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobs,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance,
                options);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var limitReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int running = 0;
            int maximum = 0;

            var snapshots = requests.Select((request, index) => runner.Enqueue(
                request.Kind,
                $"并发测试 {index}",
                request.User,
                async (_, context) =>
                {
                    int current = Interlocked.Increment(ref running);
                    UpdateMaximum(ref maximum, current);
                    if (current >= expectedMaximum)
                    {
                        limitReached.TrySetResult(true);
                    }

                    try
                    {
                        await release.Task.WaitAsync(context.CancellationToken);
                        return string.Empty;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref running);
                    }
                })).ToArray();

            await limitReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            Assert.Equal(expectedMaximum, Volatile.Read(ref maximum));

            release.TrySetResult(true);
            foreach (var snapshot in snapshots)
            {
                var completed = await WaitForTerminalJobAsync(jobs, snapshot.JobId);
                Assert.Equal(BackgroundJobStatusCatalog.Succeeded, completed.Status);
            }
        }

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref maximum);
                if (candidate <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
        }

        private static async Task<BackgroundJobSnapshot> WaitForTerminalJobAsync(
            ApiBackgroundJobService jobs,
            string jobId)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var job = await jobs.GetAsync(jobId);
                if (job != null && BackgroundJobStatusCatalog.IsTerminal(job.Status))
                {
                    return job;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException($"Background job {jobId} did not finish in time.");
        }

        private static BackgroundJobSnapshot CreateTerminalJob(string jobId, string requestedBy) => new()
        {
            JobId = jobId,
            Kind = "Test",
            Title = jobId,
            Status = BackgroundJobStatusCatalog.Succeeded,
            ProgressPercent = 100,
            RequestedBy = requestedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            CanCancel = false
        };

        private sealed class TestDatabase : IDisposable
        {
            private TestDatabase(string root, TestDbContextFactory factory)
            {
                Root = root;
                Factory = factory;
            }

            public string Root { get; }

            public TestDbContextFactory Factory { get; }

            public static TestDatabase Create(string name)
            {
                string root = Path.Combine(FindWorkspaceRoot(), ".codex-runtime", "multi-user-tests", $"{name}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(root);
                string databasePath = Path.Combine(root, "test.db");
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(DbHelper.BuildConnectionString(databasePath))
                    .Options;
                var factory = new TestDbContextFactory(options);
                using var context = factory.CreateDbContext();
                context.Database.EnsureCreated();
                return new TestDatabase(root, factory);
            }

            public void Dispose()
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }

            private static string FindWorkspaceRoot()
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

                return AppContext.BaseDirectory;
            }
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
        {
            private readonly DbContextOptions<AppDbContext> _options;

            public TestDbContextFactory(DbContextOptions<AppDbContext> options)
            {
                _options = options;
            }

            public AppDbContext CreateDbContext() => new(_options);
        }

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User user) => CurrentUser = user;
            public User CurrentUser { get; }
        }

        private sealed class MutableUserService : IUserService
        {
            private readonly User _user;

            public MutableUserService(User user) => _user = user;

            public bool IsActive { get; set; } = true;

            public Task<User> AuthenticateAsync(string username, string password) =>
                Task.FromResult(GetActive(username));

            public Task<User> GetUserByUsernameAsync(string username) => Task.FromResult(GetActive(username));

            public Task<User> GetActiveUserByIdAsync(int userId, CancellationToken cancellationToken = default) =>
                Task.FromResult(userId == _user.Id && IsActive ? _user : null);

            public Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<User>>([_user]);

            public Task<int> SaveUserAsync(User user, string resetPassword = "", CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            private User GetActive(string username) =>
                IsActive && string.Equals(username, _user.Username, StringComparison.OrdinalIgnoreCase) ? _user : null;
        }
    }
}
