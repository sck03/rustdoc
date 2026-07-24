using System.Diagnostics;
using System.Globalization;
using System.Text;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using ExportDocManager.Services.BrowserRuntime;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ChromiumHtmlToPdfService : IHtmlToPdfService
    {
        public const string ChromiumExecutableEnvironmentVariable = BrowserExecutableResolver.ChromiumExecutableEnvironmentVariable;
        public const string ChromiumTimeoutEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_TIMEOUT_SECONDS";
        public const string ChromiumNoSandboxEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_NO_SANDBOX";

        private static readonly TimeSpan DefaultRenderTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ProcessDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly IAppPathProvider _pathProvider;
        private readonly BrowserExecutableResolver _executableResolver;
        private readonly BrowserRuntimeManager _browserRuntime;
        private static readonly BrowserRuntimeManager StandaloneBrowserRuntime = new();

        public ChromiumHtmlToPdfService(IAppPathProvider pathProvider, BrowserRuntimeManager browserRuntime = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _executableResolver = new BrowserExecutableResolver(pathProvider);
            _browserRuntime = browserRuntime ?? StandaloneBrowserRuntime;
        }

        public async Task<HtmlToPdfRenderResult> RenderAsync(
            string html,
            string destinationPath,
            HtmlToPdfRenderOptions options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            string rendererPath = ResolveRendererExecutablePath();
            string destinationFullPath = Path.GetFullPath(destinationPath);
            string tempRoot = Path.Combine(_pathProvider.CacheRoot, "ReportPdf", Guid.NewGuid().ToString("N"));
            string htmlPath = Path.Combine(tempRoot, "index.html");
            string pdfPath = Path.Combine(tempRoot, "output.pdf");
            string userDataPath = Path.Combine(tempRoot, "user-data");
            string diskCachePath = Path.Combine(tempRoot, "disk-cache");
            TimeSpan renderTimeout = ResolveRenderTimeout();

            await using var workloadLease = await _browserRuntime
                .AcquireAsync(BrowserWorkloadKind.PdfRendering, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(userDataPath);
                Directory.CreateDirectory(diskCachePath);

                string preparedHtml = PrepareHtml(html, options);
                await File.WriteAllTextAsync(htmlPath, preparedHtml, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

                await RunChromiumAsync(
                        rendererPath,
                        htmlPath,
                        pdfPath,
                        userDataPath,
                        diskCachePath,
                        tempRoot,
                        renderTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
                {
                    throw new InvalidOperationException("Chromium 未生成有效的 PDF 文件。");
                }

                await AtomicFileHelper.WriteFileAtomicAsync(
                        destinationFullPath,
                        (tempPath, ct) =>
                        {
                            File.Copy(pdfPath, tempPath, overwrite: true);
                            return Task.CompletedTask;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                return new HtmlToPdfRenderResult
                {
                    DestinationPath = destinationFullPath,
                    RendererPath = rendererPath
                };
            }
            finally
            {
                await DeleteTemporaryDirectoryAsync(tempRoot).ConfigureAwait(false);
            }
        }

        public string ResolveRendererExecutablePath() => _executableResolver.Resolve();

        private string PrepareHtml(string html, HtmlToPdfRenderOptions options)
        {
            string content = html ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                content = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body></body></html>";
            }

            string baseDirectory = options?.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory)
                && content.IndexOf("<base", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string baseHref = ToFileUri(EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory)));
                string baseTag = $"<base href=\"{baseHref}\">";
                int headIndex = content.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                if (headIndex >= 0)
                {
                    int headEnd = content.IndexOf('>', headIndex);
                    if (headEnd >= 0)
                    {
                        content = content.Insert(headEnd + 1, baseTag);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(options?.DocumentTitle)
                && content.IndexOf("<title", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string title = System.Net.WebUtility.HtmlEncode(options.DocumentTitle.Trim());
                string titleTag = $"<title>{title}</title>";
                int headIndex = content.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                if (headIndex >= 0)
                {
                    int headEnd = content.IndexOf('>', headIndex);
                    if (headEnd >= 0)
                    {
                        content = content.Insert(headEnd + 1, titleTag);
                    }
                }
            }

            content = InsertHeadContent(content, ReportFontPolicy.BuildHtmlStyle(_pathProvider));

            return content;
        }

        private static string InsertHeadContent(string content, string headContent)
        {
            int headCloseIndex = content.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headCloseIndex >= 0)
            {
                return content.Insert(headCloseIndex, headContent);
            }

            int headIndex = content.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
            if (headIndex >= 0)
            {
                int headEnd = content.IndexOf('>', headIndex);
                if (headEnd >= 0)
                {
                    return content.Insert(headEnd + 1, headContent);
                }
            }

            int htmlIndex = content.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (htmlIndex >= 0)
            {
                int htmlEnd = content.IndexOf('>', htmlIndex);
                if (htmlEnd >= 0)
                {
                    return content.Insert(htmlEnd + 1, $"<head>{headContent}</head>");
                }
            }

            return $"<!doctype html><html><head>{headContent}</head><body>{content}</body></html>";
        }

        private async Task RunChromiumAsync(
            string rendererPath,
            string htmlPath,
            string pdfPath,
            string userDataPath,
            string diskCachePath,
            string tempRoot,
            TimeSpan renderTimeout,
            CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rendererPath,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            foreach (string argument in BuildChromiumArguments(
                         htmlPath,
                         pdfPath,
                         userDataPath,
                         diskCachePath,
                         ResolveNoSandboxSetting()))
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 Chromium PDF 渲染进程。");
            }

            using var processRegistration = _browserRuntime.RegisterOwnedProcess(
                process,
                BrowserWorkloadKind.PdfRendering,
                "HTML to PDF");

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(renderTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                await TryKillAndWaitAsync(process).ConfigureAwait(false);
                (string timeoutOutput, string timeoutError) = await ReadProcessOutputAsync(outputTask, errorTask).ConfigureAwait(false);
                string detail = BuildProcessOutputDetail(timeoutError, timeoutOutput);
                string timeoutSeconds = Math.Ceiling(renderTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                string message = $"Chromium PDF 渲染超过 {timeoutSeconds} 秒未完成，已终止进程树。渲染器：{rendererPath}；临时工作目录：{tempRoot}，位于运行数据根 Cache/ReportPdf 下。";
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message += $" Chromium 输出：{detail}";
                }

                throw new InvalidOperationException(message);
            }
            catch (OperationCanceledException)
            {
                await TryKillAndWaitAsync(process).ConfigureAwait(false);
                throw;
            }

            (string output, string error) = await ReadProcessOutputAsync(outputTask, errorTask).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string detail = BuildProcessOutputDetail(error, output);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                    ? $"Chromium PDF 渲染失败，退出码 {process.ExitCode}。"
                    : $"Chromium PDF 渲染失败，退出码 {process.ExitCode}：{detail}");
            }
        }

        private static TimeSpan ResolveRenderTimeout()
        {
            string configuredSeconds = Environment.GetEnvironmentVariable(ChromiumTimeoutEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredSeconds))
            {
                return DefaultRenderTimeout;
            }

            if (!int.TryParse(configuredSeconds.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                || seconds <= 0)
            {
                throw new InvalidOperationException($"{ChromiumTimeoutEnvironmentVariable} 必须配置为大于 0 的整数秒数。");
            }

            return TimeSpan.FromSeconds(seconds);
        }

        private static bool ResolveNoSandboxSetting()
        {
            string configured = Environment.GetEnvironmentVariable(ChromiumNoSandboxEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            string normalized = configured.Trim();
            if (normalized == "1")
            {
                return true;
            }

            if (normalized == "0")
            {
                return false;
            }

            if (bool.TryParse(normalized, out bool enabled))
            {
                return enabled;
            }

            throw new InvalidOperationException($"{ChromiumNoSandboxEnvironmentVariable} 只能配置为 0、1、false 或 true。");
        }

        internal static IReadOnlyList<string> BuildChromiumArguments(
            string htmlPath,
            string pdfPath,
            string userDataPath,
            string diskCachePath,
            bool disableSandbox)
        {
            var arguments = new List<string>
            {
                "--headless=new",
                "--disable-gpu",
                "--disable-extensions",
                "--disable-background-networking",
                "--disable-dev-shm-usage",
                "--disable-sync",
                "--disable-features=Translate,MediaRouter,OptimizationHints,PaintHolding",
                "--no-first-run",
                "--no-default-browser-check",
                "--allow-file-access-from-files",
                "--run-all-compositor-stages-before-draw",
                "--virtual-time-budget=1000",
                $"--user-data-dir={userDataPath}",
                $"--disk-cache-dir={diskCachePath}",
                $"--print-to-pdf={pdfPath}",
                "--print-to-pdf-no-header",
                "--no-pdf-header-footer",
                ToFileUri(htmlPath)
            };
            if (disableSandbox)
            {
                arguments.Insert(1, "--no-sandbox");
            }

            return arguments;
        }

        internal static string ToFileUri(string localPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

            string fullPath = Path.GetFullPath(localPath.Trim());
            if (OperatingSystem.IsWindows())
            {
                fullPath = RemoveWindowsExtendedPathPrefix(fullPath);
            }

            return new Uri(fullPath).AbsoluteUri;
        }

        private static string RemoveWindowsExtendedPathPrefix(string localPath)
        {
            const string extendedPathPrefix = @"\\?\";
            const string extendedUncPrefix = @"\\?\UNC\";

            if (localPath.StartsWith(extendedUncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + localPath[extendedUncPrefix.Length..];
            }

            if (localPath.StartsWith(extendedPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return localPath[extendedPathPrefix.Length..];
            }

            return localPath;
        }

        private static string BuildProcessOutputDetail(string error, string output)
        {
            return string.Join(" ", new[] { error, output }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        }

        private static async Task<(string Output, string Error)> ReadProcessOutputAsync(
            Task<string> outputTask,
            Task<string> errorTask)
        {
            string output = await ReadProcessStreamAsync(outputTask).ConfigureAwait(false);
            string error = await ReadProcessStreamAsync(errorTask).ConfigureAwait(false);
            return (output, error);
        }

        private static async Task<string> ReadProcessStreamAsync(Task<string> streamTask)
        {
            try
            {
                return await streamTask.WaitAsync(ProcessDrainTimeout).ConfigureAwait(false);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task TryKillAndWaitAsync(Process process)
        {
            TryKill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(ProcessDrainTimeout).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static async Task DeleteTemporaryDirectoryAsync(string path)
        {
            for (int attempt = 0; attempt < 5 && Directory.Exists(path); attempt++)
            {
                AtomicFileHelper.TryDeleteDirectory(path);
                if (Directory.Exists(path))
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
        }

        private static void TryKill(Process process)
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
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar)
                || path.EndsWith(Path.AltDirectorySeparatorChar)
                    ? path
                    : path + Path.DirectorySeparatorChar;
        }
    }
}
