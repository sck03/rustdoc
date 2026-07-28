using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Services.Infrastructure
{
    /// <summary>
    /// Coordinates database baseline validation, first-administrator bootstrap, and idempotent seed data.
    /// Provider-specific schema creation lives in <see cref="DatabaseSchemaBaseline"/>.
    /// </summary>
    public sealed class DatabaseInitializationService : IDatabaseInitializationService
    {
        private const long PostgreSqlInitializationLockId = 73190520260718;

        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly DatabaseInitializationCoordinator _coordinator;
        private readonly bool _requireBootstrapToken;
        private readonly string _expectedBootstrapToken;

        public DatabaseInitializationService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            DatabaseConnectionSettings databaseSettings,
            DatabaseInitializationCoordinator coordinator)
            : this(dbContextFactory, databaseSettings, coordinator, false, string.Empty)
        {
        }

        public DatabaseInitializationService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            DatabaseConnectionSettings databaseSettings,
            DatabaseInitializationCoordinator coordinator,
            bool requireBootstrapToken,
            string expectedBootstrapToken)
        {
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _requireBootstrapToken = requireBootstrapToken;
            _expectedBootstrapToken = expectedBootstrapToken?.Trim() ?? string.Empty;
        }

        public Task<DatabaseInitializationResult> InitializeAsync(
            string username,
            string password,
            string bootstrapToken = null)
        {
            return _coordinator.InitializeOnceAsync(() =>
                InitializeCoreAsync(username, password, bootstrapToken));
        }

        private async Task<DatabaseInitializationResult> InitializeCoreAsync(
            string username,
            string password,
            string bootstrapToken)
        {
            bool usesPostgreSql = DatabaseModeHelper.UsesPostgreSql(_databaseSettings);
            bool advisoryLockAcquired = false;
            AppDbContext context = null;

            try
            {
                context = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
                if (usesPostgreSql)
                {
                    await context.Database.OpenConnectionAsync().ConfigureAwait(false);
                    await context.Database.ExecuteSqlRawAsync(
                        $"SELECT pg_advisory_lock({PostgreSqlInitializationLockId});").ConfigureAwait(false);
                    advisoryLockAcquired = true;
                }
                else
                {
                    await ConfigureSingleProcessSqliteAsync(context).ConfigureAwait(false);
                }

                await DatabaseSchemaBaseline.EnsureCurrentAsync(context, usesPostgreSql).ConfigureAwait(false);

                bool requiresInitialAdministrator = usesPostgreSql &&
                    !await context.Users.AsNoTracking().AnyAsync().ConfigureAwait(false);
                if (requiresInitialAdministrator && _requireBootstrapToken &&
                    !FixedTimeEquals(_expectedBootstrapToken, bootstrapToken))
                {
                    return DatabaseInitializationResult.Fail(
                        "共享数据库首次初始化需要有效的部署初始化令牌。请联系部署管理员。",
                        shouldResetPassword: false,
                        isAuthenticationFailure: true);
                }

                DbSeeder.SeedAuxiliaryData(
                    context,
                    _databaseSettings,
                    ResolveInitialAdminPassword(usesPostgreSql, username, password));

                return DatabaseInitializationResult.Success();
            }
            catch (InvalidOperationException ex)
            {
                return DatabaseInitializationResult.Fail(ex.Message, shouldResetPassword: false);
            }
            catch (SqliteException ex)
            {
                return DatabaseInitializationResult.Fail(
                    "本地数据库初始化失败。请确认运行数据根可写；如这是预发布旧数据库，请备份后删除并重新初始化。\n\n" + ex.Message,
                    shouldResetPassword: false);
            }
            catch (NpgsqlException ex) when (usesPostgreSql)
            {
                return DatabaseInitializationResult.Fail(
                    "连接或初始化共享数据库失败，请检查 PostgreSQL 地址、端口、数据库名、账号密码、网络和建表权限。\n\n" + ex.Message,
                    shouldResetPassword: false);
            }
            catch (DbException ex) when (usesPostgreSql)
            {
                return DatabaseInitializationResult.Fail(
                    "连接或初始化共享数据库失败，请检查数据库服务状态和连接配置。\n\n" + ex.Message,
                    shouldResetPassword: false);
            }
            finally
            {
                if (advisoryLockAcquired && context != null)
                {
                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(
                            $"SELECT pg_advisory_unlock({PostgreSqlInitializationLockId});").ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                if (context != null)
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task ConfigureSingleProcessSqliteAsync(AppDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=10000;").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);
        }

        internal static string ResolveInitialAdminPassword(
            bool usesPostgreSql,
            string username,
            string password)
        {
            return usesPostgreSql &&
                   string.Equals((username ?? string.Empty).Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                ? password ?? string.Empty
                : string.Empty;
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? string.Empty));
            byte[] actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual ?? string.Empty));
            return !string.IsNullOrWhiteSpace(expected) &&
                   CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
    }
}
