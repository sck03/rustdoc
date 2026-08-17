using System.IO.Compression;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class BackupServiceTests
    {
        [Fact]
        public async Task SqliteBackupRestore_ShouldQueueAndApplyAConsistentSnapshot()
        {
            string root = CreateTestRoot("sqlite-backup-restore");
            try
            {
                var fixture = CreateFixture(root);
                await WriteValueAsync(fixture.DatabasePath, "before-backup");
                var backupResult = await fixture.Service.BackupDatabaseAsync();
                Assert.True(backupResult.Success, backupResult.Message);

                await WriteValueAsync(fixture.DatabasePath, "after-backup");
                var restoreResult = await fixture.Service.ScheduleRestoreAsync(backupResult.FilePath);

                Assert.True(restoreResult.Success);
                Assert.True(File.Exists(SqlitePendingRestoreManager.GetMarkerPath(fixture.DatabasePath)));
                Assert.True(File.Exists(SqlitePendingRestoreManager.GetStagedRestorePath(fixture.DatabasePath)));

                SqlitePendingRestoreManager.ApplyPendingRestore(fixture.PathProvider, fixture.Settings);

                Assert.Equal("before-backup", await ReadValueAsync(fixture.DatabasePath));
                Assert.False(File.Exists(SqlitePendingRestoreManager.GetMarkerPath(fixture.DatabasePath)));
                Assert.False(File.Exists(SqlitePendingRestoreManager.GetStagedRestorePath(fixture.DatabasePath)));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task SqlitePendingRestore_WhenTargetWasAlreadyReplaced_ShouldFinishIdempotently()
        {
            string root = CreateTestRoot("sqlite-backup-restore-crash-recovery");
            try
            {
                var fixture = CreateFixture(root);
                await WriteValueAsync(fixture.DatabasePath, "queued-version");
                var backupResult = await fixture.Service.BackupDatabaseAsync();
                Assert.True(backupResult.Success, backupResult.Message);

                await WriteValueAsync(fixture.DatabasePath, "current-version");
                await fixture.Service.ScheduleRestoreAsync(backupResult.FilePath);
                string markerPath = SqlitePendingRestoreManager.GetMarkerPath(fixture.DatabasePath);
                string stagedPath = SqlitePendingRestoreManager.GetStagedRestorePath(fixture.DatabasePath);

                SqliteConnection.ClearAllPools();
                File.Move(stagedPath, fixture.DatabasePath, overwrite: true);
                await File.WriteAllTextAsync(fixture.DatabasePath + "-wal", "stale wal");
                await File.WriteAllTextAsync(fixture.DatabasePath + "-shm", "stale shm");

                SqlitePendingRestoreManager.ApplyPendingRestore(fixture.PathProvider, fixture.Settings);

                Assert.Equal("queued-version", await ReadValueAsync(fixture.DatabasePath));
                Assert.False(File.Exists(markerPath));
                Assert.False(File.Exists(stagedPath));
                Assert.False(File.Exists(fixture.DatabasePath + "-wal"));
                Assert.False(File.Exists(fixture.DatabasePath + "-shm"));

                SqlitePendingRestoreManager.ApplyPendingRestore(fixture.PathProvider, fixture.Settings);
                Assert.Equal("queued-version", await ReadValueAsync(fixture.DatabasePath));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task SqliteRestoreScheduling_WhenTaskAlreadyExists_ShouldPreserveTheFirstTask()
        {
            string root = CreateTestRoot("sqlite-restore-single-pending-task");
            try
            {
                var fixture = CreateFixture(root);
                await WriteValueAsync(fixture.DatabasePath, "first-queued-version");
                var backupResult = await fixture.Service.BackupDatabaseAsync();
                Assert.True(backupResult.Success, backupResult.Message);

                await WriteValueAsync(fixture.DatabasePath, "current-version");
                await fixture.Service.ScheduleRestoreAsync(backupResult.FilePath);
                string markerPath = SqlitePendingRestoreManager.GetMarkerPath(fixture.DatabasePath);
                string stagedPath = SqlitePendingRestoreManager.GetStagedRestorePath(fixture.DatabasePath);
                byte[] firstMarker = await File.ReadAllBytesAsync(markerPath);
                byte[] firstStagedDatabase = await File.ReadAllBytesAsync(stagedPath);

                var error = await Assert.ThrowsAsync<ResourceConflictException>(() =>
                    fixture.Service.ScheduleRestoreAsync(backupResult.FilePath));

                Assert.Contains("已有 SQLite 数据库还原任务", error.Message, StringComparison.Ordinal);
                Assert.Equal(firstMarker, await File.ReadAllBytesAsync(markerPath));
                Assert.Equal(firstStagedDatabase, await File.ReadAllBytesAsync(stagedPath));

                SqlitePendingRestoreManager.ApplyPendingRestore(fixture.PathProvider, fixture.Settings);
                Assert.Equal("first-queued-version", await ReadValueAsync(fixture.DatabasePath));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task BackupList_ShouldUseManagedFileNamesAndIgnoreUnrelatedZipFiles()
        {
            string root = CreateTestRoot("sqlite-backup-list-filter");
            try
            {
                var fixture = CreateFixture(root);
                await WriteValueAsync(fixture.DatabasePath, "managed-backup");
                DatabaseBackupResult backupResult = await fixture.Service.BackupDatabaseAsync();
                Assert.True(backupResult.Success, backupResult.Message);

                string unrelatedZip = Path.Combine(fixture.PathProvider.BackupRoot, "customer-upload.zip");
                await File.WriteAllBytesAsync(unrelatedZip, [1, 2, 3, 4]);

                List<string> backups = fixture.Service.GetAvailableBackups();

                Assert.Single(backups);
                Assert.Equal(backupResult.FilePath, backups[0]);
                Assert.DoesNotContain(unrelatedZip, backups, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task ImportBackup_ShouldQuickCheckDownloadedZipAndAvoidOverwritingExistingName()
        {
            string root = CreateTestRoot("sqlite-backup-import");
            try
            {
                var fixture = CreateFixture(root);
                await WriteValueAsync(fixture.DatabasePath, "imported-value");
                DatabaseBackupResult source = await fixture.Service.BackupDatabaseAsync();
                Assert.True(source.Success, source.Message);

                DatabaseBackupImportResult imported = await fixture.Service.ImportBackupAsync(
                    source.FilePath,
                    Path.GetFileName(source.FilePath));

                Assert.True(imported.Success, imported.Message);
                Assert.NotEqual(source.FilePath, imported.FilePath);
                Assert.True(File.Exists(imported.FilePath));
                Assert.Equal(
                    new FileInfo(source.FilePath).Length,
                    new FileInfo(imported.FilePath).Length);
                Assert.Equal(2, fixture.Service.GetAvailableBackups().Count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task ImportBackup_ShouldRejectZipWithUnexpectedEntries()
        {
            string root = CreateTestRoot("sqlite-backup-import-invalid");
            try
            {
                var fixture = CreateFixture(root);
                string invalidPath = Path.Combine(root, "unexpected.zip");
                await using (var stream = new FileStream(invalidPath, FileMode.CreateNew))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    archive.CreateEntry("other.db");
                }

                await Assert.ThrowsAsync<ServiceValidationException>(() =>
                    fixture.Service.ImportBackupAsync(invalidPath));
                Assert.Empty(fixture.Service.GetAvailableBackups());
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        [Fact]
        public async Task BackupDatabase_ShouldStopBeforeSnapshotWhenRuntimeVolumeIsFull()
        {
            string root = CreateTestRoot("sqlite-backup-space");
            try
            {
                var fixture = CreateFixture(root, _ => 0);
                await WriteValueAsync(fixture.DatabasePath, "storage-budget");

                DatabaseBackupResult result = await fixture.Service.BackupDatabaseAsync();

                Assert.False(result.Success);
                Assert.Contains("可用空间", result.Message, StringComparison.Ordinal);
                Assert.Empty(fixture.Service.GetAvailableBackups());
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DeleteDirectoryIfExists(root);
            }
        }

        private static BackupFixture CreateFixture(
            string root,
            Func<string, long>? getAvailableBytes = null)
        {
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
            Directory.CreateDirectory(pathProvider.DatabaseRoot);
            string databasePath = Path.Combine(pathProvider.DatabaseRoot, "backup-test.db");
            var settings = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.SqliteProvider,
                SqliteDatabaseFileName = databasePath
            };
            var service = new BackupService(
                settings,
                pathProvider,
                backupDirectory: pathProvider.BackupRoot,
                databasePath: databasePath,
                getAvailableBytes: getAvailableBytes);
            return new BackupFixture(pathProvider, settings, service, databasePath);
        }

        private static async Task WriteValueAsync(string databasePath, string value)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(DbHelper.BuildConnectionString(databasePath));
            await connection.OpenAsync();
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE IF NOT EXISTS RestoreProbe (Value TEXT NOT NULL);";
                await create.ExecuteNonQueryAsync();
            }
            await using (var replace = connection.CreateCommand())
            {
                replace.CommandText = "DELETE FROM RestoreProbe; INSERT INTO RestoreProbe (Value) VALUES ($value);";
                replace.Parameters.AddWithValue("$value", value);
                await replace.ExecuteNonQueryAsync();
            }
        }

        private static async Task<string> ReadValueAsync(string databasePath)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(DbHelper.BuildConnectionString(databasePath));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM RestoreProbe LIMIT 1;";
            return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
        }

        private static string CreateTestRoot(string name)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "edm-backup-tests",
                $"{name[..Math.Min(name.Length, 12)]}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private sealed record BackupFixture(
            RuntimeAppPathProvider PathProvider,
            DatabaseConnectionSettings Settings,
            BackupService Service,
            string DatabasePath);
    }
}
