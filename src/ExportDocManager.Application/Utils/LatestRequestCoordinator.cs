using System.Threading;

namespace ExportDocManager.Utils
{
    public sealed class LatestRequestCoordinator : IDisposable
    {
        private CancellationTokenSource _currentRequestSource = new();
        private int _currentVersion;

        public RequestHandle Begin()
        {
            var nextRequestSource = new CancellationTokenSource();
            var previousRequestSource = Interlocked.Exchange(ref _currentRequestSource, nextRequestSource);
            CancelAndDispose(previousRequestSource);

            return new RequestHandle(
                Interlocked.Increment(ref _currentVersion),
                nextRequestSource.Token);
        }

        public void CancelCurrent()
        {
            TryCancel(Volatile.Read(ref _currentRequestSource));
        }

        public bool IsCurrent(RequestHandle handle)
        {
            return handle.Version == Volatile.Read(ref _currentVersion) &&
                   !handle.CancellationToken.IsCancellationRequested;
        }

        public void Dispose()
        {
            var requestSource = Interlocked.Exchange(ref _currentRequestSource, null);
            CancelAndDispose(requestSource);
        }

        public readonly record struct RequestHandle(int Version, CancellationToken CancellationToken);

        private static void CancelAndDispose(CancellationTokenSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                TryCancel(source);
            }
            finally
            {
                try
                {
                    source.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static void TryCancel(CancellationTokenSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
