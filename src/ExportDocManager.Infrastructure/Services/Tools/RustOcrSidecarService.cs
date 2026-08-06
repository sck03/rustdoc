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
        internal const int MaximumResponseCharacters = 4 * 1024 * 1024;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly IAppPathProvider _paths;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private static readonly TimeSpan QueueWaitLimit = TimeSpan.FromSeconds(30);
        private Process _process;
        private StreamWriter _stdin;
        private BoundedTextLineReader _stdout;
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
            using var queueTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queueTimeout.CancelAfter(QueueWaitLimit);
            try
            {
                await _gate.WaitAsync(queueTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("OCR 当前任务较多，请稍后重试。");
            }

            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await RecognizeCoreAsync(imagePath, cancellationToken);
                    }
                    catch (Exception ex) when (attempt == 0 && IsRecoverableTransportFailure(ex, cancellationToken))
                    {
                        await StopAsync();
                    }
                    catch
                    {
                        await StopAsync();
                        throw;
                    }
                }
            }
            finally { _gate.Release(); }
        }

        private async Task<OcrResult> RecognizeCoreAsync(string imagePath, CancellationToken cancellationToken)
        {
            await EnsureStartedAsync(cancellationToken);
            string id = Guid.NewGuid().ToString("N");
            string request = JsonSerializer.Serialize(new { id, command = "recognize", imagePath }, JsonOptions);
            await _stdin.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            string line;
            try
            {
                line = await _stdout.ReadLineAsync(MaximumResponseCharacters, timeout.Token)
                    ?? throw new EndOfStreamException("Rust OCR Sidecar已退出且未返回结果。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("OCR 识别超过 90 秒，任务已终止，请缩小图片后重试。");
            }
            var response = JsonSerializer.Deserialize<RustOcrResponse>(line, JsonOptions) ?? throw new InvalidDataException("Rust OCR Sidecar返回了无效响应。");
            if (!string.Equals(response.Id, id, StringComparison.Ordinal)) throw new InvalidDataException("Rust OCR Sidecar响应编号不匹配。");
            if (!response.Success) throw new RustOcrRecognitionException(TrimSidecarError(response.Error));
            return new OcrResult
            {
                FullText = response.FullText ?? string.Empty,
                Lines = (response.Lines ?? []).Select(item => new OcrLine { Text = item.Text ?? string.Empty, X = item.X, Y = item.Y, Width = item.Width, Height = item.Height }).ToList()
            };
        }

        private static bool IsRecoverableTransportFailure(Exception exception, CancellationToken callerToken) =>
            !callerToken.IsCancellationRequested && exception is IOException or JsonException or InvalidDataException;

        private static string TrimSidecarError(string error)
        {
            const int maximumErrorCharacters = 1024;
            string normalized = string.IsNullOrWhiteSpace(error) ? "未知错误" : error.Trim();
            return normalized.Length <= maximumErrorCharacters
                ? normalized
                : normalized[..maximumErrorCharacters] + "…";
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
            _stdin = _process.StandardInput;
            _stdout = new BoundedTextLineReader(_process.StandardOutput);
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
            // Keep startup deterministic and avoid walking a potentially large
            // installation tree on every sidecar restart. Packaging places the
            // native runtime in one of these bounded locations.
            foreach (string candidate in new[]
            {
                Path.Combine(_paths.AppRoot, name),
                Path.Combine(_paths.AppRoot, "sidecar", "ocr", name),
                Path.Combine(_paths.AppRoot, "runtimes", name),
            })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
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
        private sealed class RustOcrRecognitionException(string error) : InvalidOperationException($"Rust OCR识别失败：{error}");
    }

    internal sealed class BoundedTextLineReader : IDisposable
    {
        private const int BufferSize = 4096;
        private readonly TextReader _reader;
        private readonly char[] _buffer = new char[BufferSize];
        private int _bufferOffset;
        private int _bufferLength;

        public BoundedTextLineReader(TextReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<string> ReadLineAsync(
            int maximumCharacters,
            CancellationToken cancellationToken = default)
        {
            if (maximumCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
            }

            var line = new StringBuilder(Math.Min(BufferSize, maximumCharacters));
            while (true)
            {
                if (_bufferOffset >= _bufferLength)
                {
                    _bufferLength = await _reader
                        .ReadAsync(_buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    _bufferOffset = 0;
                    if (_bufferLength == 0)
                    {
                        return line.Length == 0 ? null : TrimLineEnding(line);
                    }
                }

                int newlineIndex = Array.IndexOf(
                    _buffer,
                    '\n',
                    _bufferOffset,
                    _bufferLength - _bufferOffset);
                int segmentEnd = newlineIndex >= 0 ? newlineIndex : _bufferLength;
                int segmentLength = segmentEnd - _bufferOffset;
                if (segmentLength > maximumCharacters - line.Length)
                {
                    throw new InvalidDataException(
                        $"Rust OCR Sidecar响应超过 {maximumCharacters} 字符上限。");
                }

                line.Append(_buffer, _bufferOffset, segmentLength);
                _bufferOffset = newlineIndex >= 0 ? newlineIndex + 1 : _bufferLength;
                if (newlineIndex >= 0)
                {
                    return TrimLineEnding(line);
                }
            }
        }

        private static string TrimLineEnding(StringBuilder line)
        {
            if (line.Length > 0 && line[^1] == '\r')
            {
                line.Length--;
            }
            return line.ToString();
        }

        public void Dispose() => _reader.Dispose();
    }

    public sealed class RustOcrService : IOcrService
    {
        private readonly RustOcrSidecarHost _host; private readonly IAppPathProvider _paths;
        public RustOcrService(RustOcrSidecarHost host, IAppPathProvider paths) { _host = host; _paths = paths; }
        public async Task<OcrResult> RecognizeAsync(
            Stream imageStream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(imageStream);
            string root = Path.Combine(_paths.CacheRoot, "OcrJobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string path = Path.Combine(root, "input-image.bin");
            try
            {
                await using (var output = File.Create(path))
                {
                    await BoundedStreamHelper.CopyToAsync(
                        imageStream,
                        output,
                        OcrInputLimits.MaximumImageBytes,
                        cancellationToken);
                }

                return await _host.RecognizeAsync(path, cancellationToken);
            }
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
