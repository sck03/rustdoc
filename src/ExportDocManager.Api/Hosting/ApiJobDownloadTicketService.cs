using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiJobDownloadTicket(
        string Token,
        string DownloadUrl,
        DateTimeOffset ExpiresAtUtc);

    public sealed class ApiJobDownloadTicketService
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, TicketState> _tickets =
            new(StringComparer.Ordinal);
        private readonly TimeProvider _timeProvider;

        public ApiJobDownloadTicketService()
            : this(TimeProvider.System)
        {
        }

        internal ApiJobDownloadTicketService(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public ApiJobDownloadTicket Issue(string jobId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
            CleanupExpired();
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            DateTimeOffset expiresAt = _timeProvider.GetUtcNow().Add(Lifetime);
            _tickets[token] = new TicketState(jobId.Trim(), expiresAt);
            return new ApiJobDownloadTicket(
                token,
                $"/downloads/jobs/{Uri.EscapeDataString(token)}",
                expiresAt);
        }

        public bool TryResolve(string token, out string jobId)
        {
            jobId = string.Empty;
            if (string.IsNullOrWhiteSpace(token) ||
                !_tickets.TryGetValue(token.Trim(), out TicketState ticket))
            {
                return false;
            }
            if (ticket.ExpiresAtUtc < _timeProvider.GetUtcNow())
            {
                _tickets.TryRemove(token.Trim(), out _);
                return false;
            }
            jobId = ticket.JobId;
            return true;
        }

        private void CleanupExpired()
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (KeyValuePair<string, TicketState> pair in _tickets)
            {
                if (pair.Value.ExpiresAtUtc < now)
                {
                    _tickets.TryRemove(pair.Key, out _);
                }
            }
        }

        private sealed record TicketState(string JobId, DateTimeOffset ExpiresAtUtc);
    }
}
