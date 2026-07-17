using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.Shared.Security;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.Win32;

namespace ExportDocManager.Services.Security
{
    public interface IRuntimeLicenseAnchorStore
    {
        string StorageDescription { get; }

        Task<RuntimeLicenseAnchorData> LoadAsync(CancellationToken cancellationToken = default);

        Task SaveAsync(RuntimeLicenseAnchorData data, CancellationToken cancellationToken = default);
    }

    public sealed class RuntimeLicenseAnchorData
    {
        public int SchemaVersion { get; set; } = 1;
        public string MachineSeed { get; set; } = string.Empty;
        public string LocalBindingSecret { get; set; } = string.Empty;
        public DateTime InstallDate { get; set; }
        public DateTime LastRunDate { get; set; }
        public string LicenseKey { get; set; } = string.Empty;
        public DateTime LicenseExpireDate { get; set; }
        public string Signature { get; set; } = string.Empty;
    }

    public static class RuntimeLicenseAnchorStoreFactory
    {
        public static IRuntimeLicenseAnchorStore CreateDefault(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            if (OperatingSystem.IsWindows())
            {
                return new WindowsRegistryRuntimeLicenseAnchorStore();
            }

            if (OperatingSystem.IsMacOS())
            {
                return new CommandSecretRuntimeLicenseAnchorStore(
                    "macOS Keychain: ExportDocManager.RuntimeLicense / MachineAnchor",
                    () => CommandSecretRuntimeLicenseAnchorStore.TryReadMacOsSecret(out string value) ? value : string.Empty,
                    value => CommandSecretRuntimeLicenseAnchorStore.TryWriteMacOsSecret(value));
            }

            if (OperatingSystem.IsLinux())
            {
                return new CommandSecretRuntimeLicenseAnchorStore(
                    "Linux Secret Service: application=ExportDocManager purpose=RuntimeLicenseAnchor",
                    () => CommandSecretRuntimeLicenseAnchorStore.TryReadLinuxSecret(out string value) ? value : string.Empty,
                    value => CommandSecretRuntimeLicenseAnchorStore.TryWriteLinuxSecret(value));
            }

            return new FileRuntimeLicenseAnchorStore(
                Path.Combine(pathProvider.SecurityRoot, "machine-trial-anchor.dat"),
                "平台安全锚点不可用，回退到运行数据根 Security/machine-trial-anchor.dat。");
        }
    }

    public sealed class FileRuntimeLicenseAnchorStore : IRuntimeLicenseAnchorStore
    {
        private readonly string _path;

        public FileRuntimeLicenseAnchorStore(string path, string storageDescription = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            _path = Path.GetFullPath(path);
            StorageDescription = string.IsNullOrWhiteSpace(storageDescription)
                ? _path
                : storageDescription;
        }

        public string StorageDescription { get; }

        public async Task<RuntimeLicenseAnchorData> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                string payload = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
                return RuntimeLicenseAnchorCodec.Decode(payload);
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveAsync(RuntimeLicenseAnchorData data, CancellationToken cancellationToken = default)
        {
            string payload = RuntimeLicenseAnchorCodec.Encode(data);
            await AtomicFileHelper.WriteAllTextAtomicAsync(_path, payload, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [SupportedOSPlatform("windows")]
    internal sealed class WindowsRegistryRuntimeLicenseAnchorStore : IRuntimeLicenseAnchorStore
    {
        private const string SubKeyName = @"SOFTWARE\ExportDocManager\RuntimeLicense";
        private const string ValueName = "MachineTrialAnchor";
        private const string ProtectedPrefix = "win-dpapi-localmachine-anchor-v1:";

        public string StorageDescription =>
            @"Windows 注册表 HKLM/HKCU\SOFTWARE\ExportDocManager\RuntimeLicense\MachineTrialAnchor（DPAPI LocalMachine 密封）。";

        public Task<RuntimeLicenseAnchorData> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    string payload = ReadRegistryPayload(hive, view);
                    if (TryDecodeProtectedPayload(payload, out RuntimeLicenseAnchorData data))
                    {
                        return Task.FromResult(data);
                    }
                }
            }

            return Task.FromResult<RuntimeLicenseAnchorData>(null);
        }

        public Task SaveAsync(RuntimeLicenseAnchorData data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string payload = ProtectPayload(RuntimeLicenseAnchorCodec.Encode(data));

            if (TryWriteRegistryPayload(RegistryHive.LocalMachine, RegistryView.Registry64, payload) ||
                TryWriteRegistryPayload(RegistryHive.LocalMachine, RegistryView.Registry32, payload) ||
                TryWriteRegistryPayload(RegistryHive.CurrentUser, RegistryView.Registry64, payload) ||
                TryWriteRegistryPayload(RegistryHive.CurrentUser, RegistryView.Registry32, payload))
            {
                return Task.CompletedTask;
            }

            throw new InvalidOperationException("无法写入 Windows 授权试用锚点。");
        }

        private static string ReadRegistryPayload(RegistryHive hive, RegistryView view)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.OpenSubKey(SubKeyName, writable: false);
                return subKey?.GetValue(ValueName)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryWriteRegistryPayload(RegistryHive hive, RegistryView view, string payload)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.CreateSubKey(SubKeyName, writable: true);
                subKey?.SetValue(ValueName, payload, RegistryValueKind.String);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ProtectPayload(string payload)
        {
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(payload ?? string.Empty),
                null,
                DataProtectionScope.LocalMachine);

            return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }

        private static bool TryDecodeProtectedPayload(string payload, out RuntimeLicenseAnchorData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(payload) ||
                !payload.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(payload[ProtectedPrefix.Length..]);
                byte[] bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
                string encoded = Encoding.UTF8.GetString(bytes);
                data = RuntimeLicenseAnchorCodec.Decode(encoded);
                return data != null;
            }
            catch
            {
                data = null;
                return false;
            }
        }
    }

    internal sealed class CommandSecretRuntimeLicenseAnchorStore : IRuntimeLicenseAnchorStore
    {
        private const int CommandTimeoutMs = 2500;
        private readonly Func<string> _read;
        private readonly Func<string, bool> _write;

        public CommandSecretRuntimeLicenseAnchorStore(
            string storageDescription,
            Func<string> read,
            Func<string, bool> write)
        {
            StorageDescription = storageDescription ?? string.Empty;
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        public string StorageDescription { get; }

        public Task<RuntimeLicenseAnchorData> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string payload = _read();
            return Task.FromResult(RuntimeLicenseAnchorCodec.Decode(payload));
        }

        public Task SaveAsync(RuntimeLicenseAnchorData data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string payload = RuntimeLicenseAnchorCodec.Encode(data);
            if (!_write(payload))
            {
                throw new InvalidOperationException("无法写入平台授权试用锚点。");
            }

            return Task.CompletedTask;
        }

        public static bool TryReadMacOsSecret(out string secret)
        {
            return TryRunProcess(
                "/usr/bin/security",
                new[]
                {
                    "find-generic-password",
                    "-a",
                    "MachineAnchor",
                    "-s",
                    "ExportDocManager.RuntimeLicense",
                    "-w"
                },
                null,
                out secret);
        }

        public static bool TryWriteMacOsSecret(string secret)
        {
            return TryRunProcess(
                "/usr/bin/security",
                new[]
                {
                    "add-generic-password",
                    "-U",
                    "-a",
                    "MachineAnchor",
                    "-s",
                    "ExportDocManager.RuntimeLicense",
                    "-w",
                    secret
                },
                null,
                out _);
        }

        public static bool TryReadLinuxSecret(out string secret)
        {
            return TryRunProcess(
                "secret-tool",
                new[]
                {
                    "lookup",
                    "application",
                    "ExportDocManager",
                    "purpose",
                    "RuntimeLicenseAnchor"
                },
                null,
                out secret);
        }

        public static bool TryWriteLinuxSecret(string secret)
        {
            return TryRunProcess(
                "secret-tool",
                new[]
                {
                    "store",
                    "--label",
                    "ExportDocManager Runtime License Anchor",
                    "application",
                    "ExportDocManager",
                    "purpose",
                    "RuntimeLicenseAnchor"
                },
                secret,
                out _);
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
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
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

                if (!process.WaitForExit(CommandTimeoutMs))
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
    }

    internal static class RuntimeLicenseAnchorCodec
    {
        public static string Encode(RuntimeLicenseAnchorData data)
        {
            ArgumentNullException.ThrowIfNull(data);
            data.Signature = ComputeSignature(data);
            string json = JsonSerializer.Serialize(data);
            return SecurityHelper.Encrypt(json);
        }

        public static RuntimeLicenseAnchorData Decode(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                string json = SecurityHelper.Decrypt(payload);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var data = JsonSerializer.Deserialize<RuntimeLicenseAnchorData>(json);
                if (data == null ||
                    string.IsNullOrWhiteSpace(data.Signature) ||
                    !string.Equals(data.Signature, ComputeSignature(data), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSignature(RuntimeLicenseAnchorData data)
        {
            var payload = string.Join("|",
                data.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                data.MachineSeed ?? string.Empty,
                data.LocalBindingSecret ?? string.Empty,
                data.InstallDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                data.LastRunDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                data.LicenseKey ?? string.Empty,
                data.LicenseExpireDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LicenseDefaults.RuntimeIntegrityKey));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }
    }
}
