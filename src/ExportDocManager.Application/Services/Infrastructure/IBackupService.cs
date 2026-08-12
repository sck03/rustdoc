using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IBackupService
    {
        /// <summary>
        /// 执行数据库备份
        /// </summary>
        Task<DatabaseBackupResult> BackupDatabaseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 验证并导入一个外部 SQLite 备份。导入成功后只发布一个干净的、受管的 ZIP，
        /// 不把未经 quick_check 的下载文件直接放进备份列表。
        /// </summary>
        Task<DatabaseBackupImportResult> ImportBackupAsync(
            string sourceFilePath,
            string? preferredFileName = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 清理旧备份文件
        /// </summary>
        /// <param name="daysToKeep">保留最近多少天的备份</param>
        void CleanOldBackups(int daysToKeep);

        /// <summary>
        /// 获取所有可用备份列表
        /// </summary>
        List<string> GetAvailableBackups();

        /// <summary>
        /// 安排在下一次程序启动、数据库连接建立前离线还原数据库
        /// </summary>
        /// <param name="backupFilePath">备份文件路径</param>
        Task<DatabaseRestoreScheduleResult> ScheduleRestoreAsync(
            string backupFilePath,
            CancellationToken cancellationToken = default);
    }

    public sealed record DatabaseBackupResult(
        bool Success,
        bool Skipped,
        string Message,
        string FilePath);

    public sealed record DatabaseBackupImportResult(
        bool Success,
        string Message,
        string FilePath,
        long SizeBytes);

    public sealed record DatabaseRestoreScheduleResult(
        bool Success,
        string Message,
        string BackupFilePath,
        string SafetyBackupFilePath);
}
