using System.Diagnostics;
using System.Text.RegularExpressions;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services
{
    internal sealed record PostgreSqlToolPaths(
        string BinRoot,
        string PgDumpPath,
        string PgRestorePath,
        string PsqlPath,
        bool VersionCompatible = false,
        string Version = "")
    {
        public bool ToolsReady => AvailableToolCount == 3 && VersionCompatible;

        public int AvailableToolCount =>
            CountPath(PgDumpPath) + CountPath(PgRestorePath) + CountPath(PsqlPath);

        private static int CountPath(string path) => string.IsNullOrWhiteSpace(path) ? 0 : 1;
    }

    internal static partial class PostgreSqlToolLocator
    {
        public const string BinRootEnvironmentVariable = "EXPORTDOCMANAGER_POSTGRES_BIN";
        public const string AllowPathEnvironmentVariable = "EXPORTDOCMANAGER_ALLOW_POSTGRES_PATH";
        private const int MinimumSupportedMajorVersion = 18;

        public static PostgreSqlToolPaths Resolve(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string configuredRoot = Environment.GetEnvironmentVariable(BinRootEnvironmentVariable) ?? string.Empty;
            var candidates = new[]
            {
                configuredRoot,
                Path.Combine(pathProvider.ToolRoot, "PostgreSQL", "bin"),
                Path.Combine(pathProvider.ToolRoot, "PostgreSQL")
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            if (IsPathFallbackEnabled())
            {
                string pathRoot = ResolveCommonPathRoot();
                if (!string.IsNullOrWhiteSpace(pathRoot))
                {
                    candidates.Add(pathRoot);
                }
            }

            var best = new PostgreSqlToolPaths(string.Empty, string.Empty, string.Empty, string.Empty);
            foreach (string root in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var candidate = ResolveCandidate(root);
                if (candidate.AvailableToolCount > best.AvailableToolCount)
                {
                    best = candidate;
                }
                if (candidate.AvailableToolCount != 3)
                {
                    continue;
                }

                var version = ValidateVersions(candidate);
                candidate = candidate with
                {
                    VersionCompatible = version.Compatible,
                    Version = version.Version
                };
                if (candidate.ToolsReady)
                {
                    return candidate;
                }
                if (candidate.AvailableToolCount >= best.AvailableToolCount)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static PostgreSqlToolPaths ResolveCandidate(string root)
        {
            return new PostgreSqlToolPaths(
                root,
                ResolveToolPath(root, "pg_dump"),
                ResolveToolPath(root, "pg_restore"),
                ResolveToolPath(root, "psql"));
        }

        private static (bool Compatible, string Version) ValidateVersions(PostgreSqlToolPaths tools)
        {
            try
            {
                int dumpMajor = ReadMajorVersion(tools.PgDumpPath, out string dumpVersion);
                int restoreMajor = ReadMajorVersion(tools.PgRestorePath, out string restoreVersion);
                int psqlMajor = ReadMajorVersion(tools.PsqlPath, out string psqlVersion);
                bool compatible = dumpMajor >= MinimumSupportedMajorVersion &&
                    restoreMajor == dumpMajor &&
                    psqlMajor == dumpMajor;
                string version = compatible
                    ? dumpVersion
                    : $"pg_dump={dumpVersion}; pg_restore={restoreVersion}; psql={psqlVersion}";
                return (compatible, version);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or
                                       System.ComponentModel.Win32Exception or InfrastructureServiceException)
            {
                return (false, ex.Message);
            }
        }

        private static int ReadMajorVersion(string executable, out string version)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");
            using var process = Process.Start(startInfo)
                ?? throw new InfrastructureServiceException($"无法启动 PostgreSQL 客户端工具：{executable}");
            Task<string> outputTask = BoundedProcessOutput.ReadAsync(
                process.StandardOutput,
                truncationMessage: "[PostgreSQL 客户端版本输出过长，已截断]");
            Task<string> errorTask = BoundedProcessOutput.ReadAsync(
                process.StandardError,
                truncationMessage: "[PostgreSQL 客户端错误输出过长，已截断]");
            if (!process.WaitForExit(5_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                BoundedProcessOutput.DrainProcessAsync(process, TimeSpan.FromSeconds(5))
                    .GetAwaiter()
                    .GetResult();
                BoundedProcessOutput.ObserveAsync(TimeSpan.FromSeconds(5), outputTask, errorTask)
                    .GetAwaiter()
                    .GetResult();
                throw new InfrastructureServiceException($"PostgreSQL 客户端版本检查超时：{executable}");
            }
            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InfrastructureServiceException(
                    "PostgreSQL 客户端版本检查失败。",
                    new InvalidOperationException((string.IsNullOrWhiteSpace(error) ? output : error).Trim()));
            }

            version = output.Trim();
            Match match = PostgreSqlVersionRegex().Match(version);
            if (!match.Success || !int.TryParse(match.Groups["major"].Value, out int major))
            {
                throw new InfrastructureServiceException($"无法识别 PostgreSQL 客户端版本：{version}");
            }
            return major;
        }

        private static string ResolveToolPath(string root, string toolName)
        {
            string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            string path = Path.Combine(root, toolName + extension);
            return File.Exists(path) ? Path.GetFullPath(path) : string.Empty;
        }

        private static bool IsPathFallbackEnabled()
        {
            string value = Environment.GetEnvironmentVariable(AllowPathEnvironmentVariable) ?? string.Empty;
            value = value.Trim();
            return value == "1" ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("enabled", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveCommonPathRoot()
        {
            string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(Path.Combine(directory, "pg_dump" + extension)) &&
                    File.Exists(Path.Combine(directory, "pg_restore" + extension)) &&
                    File.Exists(Path.Combine(directory, "psql" + extension)))
                {
                    return Path.GetFullPath(directory);
                }
            }
            return string.Empty;
        }

        [GeneratedRegex(@"\(PostgreSQL\)\s+(?<major>\d+)(?:\.\d+)?", RegexOptions.CultureInvariant)]
        private static partial Regex PostgreSqlVersionRegex();
    }
}
