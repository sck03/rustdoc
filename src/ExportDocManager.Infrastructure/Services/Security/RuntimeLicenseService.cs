using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.Shared.Security;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;
using Microsoft.Win32;

namespace ExportDocManager.Services.Security
{
    public sealed class RuntimeLicenseService : ILicenseService
    {
        private const int TrialDays = 7;
        private const int DeviceBindingVersion = 3;
        private const string LicenseFileName = "license.dat";
        private const string MachineSeedFileName = "machine-id.seed";
        private const string LocalBindingSecretFileName = "machine-binding.dat";
        private const string WindowsLocalMachineBindingPrefix = "win-dpapi-localmachine-v1:";
        private const string MacOsKeychainBindingPrefix = "macos-keychain-v1:";
        private const string LinuxSecretServiceBindingPrefix = "linux-secret-service-v1:";
        private const string PlatformFallbackBindingPrefix = "platform-fallback-v1:";
        private const int LocalBindingSecretByteCount = 32;
        private const int PlatformCommandTimeoutMs = 2500;
        private const string StoragePolicy =
            "Tauri/Web/API 授权状态镜像到运行数据根 Security/license.dat；试用开始时间、稳定机器码种子、本机密封随机量和已验证注册码保存到平台机器级授权锚点（Windows 注册表 HKLM/HKCU + DPAPI LocalMachine，macOS Keychain，Linux Secret Service；平台安全锚点不可用时才回退到运行数据根 Security）。删除程序目录或 App_Data 后重新解压安装不会重置 7 天试用，也不会丢失已注册授权；业务数据库、模板、OCR 模型和普通运行数据不写系统盘默认用户目录。";

        private readonly IAppPathProvider _pathProvider;
        private readonly Func<string> _deviceFingerprintProvider;
        private readonly Func<string> _localBindingSecretProvider;
        private readonly IRuntimeLicenseAnchorStore _anchorStore;
        private readonly ILicenseSignatureVerifier _signatureVerifier;

        public RuntimeLicenseService(IAppPathProvider pathProvider)
            : this(pathProvider, null, null, null, null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            IRuntimeLicenseAnchorStore anchorStore)
            : this(pathProvider, null, null, anchorStore, null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            IRuntimeLicenseAnchorStore anchorStore,
            ILicenseSignatureVerifier signatureVerifier)
            : this(pathProvider, null, null, anchorStore, signatureVerifier)
        {
        }

        public RuntimeLicenseService(IAppPathProvider pathProvider, Func<string> deviceFingerprintProvider)
            : this(pathProvider, deviceFingerprintProvider, null, null, null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            Func<string> deviceFingerprintProvider,
            Func<string> localBindingSecretProvider)
            : this(
                pathProvider,
                deviceFingerprintProvider,
                localBindingSecretProvider,
                null,
                null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            Func<string> deviceFingerprintProvider,
            Func<string> localBindingSecretProvider,
            IRuntimeLicenseAnchorStore anchorStore)
            : this(
                pathProvider,
                deviceFingerprintProvider,
                localBindingSecretProvider,
                anchorStore,
                null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            Func<string> deviceFingerprintProvider,
            Func<string> localBindingSecretProvider,
            IRuntimeLicenseAnchorStore anchorStore,
            ILicenseSignatureVerifier signatureVerifier)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _deviceFingerprintProvider = deviceFingerprintProvider ?? CreateDeviceFingerprint;
            _localBindingSecretProvider = localBindingSecretProvider;
            _anchorStore = anchorStore ?? RuntimeLicenseAnchorStoreFactory.CreateDefault(pathProvider);
            _signatureVerifier = signatureVerifier ?? new EcdsaLicenseSignatureVerifier();
        }

        public async Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.Now;
            var anchor = await ReadOrCreateMachineAnchorAsync(now, cancellationToken).ConfigureAwait(false);
            var identity = await GetLicenseIdentityAsync(anchor, cancellationToken).ConfigureAwait(false);
            var machineId = identity.MachineId;
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
                    ExpireDate = hasAnchorLicense ? anchor.LicenseExpireDate : DateTime.MinValue,
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

            if (now < anchorInstallDate.AddMinutes(-10))
            {
                now = anchorInstallDate;
            }

            if (now < anchorLastRunDate.AddMinutes(-30))
            {
                now = anchorLastRunDate;
            }

            if (now > anchor.LastRunDate)
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
                        out DateTime expireDate))
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
                        status.Message = expireDate == DateTime.MaxValue
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
                    data.ExpireDate = DateTime.MinValue;
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

            var daysUsed = (now - data.InstallDate).TotalDays;
            int remaining = TrialDays - (int)daysUsed;
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

        private static int CalculateRegisteredDaysRemaining(DateTime now, DateTime expireDate)
        {
            if (expireDate == DateTime.MaxValue)
            {
                return int.MaxValue;
            }

            if (now >= expireDate)
            {
                return 0;
            }

            double daysRemaining = Math.Ceiling((expireDate - now).TotalDays);
            return daysRemaining >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)daysRemaining);
        }

        public async Task<LicenseRegistrationResult> RegisterAsync(
            string licenseKey,
            CancellationToken cancellationToken = default)
        {
            string normalizedKey = LicenseValueNormalizer.NormalizeLicenseKey(licenseKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return new LicenseRegistrationResult
                {
                    Success = false,
                    Message = "注册码不能为空。",
                    Status = await GetStatusAsync(cancellationToken).ConfigureAwait(false)
                };
            }

            var now = DateTime.Now;
            var anchor = await ReadOrCreateMachineAnchorAsync(now, cancellationToken).ConfigureAwait(false);
            var identity = await GetLicenseIdentityAsync(anchor, cancellationToken).ConfigureAwait(false);
            var machineId = identity.MachineId;
            var anchorInstallDate = NormalizeAnchorDate(anchor.InstallDate, now);
            var anchorLastRunDate = MaxDate(NormalizeAnchorDate(anchor.LastRunDate, now), anchorInstallDate);
            if (!_signatureVerifier.TryValidate(
                    machineId,
                    normalizedKey,
                    out DateTime expireDate))
            {
                return new LicenseRegistrationResult
                {
                    Success = false,
                    Message = "注册码无效或机器码不匹配。",
                    Status = await GetStatusAsync(cancellationToken).ConfigureAwait(false)
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

            return new LicenseRegistrationResult
            {
                Success = true,
                Message = "注册成功。",
                Status = await GetStatusAsync(cancellationToken).ConfigureAwait(false)
            };
        }

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
                await AtomicFileHelper.WriteAllTextAtomicAsync(GetMachineSeedPath(), seed, Encoding.UTF8, cancellationToken)
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
            RuntimeLicenseAnchorData anchor = await LoadMachineAnchorAsync(cancellationToken)
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

        private async Task<RuntimeLicenseAnchorData> LoadMachineAnchorAsync(CancellationToken cancellationToken)
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

        private static string ReadCommandOutput(string fileName, params string[] arguments)
        {
            return TryRunProcess(fileName, arguments, null, out string output)
                ? output
                : string.Empty;
        }

        private static bool TryRunProcess(
            string fileName,
            IEnumerable<string> arguments,
            string standardInput,
            out string output)
        {
            output = string.Empty;
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = standardInput != null,
                        CreateNoWindow = true
                    }
                };

                foreach (string argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                if (!process.Start())
                {
                    return false;
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }

                if (!process.WaitForExit(PlatformCommandTimeoutMs))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return false;
                }

                output = outputTask.GetAwaiter().GetResult().Trim();
                _ = errorTask.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch
            {
                output = string.Empty;
                return false;
            }
        }

        private async Task<RuntimeLicenseData> LoadLicenseDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                string path = GetLicensePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                string encrypted = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                string json = SecurityHelper.Decrypt(encrypted);
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
            string encrypted = SecurityHelper.Encrypt(json);
            await AtomicFileHelper.WriteAllTextAtomicAsync(GetLicensePath(), encrypted, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }

        private static RuntimeLicenseData NormalizeLoadedData(RuntimeLicenseData data)
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

        private static string CreateDeviceFingerprint()
        {
            var parts = new List<string>();

            AddFingerprintPart(parts, RuntimeInformation.OSArchitecture.ToString());
            AddFingerprintPart(parts, Environment.MachineName);

            if (OperatingSystem.IsWindows())
            {
                AddWindowsFingerprintParts(parts);
            }
            else if (OperatingSystem.IsMacOS())
            {
                AddMacOsFingerprintParts(parts);
            }
            else
            {
                AddUnixFingerprintParts(parts);
            }

            if (parts.Count == 0)
            {
                AddFingerprintPart(parts, RuntimeInformation.ProcessArchitecture.ToString());
            }

            return string.Join("|", parts.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        }

        [SupportedOSPlatform("windows")]
        private static void AddWindowsFingerprintParts(List<string> parts)
        {
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"));
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry32, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"));
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId"));
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer"));
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName"));
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct"));
            AddFingerprintPart(parts, ReadWindowsRegistryValue(RegistryView.Registry64, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVendor"));
        }

        [SupportedOSPlatform("windows")]
        private static string ReadWindowsRegistryValue(RegistryView view, string subKeyName, string valueName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var subKey = baseKey.OpenSubKey(subKeyName);
                return subKey?.GetValue(valueName)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        [SupportedOSPlatform("macos")]
        private static void AddMacOsFingerprintParts(List<string> parts)
        {
            AddFingerprintPart(parts, ReadFirstLine("/var/db/dbuuid"));

            string ioreg = ReadCommandOutput(
                "/usr/sbin/ioreg",
                "-rd1",
                "-c",
                "IOPlatformExpertDevice");

            AddFingerprintPart(parts, ExtractMacOsIoregValue(ioreg, "IOPlatformUUID"));
            AddFingerprintPart(parts, ExtractMacOsIoregValue(ioreg, "IOPlatformSerialNumber"));
        }

        private static string ExtractMacOsIoregValue(string output, string key)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string marker = $"\"{key}\" =";
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int index = line.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                string value = line[(index + marker.Length)..].Trim().Trim('"');
                return value;
            }

            return string.Empty;
        }

        private static void AddUnixFingerprintParts(List<string> parts)
        {
            foreach (string path in new[]
            {
                "/etc/machine-id",
                "/var/lib/dbus/machine-id",
                "/sys/class/dmi/id/product_uuid",
                "/sys/class/dmi/id/product_serial",
                "/sys/class/dmi/id/board_serial"
            })
            {
                AddFingerprintPart(parts, ReadFirstLine(path));
            }
        }

        private static string ReadFirstLine(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return string.Empty;
                }

                return File.ReadLines(path).FirstOrDefault() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AddFingerprintPart(List<string> parts, string value)
        {
            string normalized = NormalizeFingerprintPart(value);
            if (IsUsableFingerprintPart(normalized))
            {
                parts.Add(normalized);
            }
        }

        private static string NormalizeFingerprintPart(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool IsUsableFingerprintPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.Length < 4)
            {
                return false;
            }

            if (value.Equals("TO BE FILLED BY O.E.M.", StringComparison.Ordinal) ||
                value.Equals("DEFAULT STRING", StringComparison.Ordinal) ||
                value.Equals("NONE", StringComparison.Ordinal) ||
                value.Equals("UNKNOWN", StringComparison.Ordinal) ||
                value.Equals("SYSTEM SERIAL NUMBER", StringComparison.Ordinal) ||
                value.Equals("NOT SPECIFIED", StringComparison.Ordinal) ||
                value.Equals("UNAVAILABLE", StringComparison.Ordinal) ||
                value.Equals("00000000-0000-0000-0000-000000000000", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
    }
}
