using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.Shared.Security;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;

namespace ExportDocManager.Services.Security
{
    public sealed partial class RuntimeLicenseService : ILicenseService
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
        private static readonly TimeSpan StatusCacheLifetime = TimeSpan.FromSeconds(30);
        private const string StoragePolicy =
            "Tauri/Web/API 授权状态镜像到运行数据根 Security/license.dat；试用开始时间、稳定机器码种子、本机密封随机量和已验证注册码保存到平台机器级授权锚点（Windows 注册表 HKLM/HKCU + DPAPI LocalMachine，macOS Keychain，Linux Secret Service；平台安全锚点不可用时才回退到运行数据根 Security）。删除程序目录或 App_Data 后重新解压安装不会重置 7 天试用，也不会丢失已注册授权；业务数据库、模板、OCR 模型和普通运行数据不写系统盘默认用户目录。";

        private readonly IAppPathProvider _pathProvider;
        private readonly Func<string> _deviceFingerprintProvider;
        private readonly Func<string>? _localBindingSecretProvider;
        private readonly IRuntimeLicenseAnchorStore _anchorStore;
        private readonly ILicenseSignatureVerifier _signatureVerifier;
        private readonly LocalSecretProtector _secretProtector;
        private readonly IBusinessClock _clock;
        private readonly SemaphoreSlim _stateGate = new(1, 1);
        private CachedLicenseStatus? _cachedStatus;

        public RuntimeLicenseService(IAppPathProvider pathProvider)
            : this(pathProvider, null, null, null, null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            IRuntimeLicenseAnchorStore? anchorStore)
            : this(pathProvider, null, null, anchorStore, null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            IRuntimeLicenseAnchorStore? anchorStore,
            ILicenseSignatureVerifier? signatureVerifier)
            : this(pathProvider, null, null, anchorStore, signatureVerifier)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            IRuntimeLicenseAnchorStore anchorStore,
            ILicenseSignatureVerifier signatureVerifier,
            IBusinessClock clock)
            : this(pathProvider, null, null, anchorStore, signatureVerifier, clock)
        {
        }

        public RuntimeLicenseService(IAppPathProvider pathProvider, Func<string>? deviceFingerprintProvider)
            : this(pathProvider, deviceFingerprintProvider, null, null, null)
        {
        }

        public RuntimeLicenseService(
            IAppPathProvider pathProvider,
            Func<string>? deviceFingerprintProvider,
            Func<string>? localBindingSecretProvider)
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
            Func<string>? deviceFingerprintProvider,
            Func<string>? localBindingSecretProvider,
            IRuntimeLicenseAnchorStore? anchorStore)
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
            Func<string>? deviceFingerprintProvider,
            Func<string>? localBindingSecretProvider,
            IRuntimeLicenseAnchorStore? anchorStore,
            ILicenseSignatureVerifier? signatureVerifier,
            IBusinessClock? clock = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _deviceFingerprintProvider = deviceFingerprintProvider ?? RuntimeLicenseDeviceFingerprint.Create;
            _localBindingSecretProvider = localBindingSecretProvider;
            _anchorStore = anchorStore ?? RuntimeLicenseAnchorStoreFactory.CreateDefault(pathProvider);
            _signatureVerifier = signatureVerifier ?? new EcdsaLicenseSignatureVerifier();
            _secretProtector = new LocalSecretProtector(pathProvider);
            _clock = clock ?? BusinessClock.CreateSystem();
        }

    }
}
