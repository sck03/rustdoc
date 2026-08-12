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
        string persistenceRoot = Path.Combine(paths.CacheRoot, "BackgroundJobs");
        if (Directory.Exists(persistenceRoot))
        {
            Directory.Delete(persistenceRoot, recursive: true);
        }
        File.WriteAllText(persistenceRoot, "block persistence directory recreation");
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
        if (!Directory.Exists(root))
        {
            return;
        }

        Directory.Delete(root, recursive: true);
    }
}
