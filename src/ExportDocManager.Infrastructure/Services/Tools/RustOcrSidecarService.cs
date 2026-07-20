using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Tools
{
    public sealed class RustOcrSidecarHost : IDisposable, IAsyncDisposable
    {
        public const string ExecutableEnvironmentVariable = "EXPORTDOCMANAGER_RUST_OCR_EXECUTABLE";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly IAppPathProvider _paths;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Process _process;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private int _disposed;

        public RustOcrSidecarHost(IAppPathProvider paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

        public static string FindExecutable(IAppPathProvider paths)
        {
            string configured = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim().Trim('"'));
            string file = OperatingSystem.IsWindows() ? "exportdoc-ocr.exe" : "exportdoc-ocr";
            return new[] { Path.Combine(paths.AppRoot, "sidecar", "ocr", file), Path.Combine(paths.AppRoot, "ocr", file), Path.Combine(paths.AppRoot, file) }.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        public bool IsAvailable(out string executablePath)
        {
            executablePath = ResolveExecutable();
            return !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);
        }

        public async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await EnsureStartedAsync(cancellationToken);
                string id = Guid.NewGuid().ToString("N");
                string request = JsonSerializer.Serialize(new { id, command = "recognize", imagePath }, JsonOptions);
                await _stdin.WriteLineAsync(request.AsMemory(), cancellationToken);
                await _stdin.FlushAsync(cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                string line = await _stdout.ReadLineAsync(timeout.Token) ?? throw new InvalidOperationException("Rust OCR Sidecar已退出且未返回结果。");
                var response = JsonSerializer.Deserialize<RustOcrResponse>(line, JsonOptions) ?? throw new InvalidDataException("Rust OCR Sidecar返回了无效响应。");
                if (!string.Equals(response.Id, id, StringComparison.Ordinal)) throw new InvalidDataException($"Rust OCR Sidecar响应编号不匹配。Expected={id}; Actual={response.Id}; Payload={line}");
                if (!response.Success) throw new InvalidOperationException($"Rust OCR识别失败：{response.Error}");
                return new OcrResult
                {
                    FullText = response.FullText ?? string.Empty,
                    Lines = (response.Lines ?? []).Select(item => new OcrLine { Text = item.Text ?? string.Empty, X = item.X, Y = item.Y, Width = item.Width, Height = item.Height }).ToList()
                };
            }
            catch
            {
                if (_process?.HasExited == true) await StopAsync();
                throw;
            }
            finally { _gate.Release(); }
        }

        private Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            if (_process is { HasExited: false }) return Task.CompletedTask;
            string executable = ResolveExecutable();
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) throw new FileNotFoundException("未找到Rust OCR Sidecar。", executable);
            string requestRoot = Path.Combine(_paths.CacheRoot, "OcrJobs");
            Directory.CreateDirectory(requestRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };
            startInfo.ArgumentList.Add("--model-root"); startInfo.ArgumentList.Add(Path.Combine(_paths.OcrModelRoot, "PaddleOCR", "V6"));
            startInfo.ArgumentList.Add("--allowed-root"); startInfo.ArgumentList.Add(requestRoot);
            string runtime = ResolveOnnxRuntimeLibrary();
            if (!string.IsNullOrWhiteSpace(runtime)) startInfo.Environment["ORT_DYLIB_PATH"] = runtime;
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动Rust OCR Sidecar。");
            _stdin = _process.StandardInput; _stdout = _process.StandardOutput;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private string ResolveExecutable()
        {
            return FindExecutable(_paths);
        }

        private string ResolveOnnxRuntimeLibrary()
        {
            string name = OperatingSystem.IsWindows() ? "onnxruntime.dll" : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";
            return Directory.EnumerateFiles(_paths.AppRoot, name, SearchOption.AllDirectories).OrderBy(path => path.Length).FirstOrDefault() ?? string.Empty;
        }

        private async Task StopAsync()
        {
            try { if (_process is { HasExited: false }) { await _stdin.WriteLineAsync("{\"id\":\"shutdown\",\"command\":\"shutdown\"}"); await _stdin.FlushAsync(); if (!await _process.WaitForExitAsync(TimeSpan.FromSeconds(2))) _process.Kill(true); } } catch { try { _process?.Kill(true); } catch { } }
            _stdin?.Dispose(); _stdout?.Dispose(); _process?.Dispose(); _stdin = null; _stdout = null; _process = null;
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await _gate.WaitAsync();
            try { await StopAsync(); }
            finally { _gate.Release(); _gate.Dispose(); }
        }

        private sealed record RustOcrResponse(string Id, bool Success, string FullText, List<RustOcrLine> Lines, string Error);
        private sealed record RustOcrLine(string Text, float Confidence, int X, int Y, int Width, int Height);
    }

    public sealed class RustOcrService : IOcrService
    {
        private readonly RustOcrSidecarHost _host; private readonly IAppPathProvider _paths;
        public RustOcrService(RustOcrSidecarHost host, IAppPathProvider paths) { _host = host; _paths = paths; }
        public async Task<OcrResult> RecognizeAsync(Stream imageStream)
        {
            string root = Path.Combine(_paths.CacheRoot, "OcrJobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string path = Path.Combine(root, "input-image.bin");
            try { await using (var output = File.Create(path)) await imageStream.CopyToAsync(output); return await _host.RecognizeAsync(path); }
            finally { AtomicFileHelper.TryDeleteDirectory(root); }
        }
    }

    internal static class ProcessWaitExtensions
    {
        public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(cts.Token); return true; } catch (OperationCanceledException) { return false; }
        }
    }
}
