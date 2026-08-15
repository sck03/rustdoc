using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;

namespace ExportDocManager.Services.Infrastructure
{
    /// <summary>
    /// Coordinates database baseline validation, first-administrator bootstrap, and idempotent seed data.
    /// Provider-specific schema creation lives in <see cref="DatabaseSchemaBaseline"/>.
    /// </summary>
    public sealed class DatabaseInitializationService : IDatabaseInitializationService
    {
        private const long PostgreSqlInitializationLockId = 73190520260718;
        private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(60);

        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly DatabaseInitializationCoordinator _coordinator;
        private readonly bool _requireBootstrapToken;
        private readonly string _expectedBootstrapToken;
        private readonly IAppPathProvider? _pathProvider;

        public DatabaseInitializationService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            DatabaseConnectionSettings databaseSettings,
            DatabaseInitializationCoordinator coordinator)
            : this(dbContextFactory, databaseSettings, coordinator, false, string.Empty, null)
        {
        }

        public DatabaseInitializationService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            DatabaseConnectionSettings databaseSettings,
            DatabaseInitializationCoordinator coordinator,
            bool requireBootstrapToken,
            string expectedBootstrapToken)
            : this(
                dbContextFactory,
                databaseSettings,
                coordinator,
                requireBootstrapToken,
                expectedBootstrapToken,
                null)
        {
        }

        public DatabaseInitializationService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            DatabaseConnectionSettings databaseSettings,
            DatabaseInitializationCoordinator coordinator,
            bool requireBootstrapToken,
            string expectedBootstrapToken,
            IAppPathProvider? pathProvider)
        {
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _requireBootstrapToken = requireBootstrapToken;
            _expectedBootstrapToken = expectedBootstrapToken?.Trim() ?? string.Empty;
            _pathProvider = pathProvider;
        }

        public async Task<DatabaseInitializationResult> InitializeAsync(
            string username,
            string password,
            string? bootstrapToken = null,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(InitializationTimeout);
            try
            {
                return await _coordinator.InitializeOnceAsync(
                    token => InitializeCoreAsync(username, password, bootstrapToken, token),
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DatabaseInitializationResult.Fail(
                    "数据库初始化超时，请稍后重试或联系管理员检查数据库服务。",
                    shouldResetPassword: false);
            }
        }

        private async Task<DatabaseInitializationResult> InitializeCoreAsync(
            string username,
            string password,
            string? bootstrapToken,
            CancellationToken cancellationToken)
        {
            bool usesPostgreSql = DatabaseModeHelper.UsesPostgreSql(_databaseSettings);
            bool advisoryLockAcquired = false;
            AppDbContext? context = null;
            PostgreSqlMaintenanceConnectionProfile? maintenanceProfile = null;

            try
            {
                if (usesPostgreSql && _pathProvider != null)
                {
                    maintenanceProfile = PostgreSqlMaintenanceConnectionResolver.Resolve(
                        _databaseSettings,
                        _pathProvider);
                }

                context = maintenanceProfile?.UsesDedicatedCredentials == true
                    ? CreatePostgreSqlContext(
                        maintenanceProfile.ConnectionSettings,
                        _pathProvider ?? throw new InvalidOperationException("PostgreSQL 维护连接需要有效的运行路径提供器。"))
                    : await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                if (usesPostgreSql)
                {
                    await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    if (maintenanceProfile?.UsesDedicatedCredentials == true)
                    {
                        await ExecutePostgreSqlCommandAsync(
                            context,
                            $"SET ROLE {QuotePostgreSqlIdentifier(maintenanceProfile.OwnerRole)};",
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                    await AcquirePostgreSqlInitializationLockAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                    advisoryLockAcquired = true;

                    bool databaseIsEmpty = await DatabaseSchemaBaseline
                        .IsDatabaseEmptyAsync(context, usesPostgreSql: true, cancellationToken)
                        .ConfigureAwait(false);
                    if (databaseIsEmpty && _requireBootstrapToken &&
                        !FixedTimeEquals(_expectedBootstrapToken, bootstrapToken))
                    {
                        return DatabaseInitializationResult.Fail(
                            "共享数据库首次初始化需要有效的部署初始化令牌。请联系部署管理员。",
                            shouldResetPassword: false,
                            isAuthenticationFailure: true);
                    }
                }
                else
                {
                    await ConfigureSingleProcessSqliteAsync(context, cancellationToken).ConfigureAwait(false);
                }

                await DatabaseSchemaBaseline.EnsureCurrentAsync(context, usesPostgreSql, cancellationToken)
                    .ConfigureAwait(false);
                if (maintenanceProfile?.UsesDedicatedCredentials == true)
                {
                    await GrantApplicationRolePrivilegesAsync(
                        context,
                        maintenanceProfile.OwnerRole,
                        _databaseSettings.PostgreSqlUsername,
                        _databaseSettings.PostgreSqlDatabase,
                        cancellationToken).ConfigureAwait(false);
                }

                bool requiresInitialAdministrator = usesPostgreSql &&
                    !await context.Users.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);
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
                cancellationToken.ThrowIfCancellationRequested();

                return DatabaseInitializationResult.Success();
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning(ex, "Database initialization rejected the current schema or configuration.");
                return DatabaseInitializationResult.Fail(
                    "数据库结构或配置不符合当前版本要求。项目尚未投产，请备份需要保留的文件后使用空数据库重新初始化。",
                    shouldResetPassword: false);
            }
            catch (SqliteException ex)
            {
                Log.Error(ex, "SQLite database initialization failed.");
                return DatabaseInitializationResult.Fail(
                    "本地数据库初始化失败。请确认运行数据根可写；如这是预发布旧数据库，请备份后删除并重新初始化。",
                    shouldResetPassword: false);
            }
            catch (NpgsqlException ex) when (usesPostgreSql)
            {
                Log.Error(ex, "PostgreSQL database initialization failed.");
                return DatabaseInitializationResult.Fail(
                    "连接或初始化共享数据库失败，请检查 PostgreSQL 地址、端口、数据库名、账号密码、网络和建表权限。",
                    shouldResetPassword: false);
            }
            catch (DbException ex) when (usesPostgreSql)
            {
                Log.Error(ex, "Shared database initialization failed.");
                return DatabaseInitializationResult.Fail(
                    "连接或初始化共享数据库失败，请检查数据库服务状态和连接配置。",
                    shouldResetPassword: false);
            }
            finally
            {
                if (advisoryLockAcquired && context != null)
                {
                    try
                    {
                        using var unlockTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await ExecutePostgreSqlCommandAsync(
                            context,
                            $"SELECT pg_advisory_unlock({PostgreSqlInitializationLockId});",
                            unlockTimeout.Token).ConfigureAwait(false);
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

        private static async Task ConfigureSingleProcessSqliteAsync(
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=10000;", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        }

        private static AppDbContext CreatePostgreSqlContext(
            DatabaseConnectionSettings settings,
            IAppPathProvider pathProvider)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            DbHelper.ConfigureDbContextOptions(options, settings, pathProvider);
            return new AppDbContext(options.Options);
        }

        private static Task GrantApplicationRolePrivilegesAsync(
            AppDbContext context,
            string ownerRole,
            string applicationRole,
            string databaseName,
            CancellationToken cancellationToken)
        {
            string owner = QuotePostgreSqlIdentifier(ownerRole);
            string app = QuotePostgreSqlIdentifier(applicationRole);
            string database = QuotePostgreSqlIdentifier(databaseName);
            return ExecutePostgreSqlCommandAsync(context, $$"""
                GRANT CONNECT ON DATABASE {{database}} TO {{app}};
                GRANT USAGE ON SCHEMA public TO {{app}};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {{app}};
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {{app}};
                GRANT EXECUTE ON ALL ROUTINES IN SCHEMA public TO {{app}};
                ALTER DEFAULT PRIVILEGES FOR ROLE {{owner}} IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {{app}};
                ALTER DEFAULT PRIVILEGES FOR ROLE {{owner}} IN SCHEMA public
                    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {{app}};
                ALTER DEFAULT PRIVILEGES FOR ROLE {{owner}} IN SCHEMA public
                    GRANT EXECUTE ON ROUTINES TO {{app}};
                """,
                cancellationToken);
        }

        private static async Task ExecutePostgreSqlCommandAsync(
            AppDbContext context,
            string commandText,
            CancellationToken cancellationToken)
        {
            DbConnection connection = context.Database.GetDbConnection();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            command.CommandTimeout = 10;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task AcquirePostgreSqlInitializationLockAsync(
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            DbConnection connection = context.Database.GetDbConnection();
            while (true)
            {
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT pg_try_advisory_lock({PostgreSqlInitializationLockId});";
                command.CommandTimeout = 10;
                if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }

        private static string QuotePostgreSqlIdentifier(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Any(char.IsControl) || Encoding.UTF8.GetByteCount(normalized) > 63)
            {
                throw new InvalidOperationException("PostgreSQL 角色或数据库标识无效。");
            }

            return '"' + normalized.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
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

        private static bool FixedTimeEquals(string expected, string? actual)
        {
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? string.Empty));
            byte[] actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual ?? string.Empty));
            return !string.IsNullOrWhiteSpace(expected) &&
                   CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
    }
}
