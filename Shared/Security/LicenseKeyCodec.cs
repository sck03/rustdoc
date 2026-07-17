using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExportDocManager.Shared.Security
{
    public static class LicenseDefaults
    {
        public const string RuntimeIntegrityKey = "ExportDocManagerRuntimeIntegrity2026";
        public const string SignaturePublicKey =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHfZ6xFQTzZbClzRPjqoF9VHiIjN8eDyXQuDZ2gG6oT0yF8qNZ0MzGA1n4m7Kl1Sd6DOuf32TMyGLxbqoNGcJAg==";
    }

    public static class LicenseValueNormalizer
    {
        public static string NormalizeMachineId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (char.IsWhiteSpace(ch) || ch == '-')
                {
                    continue;
                }

                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }

        public static string NormalizeLicenseKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    builder.Append(ch);
                }
            }

            string compact = builder.ToString();
            return compact.StartsWith(LicenseKeyCodec.SignedLicensePrefix, StringComparison.OrdinalIgnoreCase)
                ? LicenseKeyCodec.SignedLicensePrefix + compact[LicenseKeyCodec.SignedLicensePrefix.Length..]
                : compact;
        }
    }

    public static class LicenseKeyCodec
    {
        public const string SignedLicensePrefix = "EDM2-";

        private const long SignedLifetimeTimestamp = -1;
        private static readonly JsonSerializerOptions SignedPayloadJsonOptions = new()
        {
            PropertyNameCaseInsensitive = false
        };

        public static bool TryValidateSigned(
            string machineId,
            string licenseKey,
            string publicKeyBase64,
            out DateTime expireDate)
        {
            expireDate = DateTime.MinValue;
            machineId = LicenseValueNormalizer.NormalizeMachineId(machineId);
            licenseKey = LicenseValueNormalizer.NormalizeLicenseKey(licenseKey);
            if (string.IsNullOrEmpty(machineId) ||
                string.IsNullOrWhiteSpace(licenseKey) ||
                string.IsNullOrWhiteSpace(publicKeyBase64) ||
                !licenseKey.StartsWith(SignedLicensePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = licenseKey[SignedLicensePrefix.Length..].Split('.', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                return false;
            }

            try
            {
                byte[] payloadBytes = Base64UrlDecode(parts[0]);
                byte[] signature = Base64UrlDecode(parts[1]);

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
                if (!ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
                {
                    return false;
                }

                var payload = JsonSerializer.Deserialize<SignedLicensePayload>(payloadBytes, SignedPayloadJsonOptions);
                if (payload == null || payload.Version != 2)
                {
                    return false;
                }

                if (!string.Equals(
                        LicenseValueNormalizer.NormalizeMachineId(payload.MachineId),
                        machineId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                expireDate = DecodeSignedExpireDate(payload.ExpireAtUnixSeconds);
                return true;
            }
            catch (CryptographicException)
            {
                expireDate = DateTime.MinValue;
                return false;
            }
            catch (FormatException)
            {
                expireDate = DateTime.MinValue;
                return false;
            }
            catch (JsonException)
            {
                expireDate = DateTime.MinValue;
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                expireDate = DateTime.MinValue;
                return false;
            }
        }

        private static DateTime DecodeSignedExpireDate(long timestamp)
        {
            return timestamp == SignedLifetimeTimestamp
                ? DateTime.MaxValue
                : DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value
                .Replace('-', '+')
                .Replace('_', '/');
            int padding = padded.Length % 4;
            if (padding > 0)
            {
                padded = padded.PadRight(padded.Length + 4 - padding, '=');
            }

            return Convert.FromBase64String(padded);
        }

        private sealed class SignedLicensePayload
        {
            [JsonPropertyName("v")]
            public int Version { get; set; }

            [JsonPropertyName("mid")]
            public string MachineId { get; set; } = string.Empty;

            [JsonPropertyName("exp")]
            public long ExpireAtUnixSeconds { get; set; }
        }
    }
}
