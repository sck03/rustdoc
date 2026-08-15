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
        public async Task<LicenseRegistrationResult> RegisterAsync(
            string licenseKey,
            CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _cachedStatus, null);
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LicenseRegistrationResult result = await RegisterCoreAsync(licenseKey, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    CacheStatus(result.Status, _clock.UtcNow.UtcTicks);
                }
                else
                {
                    Volatile.Write(ref _cachedStatus, null);
                }

                return result;
            }
            finally
            {
                _stateGate.Release();
            }
        }

        private async Task<LicenseRegistrationResult> RegisterCoreAsync(
            string licenseKey,
            CancellationToken cancellationToken)
        {
            string normalizedKey = LicenseValueNormalizer.NormalizeLicenseKey(licenseKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return new LicenseRegistrationResult
                {
                    Success = false,
                    Message = "注册码不能为空。",
                    Status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false)
                };
            }

            var now = _clock.Today;
            var anchor = await ReadOrCreateMachineAnchorAsync(now, cancellationToken).ConfigureAwait(false);
            var identity = await GetLicenseIdentityAsync(anchor, cancellationToken).ConfigureAwait(false);
            var machineId = identity.MachineId;
            var anchorInstallDate = NormalizeAnchorDate(anchor.InstallDate, now);
            var anchorLastRunDate = MaxDate(NormalizeAnchorDate(anchor.LastRunDate, now), anchorInstallDate);
            if (!_signatureVerifier.TryValidate(
                    machineId,
                    normalizedKey,
                    out DateOnly expireDate))
            {
                return new LicenseRegistrationResult
                {
                    Success = false,
                    Message = "注册码无效或机器码不匹配。",
                    Status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false)
                };
            }

            var data = await LoadLicenseDataAsync(cancellationToken).ConfigureAwait(false)
                ?? new RuntimeLicenseData
                {
                    InstallDate = anchorInstallDate,
                    LastRunDate = anchorLastRunDate,
                    MachineId = machineId,
                    DeviceBindingVersion = DeviceBindingVersion,
                    DeviceFingerprintHash = identity.DeviceFingerprintHash,
                    LocalBindingSecretHash = identity.LocalBindingSecretHash
                };

            data.IsRegistered = true;
            data.LicenseKey = normalizedKey;
            data.ExpireDate = expireDate;
            data.MachineId = machineId;
            data.InstallDate = anchorInstallDate;
            data.DeviceBindingVersion = DeviceBindingVersion;
            data.DeviceFingerprintHash = identity.DeviceFingerprintHash;
            data.LocalBindingSecretHash = identity.LocalBindingSecretHash;
            data.LastRunDate = now;

            await SaveLicenseDataAsync(data, cancellationToken).ConfigureAwait(false);
            if (now > anchor.LastRunDate)
            {
                anchor.LastRunDate = now;
            }

            SetAnchorRegistration(anchor, normalizedKey, expireDate);
            await SaveMachineAnchorAsync(anchor, cancellationToken).ConfigureAwait(false);
            RecoveryLicenseReactivationMarker.Clear(_pathProvider);

            return new LicenseRegistrationResult
            {
                Success = true,
                Message = "注册成功。",
                Status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false)
            };
        }

    }
}
