using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiDownloadTicket(
        string Token,
        string DownloadUrl,
        DateTimeOffset ExpiresAtUtc);

    public sealed class ApiDownloadTicketService
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private const int MaximumTicketCount = 4096;
        private readonly ConcurrentDictionary<string, TicketState> _tickets =
            new(StringComparer.Ordinal);
        private readonly Lock _issueLock = new();
        private readonly TimeProvider _timeProvider;

        public ApiDownloadTicketService()
            : this(TimeProvider.System)
        {
        }

        internal ApiDownloadTicketService(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public ApiDownloadTicket Issue(
            string purpose,
            string resourceId,
            string downloadRoutePrefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(downloadRoutePrefix);

            string normalizedRoute = downloadRoutePrefix.Trim().TrimEnd('/');
            if (!normalizedRoute.StartsWith("/", StringComparison.Ordinal) ||
                normalizedRoute.StartsWith("//", StringComparison.Ordinal) ||
                normalizedRoute.IndexOfAny(['?', '#', '\\']) >= 0 ||
                normalizedRoute.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or ".."))
            {
                throw new ArgumentException(
                    "Download route prefix must be a normalized same-origin application path.",
                    nameof(downloadRoutePrefix));
            }

            lock (_issueLock)
            {
                CleanupExpired();
                TrimToCapacity(MaximumTicketCount - 1);
                string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                DateTimeOffset expiresAt = _timeProvider.GetUtcNow().Add(Lifetime);
                _tickets[token] = new TicketState(purpose.Trim(), resourceId.Trim(), expiresAt);
                return new ApiDownloadTicket(
                    token,
                    $"{normalizedRoute}/{Uri.EscapeDataString(token)}",
                    expiresAt);
            }
        }

        public bool TryResolve(
            string token,
            string purpose,
            out string resourceId)
        {
            resourceId = string.Empty;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(purpose))
            {
                return false;
            }

            string normalizedToken = token.Trim();
            if (!_tickets.TryGetValue(normalizedToken, out TicketState ticket))
            {
                return false;
            }
            if (ticket.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            {
                _tickets.TryRemove(normalizedToken, out _);
                return false;
            }
            if (!string.Equals(ticket.Purpose, purpose.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            resourceId = ticket.ResourceId;
            return true;
        }

        private void CleanupExpired()
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (KeyValuePair<string, TicketState> pair in _tickets)
            {
                if (pair.Value.ExpiresAtUtc <= now)
                {
                    _tickets.TryRemove(pair.Key, out _);
                }
            }
        }

        private void TrimToCapacity(int maximumCount)
        {
            int removeCount = _tickets.Count - Math.Max(0, maximumCount);
            if (removeCount <= 0)
            {
                return;
            }

            foreach (KeyValuePair<string, TicketState> pair in _tickets
                .OrderBy(item => item.Value.ExpiresAtUtc)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Take(removeCount))
            {
                _tickets.TryRemove(pair.Key, out _);
            }
        }

        private sealed record TicketState(
            string Purpose,
            string ResourceId,
            DateTimeOffset ExpiresAtUtc);
    }
}
