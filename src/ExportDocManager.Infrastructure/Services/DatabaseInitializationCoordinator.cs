namespace ExportDocManager.Services.Infrastructure
{
    public sealed class DatabaseInitializationCoordinator
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _initialized;

        public async Task<DatabaseInitializationResult> InitializeOnceAsync(
            Func<Task<DatabaseInitializationResult>> initializeAsync)
        {
            ArgumentNullException.ThrowIfNull(initializeAsync);
            if (Volatile.Read(ref _initialized))
            {
                return DatabaseInitializationResult.Success();
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initialized)
                {
                    return DatabaseInitializationResult.Success();
                }

                var result = await initializeAsync().ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    Volatile.Write(ref _initialized, true);
                }

                return result;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
