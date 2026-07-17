using System.Collections.Generic;
using System.Threading.Tasks;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IBackupService
    {
        /// <summary>
        /// 执行数据库备份
        /// </summary>
        Task BackupDatabaseAsync();

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
        /// 还原数据库
        /// </summary>
        /// <param name="backupFilePath">备份文件路径</param>
        void RestoreDatabase(string backupFilePath);
    }
}
