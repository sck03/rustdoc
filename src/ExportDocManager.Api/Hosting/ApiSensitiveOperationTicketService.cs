using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiSensitiveOperationAction
    {
        public const string RestoreDatabase = "restore-database";
        public const string RestoreServer = "restore-server";

        public static bool IsKnown(string action) => action is RestoreDatabase or RestoreServer;
    }

    public sealed record ApiSensitiveOperationTicket(string Token, DateTimeOffset ExpiresAtUtc);

    public sealed class ApiSensitiveOperationTicketService
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, TicketState> _tickets =
            new(StringComparer.Ordinal);
        private readonly TimeProvider _timeProvider;
        private int _operationsSinceCleanup;

        public ApiSensitiveOperationTicketService()
            : this(TimeProvider.System)
        {
        }

        internal ApiSensitiveOperationTicketService(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public ApiSensitiveOperationTicket Issue(int userId, string action)
        {
            if (userId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(userId));
            }
            if (!ApiSensitiveOperationAction.IsKnown(action))
            {
                throw new ArgumentException("敏感操作类型无效。", nameof(action));
            }

            CleanupExpired();
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            DateTimeOffset expiresAt = _timeProvider.GetUtcNow().Add(Lifetime);
            _tickets[token] = new TicketState(userId, action, expiresAt);
            return new ApiSensitiveOperationTicket(token, expiresAt);
        }

        public bool Consume(string token, int userId, string action)
        {
            if (string.IsNullOrWhiteSpace(token) ||
                userId <= 0 ||
                !ApiSensitiveOperationAction.IsKnown(action))
            {
                return false;
            }
            CleanupExpired();
            if (!_tickets.TryRemove(token.Trim(), out TicketState? ticket))
            {
                return false;
            }
            return ticket.ExpiresAtUtc >= _timeProvider.GetUtcNow() &&
                ticket.UserId == userId &&
                string.Equals(ticket.Action, action, StringComparison.Ordinal);
        }

        private void CleanupExpired()
        {
            if (Interlocked.Increment(ref _operationsSinceCleanup) % 32 != 0)
            {
                return;
            }
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (KeyValuePair<string, TicketState> pair in _tickets)
            {
                if (pair.Value.ExpiresAtUtc < now)
                {
                    _tickets.TryRemove(pair.Key, out _);
                }
            }
        }

        private sealed record TicketState(
            int UserId,
            string Action,
            DateTimeOffset ExpiresAtUtc);
    }
}
