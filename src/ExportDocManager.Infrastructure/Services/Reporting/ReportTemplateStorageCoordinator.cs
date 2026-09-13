using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Reporting;

/// <summary>Serializes file-template commits and journals only the files they change.</summary>
internal sealed class ReportTemplateStorageCoordinator
{
    private readonly IAppPathProvider _pathProvider;
    private readonly ISettingsService _settingsService;
    private readonly ILogger? _logger;
    private readonly ReportTemplateStorageLock _storageLock;

    public ReportTemplateStorageCoordinator(IAppPathProvider pathProvider, ISettingsService settingsService, ILogger? logger = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;
        _storageLock = new ReportTemplateStorageLock(pathProvider);
    }

    public async Task<T> ExecuteMutationAsync<T>(Func<ReportTemplateStorageMutation, Task<T>> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await using var fileLock = await _storageLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = new ReportTemplateStorageMutation(_pathProvider, _logger);
        try
        {
            T result = await mutation(transaction).ConfigureAwait(false);
            // Publish settings only after every fallible file/catalog operation has succeeded.
            // UpdateAsync reloads the latest settings under its own lock and commits atomically;
            // a failed template transaction never needs to restore another writer's settings.
            await transaction.CommitSettingsAsync(_settingsService, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception originalException)
        {
            try
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                transaction.RetainRecoveryFiles();
                throw new InvalidOperationException(
                    "报表模板存储事务失败，且无法完整恢复原状态。请停止继续修改并检查运行数据根中的事务恢复文件。",
                    new AggregateException(originalException, rollbackException));
            }
            throw;
        }
    }

    public async Task<T> ExecuteReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        await using var fileLock = await _storageLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await read().ConfigureAwait(false);
    }

    internal sealed class ReportTemplateStorageMutation : IAsyncDisposable
    {
        private readonly IAppPathProvider _pathProvider;
        private readonly ILogger? _logger;
        private readonly string _templatesRoot;
        private readonly List<Func<AppSettings, bool>> _settingsUpdates = [];
        private readonly List<(string Target, string? Backup)> _files = [];
        private string? _snapshotRoot;
        private bool _retainRecoveryFiles;

        internal ReportTemplateStorageMutation(IAppPathProvider pathProvider, ILogger? logger)
        {
            _pathProvider = pathProvider;
            _logger = logger;
            _templatesRoot = new ReportTemplatePathResolver(pathProvider).GetUserTemplatesBaseDirectory();
        }

        /// <summary>Call before the first write, move or delete of each affected file.</summary>
        public async Task CaptureFilesAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = PathBoundaryHelper.EnsureWithinRoot(path, _templatesRoot, "模板事务路径离开了受管目录。");
                PathBoundaryHelper.EnsureNoLinkLikeComponents(target, "模板事务路径不能经过符号链接或联接点。");
                if (_files.Any(file => PhysicalPathComparison.AreSamePath(file.Target, target))) continue;
                if (Directory.Exists(target)) throw new IOException("模板事务目标必须是文件。");
                string? backup = null;
                if (File.Exists(target))
                {
                    _snapshotRoot ??= RuntimeCachePathHelper.CreateUniqueDirectory(_pathProvider, "TemplateTransactions", "report-template-snapshot");
                    backup = Path.Combine(_snapshotRoot, $"{_files.Count}.bak");
                    await using var source = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                    await using var destination = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
                _files.Add((target, backup));
            }
        }

        public void UpdateSettings(Func<AppSettings, bool> update) => _settingsUpdates.Add(update);

        internal Task CommitSettingsAsync(ISettingsService settingsService, CancellationToken cancellationToken) =>
            _settingsUpdates.Count == 0 ? Task.CompletedTask : settingsService.UpdateAsync(settings =>
            {
                bool changed = false;
                foreach (var update in _settingsUpdates) changed |= update(settings);
                return changed;
            }, cancellationToken);

        public void RetainRecoveryFiles() => _retainRecoveryFiles = true;

        public async Task RollbackAsync()
        {
            foreach (var file in _files.AsEnumerable().Reverse())
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(file.Target, "模板事务恢复路径不能经过符号链接或联接点。");
                if (file.Backup == null)
                {
                    File.Delete(file.Target);
                }
                else
                {
                    await AtomicFileHelper.WriteFileAtomicAsync(file.Target, (temporaryPath, token) =>
                    {
                        File.Copy(file.Backup, temporaryPath, overwrite: true);
                        return Task.CompletedTask;
                    }, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_snapshotRoot != null && !_retainRecoveryFiles)
            {
                AtomicFileHelper.TryDeleteDirectory(_snapshotRoot);
                if (Directory.Exists(_snapshotRoot)) _logger?.LogWarning("未能清理报表模板事务快照目录 {SnapshotRoot}。", _snapshotRoot);
            }
            return ValueTask.CompletedTask;
        }

    }
}
