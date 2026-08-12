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
        private async Task<RuntimeLicenseIdentity> GetLicenseIdentityAsync(
            RuntimeLicenseAnchorData anchor,
            CancellationToken cancellationToken)
        {
            string seed = anchor.MachineSeed;
            await MirrorMachineSeedAsync(seed, cancellationToken).ConfigureAwait(false);

            string deviceFingerprint = _deviceFingerprintProvider() ?? string.Empty;
            string deviceFingerprintHash = SecurityHelper.ComputeHash($"device-v{DeviceBindingVersion}|{deviceFingerprint}");
            string localBindingSecret = _localBindingSecretProvider != null
                ? _localBindingSecretProvider() ?? string.Empty
                : anchor.LocalBindingSecret ?? string.Empty;

            if (_localBindingSecretProvider == null)
            {
                await MirrorLocalBindingSecretFileAsync(localBindingSecret, cancellationToken).ConfigureAwait(false);
            }

            string localBindingSecretHash = SecurityHelper.ComputeHash($"local-binding-v{DeviceBindingVersion}|{localBindingSecret}");
            string machineId = SecurityHelper.ComputeHash($"license-v{DeviceBindingVersion}|{seed}|{deviceFingerprintHash}|{localBindingSecretHash}");
            return new RuntimeLicenseIdentity(machineId, deviceFingerprintHash, localBindingSecretHash);
        }

        private async Task MirrorMachineSeedAsync(
            string seed,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                return;
            }

            try
            {
                string path = GetMachineSeedPath();
                if (await FileTextEqualsAsync(path, seed, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await AtomicFileHelper.WriteAllTextAtomicAsync(path, seed, Encoding.UTF8, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task<RuntimeLicenseAnchorData> ReadOrCreateMachineAnchorAsync(
            DateTime now,
            CancellationToken cancellationToken)
        {
            RuntimeLicenseAnchorData? anchor = await LoadMachineAnchorAsync(cancellationToken)
                .ConfigureAwait(false);

            bool changed = false;
            if (anchor == null)
            {
                anchor = new RuntimeLicenseAnchorData
                {
                    SchemaVersion = 1,
                    MachineSeed = Guid.NewGuid().ToString("N"),
                    LocalBindingSecret = CreateNewLocalBindingSecretValue(),
                    InstallDate = now,
                    LastRunDate = now
                };
                changed = true;
            }

            if (anchor.SchemaVersion <= 0)
            {
                anchor.SchemaVersion = 1;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(anchor.MachineSeed))
            {
                anchor.MachineSeed = Guid.NewGuid().ToString("N");
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(anchor.LocalBindingSecret))
            {
                anchor.LocalBindingSecret = CreateNewLocalBindingSecretValue();
                changed = true;
            }

            if (anchor.InstallDate == default)
            {
                anchor.InstallDate = now;
                changed = true;
            }

            if (anchor.LastRunDate == default)
            {
                anchor.LastRunDate = now;
                changed = true;
            }

            if (anchor.LastRunDate < anchor.InstallDate)
            {
                anchor.LastRunDate = anchor.InstallDate;
                changed = true;
            }

            string normalizedLicenseKey = LicenseValueNormalizer.NormalizeLicenseKey(anchor.LicenseKey);
            if (!string.Equals(anchor.LicenseKey, normalizedLicenseKey, StringComparison.Ordinal))
            {
                anchor.LicenseKey = normalizedLicenseKey;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(anchor.LicenseKey) && anchor.LicenseExpireDate != default)
            {
                anchor.LicenseExpireDate = default;
                changed = true;
            }

            await MirrorMachineSeedAsync(anchor.MachineSeed, cancellationToken).ConfigureAwait(false);
            await MirrorLocalBindingSecretFileAsync(anchor.LocalBindingSecret, cancellationToken)
                .ConfigureAwait(false);

            if (changed)
            {
                await SaveMachineAnchorAsync(anchor, cancellationToken).ConfigureAwait(false);
            }

            return anchor;
        }

        private async Task SaveMachineAnchorAsync(
            RuntimeLicenseAnchorData anchor,
            CancellationToken cancellationToken)
        {
            try
            {
                await _anchorStore.SaveAsync(anchor, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                var fallback = CreateFallbackMachineAnchorStore();
                await fallback.SaveAsync(anchor, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<RuntimeLicenseAnchorData?> LoadMachineAnchorAsync(CancellationToken cancellationToken)
        {
            try
            {
                var anchor = await _anchorStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (anchor != null)
                {
                    return anchor;
                }
            }
            catch
            {
            }

            try
            {
                return await CreateFallbackMachineAnchorStore().LoadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private FileRuntimeLicenseAnchorStore CreateFallbackMachineAnchorStore()
        {
            return new FileRuntimeLicenseAnchorStore(
                Path.Combine(_pathProvider.SecurityRoot, "machine-trial-anchor.dat"),
                "平台安全锚点不可用，回退到运行数据根 Security/machine-trial-anchor.dat。");
        }

        private async Task MirrorLocalBindingSecretFileAsync(
            string secret,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return;
            }

            try
            {
                if (OperatingSystem.IsWindows() &&
                    secret.StartsWith(WindowsLocalMachineBindingPrefix, StringComparison.Ordinal))
                {
                    string rawSecret = secret[WindowsLocalMachineBindingPrefix.Length..];
                    if (await WindowsLocalMachineSecretMatchesAsync(
                            GetLocalBindingSecretPath(),
                            rawSecret,
                            cancellationToken)
                        .ConfigureAwait(false))
                    {
                        return;
                    }
                    string protectedPayload = ProtectWindowsLocalMachineSecret(rawSecret);
                    await AtomicFileHelper.WriteAllTextAtomicAsync(
                            GetLocalBindingSecretPath(),
                            protectedPayload,
                            Encoding.UTF8,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        private static DateTime NormalizeAnchorDate(DateTime value, DateTime fallback)
        {
            return value == default ? fallback : value;
        }

        private static DateTime MaxDate(DateTime first, DateTime second)
        {
            return first >= second ? first : second;
        }

        private static bool ShouldAdvancePersistedLastRunDate(DateTime lastRunDate, DateTime now)
        {
            return lastRunDate == default || now.Date > lastRunDate.Date;
        }

        private static async Task<bool> FileTextEqualsAsync(
            string path,
            string expected,
            CancellationToken cancellationToken)
        {
            try
            {
                return File.Exists(path) &&
                    string.Equals(
                        await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                        expected,
                        StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static async Task<bool> WindowsLocalMachineSecretMatchesAsync(
            string path,
            string expectedSecret,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                string payload = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (!payload.StartsWith(WindowsLocalMachineBindingPrefix, StringComparison.Ordinal))
                {
                    return false;
                }

                byte[] protectedBytes = Convert.FromBase64String(payload[WindowsLocalMachineBindingPrefix.Length..]);
                byte[] bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
                return CryptographicOperations.FixedTimeEquals(
                    bytes,
                    Encoding.UTF8.GetBytes(expectedSecret));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
            {
                return false;
            }
        }

        private static bool SetAnchorRegistration(
            RuntimeLicenseAnchorData anchor,
            string licenseKey,
            DateTime expireDate)
        {
            string normalizedKey = LicenseValueNormalizer.NormalizeLicenseKey(licenseKey);
            if (string.Equals(anchor.LicenseKey, normalizedKey, StringComparison.OrdinalIgnoreCase) &&
                anchor.LicenseExpireDate == expireDate)
            {
                return false;
            }

            anchor.LicenseKey = normalizedKey;
            anchor.LicenseExpireDate = expireDate;
            return true;
        }

        private static bool ClearAnchorRegistration(RuntimeLicenseAnchorData anchor)
        {
            if (string.IsNullOrWhiteSpace(anchor.LicenseKey) &&
                anchor.LicenseExpireDate == default)
            {
                return false;
            }

            anchor.LicenseKey = string.Empty;
            anchor.LicenseExpireDate = default;
            return true;
        }

        private static string CreateNewLocalBindingSecretValue()
        {
            string secret = GenerateBindingSecret();
            if (OperatingSystem.IsWindows())
            {
                return WindowsLocalMachineBindingPrefix + secret;
            }

            if (OperatingSystem.IsMacOS())
            {
                return MacOsKeychainBindingPrefix + secret;
            }

            if (OperatingSystem.IsLinux())
            {
                return LinuxSecretServiceBindingPrefix + secret;
            }

            return PlatformFallbackBindingPrefix + secret;
        }

        [SupportedOSPlatform("windows")]
        private static string ProtectWindowsLocalMachineSecret(string secret)
        {
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret),
                null,
                DataProtectionScope.LocalMachine);

            return WindowsLocalMachineBindingPrefix + Convert.ToBase64String(protectedBytes);
        }

        private static string GenerateBindingSecret()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LocalBindingSecretByteCount);
            return Convert.ToBase64String(bytes);
        }

    }
}
