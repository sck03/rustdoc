using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Reporting
{
    /// <summary>
    /// Serializes classic file-template mutations across service instances and
    /// processes.  Every mutation snapshots the managed template root and the
    /// report-related settings, then restores both if any later step fails.
    /// </summary>
    internal sealed class ReportTemplateStorageCoordinator
    {
        private static readonly JsonSerializerOptions SnapshotOptions = new();

        private readonly IAppPathProvider _pathProvider;
        private readonly ISettingsService _settingsService;
        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ILogger? _logger;
        private readonly string _lockFilePath;

        public ReportTemplateStorageCoordinator(
            IAppPathProvider pathProvider,
            ISettingsService settingsService,
            ILogger? logger = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _logger = logger;
            _lockFilePath = Path.Combine(pathProvider.DataRoot, "Locks", "report-template-storage.lock");
        }

        public async Task<T> ExecuteMutationAsync<T>(
            Func<ReportTemplateStorageMutation, Task<T>> mutation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mutation);

            await using var fileLock = await CrossProcessFileLock
                .AcquireAsync(_lockFilePath, cancellationToken)
                .ConfigureAwait(false);
            var transaction = await ReportTemplateStorageMutation
                .CreateAsync(_pathProvider, _settingsService, _pathResolver, _logger, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                return await mutation(transaction).ConfigureAwait(false);
            }
            catch (Exception originalException)
            {
                try
                {
                    await transaction.RollbackAsync().ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    throw new InvalidOperationException(
                        "报表模板存储事务失败，且无法完整恢复原状态。请停止继续修改并检查运行数据根。",
                        new AggregateException(originalException, rollbackException));
                }

                throw;
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }

        public async Task<T> ExecuteReadAsync<T>(
            Func<Task<T>> read,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(read);
            await using var fileLock = await CrossProcessFileLock
                .AcquireAsync(_lockFilePath, cancellationToken)
                .ConfigureAwait(false);
            return await read().ConfigureAwait(false);
        }

        internal sealed class ReportTemplateStorageMutation : IAsyncDisposable
        {
            private readonly ISettingsService _settingsService;
            private readonly ILogger? _logger;
            private readonly string _templatesRoot;
            private readonly string _snapshotRoot;
            private readonly string _templatesSnapshotRoot;
            private readonly bool _templatesOriginallyExisted;
            private readonly ReportSettingsSnapshot _settingsSnapshot;
            private bool _templatesChanged;
            private bool _settingsChanged;

            private ReportTemplateStorageMutation(
                ISettingsService settingsService,
                ILogger? logger,
                string templatesRoot,
                string snapshotRoot,
                bool templatesOriginallyExisted,
                ReportSettingsSnapshot settingsSnapshot)
            {
                _settingsService = settingsService;
                _logger = logger;
                _templatesRoot = templatesRoot;
                _snapshotRoot = snapshotRoot;
                _templatesSnapshotRoot = Path.Combine(snapshotRoot, "Templates");
                _templatesOriginallyExisted = templatesOriginallyExisted;
                _settingsSnapshot = settingsSnapshot;
            }

            public void MarkTemplatesChanged() => _templatesChanged = true;

            public void MarkSettingsChanged() => _settingsChanged = true;

            public static async Task<ReportTemplateStorageMutation> CreateAsync(
                IAppPathProvider pathProvider,
                ISettingsService settingsService,
                ReportTemplatePathResolver pathResolver,
                ILogger? logger,
                CancellationToken cancellationToken)
            {
                string templatesRoot = pathResolver.GetUserTemplatesBaseDirectory();
                bool templatesOriginallyExisted = Directory.Exists(templatesRoot);
                string snapshotRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "TemplateTransactions",
                    "report-template-snapshot");

                try
                {
                    if (templatesOriginallyExisted)
                    {
                        CopyDirectoryTree(templatesRoot, Path.Combine(snapshotRoot, "Templates"), cancellationToken);
                    }

                    await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
                    var settingsSnapshot = ReportSettingsSnapshot.Capture(settingsService.Settings);
                    return new ReportTemplateStorageMutation(
                        settingsService,
                        logger,
                        templatesRoot,
                        snapshotRoot,
                        templatesOriginallyExisted,
                        settingsSnapshot);
                }
                catch
                {
                    AtomicFileHelper.TryDeleteDirectory(snapshotRoot);
                    throw;
                }
            }

            public async Task RollbackAsync()
            {
                if (_templatesChanged)
                {
                    RestoreTemplateRoot();
                }

                if (_settingsChanged)
                {
                    await RestoreSettingsAsync().ConfigureAwait(false);
                }
            }

            public ValueTask DisposeAsync()
            {
                AtomicFileHelper.TryDeleteDirectory(_snapshotRoot);
                if (Directory.Exists(_snapshotRoot))
                {
                    _logger?.LogWarning(
                        "未能清理报表模板事务快照目录 {SnapshotRoot}。",
                        _snapshotRoot);
                }

                return ValueTask.CompletedTask;
            }

            private void RestoreTemplateRoot()
            {
                string? parent = Path.GetDirectoryName(_templatesRoot);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    throw new InvalidOperationException("无法解析用户模板根目录的父目录。");
                }

                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    parent,
                    "用户模板事务恢复目录不能经过符号链接、目录联接或其他重解析点。");
                Directory.CreateDirectory(parent);
                string leaf = Path.GetFileName(_templatesRoot);
                string transactionId = Guid.NewGuid().ToString("N");
                string replacementRoot = Path.Combine(parent, $".{leaf}.rollback-new-{transactionId}");
                string failedRoot = Path.Combine(parent, $".{leaf}.rollback-failed-{transactionId}");

                try
                {
                    if (_templatesOriginallyExisted)
                    {
                        CopyDirectoryTree(_templatesSnapshotRoot, replacementRoot, CancellationToken.None);
                    }

                    if (Directory.Exists(_templatesRoot))
                    {
                        PathBoundaryHelper.EnsureNoLinkLikeComponents(
                            _templatesRoot,
                            "用户模板根目录不能是符号链接、目录联接或其他重解析点。");
                        Directory.Move(_templatesRoot, failedRoot);
                    }

                    if (_templatesOriginallyExisted)
                    {
                        Directory.Move(replacementRoot, _templatesRoot);
                    }

                    AtomicFileHelper.TryDeleteDirectory(failedRoot);
                }
                catch
                {
                    AtomicFileHelper.TryDeleteDirectory(replacementRoot);
                    if (!Directory.Exists(_templatesRoot) && Directory.Exists(failedRoot))
                    {
                        Directory.Move(failedRoot, _templatesRoot);
                    }

                    throw;
                }
            }

            private async Task RestoreSettingsAsync()
            {
                await _settingsService.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                var current = ReportSettingsSnapshot.Capture(_settingsService.Settings);
                if (ReportSettingsSnapshot.AreEquivalent(current, _settingsSnapshot))
                {
                    return;
                }

                await _settingsService.UpdateAsync(settings =>
                {
                    _settingsSnapshot.Apply(settings);
                    return true;
                }, CancellationToken.None).ConfigureAwait(false);
            }

            private static void CopyDirectoryTree(
                string sourceRoot,
                string targetRoot,
                CancellationToken cancellationToken)
            {
                string source = Path.GetFullPath(sourceRoot);
                string target = Path.GetFullPath(targetRoot);
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    source,
                    "模板事务源目录不能经过符号链接、目录联接或其他重解析点。");
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    target,
                    "模板事务目标目录不能经过符号链接、目录联接或其他重解析点。");
                if (!Directory.Exists(source))
                {
                    throw new DirectoryNotFoundException($"模板事务源目录不存在：{source}");
                }

                Directory.CreateDirectory(target);
                var pending = new Stack<string>();
                pending.Push(source);
                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string currentSource = pending.Pop();
                    foreach (FileSystemInfo item in new DirectoryInfo(currentSource).EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            throw new UnauthorizedAccessException(
                                $"模板事务目录包含符号链接、目录联接或其他重解析点：{item.FullName}");
                        }

                        string relativePath = Path.GetRelativePath(source, item.FullName);
                        string targetPath = PathBoundaryHelper.EnsureWithinRoot(
                            Path.Combine(target, relativePath),
                            target,
                            "模板事务目标路径离开了受管快照目录。");
                        if (item is DirectoryInfo)
                        {
                            Directory.CreateDirectory(targetPath);
                            pending.Push(item.FullName);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                            File.Copy(item.FullName, targetPath, overwrite: false);
                        }
                    }
                }
            }

            private sealed record ReportSettingsSnapshot(
                ReportTemplateDefaults ReportTemplateDefaults,
                BatchExportSettings BatchExport,
                List<PaymentTemplateItem> PaymentTemplates)
            {
                public static ReportSettingsSnapshot Capture(AppSettings settings) =>
                    new(
                        DeepCopy(settings.ReportTemplateDefaults ?? new ReportTemplateDefaults()),
                        DeepCopy(settings.BatchExport ?? new BatchExportSettings()),
                        DeepCopy(settings.PaymentTemplates ?? []));

                public void Apply(AppSettings settings)
                {
                    settings.ReportTemplateDefaults = DeepCopy(ReportTemplateDefaults);
                    settings.BatchExport = DeepCopy(BatchExport);
                    settings.PaymentTemplates = DeepCopy(PaymentTemplates);
                }

                public static bool AreEquivalent(
                    ReportSettingsSnapshot left,
                    ReportSettingsSnapshot right)
                {
                    byte[] leftJson = JsonSerializer.SerializeToUtf8Bytes(left, SnapshotOptions);
                    byte[] rightJson = JsonSerializer.SerializeToUtf8Bytes(right, SnapshotOptions);
                    return leftJson.AsSpan().SequenceEqual(rightJson);
                }

                private static T DeepCopy<T>(T value)
                {
                    byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, SnapshotOptions);
                    return JsonSerializer.Deserialize<T>(json, SnapshotOptions)
                           ?? throw new InvalidOperationException("无法创建报表模板设置快照。");
                }
            }
        }
    }
}
