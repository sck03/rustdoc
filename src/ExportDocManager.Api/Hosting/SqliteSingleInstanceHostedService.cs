using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed class SqliteSingleInstanceHostedService : IHostedService, IDisposable
    {
        private readonly IAppPathProvider _pathProvider;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private FileStream _leaseStream;
        private string _leasePath = string.Empty;
        private bool _ownsLease;

        public SqliteSingleInstanceHostedService(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (DatabaseModeHelper.UsesPostgreSql(_databaseSettings))
            {
                return;
            }

            string lockRoot = Path.Combine(_pathProvider.CacheRoot, "Locks");
            Directory.CreateDirectory(lockRoot);
            _leasePath = Path.Combine(lockRoot, "sqlite-single-instance.lock");
            try
            {
                _leaseStream = new FileStream(
                    _leasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                _ownsLease = true;
                _leaseStream.SetLength(0);
                await using var writer = new StreamWriter(_leaseStream, leaveOpen: true);
                await writer.WriteAsync($"pid={Environment.ProcessId};started={DateTimeOffset.UtcNow:O}")
                    .ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                _leaseStream.Position = 0;
            }
            catch (IOException ex)
            {
                ReleaseLease();
                throw new InvalidOperationException(
                    "SQLite 单机版已经在当前运行数据目录启动。请关闭重复程序后重试。",
                    ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            ReleaseLease();
        }

        private void ReleaseLease()
        {
            bool shouldDelete = _ownsLease;
            _ownsLease = false;
            _leaseStream?.Dispose();
            _leaseStream = null;
            if (shouldDelete && !string.IsNullOrWhiteSpace(_leasePath))
            {
                try
                {
                    File.Delete(_leasePath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
