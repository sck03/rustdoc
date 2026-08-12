using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowStationAssignmentCode
    {
        private const string Prefix = "SWAC1.";
        private const int SecretSize = 32;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string CreateProtectedSecret(LocalSecretProtector secretProtector)
        {
            ArgumentNullException.ThrowIfNull(secretProtector);
            byte[] secret = RandomNumberGenerator.GetBytes(SecretSize);
            try
            {
                return secretProtector.Protect(Convert.ToBase64String(secret));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        public static string Encode(SwClientProfile profile, LocalSecretProtector secretProtector)
        {
            ArgumentNullException.ThrowIfNull(profile);
            string secret = UnprotectProfileSecret(profile, secretProtector);
            var payload = new AssignmentCodePayload
            {
                Version = 1,
                StationKey = profile.StationKey?.Trim() ?? string.Empty,
                ProfileKey = profile.ProfileKey?.Trim() ?? string.Empty,
                ProfileName = profile.ProfileName?.Trim() ?? string.Empty,
                CompanyScope = profile.CompanyScope?.Trim() ?? string.Empty,
                CardIdentifier = profile.CardIdentifier?.Trim() ?? string.Empty,
                CanSubmitCustomsCoo = profile.CanSubmitCustomsCoo,
                CanSubmitAgentConsignment = profile.CanSubmitAgentConsignment,
                AuthenticationSecret = secret
            };
            ValidatePayload(payload);
            return Prefix + Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        }

        public static SingleWindowStationAssignment Decode(string assignmentCode)
        {
            string normalized = assignmentCode?.Trim() ?? string.Empty;
            if (!normalized.StartsWith(Prefix, StringComparison.Ordinal) || normalized.Length > 4096)
            {
                throw new InvalidDataException("持卡机授权码格式无效，请从目标操作档案重新复制。" );
            }

            AssignmentCodePayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<AssignmentCodePayload>(
                    Base64UrlDecode(normalized[Prefix.Length..]),
                    JsonOptions)
                    ?? throw new InvalidDataException("持卡机授权码内容为空，请从目标操作档案重新复制。");
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                throw new InvalidDataException("持卡机授权码无法解析，请从目标操作档案重新复制。", ex);
            }

            ValidatePayload(payload);
            return new SingleWindowStationAssignment
            {
                StationKey = payload.StationKey.Trim(),
                ProfileKey = payload.ProfileKey.Trim(),
                ProfileName = payload.ProfileName.Trim(),
                CompanyScope = payload.CompanyScope.Trim(),
                CardIdentifier = payload.CardIdentifier.Trim(),
                CanSubmitCustomsCoo = payload.CanSubmitCustomsCoo,
                CanSubmitAgentConsignment = payload.CanSubmitAgentConsignment,
                AuthenticationSecret = payload.AuthenticationSecret.Trim()
            };
        }

        public static string UnprotectProfileSecret(
            SwClientProfile profile,
            LocalSecretProtector secretProtector)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(secretProtector);
            if (string.IsNullOrWhiteSpace(profile.ProtectedHandoffSecret))
            {
                throw new InvalidDataException("操作档案缺少交接认证密钥，请重新保存该档案。" );
            }

            string secret = secretProtector.Unprotect(profile.ProtectedHandoffSecret) ?? string.Empty;
            EnsureValidSecret(secret);
            return secret;
        }

        public static void EnsureValidSecret(string secret)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(secret?.Trim() ?? string.Empty);
                try
                {
                    if (bytes.Length != SecretSize)
                    {
                        throw new InvalidDataException("单一窗口交接认证密钥长度无效。" );
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("单一窗口交接认证密钥格式无效。", ex);
            }
        }

        private static void ValidatePayload(AssignmentCodePayload payload)
        {
            if (payload == null ||
                payload.Version != 1 ||
                !IsStationKey(payload.StationKey) ||
                !IsProfileKey(payload.ProfileKey) ||
                string.IsNullOrWhiteSpace(payload.ProfileName) ||
                payload.ProfileName.Length > 80 ||
                string.IsNullOrWhiteSpace(payload.CompanyScope) ||
                payload.CompanyScope.Length > 120 ||
                string.IsNullOrWhiteSpace(payload.CardIdentifier) ||
                payload.CardIdentifier.Length > 120 ||
                (!payload.CanSubmitCustomsCoo && !payload.CanSubmitAgentConsignment))
            {
                throw new InvalidDataException("持卡机授权码缺少有效的机器、操作卡或公司绑定信息。" );
            }

            EnsureValidSecret(payload.AuthenticationSecret);
        }

        private static bool IsStationKey(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 36 &&
                   normalized.StartsWith("SWS-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(normalized[4..], "N", out _);
        }

        private static bool IsProfileKey(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 36 &&
                   normalized.StartsWith("SWP-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(normalized[4..], "N", out _);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string normalized = (value ?? string.Empty)
                .Replace('-', '+')
                .Replace('_', '/');
            normalized = (normalized.Length % 4) switch
            {
                2 => normalized + "==",
                3 => normalized + "=",
                0 => normalized,
                _ => throw new FormatException("Invalid base64url length.")
            };
            return Convert.FromBase64String(normalized);
        }

        private sealed class AssignmentCodePayload
        {
            public int Version { get; set; }

            public string StationKey { get; set; } = string.Empty;

            public string ProfileKey { get; set; } = string.Empty;

            public string ProfileName { get; set; } = string.Empty;

            public string CompanyScope { get; set; } = string.Empty;

            public string CardIdentifier { get; set; } = string.Empty;

            public bool CanSubmitCustomsCoo { get; set; }

            public bool CanSubmitAgentConsignment { get; set; }

            public string AuthenticationSecret { get; set; } = string.Empty;
        }
    }
}
