using System.Collections.Concurrent;
using System.Security.Cryptography;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public interface IApiSessionTokenService
    {
        ApiSessionToken Issue(User user, TimeSpan? lifetime = null);

        User Validate(string token);

        bool Revoke(string token);
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

        public bool Revoke(string token)
        {
            return !string.IsNullOrWhiteSpace(token) &&
                _tokens.TryRemove(token.Trim(), out _);
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
    }
}
