using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.BrowserRuntime
{
    public sealed class BrowserExecutableResolver
    {
        public const string ChromiumExecutableEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE";
        private const uint SemFailCriticalErrors = 0x0001;
        private const uint SemNoOpenFileErrorBox = 0x8000;
        private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(5);

        private readonly IAppPathProvider _pathProvider;
        private readonly Action<string> _versionProbe;
        private readonly object _cacheSync = new();
        private string _cachedExecutable;

        public BrowserExecutableResolver(IAppPathProvider pathProvider) =>
            (_pathProvider, _versionProbe) =
            (pathProvider ?? throw new ArgumentNullException(nameof(pathProvider)), ProbeVersion);

        internal BrowserExecutableResolver(
            IAppPathProvider pathProvider,
            Action<string> versionProbe)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _versionProbe = versionProbe ?? throw new ArgumentNullException(nameof(versionProbe));
        }

        public string Resolve()
        {
            string configuredPath = Environment.GetEnvironmentVariable(ChromiumExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullPath = Path.GetFullPath(configuredPath.Trim().Trim('"'));
                if (!File.Exists(fullPath))
                {
                    throw new InfrastructureServiceException(
                        $"{ChromiumExecutableEnvironmentVariable} 指向的 Chromium 可执行文件不存在：{fullPath}");
                }

                lock (_cacheSync)
                {
                    if (string.Equals(_cachedExecutable, fullPath, PathComparison))
                    {
                        return _cachedExecutable;
                    }
                }

                ValidateCandidateOrThrow(fullPath, explicitlyConfigured: true);
                lock (_cacheSync)
                {
                    _cachedExecutable = fullPath;
                }
                return fullPath;
            }

            lock (_cacheSync)
            {
                if (!string.IsNullOrWhiteSpace(_cachedExecutable) && File.Exists(_cachedExecutable))
                {
                    return _cachedExecutable;
                }
            }

            var failures = new List<string>();
            foreach (string candidate in EnumerateManagedCandidates())
            {
                try
                {
                    ValidateCandidateOrThrow(candidate, explicitlyConfigured: false);
                    string resolved = Path.GetFullPath(candidate);
                    lock (_cacheSync)
                    {
                        _cachedExecutable = resolved;
                    }
                    return resolved;
                }
                catch (InfrastructureServiceException ex)
                {
                    if (failures.Count < 3)
                    {
                        failures.Add($"{Path.GetFileName(candidate)}：{ex.Message}");
                    }
                }
            }

            string detail = failures.Count == 0
                ? string.Empty
                : $" 已发现但未通过完整性检查：{string.Join("；", failures)}";
            throw new InfrastructureServiceException(
                "未找到可用 Chromium。请把官方 Chrome Headless Shell、Chromium 或 Chrome for Testing " +
                "完整放在程序运行目录 Browsers/，或显式设置 EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE。" +
                "程序不会自动下载到系统盘，也不支持手工删除官方运行包内的 DLL、pak、locale 或 snapshot 文件。" +
                detail);
        }

        private IEnumerable<string> EnumerateManagedCandidates()
        {
            string browserRoot = Path.GetFullPath(_pathProvider.BrowserRoot);
            if (!Directory.Exists(browserRoot))
            {
                yield break;
            }

            var yielded = new HashSet<string>(PathComparer);
            foreach (string candidate in EnumerateManifestCandidates(browserRoot))
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }

            foreach (string relativePath in GetPreferredRelativeExecutables())
            {
                string candidate = Path.GetFullPath(Path.Combine(_pathProvider.AppRoot, relativePath));
                if (File.Exists(candidate) && yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }

            IEnumerable<string> recursiveCandidates = GetRuntimeExecutableNames()
                .SelectMany(name => Directory.EnumerateFiles(browserRoot, name, SearchOption.AllDirectories))
                .Select(Path.GetFullPath)
                .Where(IsCandidateForCurrentPlatform)
                .Distinct(PathComparer)
                .OrderBy(GetCandidateRank)
                .ThenBy(path => path, PathComparer);
            foreach (string candidate in recursiveCandidates)
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> EnumerateManifestCandidates(string browserRoot)
        {
            string expectedPlatform = GetRuntimePlatform();
            var manifests = new List<(int Rank, string Path)>();
            foreach (string manifestPath in Directory.EnumerateFiles(
                         browserRoot,
                         "*.manifest.json",
                         SearchOption.AllDirectories))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    JsonElement root = document.RootElement;
                    string platform = ReadString(root, "platform");
                    if (platform.Length == 0)
                    {
                        platform = ReadString(root, "architecture");
                    }
                    if (!string.Equals(platform, expectedPlatform, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string product = ReadString(root, "product");
                    bool compatible = !root.TryGetProperty("playwrightCompatible", out JsonElement compatibleElement) ||
                                      compatibleElement.ValueKind != JsonValueKind.False;
                    if (!compatible)
                    {
                        continue;
                    }

                    string executablePath = ReadString(root, "executablePath");
                    string candidate = ResolveManifestExecutable(browserRoot, manifestPath, executablePath, product);
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        int rank = product.Equals("ChromeHeadlessShell", StringComparison.OrdinalIgnoreCase) ? 0 : 10;
                        manifests.Add((rank, candidate));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A malformed optional manifest must not hide another complete
                    // runtime under Browsers; recursive discovery remains available.
                }
            }

            return manifests
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.Path, PathComparer)
                .Select(item => item.Path);
        }

        private static string ResolveManifestExecutable(
            string browserRoot,
            string manifestPath,
            string executablePath,
            string product)
        {
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                string fullPath = Path.IsPathRooted(executablePath)
                    ? Path.GetFullPath(executablePath)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, executablePath));
                if (File.Exists(fullPath) && IsUnderRoot(fullPath, browserRoot))
                {
                    return fullPath;
                }
            }

            string manifestRoot = Path.GetDirectoryName(manifestPath)!;
            string expectedName = product.Equals("ChromeHeadlessShell", StringComparison.OrdinalIgnoreCase)
                ? OperatingSystem.IsWindows() ? "chrome-headless-shell.exe" : "chrome-headless-shell"
                : OperatingSystem.IsWindows() ? "chrome.exe" : OperatingSystem.IsMacOS()
                    ? "Google Chrome for Testing"
                    : "chrome";
            return Directory.EnumerateFiles(manifestRoot, expectedName, SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(path => IsUnderRoot(path, browserRoot))
                .OrderBy(path => path, PathComparer)
                .FirstOrDefault() ?? string.Empty;
        }

        private void ValidateCandidateOrThrow(string candidate, bool explicitlyConfigured)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (!explicitlyConfigured && !IsUnderRoot(fullPath, _pathProvider.BrowserRoot))
            {
                throw new InfrastructureServiceException("浏览器候选路径不在受管 Browsers 目录内。");
            }

            if (!IsCandidateForCurrentPlatform(fullPath))
            {
                throw new InfrastructureServiceException("浏览器可执行文件与当前操作系统或 CPU 架构不匹配。");
            }

            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode mode;
                try
                {
                    mode = File.GetUnixFileMode(fullPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InfrastructureServiceException("无法读取浏览器可执行权限。", ex);
                }

                const UnixFileMode executeBits = UnixFileMode.UserExecute |
                                                 UnixFileMode.GroupExecute |
                                                 UnixFileMode.OtherExecute;
                if ((mode & executeBits) == 0)
                {
                    throw new InfrastructureServiceException("浏览器文件没有 Unix executable bit。");
                }
            }

            if (Path.GetFileName(fullPath).StartsWith("chrome-headless-shell", StringComparison.OrdinalIgnoreCase))
            {
                ValidateHeadlessShellBundle(fullPath);
            }

            _versionProbe(fullPath);
        }

        private static void ValidateHeadlessShellBundle(string executablePath)
        {
            string root = Path.GetDirectoryName(executablePath)!;
            string runtimePlatform = GetRuntimePlatform();
            IReadOnlyList<string> requiredFiles = GetRequiredHeadlessShellFiles(runtimePlatform);
            string missing = requiredFiles.FirstOrDefault(file =>
            {
                string path = Path.Combine(root, file);
                return !File.Exists(path) || new FileInfo(path).Length == 0;
            });
            if (!string.IsNullOrWhiteSpace(missing))
            {
                throw new InfrastructureServiceException(
                    $"Chrome Headless Shell 运行包不完整，缺少或损坏 {missing}。请重新解压官方完整包，不要手工精简模块。");
            }

            string localesRoot = Path.Combine(root, "locales");
            if (RequiresHeadlessShellLocales(runtimePlatform) &&
                (!Directory.Exists(localesRoot) || !Directory.EnumerateFiles(localesRoot).Any()))
            {
                throw new InfrastructureServiceException("Chrome Headless Shell 运行包缺少 locales 资源。");
            }

            // Official Chrome Headless Shell does not ship a private version.dll;
            // Windows loads that API from the operating system. A dialog claiming
            // version.dll is missing is therefore treated as a loader/sandbox
            // startup failure and caught by the version probe, not "fixed" by
            // copying an arbitrary DLL beside the executable.
        }

        internal static IReadOnlyList<string> GetRequiredHeadlessShellFiles(string runtimePlatform)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimePlatform);
            string snapshotFile = runtimePlatform.ToLowerInvariant() switch
            {
                "mac-arm64" => "v8_context_snapshot.arm64.bin",
                "mac-x64" => "v8_context_snapshot.x86_64.bin",
                _ => "v8_context_snapshot.bin"
            };
            return
            [
                "icudtl.dat",
                snapshotFile,
                "headless_lib_data.pak",
                "headless_lib_strings.pak"
            ];
        }

        internal static bool RequiresHeadlessShellLocales(string runtimePlatform)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimePlatform);
            // The official macOS Headless Shell archive has no locales directory;
            // its strings are carried by headless_lib_strings.pak instead.
            return !runtimePlatform.StartsWith("mac-", StringComparison.OrdinalIgnoreCase);
        }

        private static void ProbeVersion(string executablePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ErrorDialog = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!
            };
            startInfo.ArgumentList.Add("--version");

            Process process = null;
            uint previousErrorMode = 0;
            bool restoreErrorMode = false;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    restoreErrorMode = SetThreadErrorMode(
                        SemFailCriticalErrors | SemNoOpenFileErrorBox,
                        out previousErrorMode);
                }

                process = Process.Start(startInfo) ??
                          throw new InfrastructureServiceException("无法启动 Chromium 版本探针。");
            }
            catch (Exception ex) when (ex is not InfrastructureServiceException)
            {
                throw new InfrastructureServiceException("Chromium 加载失败，运行包可能缺少系统或随包依赖。", ex);
            }
            finally
            {
                if (restoreErrorMode)
                {
                    SetThreadErrorMode(previousErrorMode, out _);
                }
            }

            using (process)
            {
                try
                {
                    if (!process.WaitForExit(VersionProbeTimeout))
                    {
                        process.Kill(entireProcessTree: true);
                        throw new InfrastructureServiceException("Chromium 版本探针超时，运行包无法正常启动。");
                    }

                    string output = (process.StandardOutput.ReadToEnd() + " " + process.StandardError.ReadToEnd()).Trim();
                    if (process.ExitCode != 0 ||
                        (!output.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                         !output.Contains("Chromium", StringComparison.OrdinalIgnoreCase)))
                    {
                        string detail = output.Length > 300 ? output[..300] + "…" : output;
                        throw new InfrastructureServiceException(
                            $"Chromium 版本探针失败（退出码 {process.ExitCode}）：{detail}");
                    }
                }
                catch (InfrastructureServiceException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }
                    throw new InfrastructureServiceException("Chromium 版本探针执行失败。", ex);
                }
            }
        }

        private static bool IsCandidateForCurrentPlatform(string path)
        {
            string fileName = Path.GetFileName(path);
            if (OperatingSystem.IsWindows())
            {
                if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            string expectedPlatform = GetRuntimePlatform();
            string[] knownPlatforms =
            {
                "win32", "win64", "win-arm64", "linux32", "linux64", "linux-arm64",
                "mac-x64", "mac-arm64"
            };
            string embeddedPlatform = knownPlatforms.FirstOrDefault(platform =>
                normalized.Contains($"/{platform}/", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(embeddedPlatform) ||
                   string.Equals(embeddedPlatform, expectedPlatform, StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetRuntimePlatform() =>
            OperatingSystem.IsWindows()
                ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win64"
                : OperatingSystem.IsMacOS()
                    ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "mac-arm64" : "mac-x64"
                    : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux64";

        private static int GetCandidateRank(string path)
        {
            string name = Path.GetFileName(path);
            int productRank = name.StartsWith("chrome-headless-shell", StringComparison.OrdinalIgnoreCase) ? 0 : 10;
            int managedRank = path.Contains("ChromeForTesting", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
            return productRank + managedRank;
        }

        private static IReadOnlyList<string> GetRuntimeExecutableNames() => OperatingSystem.IsWindows()
            ? ["chrome-headless-shell.exe", "chrome.exe", "chromium.exe"]
            : OperatingSystem.IsMacOS()
                ? ["chrome-headless-shell", "Chromium", "Google Chrome for Testing"]
                : ["chrome-headless-shell", "chrome", "chromium"];

        private static IReadOnlyList<string> GetPreferredRelativeExecutables() => OperatingSystem.IsWindows()
            ?
            [
                Path.Combine("Browsers", "chrome-headless-shell.exe"),
                Path.Combine("Browsers", "ChromeHeadlessShell", "chrome-headless-shell.exe"),
                Path.Combine("Browsers", "chrome.exe"),
                Path.Combine("Browsers", "chromium.exe")
            ]
            : OperatingSystem.IsMacOS()
                ?
                [
                    Path.Combine("Browsers", "chrome-headless-shell"),
                    Path.Combine("Browsers", "Chromium.app", "Contents", "MacOS", "Chromium"),
                    Path.Combine("Browsers", "Google Chrome for Testing.app", "Contents", "MacOS", "Google Chrome for Testing")
                ]
                :
                [
                    Path.Combine("Browsers", "chrome-headless-shell"),
                    Path.Combine("Browsers", "chrome"),
                    Path.Combine("Browsers", "chromium")
                ];

        private static string ReadString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static bool IsUnderRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string prefix = fullRoot + Path.DirectorySeparatorChar;
            return fullPath.Equals(fullRoot, PathComparison) || fullPath.StartsWith(prefix, PathComparison);
        }

        private static StringComparer PathComparer => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        private static StringComparison PathComparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);
    }
}
