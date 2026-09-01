using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Tests;

public sealed class ApiBackgroundJobPersistenceAtomicityTests
{
    [Fact]
    public async Task Cancel_WhenPersistenceFails_ShouldRestoreSnapshotAndNotSignalWorker()
    {
        string root = CreateTestRoot("cancel");
        try
        {
            var paths = CreatePathProvider(root);
            var service = new ApiBackgroundJobService(paths);
            service.Upsert(CreateJob("cancel-job", BackgroundJobStatusCatalog.Running, canCancel: true));
            using var cancellation = new CancellationTokenSource();
            service.RegisterCancellationSource("cancel-job", cancellation);
            BlockPersistenceDirectory(paths);

            await Assert.ThrowsAnyAsync<Exception>(() => service.RequestCancelAsync("cancel-job"));

            BackgroundJobSnapshot snapshot = Assert.IsType<BackgroundJobSnapshot>(await service.GetAsync("cancel-job"));
            Assert.Equal(BackgroundJobStatusCatalog.Running, snapshot.Status);
            Assert.True(snapshot.CanCancel);
            Assert.False(cancellation.IsCancellationRequested);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExistingCorruptHistory_ShouldBeMarkedUnavailableInsteadOfLookingEmpty()
    {
        string root = CreateTestRoot("corrupt-history");
        try
        {
            var paths = CreatePathProvider(root);
            string storeDirectory = Path.Combine(paths.CacheRoot, "BackgroundJobs");
            Directory.CreateDirectory(storeDirectory);
            File.WriteAllText(Path.Combine(storeDirectory, "jobs.json"), "{not-json");

            var service = new ApiBackgroundJobService(paths);

            Assert.False(service.PersistenceStoreReady);
            Assert.Equal("invalid-json", service.PersistenceStoreStatus);
            Assert.Empty((await service.QueryAsync(new BackgroundJobQuery())).Items);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExistingHistoryWithNullRecord_ShouldBeMarkedUnavailableInsteadOfPartiallyLoading()
    {
        string root = CreateTestRoot("null-record");
        try
        {
            var paths = CreatePathProvider(root);
            string storeDirectory = Path.Combine(paths.CacheRoot, "BackgroundJobs");
            Directory.CreateDirectory(storeDirectory);
            File.WriteAllText(
                Path.Combine(storeDirectory, "jobs.json"),
                "[{\"JobId\":\"valid-job\",\"Status\":\"Succeeded\"},null]");

            var service = new ApiBackgroundJobService(paths);

            Assert.False(service.PersistenceStoreReady);
            Assert.Equal("invalid-record", service.PersistenceStoreStatus);
            Assert.Empty((await service.QueryAsync(new BackgroundJobQuery())).Items);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExistingHistoryWithMissingJobId_ShouldBeMarkedUnavailableInsteadOfPartiallyLoading()
    {
        string root = CreateTestRoot("missing-job-id");
        try
        {
            var paths = CreatePathProvider(root);
            string storeDirectory = Path.Combine(paths.CacheRoot, "BackgroundJobs");
            Directory.CreateDirectory(storeDirectory);
            File.WriteAllText(
                Path.Combine(storeDirectory, "jobs.json"),
                "[{\"JobId\":\"valid-job\",\"Status\":\"Succeeded\"},{\"Status\":\"Running\"}]");

            var service = new ApiBackgroundJobService(paths);

            Assert.False(service.PersistenceStoreReady);
            Assert.Equal("invalid-record", service.PersistenceStoreStatus);
            Assert.Empty((await service.QueryAsync(new BackgroundJobQuery())).Items);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExistingHistoryWithDuplicateJobId_ShouldBeMarkedUnavailableInsteadOfOverwriting()
    {
        string root = CreateTestRoot("duplicate-job-id");
        try
        {
            var paths = CreatePathProvider(root);
            string storeDirectory = Path.Combine(paths.CacheRoot, "BackgroundJobs");
            Directory.CreateDirectory(storeDirectory);
            File.WriteAllText(
                Path.Combine(storeDirectory, "jobs.json"),
                "[{\"JobId\":\"same-job\",\"Status\":\"Succeeded\"},{\"JobId\":\"SAME-JOB\",\"Status\":\"Failed\"}]");

            var service = new ApiBackgroundJobService(paths);

            Assert.False(service.PersistenceStoreReady);
            Assert.Equal("invalid-record", service.PersistenceStoreStatus);
            Assert.Empty((await service.QueryAsync(new BackgroundJobQuery())).Items);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExistingHistoryStoreDirectory_ShouldBeMarkedUnavailable()
    {
        string root = CreateTestRoot("store-directory");
        try
        {
            var paths = CreatePathProvider(root);
            string storePath = Path.Combine(paths.CacheRoot, "BackgroundJobs", "jobs.json");
            Directory.CreateDirectory(storePath);

            var service = new ApiBackgroundJobService(paths);

            Assert.False(service.PersistenceStoreReady);
            Assert.Equal("invalid-store", service.PersistenceStoreStatus);
            Assert.Empty((await service.QueryAsync(new BackgroundJobQuery())).Items);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Update_WhenPersistenceFails_ShouldRestorePreviousSnapshot()
    {
        string root = CreateTestRoot("update");
        try
        {
            var paths = CreatePathProvider(root);
            var service = new ApiBackgroundJobService(paths);
            service.Upsert(CreateJob("update-job", BackgroundJobStatusCatalog.Running, canCancel: true));
            BlockPersistenceDirectory(paths);

            Assert.ThrowsAny<Exception>(() => service.Update(
                "update-job",
                current => CopyWithStatus(current, BackgroundJobStatusCatalog.Failed)));

            BackgroundJobSnapshot snapshot = Assert.IsType<BackgroundJobSnapshot>(await service.GetAsync("update-job"));
            Assert.Equal(BackgroundJobStatusCatalog.Running, snapshot.Status);
            Assert.True(snapshot.CanCancel);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Delete_WhenPersistenceFails_ShouldRestoreHistoryAndKeepOutput()
    {
        string root = CreateTestRoot("delete");
        try
        {
            var paths = CreatePathProvider(root);
            string outputPath = Path.Combine(paths.ExportRoot, "Browser", "report", "delete-job", "report.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, "report");

            var service = new ApiBackgroundJobService(paths);
            var job = CreateJob("delete-job", BackgroundJobStatusCatalog.Succeeded, canCancel: false);
            job = CopyWithOutput(job, outputPath);
            service.Upsert(job);
            BlockPersistenceDirectory(paths);

            await Assert.ThrowsAnyAsync<Exception>(() => service.DeleteAsync("delete-job"));

            Assert.NotNull(await service.GetAsync("delete-job"));
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static BackgroundJobSnapshot CreateJob(string id, string status, bool canCancel) => new()
    {
        JobId = id,
        Kind = "test",
        Title = id,
        Status = status,
        StatusText = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CompletedAt = BackgroundJobStatusCatalog.IsTerminal(status) ? DateTimeOffset.UtcNow : null,
        CanCancel = canCancel
    };

    private static BackgroundJobSnapshot CopyWithStatus(BackgroundJobSnapshot source, string status) => new()
    {
        JobId = source.JobId,
        Kind = source.Kind,
        Title = source.Title,
        Status = status,
        StatusText = status,
        RequestedBy = source.RequestedBy,
        RequestedByUserId = source.RequestedByUserId,
        CreatedAt = source.CreatedAt,
        StartedAt = source.StartedAt,
        CompletedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        OutputPath = source.OutputPath,
        ErrorMessage = "failed",
        CanCancel = false,
        CanRetry = source.CanRetry,
        RetryOperation = source.RetryOperation,
        RetryRequestJson = source.RetryRequestJson
    };

    private static BackgroundJobSnapshot CopyWithOutput(BackgroundJobSnapshot source, string outputPath) => new()
    {
        JobId = source.JobId,
        Kind = source.Kind,
        Title = source.Title,
        Status = source.Status,
        StatusText = source.StatusText,
        RequestedBy = source.RequestedBy,
        RequestedByUserId = source.RequestedByUserId,
        CreatedAt = source.CreatedAt,
        StartedAt = source.StartedAt,
        CompletedAt = source.CompletedAt,
        UpdatedAt = source.UpdatedAt,
        OutputPath = outputPath,
        ErrorMessage = source.ErrorMessage,
        CanCancel = source.CanCancel,
        CanRetry = source.CanRetry,
        RetryOperation = source.RetryOperation,
        RetryRequestJson = source.RetryRequestJson
    };

    private static RuntimeAppPathProvider CreatePathProvider(string root) =>
        new(Path.Combine(root, "app"), Path.Combine(root, "data"));

    private static void BlockPersistenceDirectory(RuntimeAppPathProvider paths)
    {
        // Make the store path itself a directory.  This deterministically makes
        // the atomic writer fail while leaving the parent cache directory usable;
        // trying to replace the parent directory with a file is rejected by
        // Windows before the service is exercised and makes the test depend on
        // ACL/administrator behavior.
        string storePath = Path.Combine(paths.CacheRoot, "BackgroundJobs", "jobs.json");
        if (File.Exists(storePath))
        {
            File.Delete(storePath);
        }

        Directory.CreateDirectory(storePath);
    }

    private static string CreateTestRoot(string suffix)
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            ".codex-runtime",
            $"background-job-atomicity-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string root)
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }
}
