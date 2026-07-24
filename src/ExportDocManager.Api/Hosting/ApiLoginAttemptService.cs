namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiLoginAttemptDecision(bool Allowed, TimeSpan RetryAfter)
    {
        public static ApiLoginAttemptDecision Allow { get; } = new(true, TimeSpan.Zero);
    }

    public sealed class ApiLoginAttemptService
    {
        public const int MaximumFailures = 5;
        private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

        private readonly object _gate = new();
        private readonly Dictionary<string, AttemptState> _states = new(StringComparer.Ordinal);
        private readonly TimeProvider _timeProvider;
        private int _operationsSinceCleanup;

        public ApiLoginAttemptService()
            : this(TimeProvider.System)
        {
        }

        public ApiLoginAttemptService(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public ApiLoginAttemptDecision Evaluate(string username, string remoteAddress)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            lock (_gate)
            {
                CleanupIfNeeded(now);
                TimeSpan retryAfter = GetRetryAfter(AccountKey(username), now);
                TimeSpan ipRetryAfter = GetRetryAfter(IpKey(remoteAddress), now);
                if (ipRetryAfter > retryAfter)
                {
                    retryAfter = ipRetryAfter;
                }
                return retryAfter > TimeSpan.Zero
                    ? new ApiLoginAttemptDecision(false, retryAfter)
                    : ApiLoginAttemptDecision.Allow;
            }
        }

        public ApiLoginAttemptDecision RecordFailure(string username, string remoteAddress)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            lock (_gate)
            {
                TimeSpan accountRetry = RecordFailure(AccountKey(username), now);
                TimeSpan ipRetry = RecordFailure(IpKey(remoteAddress), now);
                TimeSpan retryAfter = accountRetry > ipRetry ? accountRetry : ipRetry;
                return retryAfter > TimeSpan.Zero
                    ? new ApiLoginAttemptDecision(false, retryAfter)
                    : ApiLoginAttemptDecision.Allow;
            }
        }

        public void RecordSuccess(string username, string remoteAddress)
        {
            lock (_gate)
            {
                _states.Remove(AccountKey(username));
                _states.Remove(IpKey(remoteAddress));
            }
        }

        private TimeSpan RecordFailure(string key, DateTimeOffset now)
        {
            if (!_states.TryGetValue(key, out var state) || now - state.WindowStarted > FailureWindow)
            {
                state = new AttemptState(now);
                _states[key] = state;
            }

            state.FailureCount++;
            state.LastSeen = now;
            if (state.FailureCount >= MaximumFailures)
            {
                state.LockedUntil = now.Add(LockDuration);
                state.FailureCount = 0;
                state.WindowStarted = now;
                return LockDuration;
            }

            return TimeSpan.Zero;
        }

        private TimeSpan GetRetryAfter(string key, DateTimeOffset now)
        {
            if (!_states.TryGetValue(key, out var state) || state.LockedUntil <= now)
            {
                return TimeSpan.Zero;
            }
            return state.LockedUntil - now;
        }

        private void CleanupIfNeeded(DateTimeOffset now)
        {
            _operationsSinceCleanup++;
            if (_operationsSinceCleanup < 128)
            {
                return;
            }

            _operationsSinceCleanup = 0;
            foreach (string key in _states
                         .Where(pair => now - pair.Value.LastSeen > Retention && pair.Value.LockedUntil <= now)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _states.Remove(key);
            }
        }

        private static string AccountKey(string username) =>
            "account:" + (username ?? string.Empty).Trim().ToUpperInvariant();

        private static string IpKey(string remoteAddress) =>
            "ip:" + (string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress.Trim());

        private sealed class AttemptState
        {
            public AttemptState(DateTimeOffset now)
            {
                WindowStarted = now;
                LastSeen = now;
            }

            public DateTimeOffset WindowStarted { get; set; }
            public DateTimeOffset LastSeen { get; set; }
            public DateTimeOffset LockedUntil { get; set; }
            public int FailureCount { get; set; }
        }
    }
}
