using System.Globalization;
using System.Text;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using ExportDocManager.Services.BrowserRuntime;
using Microsoft.Playwright;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ChromiumHtmlToPdfService : IHtmlToPdfService
    {
        public const string ChromiumExecutableEnvironmentVariable = BrowserExecutableResolver.ChromiumExecutableEnvironmentVariable;
        public const string ChromiumTimeoutEnvironmentVariable = "EXPORTDOCMANAGER_CHROMIUM_TIMEOUT_SECONDS";
        public const string ChromiumNoSandboxEnvironmentVariable = ChromiumSandboxPolicy.NoSandboxEnvironmentVariable;

        private static readonly TimeSpan DefaultRenderTimeout = TimeSpan.FromSeconds(60);
        private const int TemporaryDirectoryCleanupAttempts = 20;
        private const int TemporaryDirectoryCleanupDelayMilliseconds = 250;
        private const string ContentSecurityPolicy =
            "default-src 'none'; script-src 'none'; connect-src 'none'; frame-src 'none'; " +
            "object-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src file: data:; " +
            "form-action 'none'; base-uri 'self'";

        private readonly IAppPathProvider _pathProvider;
        private readonly BrowserExecutableResolver _executableResolver;
        private readonly BrowserRuntimeManager _browserRuntime;
        private readonly ManagedPlaywrightBrowserHost _browserHost;
        private static readonly BrowserRuntimeManager StandaloneBrowserRuntime = new();
        private static readonly Lock AbandonedDirectoryCleanupGate = new();
        private static readonly HashSet<string> CleanedReportRoots = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public ChromiumHtmlToPdfService(
            IAppPathProvider pathProvider,
            BrowserRuntimeManager browserRuntime = null,
            ManagedPlaywrightBrowserHost browserHost = null,
            BrowserExecutableResolver executableResolver = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _executableResolver = executableResolver ?? new BrowserExecutableResolver(pathProvider);
            _browserRuntime = browserRuntime ?? StandaloneBrowserRuntime;
            _browserHost = browserHost;
            CleanupAbandonedTemporaryDirectoriesOnce(
                Path.Combine(_pathProvider.CacheRoot, "ReportPdf"));
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
            TimeSpan renderTimeout = ResolveRenderTimeout();

            try
            {
                Directory.CreateDirectory(tempRoot);

                string preparedHtml = PrepareHtml(html, options);
                await File.WriteAllTextAsync(htmlPath, preparedHtml, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

                await RenderWithPlaywrightAsync(
                        htmlPath,
                        pdfPath,
                        renderTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
                {
                    throw new InfrastructureServiceException("Chromium 未生成有效的 PDF 文件。");
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

        public string ResolveRendererExecutablePath() =>
            BrowserCdpEndpointPolicy.TryResolve(out Uri endpoint)
                ? endpoint.ToString().TrimEnd('/')
                : _executableResolver.Resolve();

        internal string PrepareHtml(string html, HtmlToPdfRenderOptions options)
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

            string csp = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{ContentSecurityPolicy}\">";
            content = InsertHeadContentAtStart(content, csp);
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

        private static string InsertHeadContentAtStart(string content, string headContent)
        {
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

        private async Task RenderWithPlaywrightAsync(
            string htmlPath,
            string pdfPath,
            TimeSpan renderTimeout,
            CancellationToken cancellationToken)
        {
            ManagedPlaywrightBrowserHost browserHost = _browserHost;
            bool disposeBrowserHost = browserHost == null;
            browserHost ??= new ManagedPlaywrightPdfBrowserHost(
                _browserRuntime,
                _executableResolver,
                _pathProvider);
            try
            {
                await browserHost.ExecuteAsync(
                        BrowserWorkloadKind.PdfRendering,
                        renderTimeout,
                        async (page, ct) =>
                        {
                            await page.GotoAsync(
                                    ToFileUri(htmlPath),
                                    new PageGotoOptions { WaitUntil = WaitUntilState.Load })
                                .WaitAsync(ct)
                                .ConfigureAwait(false);
                            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print })
                                .WaitAsync(ct)
                                .ConfigureAwait(false);
                            await WaitForPageAssetsAsync(page, ct).ConfigureAwait(false);
                            await page.PdfAsync(new PagePdfOptions
                                {
                                    Path = pdfPath,
                                    DisplayHeaderFooter = false,
                                    PrintBackground = true,
                                    PreferCSSPageSize = true
                                })
                                .WaitAsync(ct)
                                .ConfigureAwait(false);
                            return true;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (disposeBrowserHost)
                {
                    await browserHost.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        internal static async Task WaitForPageAssetsAsync(IPage page, CancellationToken cancellationToken)
        {
            int brokenImageCount = await page.EvaluateAsync<int>("""
                async () => {
                    const images = Array.from(document.images).filter(image => image.getAttribute('src')?.trim());
                    await Promise.all(images.map(image => image.complete
                        ? Promise.resolve()
                        : new Promise(resolve => {
                            image.addEventListener('load', resolve, { once: true });
                            image.addEventListener('error', resolve, { once: true });
                        })));
                    await document.fonts.ready;
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    return images.filter(image => !image.complete || image.naturalWidth <= 0).length;
                }
                """)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (brokenImageCount > 0)
            {
                throw new ServiceValidationException($"报表中有 {brokenImageCount} 张图片加载或解码失败，已停止生成 PDF，避免输出缺图文件。");
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
                throw new ServiceValidationException($"{ChromiumTimeoutEnvironmentVariable} 必须配置为大于 0 的整数秒数。");
            }

            return TimeSpan.FromSeconds(seconds);
        }

        internal static string ToFileUri(string localPath)
        {
            return LocalFileUriHelper.FromPath(localPath);
        }

        private static async Task DeleteTemporaryDirectoryAsync(string path)
        {
            for (int attempt = 0; attempt < TemporaryDirectoryCleanupAttempts && Directory.Exists(path); attempt++)
            {
                AtomicFileHelper.TryDeleteDirectory(path);
                if (Directory.Exists(path))
                {
                    await Task.Delay(TemporaryDirectoryCleanupDelayMilliseconds).ConfigureAwait(false);
                }
            }
        }

        private static void CleanupAbandonedTemporaryDirectoriesOnce(string reportRoot)
        {
            string fullRoot = Path.GetFullPath(reportRoot);
            lock (AbandonedDirectoryCleanupGate)
            {
                if (!CleanedReportRoots.Add(fullRoot) || !Directory.Exists(fullRoot))
                {
                    return;
                }

                try
                {
                    foreach (string directory in Directory.EnumerateDirectories(fullRoot, "*", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(directory);
                        if (!Guid.TryParseExact(name, "N", out _))
                        {
                            continue;
                        }

                        try
                        {
                            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                            {
                                AtomicFileHelper.TryDeleteDirectory(directory);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Crash leftovers are opportunistic cleanup; a locked item must not block PDF rendering.
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The normal per-render cleanup remains authoritative when enumeration is unavailable.
                }
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
