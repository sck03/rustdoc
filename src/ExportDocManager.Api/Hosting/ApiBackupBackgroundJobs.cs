using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static BackgroundJobSnapshot EnqueueDatabaseBackupJob(
        ApiBackgroundJobRunner jobRunner,
        string requestedBy) =>
        jobRunner.Enqueue(
            "DatabaseBackup",
            "创建 SQLite 数据库备份",
            requestedBy,
            async (provider, jobContext) =>
            {
                jobContext.Report(5, "准备数据库备份", "正在创建一致性快照，可离开本页继续工作。");
                var service = provider.GetRequiredService<IBackupService>();
                DatabaseBackupResult result = await service
                    .BackupDatabaseAsync(jobContext.CancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success && !result.Skipped)
                {
                    throw new InfrastructureServiceException(result.Message);
                }

                jobContext.Report(95, result.Skipped ? "无需创建备份" : "校验备份文件", result.Message);
                return string.Empty;
            });

    private static BackgroundJobSnapshot EnqueueDatabaseRestoreJob(
        ApiBackgroundJobRunner jobRunner,
        string requestedBy,
        string backupPath) =>
        jobRunner.Enqueue(
            "DatabaseRestorePreparation",
            "校验并安排 SQLite 数据库还原",
            requestedBy,
            async (provider, jobContext) =>
            {
                jobContext.Report(5, "准备数据库还原", "正在创建安全备份并校验所选备份包。");
                var service = provider.GetRequiredService<IBackupService>();
                DatabaseRestoreScheduleResult result = await service
                    .ScheduleRestoreAsync(backupPath, jobContext.CancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new InfrastructureServiceException(result.Message);
                }

                jobContext.Report(95, "还原已安排", result.Message);
                return string.Empty;
            });

    private static BackgroundJobSnapshot EnqueueDisasterRecoveryPackageJob(
        ApiBackgroundJobRunner jobRunner,
        string requestedBy,
        string password) =>
        jobRunner.Enqueue(
            "DisasterRecoveryPackage",
            "创建持卡机灾难恢复包",
            requestedBy,
            async (provider, jobContext) =>
            {
                jobContext.Report(5, "准备恢复包", "正在创建数据库快照并收集受控恢复文件。");
                var service = provider.GetRequiredService<ISingleWindowDisasterRecoveryService>();
                SingleWindowDisasterRecoveryPackageResult result = await service
                    .CreatePackageAsync(password, jobContext.CancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new InfrastructureServiceException(result.Message);
                }

                jobContext.Report(95, "恢复包已加密", $"{result.FileName}（{result.SizeBytes:N0} 字节）已写入运行数据目录。{result.Message}");
                return string.Empty;
            });

    private static BackgroundJobSnapshot EnqueueDisasterRecoveryRestoreJob(
        ApiBackgroundJobRunner jobRunner,
        string requestedBy,
        string packagePath,
        string password) =>
        jobRunner.Enqueue(
            "DisasterRecoveryPreparation",
            "校验并安排持卡机灾难恢复",
            requestedBy,
            async (provider, jobContext) =>
            {
                jobContext.Report(5, "校验恢复包", "正在解密、校验并暂存恢复文件。");
                var service = provider.GetRequiredService<ISingleWindowDisasterRecoveryService>();
                SingleWindowDisasterRecoveryRestoreResult result = await service
                    .ScheduleRestoreAsync(packagePath, password, jobContext.CancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new InfrastructureServiceException(result.Message);
                }

                jobContext.Report(95, "灾难恢复已安排", result.Message);
                return string.Empty;
            });

    private static BackgroundJobSnapshot EnqueueCloudBackupUploadJob(
        ApiBackgroundJobRunner jobRunner,
        string requestedBy) =>
        jobRunner.Enqueue(
            "CloudBackupUpload",
            "上传最新数据库备份到 WebDAV",
            requestedBy,
            async (provider, jobContext) =>
            {
                var settingsService = provider.GetRequiredService<ISettingsService>();
                await settingsService.LoadAsync(jobContext.CancellationToken).ConfigureAwait(false);
                WebDavSettings webDav = settingsService.Settings.WebDav;
                EnsureCloudBackupConfigured(webDav);

                var backupService = provider.GetRequiredService<IBackupService>();
                FileInfo latestBackup = GetLatestBackupFile(backupService)
                    ?? throw new ResourceNotFoundException("当前没有可上传的数据库备份，请先创建本地备份。");
                jobContext.Report(5, "准备云备份上传", $"正在上传 {latestBackup.Name}。");
                var cloudSyncService = provider.GetRequiredService<ICloudSyncService>();
                await cloudSyncService
                    .UploadFileAsync(latestBackup.FullName, latestBackup.Name, jobContext.CancellationToken)
                    .ConfigureAwait(false);
                jobContext.Report(95, "云备份上传完成", $"已上传 {latestBackup.Name}（{latestBackup.Length:N0} 字节）。");
                return string.Empty;
            });

    private static BackgroundJobSnapshot EnqueueCloudBackupDownloadJob(
        ApiBackgroundJobRunner jobRunner,
        string requestedBy,
        string remoteFileName) =>
        jobRunner.Enqueue(
            "CloudBackupDownload",
            "下载并验证 WebDAV 数据库备份",
            requestedBy,
            async (provider, jobContext) =>
            {
                var settingsService = provider.GetRequiredService<ISettingsService>();
                await settingsService.LoadAsync(jobContext.CancellationToken).ConfigureAwait(false);
                EnsureCloudBackupConfigured(settingsService.Settings.WebDav);

                var cloudSyncService = provider.GetRequiredService<ICloudSyncService>();
                IReadOnlyList<CloudBackupFileInfo> remoteBackups = await cloudSyncService
                    .ListBackupFilesAsync(jobContext.CancellationToken)
                    .ConfigureAwait(false);
                CloudBackupFileInfo selectedBackup = remoteBackups.FirstOrDefault(backup =>
                    string.Equals(backup.FileName, remoteFileName, StringComparison.Ordinal))
                    ?? throw new ServiceValidationException("只能下载当前 WebDAV 云备份列表中的 ZIP 文件。");
                if (selectedBackup.SizeBytes > WebDavCloudSyncService.MaximumDownloadBytes)
                {
                    throw new ServiceValidationException("所选 WebDAV 备份超过 4 GiB 下载上限。");
                }

                var pathProvider = provider.GetRequiredService<IAppPathProvider>();
                string stagingRoot = Path.Combine(pathProvider.BackupRoot, ".cloud-downloads");
                Directory.CreateDirectory(stagingRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(stagingRoot);
                string stagedPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}.zip");
                try
                {
                    jobContext.Report(5, "下载云备份", $"正在下载 {remoteFileName}。");
                    await cloudSyncService
                        .DownloadFileAsync(remoteFileName, stagedPath, jobContext.CancellationToken)
                        .ConfigureAwait(false);
                    jobContext.Report(75, "验证云备份", "下载完成，正在校验 SQLite 快照并导入受管备份目录。");
                    var backupService = provider.GetRequiredService<IBackupService>();
                    DatabaseBackupImportResult imported = await backupService
                        .ImportBackupAsync(stagedPath, remoteFileName, jobContext.CancellationToken)
                        .ConfigureAwait(false);
                    jobContext.Report(95, "云备份已导入", $"{Path.GetFileName(imported.FilePath)}（{imported.SizeBytes:N0} 字节）已通过校验。");
                    return string.Empty;
                }
                finally
                {
                    AtomicFileHelper.TryDeleteFile(stagedPath);
                }
            });

    private static void EnsureCloudBackupConfigured(WebDavSettings webDav)
    {
        if (!webDav.Enabled)
        {
            throw new ServiceValidationException("WebDAV 云备份未启用，请先保存启用状态。");
        }
        if (!IsWebDavConfigured(webDav))
        {
            throw new ServiceValidationException("WebDAV 尚未配置，请先保存服务器地址和用户名。");
        }
    }
}
