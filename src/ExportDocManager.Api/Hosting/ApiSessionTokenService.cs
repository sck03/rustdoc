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

        Task<User?> ValidateAsync(string token, CancellationToken cancellationToken = default);

        Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default);

        Task<int> RevokeUserSessionsAsync(int userId, CancellationToken cancellationToken = default);
    }

    public sealed record ApiSessionToken(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        User User);

    public sealed class InMemoryApiSessionTokenService : IApiSessionTokenService, IDisposable
    {
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);
        private const int MaximumTokenCount = 10_000;
        private readonly ConcurrentDictionary<string, ApiSessionToken> _tokens = new(StringComparer.Ordinal);
        private readonly Timer _cleanupTimer;

        public InMemoryApiSessionTokenService()
        {
            _cleanupTimer = new Timer(
                static state => ((InMemoryApiSessionTokenService?)state)?.CleanupExpiredTokens(),
                this,
                CleanupInterval,
                CleanupInterval);
        }

        public ApiSessionToken Issue(User user, TimeSpan? lifetime = null)
        {
            ArgumentNullException.ThrowIfNull(user);

            string token = CreateToken();
            var issued = new ApiSessionToken(
                token,
                DateTimeOffset.UtcNow.Add(lifetime ?? DefaultLifetime),
                ApiUserDtoFactory.ToUserSnapshot(user));

            _tokens[token] = issued;
            CleanupExpiredTokens();
            return issued;
        }

        public Task<ApiSessionToken> IssueAsync(
            User user,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue(user, lifetime));

        public User? Validate(string token)
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

        public Task<User?> ValidateAsync(string token, CancellationToken cancellationToken = default) =>
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

        private void CleanupExpiredTokens()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (var pair in _tokens.ToArray())
            {
                if (pair.Value.ExpiresAt <= now)
                {
                    _tokens.TryRemove(pair.Key, out _);
                }
            }

            if (_tokens.Count <= MaximumTokenCount)
            {
                return;
            }

            foreach (var pair in _tokens
                .OrderBy(item => item.Value.ExpiresAt)
                .Take(Math.Max(0, _tokens.Count - MaximumTokenCount)))
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }

        public void Dispose()
        {
            _cleanupTimer.Dispose();
        }
    }

    public sealed class DatabaseApiSessionTokenService : IApiSessionTokenService
    {
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);
        private static readonly TimeSpan ValidationCacheLifetime = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LastAccessWriteInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan CleanupContinuationInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan SessionHistoryRetention = TimeSpan.FromDays(7);
        private const int MaximumValidationCacheEntries = 10_000;
        private const int SessionMutationBatchSize = 500;
        private const int MaximumCleanupScanCount = 10_000;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ConcurrentDictionary<string, CachedSessionValidation> _validationCache = new(StringComparer.Ordinal);
        private long _nextCleanupUtcTicks;
        private long _cleanupScanCursor;

        public DatabaseApiSessionTokenService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

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

        public async Task<User?> ValidateAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string tokenHash = HashToken(token.Trim());
            var now = DateTimeOffset.UtcNow;
            if (TryGetCachedValidation(tokenHash, now, out User? cachedUser))
            {
                return cachedUser;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await TryCleanupExpiredSessionsAsync(context, now, cancellationToken).ConfigureAwait(false);
            var session = await context.ApiUserSessions
                .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
            if (session == null || session.RevokedAt.HasValue || session.ExpiresAt <= now)
            {
                _validationCache.TryRemove(tokenHash, out _);
                return null;
            }

            var user = await context.Users
                .Include(item => item.PermissionTemplate)
                .ThenInclude(template => template!.Modules)
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
            if (user == null)
            {
                session.RevokedAt = now;
                await context.SaveChangesAsync(cancellationToken);
                _validationCache.TryRemove(tokenHash, out _);
                return null;
            }

            UserPermissionAccessResolver.PopulateEffectiveModuleAccess(user);

            if (now - session.LastAccessAt >= LastAccessWriteInterval)
            {
                session.LastAccessAt = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            CacheValidation(tokenHash, user, session.ExpiresAt, now);
            return ApiUserDtoFactory.ToUserSnapshot(user);
        }

        public async Task<bool> RevokeAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string tokenHash = HashToken(token.Trim());
            _validationCache.TryRemove(tokenHash, out _);
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
            int revokedCount = 0;
            long lastSeenId = 0;
            while (true)
            {
                var candidates = await activeSessions
                    .AsNoTracking()
                    .Where(item => item.Id > lastSeenId)
                    .OrderBy(item => item.Id)
                    .Select(item => new { item.Id, item.ExpiresAt })
                    .Take(SessionMutationBatchSize)
                    .ToListAsync(cancellationToken);
                if (candidates.Count == 0)
                {
                    break;
                }

                lastSeenId = candidates[^1].Id;
                long[] sessionIds = candidates
                    .Where(item => item.ExpiresAt > now)
                    .Select(item => item.Id)
                    .ToArray();
                if (sessionIds.Length > 0)
                {
                    revokedCount += await context.ApiUserSessions
                        .Where(item => sessionIds.Contains(item.Id) && !item.RevokedAt.HasValue)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(item => item.RevokedAt, (DateTimeOffset?)now),
                            cancellationToken);
                }

                if (candidates.Count < SessionMutationBatchSize)
                {
                    break;
                }
            }

            foreach (var pair in _validationCache.ToArray())
            {
                if (pair.Value.User.Id == userId)
                {
                    _validationCache.TryRemove(pair.Key, out _);
                }
            }

            return revokedCount;
        }

        private bool TryGetCachedValidation(
            string tokenHash,
            DateTimeOffset now,
            out User? user)
        {
            user = null;
            if (!_validationCache.TryGetValue(tokenHash, out var cached))
            {
                return false;
            }

            if (cached.CacheExpiresAt <= now || cached.SessionExpiresAt <= now)
            {
                _validationCache.TryRemove(tokenHash, out _);
                return false;
            }

            user = ApiUserDtoFactory.ToUserSnapshot(cached.User);
            return true;
        }

        private void CacheValidation(
            string tokenHash,
            User user,
            DateTimeOffset sessionExpiresAt,
            DateTimeOffset now)
        {
            var cacheExpiresAt = now.Add(ValidationCacheLifetime);
            if (cacheExpiresAt > sessionExpiresAt)
            {
                cacheExpiresAt = sessionExpiresAt;
            }

            _validationCache[tokenHash] = new CachedSessionValidation(
                ApiUserDtoFactory.ToUserSnapshot(user),
                sessionExpiresAt,
                cacheExpiresAt);
            TrimValidationCache(now);
        }

        private void TrimValidationCache(DateTimeOffset now)
        {
            if (_validationCache.Count <= MaximumValidationCacheEntries)
            {
                return;
            }

            foreach (var pair in _validationCache.ToArray())
            {
                if (pair.Value.CacheExpiresAt <= now || pair.Value.SessionExpiresAt <= now)
                {
                    _validationCache.TryRemove(pair.Key, out _);
                }
            }

            int overflow = _validationCache.Count - MaximumValidationCacheEntries;
            if (overflow <= 0)
            {
                return;
            }

            foreach (var pair in _validationCache
                .OrderBy(item => item.Value.CacheExpiresAt)
                .Take(overflow))
            {
                _validationCache.TryRemove(pair.Key, out _);
            }
        }

        private sealed record CachedSessionValidation(
            User User,
            DateTimeOffset SessionExpiresAt,
            DateTimeOffset CacheExpiresAt);

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
            long lastSeenId = Volatile.Read(ref _cleanupScanCursor);
            int scannedCount = 0;
            bool hasMoreRows = false;
            while (scannedCount < MaximumCleanupScanCount)
            {
                int takeCount = Math.Min(SessionMutationBatchSize, MaximumCleanupScanCount - scannedCount);
                var candidates = await context.ApiUserSessions
                    .AsNoTracking()
                    .Where(item => item.Id > lastSeenId)
                    .OrderBy(item => item.Id)
                    .Select(item => new { item.Id, item.ExpiresAt, item.RevokedAt })
                    .Take(takeCount)
                    .ToListAsync(cancellationToken);
                if (candidates.Count == 0)
                {
                    hasMoreRows = false;
                    break;
                }

                scannedCount += candidates.Count;
                lastSeenId = candidates[^1].Id;
                long[] obsoleteSessionIds = candidates
                    .Where(item => item.ExpiresAt <= cutoff ||
                        (item.RevokedAt.HasValue && item.RevokedAt.Value <= cutoff))
                    .Select(item => item.Id)
                    .ToArray();
                if (obsoleteSessionIds.Length > 0)
                {
                    await context.ApiUserSessions
                        .Where(item => obsoleteSessionIds.Contains(item.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                }

                hasMoreRows = candidates.Count == takeCount;
                if (!hasMoreRows)
                {
                    break;
                }
            }

            if (hasMoreRows && scannedCount >= MaximumCleanupScanCount)
            {
                Volatile.Write(ref _cleanupScanCursor, lastSeenId);
                Volatile.Write(
                    ref _nextCleanupUtcTicks,
                    now.Add(CleanupContinuationInterval).UtcTicks);
            }
            else
            {
                Volatile.Write(ref _cleanupScanCursor, 0);
            }
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
