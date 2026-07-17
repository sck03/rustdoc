using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class WorkspaceSessionService : IWorkspaceSessionService
    {
        private readonly object _syncRoot = new();
        private Func<Task<bool>> _saveHandler;
        private Task<bool> _pendingSaveTask;

        public Invoice CurrentInvoice { get; private set; }

        public int ActiveInvoiceId => CurrentInvoice?.Id ?? 0;

        public void UpdateCurrentInvoice(Invoice invoice)
        {
            lock (_syncRoot)
            {
                CurrentInvoice = invoice;
            }
        }

        public void RegisterSaveHandler(Func<Task<bool>> saveHandler)
        {
            lock (_syncRoot)
            {
                _saveHandler = saveHandler;
            }
        }

        public void ClearSaveHandler(Func<Task<bool>> saveHandler = null)
        {
            lock (_syncRoot)
            {
                if (saveHandler == null || _saveHandler == saveHandler)
                {
                    _saveHandler = null;
                }
            }
        }

        public Task<bool> RequestSaveAsync()
        {
            Func<Task<bool>> saveHandler;
            lock (_syncRoot)
            {
                if (_pendingSaveTask != null)
                {
                    return _pendingSaveTask;
                }

                saveHandler = _saveHandler;
                if (saveHandler == null)
                {
                    return Task.FromResult(false);
                }

                var pendingSaveSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pendingSaveTask = pendingSaveSource.Task;
                _pendingSaveTask = pendingSaveTask;
                _ = ExecuteSaveAsync(saveHandler, pendingSaveSource);
                return pendingSaveTask;
            }
        }

        private async Task ExecuteSaveAsync(Func<Task<bool>> saveHandler, TaskCompletionSource<bool> pendingSaveSource)
        {
            bool saveSucceeded = false;
            Exception saveException = null;

            try
            {
                saveSucceeded = await saveHandler();
            }
            catch (Exception ex)
            {
                saveException = ex;
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_pendingSaveTask, pendingSaveSource.Task))
                    {
                        _pendingSaveTask = null;
                    }
                }
            }

            if (saveException != null)
            {
                pendingSaveSource.TrySetException(saveException);
                return;
            }

            pendingSaveSource.TrySetResult(saveSucceeded);
        }
    }
}
