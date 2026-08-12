using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiDownloadTicket(
        string Token,
        string DownloadUrl,
        DateTimeOffset ExpiresAtUtc);

    public sealed class ApiDownloadTicketService
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
        private const int MaximumTicketCount = 4096;
        private const string DownloadSessionCookieName = "ExportDocManager.DownloadSession";
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
            HttpContext context,
            string purpose,
            string resourceId,
            string subject,
            string downloadRoutePrefix,
            bool requireSessionBinding = true)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);
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
                string sessionBinding = requireSessionBinding
                    ? GetOrCreateDownloadSession(context)
                    : string.Empty;
                _tickets[token] = new TicketState(
                    purpose.Trim(),
                    resourceId.Trim(),
                    subject.Trim(),
                    requireSessionBinding
                        ? SHA256.HashData(Encoding.UTF8.GetBytes(sessionBinding))
                        : [],
                    requireSessionBinding,
                    expiresAt);
                return new ApiDownloadTicket(
                    token,
                    $"{normalizedRoute}/{Uri.EscapeDataString(token)}",
                    expiresAt);
            }
        }

        public bool TryResolve(
            HttpContext context,
            string token,
            string purpose,
            out string resourceId)
        {
            ArgumentNullException.ThrowIfNull(context);
            resourceId = string.Empty;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(purpose))
            {
                return false;
            }

            string normalizedToken = token.Trim();
            if (!_tickets.TryGetValue(normalizedToken, out TicketState? ticket))
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
            if (!ticket.RequiresSessionBinding)
            {
                resourceId = ticket.ResourceId;
                return true;
            }

            if (!context.Request.Cookies.TryGetValue(DownloadSessionCookieName, out string? sessionBinding) ||
                !IsValidSessionBinding(sessionBinding))
            {
                return false;
            }

            byte[] presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionBinding));
            if (!CryptographicOperations.FixedTimeEquals(presentedHash, ticket.SessionBindingHash))
            {
                return false;
            }

            resourceId = ticket.ResourceId;
            return true;
        }

        public void ResetSession(HttpContext context, bool revokeUnboundDesktopTickets = false)
        {
            ArgumentNullException.ThrowIfNull(context);
            string sessionBinding = context.Request.Cookies.TryGetValue(
                DownloadSessionCookieName,
                out string? existing)
                ? existing
                : string.Empty;
            byte[] sessionHash = IsValidSessionBinding(sessionBinding)
                ? SHA256.HashData(Encoding.UTF8.GetBytes(sessionBinding))
                : [];

            foreach (KeyValuePair<string, TicketState> item in _tickets)
            {
                bool sameSession = sessionHash.Length > 0 &&
                    item.Value.RequiresSessionBinding &&
                    CryptographicOperations.FixedTimeEquals(sessionHash, item.Value.SessionBindingHash);
                if (sameSession || (revokeUnboundDesktopTickets && !item.Value.RequiresSessionBinding))
                {
                    _tickets.TryRemove(item.Key, out _);
                }
            }

            context.Response.Cookies.Delete(
                DownloadSessionCookieName,
                new CookieOptions
                {
                    Path = "/",
                    SameSite = SameSiteMode.Strict,
                    Secure = context.Request.IsHttps,
                });
        }

        public void RevokeSubject(HttpContext context, string subject)
        {
            ArgumentNullException.ThrowIfNull(context);
            string normalizedSubject = subject?.Trim() ?? string.Empty;
            if (normalizedSubject.Length > 0)
            {
                foreach (KeyValuePair<string, TicketState> item in _tickets)
                {
                    if (string.Equals(item.Value.Subject, normalizedSubject, StringComparison.Ordinal))
                    {
                        _tickets.TryRemove(item.Key, out _);
                    }
                }
            }

            ResetSession(context);
        }

        private string GetOrCreateDownloadSession(HttpContext context)
        {
            if (context.Request.Cookies.TryGetValue(DownloadSessionCookieName, out string? existing) &&
                IsValidSessionBinding(existing))
            {
                return existing;
            }

            string binding = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            context.Response.Cookies.Append(
                DownloadSessionCookieName,
                binding,
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    MaxAge = SessionLifetime,
                    Path = "/",
                    SameSite = SameSiteMode.Strict,
                    Secure = context.Request.IsHttps
                });
            return binding;
        }

        private static bool IsValidSessionBinding(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Length == 43 &&
            value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

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
            string Subject,
            byte[] SessionBindingHash,
            bool RequiresSessionBinding,
            DateTimeOffset ExpiresAtUtc);
    }
}
