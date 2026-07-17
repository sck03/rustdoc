using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Utils;
using Serilog;

namespace ExportDocManager.Services.Infrastructure
{
    public class BackupService : IBackupService
    {
        private readonly string _backupDirectory;
        private readonly string _databasePath;
        private readonly string _databaseFileName;
        private readonly bool _usesSqlite;

        public BackupService(
            DatabaseConnectionSettings databaseSettings,
            string backupDirectory = null,
            string databasePath = null)
            : this(databaseSettings, new RuntimeAppPathProvider(), backupDirectory, databasePath)
        {
        }

        public BackupService(
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            string backupDirectory = null,
            string databasePath = null)
        {
            ArgumentNullException.ThrowIfNull(databaseSettings);
            ArgumentNullException.ThrowIfNull(pathProvider);
            _usesSqlite = !DatabaseModeHelper.UsesPostgreSql(databaseSettings);

            if (_usesSqlite)
            {
                var sqliteFileName = string.IsNullOrWhiteSpace(databaseSettings.SqliteDatabaseFileName)
                    ? "data.db"
                    : databaseSettings.SqliteDatabaseFileName.Trim();
                _databasePath = string.IsNullOrWhiteSpace(databasePath)
                    ? DbHelper.GetDatabasePath(sqliteFileName)
                    : databasePath;
                _databaseFileName = Path.GetFileName(_databasePath);
            }
            else
            {
                _databasePath = string.Empty;
                _databaseFileName = string.Empty;
            }

            _backupDirectory = string.IsNullOrWhiteSpace(backupDirectory)
                ? pathProvider.BackupRoot
                : backupDirectory;

            Directory.CreateDirectory(_backupDirectory);
        }

        public async Task BackupDatabaseAsync()
        {
            try
            {
                if (!_usesSqlite)
                {
                    Log.Information("Skipping local database backup because the current provider is PostgreSQL.");
                    return;
                }

                if (!File.Exists(_databasePath))
                {
                    Log.Warning("Database file not found at {Path}, skipping backup.", _databasePath);
                    return;
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = $"{timestamp}_{BuildBackupNameToken(_databaseFileName)}.zip";
                string backupPath = Path.Combine(_backupDirectory, backupFileName);
                string databaseFileName = Path.GetFileName(_databasePath);

                string tempDbCopy = AtomicFileHelper.GetSiblingTempFilePath(_databasePath);
                try
                {
                    await FileCopyHelper.CopyAsync(
                        _databasePath,
                        tempDbCopy,
                        overwrite: true,
                        sourceFileShare: FileShare.ReadWrite | FileShare.Delete);
                    await ZipArchiveHelper.CreateFromFilesAsync(
                        new[] { (SourcePath: tempDbCopy, EntryName: databaseFileName) },
                        backupPath);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteFile(tempDbCopy);
                }

                Log.Information("Database backed up successfully to {Path}", backupPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to backup database.");
            }
        }

        public void CleanOldBackups(int daysToKeep)
        {
            try
            {
                if (!_usesSqlite || daysToKeep <= 0 || !Directory.Exists(_backupDirectory))
                {
                    return;
                }

                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);

                foreach (var file in GetCandidateBackupFiles())
                {
                    if (file.LastWriteTime < cutoffDate)
                    {
                        file.Delete();
                        Log.Information("Deleted old backup: {FileName}", file.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to clean old backups.");
            }
        }

        public List<string> GetAvailableBackups()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                {
                    return new List<string>();
                }

                return GetCandidateBackupFiles()
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(file => file.FullName)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get available backups.");
                return new List<string>();
            }
        }

        public void RestoreDatabase(string backupFilePath)
        {
            if (!_usesSqlite)
            {
                throw new NotSupportedException("当前数据库类型为 PostgreSQL，暂不支持通过本地 SQLite 备份文件还原。");
            }

            if (!File.Exists(backupFilePath))
            {
                throw new FileNotFoundException("Backup file not found.", backupFilePath);
            }

            string tempBackup = null;
            try
            {
                // 1. 确保目标目录存在
                string dbDir = Path.GetDirectoryName(_databasePath);
                if (!Directory.Exists(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                // 2. 备份当前的（以防万一还原失败）
                tempBackup = AtomicFileHelper.GetSiblingTempFilePath(_databasePath);
                if (File.Exists(_databasePath))
                {
                    FileCopyHelper.Copy(
                        _databasePath,
                        tempBackup,
                        overwrite: true,
                        sourceFileShare: FileShare.ReadWrite | FileShare.Delete);
                }

                // 3. 尝试清除连接池
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                string tempRestore = AtomicFileHelper.GetSiblingTempFilePath(_databasePath);

                // 4. 解压覆盖
                try
                {
                    if (backupFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var archive = ZipFile.OpenRead(backupFilePath))
                        {
                            var entry = archive.Entries.FirstOrDefault(e =>
                                e.Name.Equals(_databaseFileName, StringComparison.OrdinalIgnoreCase));
                            if (entry == null)
                            {
                                throw new InvalidDataException(
                                    $"备份压缩包中未找到当前数据库文件 '{_databaseFileName}'。");
                            }

                            using var entryStream = entry.Open();
                            using var outputStream = File.Create(tempRestore);
                            entryStream.CopyTo(outputStream);
                        }
                    }
                    else
                    {
                        FileCopyHelper.Copy(backupFilePath, tempRestore, overwrite: true);
                    }

                    AtomicFileHelper.ReplaceFile(tempRestore, _databasePath);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteFile(tempRestore);
                }
                
                Log.Information("Database restored successfully from {Path}", backupFilePath);
                
                // 恢复成功后删除临时备份
                AtomicFileHelper.TryDeleteFile(tempBackup);
            }
            catch (IOException ioEx)
            {
                Log.Error(ioEx, "File access error during restore. The database might be in use.");
                throw new Exception("数据库文件正被使用，无法还原。请确保关闭所有相关窗口后再试，或重启程序。", ioEx);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restore database.");
                // 尝试恢复之前的版本
                if (!string.IsNullOrWhiteSpace(tempBackup) && File.Exists(tempBackup))
                {
                    try
                    {
                        AtomicFileHelper.ReplaceFile(tempBackup, _databasePath);
                    }
                    catch { /* 尽力而为 */ }
                }
                throw;
            }
        }

        private IEnumerable<FileInfo> GetCandidateBackupFiles()
        {
            if (!_usesSqlite || string.IsNullOrWhiteSpace(_databaseFileName))
            {
                return Enumerable.Empty<FileInfo>();
            }

            var directoryInfo = new DirectoryInfo(_backupDirectory);
            if (!directoryInfo.Exists)
            {
                return Enumerable.Empty<FileInfo>();
            }

            return directoryInfo
                .EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly)
                .Where(ContainsCurrentDatabaseBackup)
                .ToList();
        }

        private bool ContainsCurrentDatabaseBackup(FileInfo file)
        {
            try
            {
                using var archive = ZipFile.OpenRead(file.FullName);
                return archive.Entries.Any(entry =>
                    entry.Name.Equals(_databaseFileName, StringComparison.OrdinalIgnoreCase));
            }
            catch (InvalidDataException)
            {
                return MatchesBackupNameFallback(file.Name);
            }
            catch (IOException)
            {
                return MatchesBackupNameFallback(file.Name);
            }
        }

        private bool MatchesBackupNameFallback(string fileName)
        {
            string expectedSuffix = "_" + BuildBackupNameToken(_databaseFileName) + ".zip";
            return fileName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildBackupNameToken(string databaseFileName)
        {
            string rawName = Path.GetFileNameWithoutExtension(databaseFileName);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "data";
            }

            var buffer = rawName
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
                .ToArray();
            string normalized = new string(buffer).Trim('_');
            return string.IsNullOrWhiteSpace(normalized) ? "data" : normalized;
        }

    }
}
