using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Shared.Security;

namespace ExportDocManager.Infrastructure.Tests
{
    public class RuntimeLicenseServiceTests
    {
        [Fact]
        public async Task RegisterAsync_WhenDeviceFingerprintIsStable_ShouldPersistRegisteredStatus()
        {
            string appRoot = CreateTempDirectory();
            string dataRoot = CreateTempDirectory();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot, dataRoot);
                string anchorRoot = CreateTempDirectory();
                var anchorStore = CreateAnchorStore(anchorRoot);
                var service = CreateService(provider, () => "DEVICE-A", null, anchorStore);
                var status = await service.GetStatusAsync();
                string key = TestLicenseSignatureVerifier.ValidLicenseKey;

                var result = await service.RegisterAsync(key);
                var reloadedStatus = await CreateService(provider, () => "DEVICE-A", null, anchorStore).GetStatusAsync();

                Assert.True(result.Success);
                Assert.True(reloadedStatus.IsRegistered);
                Assert.True(reloadedStatus.DaysRemaining > 0);
                Assert.Equal(status.MachineId, reloadedStatus.MachineId);
                Assert.True(File.Exists(Path.Combine(provider.SecurityRoot, "license.dat")));
                Assert.True(File.Exists(Path.Combine(provider.SecurityRoot, "machine-id.seed")));
                if (OperatingSystem.IsWindows())
                {
                    Assert.True(File.Exists(Path.Combine(provider.SecurityRoot, "machine-binding.dat")));
                }

                DeleteDirectory(anchorRoot);
            }
            finally
            {
                DeleteDirectory(appRoot);
                DeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public async Task GetStatusAsync_WhenRuntimeDirectoryIsDeletedAfterRegistration_ShouldKeepRegisteredStatus()
        {
            string firstAppRoot = CreateTempDirectory();
            string firstDataRoot = CreateTempDirectory();
            string secondAppRoot = CreateTempDirectory();
            string secondDataRoot = CreateTempDirectory();
            string anchorRoot = CreateTempDirectory();

            try
            {
                var anchorStore = CreateAnchorStore(anchorRoot);
                var firstProvider = new RuntimeAppPathProvider(firstAppRoot, firstDataRoot);
                var firstService = CreateService(
                    firstProvider,
                    () => "DEVICE-A",
                    null,
                    anchorStore);
                var firstStatus = await firstService.GetStatusAsync();
                string key = TestLicenseSignatureVerifier.ValidLicenseKey;

                var registration = await firstService.RegisterAsync(key);
                Assert.True(registration.Success);

                DeleteDirectory(firstAppRoot);
                DeleteDirectory(firstDataRoot);

                var secondProvider = new RuntimeAppPathProvider(secondAppRoot, secondDataRoot);
                var secondStatus = await CreateService(
                    secondProvider,
                    () => "DEVICE-A",
                    null,
                    anchorStore).GetStatusAsync();
                var anchor = await anchorStore.LoadAsync();

                Assert.True(secondStatus.IsRegistered);
                Assert.False(secondStatus.IsTrialExpired);
                Assert.Equal(firstStatus.MachineId, secondStatus.MachineId);
                Assert.Equal(LicenseValueNormalizer.NormalizeLicenseKey(key), anchor.LicenseKey);
                Assert.True(File.Exists(Path.Combine(secondProvider.SecurityRoot, "license.dat")));
            }
            finally
            {
                DeleteDirectory(firstAppRoot);
                DeleteDirectory(firstDataRoot);
                DeleteDirectory(secondAppRoot);
                DeleteDirectory(secondDataRoot);
                DeleteDirectory(anchorRoot);
            }
        }

        [Fact]
        public async Task GetStatusAsync_WhenSecurityDirectoryIsCopiedToDifferentDevice_ShouldRequireRegistration()
        {
            string sourceAppRoot = CreateTempDirectory();
            string sourceDataRoot = CreateTempDirectory();
            string targetAppRoot = CreateTempDirectory();
            string targetDataRoot = CreateTempDirectory();

            try
            {
                var sourceProvider = new RuntimeAppPathProvider(sourceAppRoot, sourceDataRoot);
                string sourceAnchorRoot = CreateTempDirectory();
                string targetAnchorRoot = CreateTempDirectory();
                var sourceService = CreateService(
                    sourceProvider,
                    () => "DEVICE-A",
                    null,
                    CreateAnchorStore(sourceAnchorRoot));
                await sourceService.GetStatusAsync();
                string key = TestLicenseSignatureVerifier.ValidLicenseKey;
                var registration = await sourceService.RegisterAsync(key);
                Assert.True(registration.Success);

                var targetProvider = new RuntimeAppPathProvider(targetAppRoot, targetDataRoot);
                Directory.Delete(targetProvider.SecurityRoot, recursive: true);
                CopyDirectory(sourceProvider.SecurityRoot, targetProvider.SecurityRoot);

                var copiedStatus = await CreateService(
                    targetProvider,
                    () => "DEVICE-B",
                    null,
                    CreateAnchorStore(targetAnchorRoot)).GetStatusAsync();

                Assert.False(copiedStatus.IsRegistered);
                Assert.True(copiedStatus.IsTrialExpired);
                Assert.Contains("设备指纹变更", copiedStatus.Message, StringComparison.Ordinal);

                DeleteDirectory(sourceAnchorRoot);
                DeleteDirectory(targetAnchorRoot);
            }
            finally
            {
                DeleteDirectory(sourceAppRoot);
                DeleteDirectory(sourceDataRoot);
                DeleteDirectory(targetAppRoot);
                DeleteDirectory(targetDataRoot);
            }
        }

        [Fact]
        public async Task GetStatusAsync_WhenSecurityDirectoryIsCopiedWithSpoofedFingerprint_ShouldRequireRegistration()
        {
            string sourceAppRoot = CreateTempDirectory();
            string sourceDataRoot = CreateTempDirectory();
            string targetAppRoot = CreateTempDirectory();
            string targetDataRoot = CreateTempDirectory();

            try
            {
                var sourceProvider = new RuntimeAppPathProvider(sourceAppRoot, sourceDataRoot);
                string sourceAnchorRoot = CreateTempDirectory();
                string targetAnchorRoot = CreateTempDirectory();
                var sourceService = CreateService(
                    sourceProvider,
                    () => "DEVICE-A",
                    () => "LOCAL-SEAL-A",
                    CreateAnchorStore(sourceAnchorRoot));
                await sourceService.GetStatusAsync();
                string key = TestLicenseSignatureVerifier.ValidLicenseKey;
                var registration = await sourceService.RegisterAsync(key);
                Assert.True(registration.Success);

                var targetProvider = new RuntimeAppPathProvider(targetAppRoot, targetDataRoot);
                Directory.Delete(targetProvider.SecurityRoot, recursive: true);
                CopyDirectory(sourceProvider.SecurityRoot, targetProvider.SecurityRoot);

                var copiedStatus = await CreateService(
                    targetProvider,
                    () => "DEVICE-A",
                    () => "LOCAL-SEAL-B",
                    CreateAnchorStore(targetAnchorRoot)).GetStatusAsync();

                Assert.False(copiedStatus.IsRegistered);
                Assert.True(copiedStatus.IsTrialExpired);
                Assert.Contains("本机授权密封信息变更", copiedStatus.Message, StringComparison.Ordinal);

                DeleteDirectory(sourceAnchorRoot);
                DeleteDirectory(targetAnchorRoot);
            }
            finally
            {
                DeleteDirectory(sourceAppRoot);
                DeleteDirectory(sourceDataRoot);
                DeleteDirectory(targetAppRoot);
                DeleteDirectory(targetDataRoot);
            }
        }

        [Fact]
        public async Task RegisterAsync_WhenUnsignedLegacyKeyIsUsed_ShouldRejectRegistration()
        {
            string appRoot = CreateTempDirectory();
            string dataRoot = CreateTempDirectory();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot, dataRoot);
                string anchorRoot = CreateTempDirectory();
                var service = CreateService(
                    provider,
                    () => "DEVICE-A",
                    () => "LOCAL-SEAL-A",
                    CreateAnchorStore(anchorRoot));

                var result = await service.RegisterAsync("FFFF-FFFF-FFFF-FFFF-FFFF-FFFF");

                Assert.False(result.Success);
                Assert.False(result.Status.IsRegistered);
                Assert.Contains("注册码无效", result.Message, StringComparison.Ordinal);

                DeleteDirectory(anchorRoot);
            }
            finally
            {
                DeleteDirectory(appRoot);
                DeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public async Task GetStatusAsync_WhenRuntimeDirectoryIsDeleted_ShouldKeepTrialAnchorAndMachineId()
        {
            string firstAppRoot = CreateTempDirectory();
            string firstDataRoot = CreateTempDirectory();
            string secondAppRoot = CreateTempDirectory();
            string secondDataRoot = CreateTempDirectory();
            string anchorRoot = CreateTempDirectory();

            try
            {
                var anchorStore = CreateAnchorStore(anchorRoot);
                var firstProvider = new RuntimeAppPathProvider(firstAppRoot, firstDataRoot);
                var firstService = CreateService(
                    firstProvider,
                    () => "DEVICE-A",
                    null,
                    anchorStore);
                var firstStatus = await firstService.GetStatusAsync();

                var anchor = await anchorStore.LoadAsync();
                anchor.InstallDate = DateTime.Now.AddDays(-8);
                anchor.LastRunDate = DateTime.Now;
                await anchorStore.SaveAsync(anchor);

                DeleteDirectory(firstAppRoot);
                DeleteDirectory(firstDataRoot);

                var secondProvider = new RuntimeAppPathProvider(secondAppRoot, secondDataRoot);
                var secondStatus = await CreateService(
                    secondProvider,
                    () => "DEVICE-A",
                    null,
                    anchorStore).GetStatusAsync();

                Assert.Equal(firstStatus.MachineId, secondStatus.MachineId);
                Assert.True(secondStatus.IsTrialExpired);
                Assert.Contains("试用期已过", secondStatus.Message, StringComparison.Ordinal);
                Assert.True(File.Exists(Path.Combine(secondProvider.SecurityRoot, "license.dat")));
                Assert.True(File.Exists(Path.Combine(secondProvider.SecurityRoot, "machine-id.seed")));
            }
            finally
            {
                DeleteDirectory(firstAppRoot);
                DeleteDirectory(firstDataRoot);
                DeleteDirectory(secondAppRoot);
                DeleteDirectory(secondDataRoot);
                DeleteDirectory(anchorRoot);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(GetTestRoot(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string GetTestRoot()
        {
            string configuredRoot = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_TEST_ROOT") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                string resolved = Path.GetFullPath(configuredRoot);
                Directory.CreateDirectory(resolved);
                return resolved;
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
                {
                    string workspaceRoot = Path.Combine(directory.FullName, ".codex-runtime", "license-tests");
                    Directory.CreateDirectory(workspaceRoot);
                    return workspaceRoot;
                }

                directory = directory.Parent;
            }

            string localRoot = Path.Combine(AppContext.BaseDirectory, ".test-runs");
            Directory.CreateDirectory(localRoot);
            return localRoot;
        }

        private static RuntimeLicenseService CreateService(
            IAppPathProvider pathProvider,
            Func<string> deviceFingerprintProvider,
            Func<string> localBindingSecretProvider,
            IRuntimeLicenseAnchorStore anchorStore)
        {
            return new RuntimeLicenseService(
                pathProvider,
                deviceFingerprintProvider,
                localBindingSecretProvider,
                anchorStore,
                TestLicenseSignatureVerifier.Instance);
        }

        private static FileRuntimeLicenseAnchorStore CreateAnchorStore(string anchorRoot)
        {
            return new FileRuntimeLicenseAnchorStore(
                Path.Combine(anchorRoot, "license-anchor.dat"),
                "测试授权锚点。");
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
            }

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private sealed class TestLicenseSignatureVerifier : ILicenseSignatureVerifier
        {
            public const string ValidLicenseKey = "EDM2-TEST-LICENSE";
            public static readonly TestLicenseSignatureVerifier Instance = new();

            public bool TryValidate(string machineId, string licenseKey, out DateTime expireDate)
            {
                expireDate = DateTime.Today.AddYears(1).Date.AddDays(1).AddTicks(-1);
                return !string.IsNullOrWhiteSpace(machineId) &&
                    string.Equals(licenseKey, ValidLicenseKey, StringComparison.Ordinal);
            }
        }
    }
}
