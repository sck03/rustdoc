using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService : ISharedDatabaseMaintenanceService
    {
        private static readonly SemaphoreSlim PostgreSqlBackupGate = new(1, 1);
        private static readonly TimeSpan PostgreSqlBackupTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan PostgreSqlVersionCheckTimeout = TimeSpan.FromSeconds(15);
        private const int MaximumPostgreSqlIdentifierBytes = 63;
        private const int MaximumPostgreSqlOldOwnerRoleCount = 100;
        private static readonly Regex PostgreSqlVersionPattern = new(
            @"(?<!\d)(?<major>\d{1,3})(?:\.\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        private const string OwnershipStoragePolicy =
            "共享库权限改派只更新发票、付款报销、客户、出口商、CRM、供应商、商机、邮件/报表模板和装柜方案的 OwnerUserId、DepartmentId、CompanyScope 归属字段；关联子记录继续随所属业务聚合访问，不移动附件、不生成导出目录、不读取用户显式导出文件。";
        private const string SupportPackageStoragePolicy =
            "支持包默认写入运行数据根 SupportPackages/，只收集脱敏运行诊断、任务快照、设置摘要和运行数据根 Logs 最近文本日志；默认不打包数据库正文或样张文件，管理员显式勾选并确认后才包含最近数据库备份或样张索引；不会打包授权私钥、邮件密码、WebDAV 密码或 PostgreSQL 密码。";
        private const string PostgreSqlPhysicalBackupStoragePolicy =
            "PostgreSQL 团队版业务数据库物理备份默认写入运行数据根 Backups/PostgreSQL/，优先使用程序根 Tools/PostgreSQL/bin 下的 pg_dump/pg_restore/psql；目录和文件会收紧为当前服务账号可访问。custom-format .dump 包含完整业务数据但本身不加密，复制到外部介质前必须使用受控加密存储；不把 PostgreSQL 工具或备份默认放到系统 C 盘、AppData 或 ProgramData。";
        private const string PostgreSqlRestorePlanStoragePolicy =
            "PostgreSQL 还原计划默认写入运行数据根 Backups/PostgreSQL/RestorePlans/，生成 pg_restore 脚本和 post_restore_ownership.sql；脚本包含 REASSIGN OWNED、ALTER OWNER、GRANT 和默认权限修复流程，执行前仍需管理员按目标服务器复核。";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly DatabaseConnectionSettings _maintenanceDatabaseSettings;
        private readonly string _postgreSqlOwnerRole;
        private readonly IAppPathProvider _pathProvider;
        private readonly IBackgroundJobService? _backgroundJobs;
        private readonly IBusinessClock _clock;

        public SharedDatabaseMaintenanceService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            IBackgroundJobService? backgroundJobs = null,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _backgroundJobs = backgroundJobs;
            _clock = clock ?? BusinessClock.CreateSystem();
            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                PostgreSqlMaintenanceConnectionProfile profile =
                    PostgreSqlMaintenanceConnectionResolver.Resolve(databaseSettings, pathProvider);
                _maintenanceDatabaseSettings = profile.ConnectionSettings;
                _postgreSqlOwnerRole = profile.OwnerRole;
            }
            else
            {
                _maintenanceDatabaseSettings = databaseSettings;
                _postgreSqlOwnerRole = databaseSettings.PostgreSqlUsername;
            }
        }

        public bool IsSharedDatabaseEnabled => DatabaseModeHelper.UsesSharedDatabase(_databaseSettings);

        public string SupportPackageRoot => EnsureDirectory(Path.Combine(_pathProvider.DataRoot, "SupportPackages"));

        private string PostgreSqlBackupRoot => EnsureDirectory(
            Path.Combine(_pathProvider.DataRoot, "Backups", "PostgreSQL"));

        private string PostgreSqlRestorePlanRoot => EnsureDirectory(Path.Combine(PostgreSqlBackupRoot, "RestorePlans"));

        public PostgreSqlMaintenanceStatus GetPostgreSqlMaintenanceStatus()
        {
            var tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            return new PostgreSqlMaintenanceStatus(
                DatabaseModeHelper.UsesPostgreSql(_databaseSettings),
                DatabaseModeHelper.UsesSharedDatabase(_databaseSettings),
                DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlHost),
                DbHelper.NormalizePostgreSqlPort(_databaseSettings.PostgreSqlPort),
                DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlDatabase),
                DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername),
                PostgreSqlBackupRoot,
                tools.BinRoot,
                tools.PgDumpPath,
                tools.PgRestorePath,
                tools.PsqlPath,
                tools.ToolsReady,
                PostgreSqlPhysicalBackupStoragePolicy);
        }

        public IReadOnlyList<SharedDatabaseBackupItem> ListPostgreSqlPhysicalBackups()
        {
            string root;
            try
            {
                root = PostgreSqlBackupRoot;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                throw new InfrastructureServiceException(
                    "PostgreSQL 备份目录暂时不可用，请检查运行数据目录权限。",
                    ex);
            }
            if (!Directory.Exists(root))
            {
                return Array.Empty<SharedDatabaseBackupItem>();
            }

            try
            {
                return new DirectoryInfo(root)
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(file => string.Equals(file.Extension, ".dump", StringComparison.OrdinalIgnoreCase))
                    .Where(IsRegularPostgreSqlBackupFile)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ToBackupItem)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InfrastructureServiceException(
                    "PostgreSQL 备份目录暂时不可用，请检查运行数据目录权限。",
                    ex);
            }
        }

        private static bool IsRegularPostgreSqlBackupFile(FileInfo file)
        {
            try
            {
                return file.Exists &&
                    (file.Attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

    }
}
