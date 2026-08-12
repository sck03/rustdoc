using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.Shared.Security;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Security
{
    public sealed partial class RuntimeLicenseService
    {
        private async Task<RuntimeLicenseData?> LoadLicenseDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                string path = GetLicensePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                string encrypted = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                string? json = _secretProtector.Unprotect(encrypted);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var data = JsonSerializer.Deserialize<RuntimeLicenseData>(json);
                return NormalizeLoadedData(data);
            }
            catch
            {
                return null;
            }
        }

        private async Task SaveLicenseDataAsync(
            RuntimeLicenseData data,
            CancellationToken cancellationToken)
        {
            if (data == null)
            {
                return;
            }

            data.Signature = ComputeDataSignature(data);
            string json = JsonSerializer.Serialize(data);
            string encrypted = _secretProtector.Protect(json);
            await AtomicFileHelper.WriteAllTextAtomicAsync(GetLicensePath(), encrypted, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }

        private static RuntimeLicenseData? NormalizeLoadedData(RuntimeLicenseData? data)
        {
            if (data == null)
            {
                return null;
            }

            return !string.IsNullOrEmpty(data.Signature) && ValidateDataSignature(data) ? data : null;
        }

        private static string ComputeDataSignature(RuntimeLicenseData data)
        {
            var payload = string.Join("|",
                data.InstallDate.ToString("O", CultureInfo.InvariantCulture),
                data.LastRunDate.ToString("O", CultureInfo.InvariantCulture),
                data.IsRegistered ? "1" : "0",
                data.LicenseKey ?? string.Empty,
                data.ExpireDate.ToString("O", CultureInfo.InvariantCulture),
                data.MachineId ?? string.Empty,
                data.DeviceBindingVersion.ToString(CultureInfo.InvariantCulture),
                data.DeviceFingerprintHash ?? string.Empty,
                data.LocalBindingSecretHash ?? string.Empty);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LicenseDefaults.RuntimeIntegrityKey));
            byte[] bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static bool ValidateDataSignature(RuntimeLicenseData data)
        {
            string expected = ComputeDataSignature(data);
            return string.Equals(expected, data.Signature, StringComparison.OrdinalIgnoreCase);
        }

        private string GetLicensePath()
        {
            return Path.Combine(_pathProvider.SecurityRoot, LicenseFileName);
        }

        private string GetMachineSeedPath()
        {
            return Path.Combine(_pathProvider.SecurityRoot, MachineSeedFileName);
        }

        private string GetLocalBindingSecretPath()
        {
            return Path.Combine(_pathProvider.SecurityRoot, LocalBindingSecretFileName);
        }

        private LicenseStatus ToStatus(MutableLicenseStatus status)
        {
            return new LicenseStatus
            {
                IsRegistered = status.IsRegistered,
                IsTrialExpired = status.IsTrialExpired,
                TrialDays = TrialDays,
                DaysRemaining = status.DaysRemaining,
                MachineId = status.MachineId ?? string.Empty,
                Message = status.Message ?? string.Empty,
                ExpireDate = status.ExpireDate,
                LicenseStoragePath = GetLicensePath(),
                StoragePolicy = $"{StoragePolicy} 当前试用锚点: {_anchorStore.StorageDescription}"
            };
        }

        private void CacheStatus(LicenseStatus status, long nowUtcTicks)
        {
            if (status == null)
            {
                Volatile.Write(ref _cachedStatus, null);
                return;
            }

            Volatile.Write(
                ref _cachedStatus,
                new CachedLicenseStatus(status, nowUtcTicks + StatusCacheLifetime.Ticks));
        }

        private sealed record CachedLicenseStatus(
            LicenseStatus Status,
            long ExpiresAtUtcTicks);

        private sealed class MutableLicenseStatus
        {
            public bool IsRegistered { get; set; }
            public bool IsTrialExpired { get; set; }
            public int DaysRemaining { get; set; }
            public string MachineId { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTime ExpireDate { get; set; }
        }

        private sealed record RuntimeLicenseIdentity(
            string MachineId,
            string DeviceFingerprintHash,
            string LocalBindingSecretHash);

        private sealed class RuntimeLicenseData
        {
            public DateTime InstallDate { get; set; }
            public DateTime LastRunDate { get; set; }
            public bool IsRegistered { get; set; }
            public string LicenseKey { get; set; } = string.Empty;
            public DateTime ExpireDate { get; set; }
            public string MachineId { get; set; } = string.Empty;
            public int DeviceBindingVersion { get; set; }
            public string DeviceFingerprintHash { get; set; } = string.Empty;
            public string LocalBindingSecretHash { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
        }

    }
}
