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

    public sealed record DatabaseRestoreScheduleResult(
        bool Success,
        string Message,
        string BackupFilePath,
        string SafetyBackupFilePath);
}
