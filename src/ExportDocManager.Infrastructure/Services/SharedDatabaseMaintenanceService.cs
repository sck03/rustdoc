using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class SharedDatabaseMaintenanceService : ISharedDatabaseMaintenanceService
    {
        private const string OwnershipStoragePolicy =
            "共享库权限改派只更新发票和付款报销的 OwnerUserId、DepartmentId、CompanyScope 归属字段；不移动附件、不生成导出目录、不读取用户显式导出文件。";
        private const string SupportPackageStoragePolicy =
            "支持包默认写入运行数据根 SupportPackages/，只收集脱敏运行诊断、任务快照、设置摘要和运行数据根 Logs 最近文本日志；默认不打包数据库正文或样张文件，管理员显式勾选并确认后才包含最近数据库备份或样张索引；不会打包授权私钥、邮件密码、WebDAV 密码或 PostgreSQL 密码。";
        private const string PostgreSqlPhysicalBackupStoragePolicy =
            "PostgreSQL 团队版业务数据库物理备份默认写入运行数据根 Backups/PostgreSQL/，优先使用程序根 Tools/PostgreSQL/bin 下的 pg_dump/pg_restore/psql；不把 PostgreSQL 工具或备份默认放到系统 C 盘、AppData 或 ProgramData。";
        private const string PostgreSqlRestorePlanStoragePolicy =
            "PostgreSQL 还原计划默认写入运行数据根 Backups/PostgreSQL/RestorePlans/，生成 pg_restore 脚本和 post_restore_ownership.sql；脚本包含 REASSIGN OWNED、ALTER OWNER、GRANT 和默认权限修复流程，执行前仍需管理员按目标服务器复核。";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly IAppPathProvider _pathProvider;
        private readonly IBackgroundJobService _backgroundJobs;

        public SharedDatabaseMaintenanceService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            IBackgroundJobService backgroundJobs = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _backgroundJobs = backgroundJobs;
        }

        public bool IsSharedDatabaseEnabled => DatabaseModeHelper.UsesSharedDatabase(_databaseSettings);

        public string SupportPackageRoot => EnsureDirectory(Path.Combine(_pathProvider.DataRoot, "SupportPackages"));

        private string PostgreSqlBackupRoot => EnsureDirectory(Path.Combine(_pathProvider.BackupRoot, "PostgreSQL"));

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
            var root = PostgreSqlBackupRoot;
            if (!Directory.Exists(root))
            {
                return Array.Empty<SharedDatabaseBackupItem>();
            }

            return new DirectoryInfo(root)
                .EnumerateFiles("*.dump", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToBackupItem)
                .ToArray();
        }

        public async Task<PostgreSqlPhysicalBackupResult> CreatePostgreSqlPhysicalBackupAsync(CancellationToken cancellationToken = default)
        {
            EnsurePostgreSqlReady();
            var tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            if (string.IsNullOrWhiteSpace(tools.PgDumpPath))
            {
                throw new InvalidOperationException("未找到 pg_dump。请把 PostgreSQL 客户端工具放到程序根 Tools/PostgreSQL/bin，或用 EXPORTDOCMANAGER_POSTGRES_BIN 指向工具目录。");
            }

            string database = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlDatabase);
            string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}_{NormalizeFileToken(database)}.dump";
            string outputPath = Path.Combine(PostgreSqlBackupRoot, fileName);
            string tempPath = outputPath + ".tmp";

            var arguments = new[]
            {
                "--format=custom",
                "--blobs",
                "--verbose",
                "--no-owner",
                "--file", tempPath,
                "--host", DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlHost),
                "--port", DbHelper.NormalizePostgreSqlPort(_databaseSettings.PostgreSqlPort).ToString(),
                "--username", DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername),
                "--dbname", database
            };

            try
            {
                await RunPostgreSqlToolAsync(tools.PgDumpPath, arguments, cancellationToken).ConfigureAwait(false);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.Move(tempPath, outputPath);
                var file = new FileInfo(outputPath);
                return new PostgreSqlPhysicalBackupResult(
                    true,
                    $"PostgreSQL 团队库物理备份已创建：{file.Name}",
                    file.Name,
                    file.FullName,
                    file.Length,
                    PostgreSqlBackupRoot,
                    PostgreSqlPhysicalBackupStoragePolicy);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        public async Task<PostgreSqlRestorePlanResult> CreatePostgreSqlRestorePlanAsync(
            PostgreSqlRestorePlanRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string backupPath = ResolveKnownPostgreSqlBackupPath(request.BackupFileName);
            string targetDatabase = string.IsNullOrWhiteSpace(request.TargetDatabase)
                ? DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlDatabase)
                : request.TargetDatabase.Trim();
            string appRole = string.IsNullOrWhiteSpace(request.ApplicationRole)
                ? DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername)
                : request.ApplicationRole.Trim();
            if (string.IsNullOrWhiteSpace(targetDatabase))
            {
                throw new InvalidOperationException("目标数据库名不能为空。");
            }

            if (string.IsNullOrWhiteSpace(appRole))
            {
                throw new InvalidOperationException("应用账号不能为空。");
            }

            var tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
            string planRoot = EnsureDirectory(Path.Combine(PostgreSqlRestorePlanRoot, timestamp));
            string ownershipSqlPath = Path.Combine(planRoot, "post_restore_ownership.sql");
            string restoreScriptPath = Path.Combine(planRoot, OperatingSystem.IsWindows() ? "restore-postgresql.cmd" : "restore-postgresql.sh");

            await File.WriteAllTextAsync(
                ownershipSqlPath,
                BuildPostRestoreOwnershipSql(targetDatabase, appRole, request.OldOwnerRoles ?? Array.Empty<string>()),
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                restoreScriptPath,
                BuildRestoreScript(backupPath, targetDatabase, appRole, ownershipSqlPath, tools),
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);

            return new PostgreSqlRestorePlanResult(
                true,
                "PostgreSQL 还原计划已生成。请在目标服务器复核脚本后执行，完成后重启应用客户端。",
                planRoot,
                restoreScriptPath,
                ownershipSqlPath,
                backupPath,
                PostgreSqlRestorePlanStoragePolicy);
        }

        public async Task<SharedDatabaseOwnershipSummary> GetOwnershipSummaryAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var users = await context.Users.AsNoTracking().OrderBy(user => user.Username).ToListAsync(cancellationToken).ConfigureAwait(false);
            var invoiceGroups = await context.Invoices.AsNoTracking()
                .GroupBy(invoice => invoice.OwnerUserId)
                .Select(group => new { OwnerUserId = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var paymentGroups = await context.Payments.AsNoTracking()
                .GroupBy(payment => payment.OwnerUserId)
                .Select(group => new { OwnerUserId = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            int invoiceTotal = invoiceGroups.Sum(group => group.Count);
            int paymentTotal = paymentGroups.Sum(group => group.Count);
            int unassignedInvoices = invoiceGroups.FirstOrDefault(group => group.OwnerUserId == null)?.Count ?? 0;
            int unassignedPayments = paymentGroups.FirstOrDefault(group => group.OwnerUserId == null)?.Count ?? 0;

            var ownerItems = users
                .Select(user => new SharedDatabaseOwnerSummaryItem(
                    user.Id,
                    user.Username ?? string.Empty,
                    user.FullName ?? string.Empty,
                    user.Role ?? string.Empty,
                    user.DepartmentId ?? string.Empty,
                    user.CompanyScope ?? string.Empty,
                    invoiceGroups.FirstOrDefault(group => group.OwnerUserId == user.Id)?.Count ?? 0,
                    paymentGroups.FirstOrDefault(group => group.OwnerUserId == user.Id)?.Count ?? 0))
                .ToArray();

            return new SharedDatabaseOwnershipSummary(
                invoiceTotal,
                unassignedInvoices,
                paymentTotal,
                unassignedPayments,
                ownerItems,
                OwnershipStoragePolicy);
        }

        public async Task<SharedDatabaseOwnershipTransferResult> TransferOwnershipAsync(
            SharedDatabaseOwnershipTransferRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!request.IncludeInvoices && !request.IncludePayments)
            {
                throw new InvalidOperationException("请至少选择发票或付款报销一种业务数据。");
            }

            if (request.ToUserId <= 0)
            {
                throw new InvalidOperationException("请选择新的归属用户。");
            }

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var targetUser = await context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(user => user.Id == request.ToUserId && user.IsActive, token)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException("新的归属用户不存在或已停用。");

                    int updatedInvoices = 0;
                    int updatedPayments = 0;
                    string departmentId = string.IsNullOrWhiteSpace(request.DepartmentId)
                        ? targetUser.DepartmentId ?? string.Empty
                        : request.DepartmentId.Trim();
                    string companyScope = string.IsNullOrWhiteSpace(request.CompanyScope)
                        ? targetUser.CompanyScope ?? string.Empty
                        : request.CompanyScope.Trim();

                    if (request.IncludeInvoices)
                    {
                        var invoices = await BuildOwnershipQuery(context.Invoices, request)
                            .ToListAsync(token)
                            .ConfigureAwait(false);
                        foreach (var invoice in invoices)
                        {
                            invoice.OwnerUserId = targetUser.Id;
                            invoice.DepartmentId = departmentId;
                            invoice.CompanyScope = companyScope;
                        }

                        updatedInvoices = invoices.Count;
                    }

                    if (request.IncludePayments)
                    {
                        var payments = await BuildOwnershipQuery(context.Payments, request)
                            .ToListAsync(token)
                            .ConfigureAwait(false);
                        foreach (var payment in payments)
                        {
                            payment.OwnerUserId = targetUser.Id;
                            payment.DepartmentId = departmentId;
                            payment.CompanyScope = companyScope;
                        }

                        updatedPayments = payments.Count;
                    }

                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return new SharedDatabaseOwnershipTransferResult(
                        true,
                        $"归属改派完成：发票 {updatedInvoices} 条，付款报销 {updatedPayments} 条。",
                        updatedInvoices,
                        updatedPayments,
                        OwnershipStoragePolicy);
                },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<SupportPackageResult> CreateSupportPackageAsync(CancellationToken cancellationToken = default)
        {
            return await CreateSupportPackageAsync(new SupportPackageOptions(), cancellationToken).ConfigureAwait(false);
        }

        public async Task<SupportPackageResult> CreateSupportPackageAsync(
            SupportPackageOptions options,
            CancellationToken cancellationToken = default)
        {
            options ??= new SupportPackageOptions();
            string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}_support_package.zip";
            string path = Path.Combine(SupportPackageRoot, fileName);
            string tempPath = path + ".tmp";

            try
            {
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    await WriteJsonEntryAsync(archive, "diagnostics/runtime.json", CreateRuntimeDiagnostics(), cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "diagnostics/database.json", CreateDatabaseDiagnostics(), cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "diagnostics/paths.json", CreatePathDiagnostics(), cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "diagnostics/settings-redacted.json", ReadRedactedSettings(), cancellationToken).ConfigureAwait(false);
                    await WriteJobSnapshotAsync(archive, cancellationToken).ConfigureAwait(false);
                    await WriteRecentLogsAsync(archive, cancellationToken).ConfigureAwait(false);
                    await WriteOptionalSupportPackageEntriesAsync(archive, options, cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
                var file = new FileInfo(path);
                return new SupportPackageResult(
                    true,
                    $"支持包已导出：{file.Name}",
                    file.Name,
                    file.FullName,
                    file.Length,
                    SupportPackageRoot,
                    SupportPackageStoragePolicy);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static async Task WriteJsonEntryAsync<T>(
            ZipArchive archive,
            string entryName,
            T value,
            CancellationToken cancellationToken)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        private static IQueryable<Invoice> BuildOwnershipQuery(
            IQueryable<Invoice> source,
            SharedDatabaseOwnershipTransferRequest request)
        {
            if (request.OnlyUnassigned)
            {
                return source.Where(item => item.OwnerUserId == null);
            }

            return request.FromUserId.HasValue
                ? source.Where(item => item.OwnerUserId == request.FromUserId.Value)
                : source;
        }

        private static IQueryable<Payment> BuildOwnershipQuery(
            IQueryable<Payment> source,
            SharedDatabaseOwnershipTransferRequest request)
        {
            if (request.OnlyUnassigned)
            {
                return source.Where(item => item.OwnerUserId == null);
            }

            return request.FromUserId.HasValue
                ? source.Where(item => item.OwnerUserId == request.FromUserId.Value)
                : source;
        }

        private async Task WriteJobSnapshotAsync(ZipArchive archive, CancellationToken cancellationToken)
        {
            if (_backgroundJobs == null)
            {
                await WriteJsonEntryAsync(archive, "diagnostics/background-jobs.json", Array.Empty<BackgroundJobSnapshot>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var jobs = await _backgroundJobs.QueryAsync(new BackgroundJobQuery
            {
                PageNumber = 1,
                PageSize = 200
            }, cancellationToken).ConfigureAwait(false);
            await WriteJsonEntryAsync(archive, "diagnostics/background-jobs.json", jobs, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteRecentLogsAsync(ZipArchive archive, CancellationToken cancellationToken)
        {
            var logRoot = _pathProvider.LogRoot;
            if (!Directory.Exists(logRoot))
            {
                await WriteJsonEntryAsync(archive, "logs/log-index.json", Array.Empty<object>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var allLogs = new DirectoryInfo(logRoot)
                .EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                .Where(file => IsTextLog(file.Name))
                .ToArray();
            var priorityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "api-sidecar.stdout.log",
                "api-sidecar.stderr.log",
                "frontend-errors.log",
                "tauri-errors.log",
                "tauri-bootstrap-error.log"
            };
            var logs = allLogs
                .Where(file => priorityNames.Contains(file.Name))
                .Concat(allLogs.OrderByDescending(file => file.LastWriteTimeUtc).Take(20))
                .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            var index = logs
                .Select(file => new
                {
                    file.Name,
                    file.Length,
                    file.LastWriteTimeUtc
                })
                .ToArray();
            await WriteJsonEntryAsync(archive, "logs/log-index.json", index, cancellationToken).ConfigureAwait(false);

            foreach (var file in logs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry($"logs/{file.Name}", CompressionLevel.Optimal);
                await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await using var target = entry.Open();
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }

        private object CreateRuntimeDiagnostics()
        {
            using var currentProcess = Process.GetCurrentProcess();
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return new
            {
                CreatedAt = DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.OSVersion.Platform,
                Environment.OSVersion.VersionString,
                Environment.Is64BitOperatingSystem,
                Environment.Is64BitProcess,
                Environment.ProcessorCount,
                Environment.Version,
                ProcessWorkingSet64 = currentProcess.WorkingSet64,
                ProcessPrivateMemorySize64 = currentProcess.PrivateMemorySize64,
                GCTotalAvailableMemoryBytes = gcMemoryInfo.TotalAvailableMemoryBytes,
                WebView2LoaderVersion = ReadFileVersion(Path.Combine(_pathProvider.AppRoot, "WebView2Loader.dll")),
                ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                OSArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
            };
        }

        private object CreateDatabaseDiagnostics()
        {
            return new
            {
                Provider = DatabaseModeHelper.GetCurrentModeText(_databaseSettings),
                SharedDatabaseEnabled = IsSharedDatabaseEnabled,
                PostgreSqlHost = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlHost),
                PostgreSqlPort = DbHelper.NormalizePostgreSqlPort(_databaseSettings.PostgreSqlPort),
                PostgreSqlDatabase = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlDatabase),
                PostgreSqlUsername = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername),
                HasPostgreSqlPassword = !string.IsNullOrEmpty(_databaseSettings.PostgreSqlPassword),
                SQLiteDatabaseFileName = DatabaseModeHelper.UsesPostgreSql(_databaseSettings)
                    ? string.Empty
                    : DbHelper.NormalizeSqliteDatabaseFileName(_databaseSettings.SqliteDatabaseFileName)
            };
        }

        private object CreatePathDiagnostics()
        {
            return new
            {
                _pathProvider.AppRoot,
                _pathProvider.DataRoot,
                _pathProvider.DatabaseRoot,
                _pathProvider.BackupRoot,
                PostgreSqlBackupRoot,
                PostgreSqlRestorePlanRoot,
                SupportPackageRoot,
                _pathProvider.LogRoot,
                _pathProvider.TemplateRoot,
                _pathProvider.ResourceRoot,
                _pathProvider.BrowserRoot,
                _pathProvider.ToolRoot,
                _pathProvider.OcrModelRoot,
                _pathProvider.SingleWindowRoot,
                _pathProvider.CacheRoot,
                _pathProvider.SecurityRoot,
                _pathProvider.WebViewRoot
            };
        }

        private async Task WriteOptionalSupportPackageEntriesAsync(
            ZipArchive archive,
            SupportPackageOptions options,
            CancellationToken cancellationToken)
        {
            var optionalIndex = new List<object>();
            if (options.IncludeLatestDatabaseBackup)
            {
                var latestBackup = GetLatestAnyDatabaseBackup();
                if (latestBackup != null)
                {
                    optionalIndex.Add(new
                    {
                        kind = "latest-database-backup",
                        latestBackup.Name,
                        latestBackup.Length,
                        latestBackup.LastWriteTimeUtc
                    });
                    var entry = archive.CreateEntry($"optional/database-backup/{latestBackup.Name}", CompressionLevel.Optimal);
                    await using var source = new FileStream(latestBackup.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }

            if (options.IncludeSampleFiles)
            {
                optionalIndex.Add(new
                {
                    kind = "sample-files",
                    message = "当前未配置自动样张目录。样张请由用户另行选择后提供，避免默认打包业务附件。"
                });
            }

            await WriteJsonEntryAsync(archive, "optional/index.json", optionalIndex, cancellationToken).ConfigureAwait(false);
        }

        private FileInfo GetLatestAnyDatabaseBackup()
        {
            var candidates = new List<FileInfo>();
            if (Directory.Exists(_pathProvider.BackupRoot))
            {
                candidates.AddRange(new DirectoryInfo(_pathProvider.BackupRoot)
                    .EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly));
            }

            if (Directory.Exists(PostgreSqlBackupRoot))
            {
                candidates.AddRange(new DirectoryInfo(PostgreSqlBackupRoot)
                    .EnumerateFiles("*.dump", SearchOption.TopDirectoryOnly));
            }

            return candidates
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }

        private async Task RunPostgreSqlToolAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!string.IsNullOrEmpty(_databaseSettings.PostgreSqlPassword))
            {
                startInfo.Environment["PGPASSWORD"] = _databaseSettings.PostgreSqlPassword;
            }

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    outputBuilder.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    errorBuilder.AppendLine(args.Data);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 PostgreSQL 客户端工具。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string message = errorBuilder.Length > 0 ? errorBuilder.ToString().Trim() : outputBuilder.ToString().Trim();
                throw new InvalidOperationException($"PostgreSQL 客户端工具执行失败：{message}");
            }
        }

        private string ResolveKnownPostgreSqlBackupPath(string backupFileName)
        {
            string fileName = (backupFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException("PostgreSQL 备份文件名不能为空。");
            }

            if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("只能选择 PostgreSQL 备份列表中的文件名，不能传入路径。");
            }

            var item = ListPostgreSqlPhysicalBackups()
                .FirstOrDefault(backup => string.Equals(backup.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            return item?.FullPath ?? throw new InvalidOperationException("未找到指定 PostgreSQL 物理备份。");
        }

        private string BuildRestoreScript(
            string backupPath,
            string targetDatabase,
            string appRole,
            string ownershipSqlPath,
            PostgreSqlToolPaths tools)
        {
            string host = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlHost);
            string port = DbHelper.NormalizePostgreSqlPort(_databaseSettings.PostgreSqlPort).ToString();
            string username = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername);
            string pgRestore = string.IsNullOrWhiteSpace(tools.PgRestorePath) ? "pg_restore" : tools.PgRestorePath;
            string psql = string.IsNullOrWhiteSpace(tools.PsqlPath) ? "psql" : tools.PsqlPath;

            if (OperatingSystem.IsWindows())
            {
                return $"""
@echo off
setlocal
rem PostgreSQL 团队版业务数据库还原计划。执行前请确认目标服务器、数据库名和应用账号。
rem 如需避免输入密码，可临时设置 PGPASSWORD，或使用 .pgpass / 密码管理工具。
"{pgRestore}" --clean --if-exists --no-owner --role "{EscapeCmd(appRole)}" --host "{EscapeCmd(host)}" --port "{EscapeCmd(port)}" --username "{EscapeCmd(username)}" --dbname "{EscapeCmd(targetDatabase)}" "{backupPath}"
if errorlevel 1 exit /b %errorlevel%
"{psql}" --host "{EscapeCmd(host)}" --port "{EscapeCmd(port)}" --username "{EscapeCmd(username)}" --dbname "{EscapeCmd(targetDatabase)}" --file "{ownershipSqlPath}"
endlocal
""";
            }

            return $"""
#!/usr/bin/env sh
set -eu
# PostgreSQL 团队版业务数据库还原计划。执行前请确认目标服务器、数据库名和应用账号。
"{pgRestore}" --clean --if-exists --no-owner --role "{EscapeShell(appRole)}" --host "{EscapeShell(host)}" --port "{EscapeShell(port)}" --username "{EscapeShell(username)}" --dbname "{EscapeShell(targetDatabase)}" "{EscapeShell(backupPath)}"
"{psql}" --host "{EscapeShell(host)}" --port "{EscapeShell(port)}" --username "{EscapeShell(username)}" --dbname "{EscapeShell(targetDatabase)}" --file "{EscapeShell(ownershipSqlPath)}"
""";
        }

        private static string BuildPostRestoreOwnershipSql(
            string targetDatabase,
            string appRole,
            IReadOnlyList<string> oldOwnerRoles)
        {
            string roleLiteral = ToSqlLiteral(appRole);
            var oldRoles = (oldOwnerRoles ?? Array.Empty<string>())
                .Select(role => (role ?? string.Empty).Trim())
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string reassignBlock = oldRoles.Length == 0
                ? "-- 如迁移后存在旧 owner 角色，可按需执行：REASSIGN OWNED BY old_role TO " + QuoteIdentifier(appRole) + ";" + Environment.NewLine
                : string.Join(Environment.NewLine, oldRoles.Select(role => $"REASSIGN OWNED BY {QuoteIdentifier(role)} TO {QuoteIdentifier(appRole)};"));

            return $"""
-- PostgreSQL 团队版业务数据库还原后 owner / schema / table / sequence / 权限改派脚本
-- Target database: {targetDatabase}
-- Application role: {appRole}

{reassignBlock}

DO $$
DECLARE
    app_role text := {roleLiteral};
    item record;
BEGIN
    FOR item IN
        SELECT nspname
        FROM pg_namespace
        WHERE nspname NOT IN ('pg_catalog', 'information_schema')
          AND nspname NOT LIKE 'pg_toast%'
    LOOP
        EXECUTE format('ALTER SCHEMA %I OWNER TO %I', item.nspname, app_role);
        EXECUTE format('GRANT USAGE, CREATE ON SCHEMA %I TO %I', item.nspname, app_role);
    END LOOP;

    FOR item IN
        SELECT schemaname, tablename
        FROM pg_tables
        WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER TABLE %I.%I OWNER TO %I', item.schemaname, item.tablename, app_role);
    END LOOP;

    FOR item IN
        SELECT sequence_schema, sequence_name
        FROM information_schema.sequences
        WHERE sequence_schema NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO %I', item.sequence_schema, item.sequence_name, app_role);
    END LOOP;

    FOR item IN
        SELECT schemaname, viewname
        FROM pg_views
        WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER VIEW %I.%I OWNER TO %I', item.schemaname, item.viewname, app_role);
    END LOOP;

    FOR item IN
        SELECT n.nspname AS schema_name, p.proname AS routine_name, pg_get_function_identity_arguments(p.oid) AS args
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER FUNCTION %I.%I(%s) OWNER TO %I', item.schema_name, item.routine_name, item.args, app_role);
    END LOOP;
END $$;

GRANT CONNECT, TEMPORARY ON DATABASE {QuoteIdentifier(targetDatabase)} TO {QuoteIdentifier(appRole)};
GRANT USAGE, CREATE ON SCHEMA public TO {QuoteIdentifier(appRole)};
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO {QuoteIdentifier(appRole)};
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO {QuoteIdentifier(appRole)};
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO {QuoteIdentifier(appRole)};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO {QuoteIdentifier(appRole)};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO {QuoteIdentifier(appRole)};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT EXECUTE ON FUNCTIONS TO {QuoteIdentifier(appRole)};
""";
        }

        private void EnsurePostgreSqlReady()
        {
            if (!DatabaseModeHelper.UsesSharedDatabase(_databaseSettings))
            {
                throw new InvalidOperationException("当前未启用 PostgreSQL 团队版业务数据库，无法执行 PostgreSQL 物理备份。");
            }
        }

        private JsonNode ReadRedactedSettings()
        {
            string settingsPath = Path.Combine(_pathProvider.AppRoot, "appsettings.json");
            if (!File.Exists(settingsPath))
            {
                return new JsonObject
                {
                    ["exists"] = false
                };
            }

            try
            {
                var node = JsonNode.Parse(File.ReadAllText(settingsPath, Encoding.UTF8)) ?? new JsonObject();
                RedactSensitiveProperties(node);
                return node;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new JsonObject
                {
                    ["exists"] = true,
                    ["readError"] = ex.Message
                };
            }
        }

        private static void RedactSensitiveProperties(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var property in obj.ToList())
                {
                    if (IsSensitiveKey(property.Key))
                    {
                        obj[property.Key] = "***";
                    }
                    else if (property.Value != null)
                    {
                        RedactSensitiveProperties(property.Value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        RedactSensitiveProperties(item);
                    }
                }
            }
        }

        private static bool IsSensitiveKey(string key)
        {
            var value = key ?? string.Empty;
            return value.Contains("password", StringComparison.OrdinalIgnoreCase)
                || value.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || value.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("apiKey", StringComparison.Ordinal)
                || value.Contains("token", StringComparison.OrdinalIgnoreCase)
                || value.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("privateKey", StringComparison.Ordinal);
        }

        private static SharedDatabaseBackupItem ToBackupItem(FileInfo file)
        {
            return new SharedDatabaseBackupItem(
                file.Name,
                file.FullName,
                file.Exists ? file.Length : 0,
                file.CreationTime,
                file.LastWriteTime);
        }

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static string NormalizeFileToken(string value)
        {
            var chars = (value ?? string.Empty)
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
                .ToArray();
            string token = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(token) ? "database" : token;
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private static string ToSqlLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private static string EscapeCmd(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        }

        private static string EscapeShell(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string ReadFileVersion(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                return FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsTextLog(string fileName)
        {
            return fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup for an interrupted package write.
            }
        }

    }
}
