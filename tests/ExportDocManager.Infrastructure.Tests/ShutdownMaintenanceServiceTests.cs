using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class ShutdownMaintenanceServiceTests
    {
        [Fact]
        public async Task RunAsync_ShouldExecutePortableShutdownMaintenanceRules()
        {
            string root = CreateTestRoot("shutdown-maintenance");
            try
            {
                string appRoot = Path.Combine(root, "app");
                string dataRoot = Path.Combine(root, "data");
                string logRoot = Path.Combine(dataRoot, "Logs");
                string backupRoot = Path.Combine(dataRoot, "Backups");
                Directory.CreateDirectory(logRoot);
                Directory.CreateDirectory(backupRoot);

                string oldLog = Path.Combine(logRoot, "old.txt");
                string recentLog = Path.Combine(logRoot, "recent.txt");
                await File.WriteAllTextAsync(oldLog, "old");
                await File.WriteAllTextAsync(recentLog, "recent");
                File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-5));
                File.SetLastWriteTime(recentLog, DateTime.Now);

                string backupPath = Path.Combine(backupRoot, "20260625_data.zip");
                await File.WriteAllTextAsync(backupPath, "backup");

                var settingsService = new TestSettingsService(new AppSettings
                {
                    System = new SystemSettings
                    {
                        BackupRetentionDays = 7,
                        AuditLogRetentionDays = 30,
                        LogRetentionDays = 1
                    },
                    WebDav = new WebDavSettings
                    {
                        Enabled = true
                    }
                });
                var backupService = new TestBackupService([backupPath]);
                var cloudSyncService = new TestCloudSyncService();
                var auditLogService = new TestAuditLogService(12);
                var pathProvider = new TestAppPathProvider(appRoot, dataRoot);

                var service = new ShutdownMaintenanceService(
                    settingsService,
                    backupService,
                    cloudSyncService,
                    auditLogService,
                    pathProvider);

                var result = await service.RunAsync();

                Assert.True(settingsService.LoadCalled);
                Assert.Equal(7, backupService.CleanOldBackupsDaysToKeep);
                Assert.True(backupService.BackupDatabaseCalled);
                Assert.Equal(12, result.DeletedAuditLogs);
                Assert.Equal(1, result.DeletedTextLogs);
                Assert.Equal("20260625_data.zip", result.UploadedBackupFileName);
                Assert.Equal(backupPath, cloudSyncService.UploadedLocalPath);
                Assert.Equal("20260625_data.zip", cloudSyncService.UploadedRemoteFileName);
                Assert.True(auditLogService.CutoffUtc <= DateTime.UtcNow.AddDays(-29));
                Assert.False(File.Exists(oldLog));
                Assert.True(File.Exists(recentLog));
            }
            finally
            {
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task RunAsync_ShouldReturnCloudSyncErrorAndContinueCleanup()
        {
            string root = CreateTestRoot("shutdown-maintenance-cloud-error");
            try
            {
                string appRoot = Path.Combine(root, "app");
                string dataRoot = Path.Combine(root, "data");
                string backupRoot = Path.Combine(dataRoot, "Backups");
                Directory.CreateDirectory(Path.Combine(dataRoot, "Logs"));
                Directory.CreateDirectory(backupRoot);
                string backupPath = Path.Combine(backupRoot, "20260625_data.zip");
                await File.WriteAllTextAsync(backupPath, "backup");

                var settingsService = new TestSettingsService(new AppSettings
                {
                    System = new SystemSettings
                    {
                        BackupRetentionDays = 7,
                        AuditLogRetentionDays = 30,
                        LogRetentionDays = 1
                    },
                    WebDav = new WebDavSettings
                    {
                        Enabled = true
                    }
                });
                var backupService = new TestBackupService([backupPath]);
                var cloudSyncService = new TestCloudSyncService
                {
                    UploadException = new InvalidOperationException("webdav unavailable")
                };
                var auditLogService = new TestAuditLogService(3);

                var service = new ShutdownMaintenanceService(
                    settingsService,
                    backupService,
                    cloudSyncService,
                    auditLogService,
                    new TestAppPathProvider(appRoot, dataRoot));

                var result = await service.RunAsync();

                Assert.True(result.CloudSyncFailed);
                Assert.Contains("webdav unavailable", result.CloudSyncErrorMessage, StringComparison.Ordinal);
                Assert.Equal(3, result.DeletedAuditLogs);
            }
            finally
            {
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task CleanAsync_ShouldCleanAuditLogsAndRuntimeDataTextLogs()
        {
            string root = CreateTestRoot("system-log-cleanup");
            try
            {
                string appRoot = Path.Combine(root, "app");
                string dataRoot = Path.Combine(root, "data");
                string logRoot = Path.Combine(dataRoot, "Logs");
                Directory.CreateDirectory(logRoot);

                string oldByAge = Path.Combine(logRoot, "old-by-age.txt");
                string oldByCount = Path.Combine(logRoot, "old-by-count.txt");
                string retained = Path.Combine(logRoot, "retained.txt");
                await File.WriteAllTextAsync(oldByAge, "old by age");
                await File.WriteAllTextAsync(oldByCount, "old by count");
                await File.WriteAllTextAsync(retained, "retained");
                File.SetLastWriteTime(oldByAge, DateTime.Now.AddDays(-5));
                File.SetLastWriteTime(oldByCount, DateTime.Now.AddMinutes(-5));
                File.SetLastWriteTime(retained, DateTime.Now);

                var settingsService = new TestSettingsService(new AppSettings
                {
                    System = new SystemSettings
                    {
                        AuditLogRetentionDays = 10,
                        LogRetentionDays = 1,
                        LogRetainedFileCount = 1
                    }
                });
                var auditLogService = new TestAuditLogService(5, expectedMaxCount: 300000);
                var service = new SystemLogCleanupService(
                    settingsService,
                    auditLogService,
                    new TestAppPathProvider(appRoot, dataRoot));

                var result = await service.CleanAsync();

                Assert.True(settingsService.LoadCalled);
                Assert.Equal(5, result.DeletedAuditLogs);
                Assert.Equal(1, result.DeletedTextLogsByAge);
                Assert.Equal(1, result.DeletedTextLogsByCount);
                Assert.Equal(2, result.DeletedTextLogs);
                Assert.True(auditLogService.CutoffUtc <= DateTime.UtcNow.AddDays(-9));
                Assert.False(File.Exists(oldByAge));
                Assert.False(File.Exists(oldByCount));
                Assert.True(File.Exists(retained));
                Assert.False(Directory.Exists(Path.Combine(appRoot, "logs")));
            }
            finally
            {
                DeleteDirectoryIfExists(root);
            }
        }

        private static string CreateTestRoot(string name)
        {
            string root = Path.Combine(
                AppContext.BaseDirectory,
                "shutdown-maintenance-tests",
                $"{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private sealed class TestSettingsService : ISettingsService
        {
            public TestSettingsService(AppSettings settings)
            {
                Settings = settings;
            }

            public AppSettings Settings { get; }

            public bool LoadCalled { get; private set; }

            public Task LoadAsync()
            {
                LoadCalled = true;
                return Task.CompletedTask;
            }

            public Task SaveAsync()
            {
                return Task.CompletedTask;
            }
        }

        private sealed class TestBackupService : IBackupService
        {
            private readonly List<string> _availableBackups;

            public TestBackupService(IEnumerable<string> availableBackups)
            {
                _availableBackups = availableBackups.ToList();
            }

            public int CleanOldBackupsDaysToKeep { get; private set; }

            public bool BackupDatabaseCalled { get; private set; }

            public Task BackupDatabaseAsync()
            {
                BackupDatabaseCalled = true;
                return Task.CompletedTask;
            }

            public void CleanOldBackups(int daysToKeep)
            {
                CleanOldBackupsDaysToKeep = daysToKeep;
            }

            public List<string> GetAvailableBackups()
            {
                return _availableBackups;
            }

            public void RestoreDatabase(string backupFilePath)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestCloudSyncService : ICloudSyncService
        {
            public Exception UploadException { get; init; }

            public string UploadedLocalPath { get; private set; } = string.Empty;

            public string UploadedRemoteFileName { get; private set; } = string.Empty;

            public Task UploadFileAsync(string localFilePath, string remoteFileName)
            {
                if (UploadException != null)
                {
                    throw UploadException;
                }

                UploadedLocalPath = localFilePath;
                UploadedRemoteFileName = remoteFileName;
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<CloudBackupFileInfo>> ListBackupFilesAsync()
            {
                return Task.FromResult<IReadOnlyList<CloudBackupFileInfo>>([]);
            }

            public Task DownloadFileAsync(string remoteFileName, string localFilePath)
            {
                return Task.CompletedTask;
            }

            public Task<bool> TestConnectionAsync(WebDavSettings settings)
            {
                return Task.FromResult(true);
            }
        }

        private sealed class TestAuditLogService : IAuditLogService
        {
            private readonly int _deletedCount;
            private readonly int _expectedMaxCount;

            public TestAuditLogService(int deletedCount, int expectedMaxCount = 200000)
            {
                _deletedCount = deletedCount;
                _expectedMaxCount = expectedMaxCount;
            }

            public DateTime CutoffUtc { get; private set; }

            public Task<List<ExportDocManager.Models.Entities.AuditLog>> QueryAsync(
                AuditLogQueryCriteria criteria,
                int maxCount = 2000)
            {
                return Task.FromResult(new List<ExportDocManager.Models.Entities.AuditLog>());
            }

            public Task<int> ExportToExcelAsync(
                AuditLogQueryCriteria criteria,
                string filePath,
                int maxCount = 50000,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> ExportToExcelBytesAsync(
                AuditLogQueryCriteria criteria,
                int maxCount = 50000,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<int> DeleteByCriteriaAsync(AuditLogQueryCriteria criteria, int maxCount = 50000)
            {
                throw new NotSupportedException();
            }

            public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int maxCount = 200000)
            {
                CutoffUtc = cutoffUtc;
                Assert.Equal(_expectedMaxCount, maxCount);
                return Task.FromResult(_deletedCount);
            }
        }

        private sealed class TestAppPathProvider : IAppPathProvider
        {
            public TestAppPathProvider(string appRoot, string dataRoot)
            {
                AppRoot = appRoot;
                DataRoot = dataRoot;
            }

            public string AppRoot { get; }
            public string DataRoot { get; }
            public string DatabaseRoot => Path.Combine(DataRoot, "Database");
            public string TemplateRoot => Path.Combine(AppRoot, "Templates");
            public string ResourceRoot => Path.Combine(AppRoot, "Resources");
            public string BrowserRoot => Path.Combine(AppRoot, "Browsers");
            public string ToolRoot => Path.Combine(AppRoot, "Tools");
            public string FileRoot => Path.Combine(DataRoot, "Files");
            public string ExportRoot => Path.Combine(DataRoot, "Exports");
            public string BackupRoot => Path.Combine(DataRoot, "Backups");
            public string SingleWindowRoot => Path.Combine(DataRoot, "SingleWindow");
            public string OcrModelRoot => Path.Combine(AppRoot, "OcrModels");
            public string LogRoot => Path.Combine(DataRoot, "Logs");
            public string CacheRoot => Path.Combine(DataRoot, "Cache");
            public string ConfigRoot => Path.Combine(DataRoot, "Config");
            public string SecurityRoot => Path.Combine(DataRoot, "Security");
            public string WebViewRoot => Path.Combine(DataRoot, "WebView");
        }
    }
}
