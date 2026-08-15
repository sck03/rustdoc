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
        public async Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var cached = Volatile.Read(ref _cachedStatus);
            long nowUtcTicks = _clock.UtcNow.UtcTicks;
            if (cached != null && cached.ExpiresAtUtcTicks > nowUtcTicks)
            {
                return cached.Status;
            }

            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cached = Volatile.Read(ref _cachedStatus);
                nowUtcTicks = _clock.UtcNow.UtcTicks;
                if (cached != null && cached.ExpiresAtUtcTicks > nowUtcTicks)
                {
                    return cached.Status;
                }

                LicenseStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
                CacheStatus(status, nowUtcTicks);
                return status;
            }
            finally
            {
                _stateGate.Release();
            }
        }

        private async Task<LicenseStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
        {
            var now = _clock.Today;
            var anchor = await ReadOrCreateMachineAnchorAsync(now, cancellationToken).ConfigureAwait(false);
            var identity = await GetLicenseIdentityAsync(anchor, cancellationToken).ConfigureAwait(false);
            var machineId = identity.MachineId;
            if (RecoveryLicenseReactivationMarker.Exists(_pathProvider))
            {
                return ToStatus(new MutableLicenseStatus
                {
                    IsRegistered = false,
                    IsTrialExpired = true,
                    DaysRemaining = 0,
                    MachineId = machineId,
                    Message = "持卡机灾难恢复已完成。为防止旧设备授权被复制，必须使用当前机器码重新激活授权。"
                });
            }
            var anchorInstallDate = NormalizeAnchorDate(anchor.InstallDate, now);
            var anchorLastRunDate = MaxDate(NormalizeAnchorDate(anchor.LastRunDate, now), anchorInstallDate);
            string anchorLicenseKey = LicenseValueNormalizer.NormalizeLicenseKey(anchor.LicenseKey);
            bool hasAnchorLicense = !string.IsNullOrWhiteSpace(anchorLicenseKey);
            var data = await LoadLicenseDataAsync(cancellationToken).ConfigureAwait(false);
            var dataChanged = false;
            var anchorChanged = false;
            var hasTerminalStatus = false;
            var status = new MutableLicenseStatus
            {
                MachineId = machineId
            };

            if (data == null)
            {
                data = new RuntimeLicenseData
                {
                    InstallDate = anchorInstallDate,
                    LastRunDate = anchorLastRunDate,
                    IsRegistered = hasAnchorLicense,
                    LicenseKey = hasAnchorLicense ? anchorLicenseKey : string.Empty,
                    ExpireDate = hasAnchorLicense ? anchor.LicenseExpireDate : default,
                    MachineId = machineId,
                    DeviceBindingVersion = DeviceBindingVersion,
                    DeviceFingerprintHash = identity.DeviceFingerprintHash,
                    LocalBindingSecretHash = identity.LocalBindingSecretHash
                };
                dataChanged = true;
            }
            else
            {
                if (data.InstallDate != anchorInstallDate)
                {
                    data.InstallDate = anchorInstallDate;
                    dataChanged = true;
                }

                if (data.LastRunDate != anchorLastRunDate)
                {
                    data.LastRunDate = anchorLastRunDate;
                    dataChanged = true;
                }

                if (hasAnchorLicense &&
                    (!data.IsRegistered ||
                     !string.Equals(
                         LicenseValueNormalizer.NormalizeLicenseKey(data.LicenseKey),
                         anchorLicenseKey,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    data.IsRegistered = true;
                    data.LicenseKey = anchorLicenseKey;
                    data.ExpireDate = anchor.LicenseExpireDate;
                    dataChanged = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(data.DeviceFingerprintHash) &&
                !string.Equals(data.DeviceFingerprintHash, identity.DeviceFingerprintHash, StringComparison.OrdinalIgnoreCase))
            {
                status.IsTrialExpired = true;
                status.Message = "设备指纹变更，请重新注册。";
                return ToStatus(status);
            }

            if (!string.IsNullOrWhiteSpace(data.LocalBindingSecretHash) &&
                !string.Equals(data.LocalBindingSecretHash, identity.LocalBindingSecretHash, StringComparison.OrdinalIgnoreCase))
            {
                status.IsTrialExpired = true;
                status.Message = "本机授权密封信息变更，请重新注册。";
                return ToStatus(status);
            }

            if (!string.IsNullOrEmpty(data.MachineId) &&
                !string.Equals(data.MachineId, machineId, StringComparison.OrdinalIgnoreCase))
            {
                status.IsTrialExpired = true;
                status.Message = "机器码变更，请重新注册。";
                return ToStatus(status);
            }

            if (string.IsNullOrEmpty(data.MachineId))
            {
                data.MachineId = machineId;
                dataChanged = true;
            }

            if (data.DeviceBindingVersion != DeviceBindingVersion)
            {
                data.DeviceBindingVersion = DeviceBindingVersion;
                dataChanged = true;
            }

            if (string.IsNullOrWhiteSpace(data.DeviceFingerprintHash))
            {
                data.DeviceFingerprintHash = identity.DeviceFingerprintHash;
                dataChanged = true;
            }

            if (string.IsNullOrWhiteSpace(data.LocalBindingSecretHash))
            {
                data.LocalBindingSecretHash = identity.LocalBindingSecretHash;
                dataChanged = true;
            }

            if (now < anchorLastRunDate)
            {
                now = anchorLastRunDate;
            }

            if (ShouldAdvancePersistedLastRunDate(anchor.LastRunDate, now))
            {
                anchor.LastRunDate = now;
                anchorChanged = true;
            }

            if (data.LastRunDate != anchor.LastRunDate)
            {
                data.LastRunDate = anchor.LastRunDate;
                dataChanged = true;
            }

            if (data.IsRegistered && !string.IsNullOrWhiteSpace(data.LicenseKey))
            {
                string normalizedKey = LicenseValueNormalizer.NormalizeLicenseKey(data.LicenseKey);
                if (_signatureVerifier.TryValidate(
                        machineId,
                        normalizedKey,
                        out DateOnly expireDate))
                {
                    if (data.ExpireDate != expireDate)
                    {
                        data.ExpireDate = expireDate;
                        dataChanged = true;
                    }

                    if (SetAnchorRegistration(anchor, normalizedKey, expireDate))
                    {
                        anchorChanged = true;
                    }

                    if (now > expireDate)
                    {
                        status.IsTrialExpired = true;
                        status.Message = "授权已过期，请重新注册。";
                        status.ExpireDate = expireDate;
                        hasTerminalStatus = true;

                        data.IsRegistered = false;
                        dataChanged = true;
                        if (ClearAnchorRegistration(anchor))
                        {
                            anchorChanged = true;
                        }
                    }
                    else
                    {
                        status.IsRegistered = true;
                        status.ExpireDate = expireDate;
                        status.DaysRemaining = CalculateRegisteredDaysRemaining(now, expireDate);
                        status.Message = expireDate == DateOnly.MaxValue
                            ? "已注册 (终身授权)"
                            : $"已注册 (有效期至: {expireDate:yyyy-MM-dd})";

                        if (dataChanged)
                        {
                            await SaveLicenseDataAsync(data, cancellationToken).ConfigureAwait(false);
                        }

                        if (anchorChanged)
                        {
                            await SaveMachineAnchorAsync(anchor, cancellationToken).ConfigureAwait(false);
                        }

                        return ToStatus(status);
                    }
                }
                else
                {
                    status.Message = "注册码无效或机器码已变更。";
                    hasTerminalStatus = true;
                    data.IsRegistered = false;
                    data.LicenseKey = string.Empty;
                    data.ExpireDate = default;
                    dataChanged = true;
                    if (ClearAnchorRegistration(anchor))
                    {
                        anchorChanged = true;
                    }
                }
            }

            if (dataChanged)
            {
                await SaveLicenseDataAsync(data, cancellationToken).ConfigureAwait(false);
            }

            if (anchorChanged)
            {
                await SaveMachineAnchorAsync(anchor, cancellationToken).ConfigureAwait(false);
            }

            if (hasTerminalStatus)
            {
                return ToStatus(status);
            }

            var daysUsed = now.DayNumber - data.InstallDate.DayNumber;
            int remaining = TrialDays - daysUsed;
            status.DaysRemaining = remaining > 0 ? remaining : 0;

            if (daysUsed > TrialDays)
            {
                status.IsTrialExpired = true;
                status.Message = "试用期已过，请注册。";
            }
            else
            {
                status.Message = $"试用期剩余 {status.DaysRemaining} 天。";
            }

            return ToStatus(status);
        }

        private static int CalculateRegisteredDaysRemaining(DateOnly now, DateOnly expireDate)
        {
            if (expireDate == DateOnly.MaxValue)
            {
                return int.MaxValue;
            }

            if (now >= expireDate)
            {
                return 0;
            }

            return Math.Max(0, expireDate.DayNumber - now.DayNumber + 1);
        }

    }
}
