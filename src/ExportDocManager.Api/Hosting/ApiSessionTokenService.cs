using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Hosting
{
    public interface IApiSessionTokenService
    {
        Task<ApiSessionToken> IssueAsync(User user, TimeSpan? lifetime = null, CancellationToken cancellationToken = default);

        Task<User> ValidateAsync(string token, CancellationToken cancellationToken = default);

        Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default);

        Task<int> RevokeUserSessionsAsync(int userId, CancellationToken cancellationToken = default);
    }

    public sealed record ApiSessionToken(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        User User);

    public sealed class InMemoryApiSessionTokenService : IApiSessionTokenService
    {
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);
        private readonly ConcurrentDictionary<string, ApiSessionToken> _tokens = new(StringComparer.Ordinal);

        public ApiSessionToken Issue(User user, TimeSpan? lifetime = null)
        {
            ArgumentNullException.ThrowIfNull(user);

            string token = CreateToken();
            var issued = new ApiSessionToken(
                token,
                DateTimeOffset.UtcNow.Add(lifetime ?? DefaultLifetime),
                ApiUserDtoFactory.ToUserSnapshot(user));

            _tokens[token] = issued;
            return issued;
        }

        public Task<ApiSessionToken> IssueAsync(
            User user,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue(user, lifetime));

        public User Validate(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            if (!_tokens.TryGetValue(token.Trim(), out var issued))
            {
                return null;
            }

            if (issued.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _tokens.TryRemove(token.Trim(), out _);
                return null;
            }

            return ApiUserDtoFactory.ToUserSnapshot(issued.User);
        }

        public Task<User> ValidateAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(token));

        public bool Revoke(string token)
        {
            return !string.IsNullOrWhiteSpace(token) &&
                _tokens.TryRemove(token.Trim(), out _);
        }

        public Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(Revoke(token));

        public int RevokeUserSessions(int userId)
        {
            if (userId <= 0)
            {
                return 0;
            }

            int revoked = 0;
            foreach (var pair in _tokens.ToArray())
            {
                if (pair.Value.User?.Id == userId && _tokens.TryRemove(pair.Key, out _))
                {
                    revoked++;
                }
            }

            return revoked;
        }

        public Task<int> RevokeUserSessionsAsync(
            int userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RevokeUserSessions(userId));

        private static string CreateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }

    public sealed class DatabaseApiSessionTokenService : IApiSessionTokenService
    {
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);
        private static readonly TimeSpan LastAccessWriteInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan SessionHistoryRetention = TimeSpan.FromDays(7);
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private long _nextCleanupUtcTicks;

        public DatabaseApiSessionTokenService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public ApiSessionToken Issue(User user, TimeSpan? lifetime = null)
            => IssueAsync(user, lifetime).GetAwaiter().GetResult();

        public async Task<ApiSessionToken> IssueAsync(
            User user,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(user);

            string token = CreateToken();
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.Add(lifetime ?? DefaultLifetime);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await TryCleanupExpiredSessionsAsync(context, now, cancellationToken).ConfigureAwait(false);
            await context.ApiUserSessions.AddAsync(new ApiUserSession
            {
                UserId = user.Id,
                TokenHash = HashToken(token),
                CreatedAt = now,
                ExpiresAt = expiresAt,
                LastAccessAt = now
            }, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return new ApiSessionToken(token, expiresAt, ApiUserDtoFactory.ToUserSnapshot(user));
        }

        public User Validate(string token)
            => ValidateAsync(token).GetAwaiter().GetResult();

        public async Task<User> ValidateAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string tokenHash = HashToken(token.Trim());
            var now = DateTimeOffset.UtcNow;
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await TryCleanupExpiredSessionsAsync(context, now, cancellationToken).ConfigureAwait(false);
            var session = await context.ApiUserSessions
                .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
            if (session == null || session.RevokedAt.HasValue || session.ExpiresAt <= now)
            {
                return null;
            }

            var user = await context.Users
                .Include(item => item.PermissionTemplate)
                .ThenInclude(template => template.Modules)
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
            if (user == null)
            {
                session.RevokedAt = now;
                await context.SaveChangesAsync(cancellationToken);
                return null;
            }

            UserPermissionAccessResolver.PopulateEffectiveModuleAccess(user);

            if (now - session.LastAccessAt >= LastAccessWriteInterval)
            {
                session.LastAccessAt = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            return ApiUserDtoFactory.ToUserSnapshot(user);
        }

        public bool Revoke(string token)
            => RevokeAsync(token).GetAwaiter().GetResult();

        public async Task<bool> RevokeAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string tokenHash = HashToken(token.Trim());
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await context.ApiUserSessions
                .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
            if (session == null || session.RevokedAt.HasValue)
            {
                return false;
            }

            session.RevokedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public int RevokeUserSessions(int userId)
            => RevokeUserSessionsAsync(userId).GetAwaiter().GetResult();

        public async Task<int> RevokeUserSessionsAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            if (userId <= 0)
            {
                return 0;
            }

            var now = DateTimeOffset.UtcNow;
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var activeSessions = context.ApiUserSessions
                .Where(item => item.UserId == userId && !item.RevokedAt.HasValue);
            var sessions = context.Database.IsSqlite()
                ? activeSessions.AsEnumerable().Where(item => item.ExpiresAt > now).ToList()
                : await activeSessions.Where(item => item.ExpiresAt > now).ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAt = now;
            }

            if (sessions.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }

            return sessions.Count;
        }

        private async Task TryCleanupExpiredSessionsAsync(
            AppDbContext context,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            long nextCleanup = Volatile.Read(ref _nextCleanupUtcTicks);
            if (now.UtcTicks < nextCleanup)
            {
                return;
            }

            long next = now.Add(CleanupInterval).UtcTicks;
            if (Interlocked.CompareExchange(ref _nextCleanupUtcTicks, next, nextCleanup) != nextCleanup)
            {
                return;
            }

            var cutoff = now.Subtract(SessionHistoryRetention);
            var obsoleteSessions = context.Database.IsSqlite()
                ? context.ApiUserSessions
                    .AsEnumerable()
                    .Where(item => item.ExpiresAt <= cutoff ||
                        (item.RevokedAt.HasValue && item.RevokedAt.Value <= cutoff))
                    .ToList()
                : await context.ApiUserSessions
                    .Where(item => item.ExpiresAt <= cutoff ||
                        (item.RevokedAt.HasValue && item.RevokedAt.Value <= cutoff))
                    .ToListAsync(cancellationToken);
            if (obsoleteSessions.Count == 0)
            {
                return;
            }

            context.ApiUserSessions.RemoveRange(obsoleteSessions);
            await context.SaveChangesAsync(cancellationToken);
        }

        private static string CreateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
