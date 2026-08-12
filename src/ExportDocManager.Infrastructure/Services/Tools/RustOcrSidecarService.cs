using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Tools
{
    public sealed class RustOcrSidecarHost : IDisposable, IAsyncDisposable
    {
        public const string ExecutableEnvironmentVariable = "EXPORTDOCMANAGER_RUST_OCR_EXECUTABLE";
        internal const int MaximumResponseCharacters = 4 * 1024 * 1024;
        internal static readonly TimeSpan DefaultRecognitionTimeout = TimeSpan.FromSeconds(90);
        internal static readonly TimeSpan MaximumRecognitionTimeout = TimeSpan.FromMinutes(5);
        private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;
        private readonly IAppPathProvider _paths;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _shutdownSource = new();
        private static readonly TimeSpan QueueWaitLimit = TimeSpan.FromSeconds(30);
        private Process? _process;
        private StreamWriter? _stdin;
        private BoundedTextLineReader? _stdout;
        private BoundedTextCollector? _stderr;
        private int _disposed;

        public RustOcrSidecarHost(IAppPathProvider paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

        public static string FindExecutable(IAppPathProvider paths)
        {
            string? configured = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim().Trim('"'));
            string file = OperatingSystem.IsWindows() ? "exportdoc-ocr.exe" : "exportdoc-ocr";
            return new[] { Path.Combine(paths.AppRoot, "sidecar", "ocr", file), Path.Combine(paths.AppRoot, "ocr", file), Path.Combine(paths.AppRoot, file) }.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        public bool IsAvailable(out string executablePath)
        {
            executablePath = ResolveExecutable();
            return !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);
        }

        public Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default) =>
            RecognizeAsync(imagePath, DefaultRecognitionTimeout, cancellationToken);

        internal async Task<OcrResult> RecognizeAsync(
            string imagePath,
            TimeSpan recognitionTimeout,
            CancellationToken cancellationToken = default)
        {
            if (recognitionTimeout <= TimeSpan.Zero || recognitionTimeout > MaximumRecognitionTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(recognitionTimeout),
                    recognitionTimeout,
                    $"OCR recognition timeout must be greater than zero and no longer than {MaximumRecognitionTimeout.TotalSeconds:0} seconds.");
            }

            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var queueTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdownSource.Token);
            queueTimeout.CancelAfter(QueueWaitLimit);
            try
            {
                await _gate.WaitAsync(queueTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (_shutdownSource.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(nameof(RustOcrSidecarHost));
                }
                throw new ServiceBusyException("OCR 当前任务较多，请稍后重试。");
            }

            try
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref _disposed) != 0 || _shutdownSource.IsCancellationRequested,
                    this);
                for (int attempt = 0; ; attempt++)
                {
                    using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _shutdownSource.Token);
                    operationTimeout.CancelAfter(recognitionTimeout);
                    try
                    {
                        return await RecognizeCoreAsync(imagePath, operationTimeout.Token);
                    }
                    catch (OperationCanceledException) when (
                        _shutdownSource.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested)
                    {
                        await StopAsync();
                        throw new ObjectDisposedException(
                            nameof(RustOcrSidecarHost),
                            "Rust OCR Sidecar 正在停止，当前识别已取消。");
                    }
                    catch (OperationCanceledException) when (
                        !cancellationToken.IsCancellationRequested &&
                        operationTimeout.IsCancellationRequested)
                    {
                        await StopAsync();
                        throw new ServiceTimeoutException(
                            $"OCR 识别超过 {Math.Ceiling(recognitionTimeout.TotalSeconds):0} 秒，任务已终止，请缩小图片后重试。");
                    }
                    catch (Exception ex) when (attempt == 0 && IsRecoverableTransportFailure(ex, cancellationToken))
                    {
                        await StopAsync();
                    }
                    catch (Exception ex)
                    {
                        string stderr = _stderr?.GetText() ?? string.Empty;
                        await StopAsync();
                        if (IsTransportFailure(ex))
                        {
                            string detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" Sidecar 诊断：{stderr}";
                            throw new InfrastructureServiceException(
                                $"Rust OCR Sidecar 通信失败或返回了无效响应。{detail}",
                                ex);
                        }

                        throw;
                    }
                }
            }
            finally { _gate.Release(); }
        }

        private async Task<OcrResult> RecognizeCoreAsync(string imagePath, CancellationToken cancellationToken)
        {
            await EnsureStartedAsync(cancellationToken);
            var stdin = _stdin ?? throw new EndOfStreamException("Rust OCR Sidecar 标准输入不可用。");
            var stdout = _stdout ?? throw new EndOfStreamException("Rust OCR Sidecar 标准输出不可用。");
            string id = Guid.NewGuid().ToString("N");
            string request = JsonSerializer.Serialize(new { id, command = "recognize", imagePath }, JsonOptions);
            await stdin.WriteLineAsync(request.AsMemory(), cancellationToken);
            await stdin.FlushAsync(cancellationToken);
            string line = await stdout.ReadLineAsync(MaximumResponseCharacters, cancellationToken)
                ?? throw new EndOfStreamException("Rust OCR Sidecar已退出且未返回结果。");
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
            !callerToken.IsCancellationRequested && IsTransportFailure(exception);

        private static bool IsTransportFailure(Exception exception) =>
            exception is IOException or JsonException or InvalidDataException or
                Win32Exception or DllNotFoundException or BadImageFormatException;

        private static string TrimSidecarError(string? error)
        {
            const int maximumErrorCharacters = 1024;
            string normalized = string.IsNullOrWhiteSpace(error) ? "未知错误" : error.Trim();
            return normalized.Length <= maximumErrorCharacters
                ? normalized
                : normalized[..maximumErrorCharacters] + "…";
        }

        private Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0 || _shutdownSource.IsCancellationRequested,
                this);
            if (_process is { HasExited: false }) return Task.CompletedTask;
            string executable = ResolveExecutable();
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                throw new InfrastructureServiceException("未找到 Rust OCR Sidecar，请安装随程序提供的 OCR 可选运行包。");
            }
            string requestRoot = Path.Combine(_paths.CacheRoot, "OcrJobs");
            Directory.CreateDirectory(requestRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };
            startInfo.ArgumentList.Add("--model-root"); startInfo.ArgumentList.Add(Path.Combine(_paths.OcrModelRoot, "PaddleOCR", "V6"));
            startInfo.ArgumentList.Add("--allowed-root"); startInfo.ArgumentList.Add(requestRoot);
            string runtime = FindOnnxRuntimeLibrary(_paths);
            if (string.IsNullOrWhiteSpace(runtime))
            {
                throw new InfrastructureServiceException(
                    "未找到 Rust OCR 所需的 ONNX Runtime 原生库，请重新安装完整 OCR 运行包。");
            }
            startInfo.Environment["ORT_DYLIB_PATH"] = runtime;
            try
            {
                _process = Process.Start(startInfo) ?? throw new InfrastructureServiceException("无法启动 Rust OCR Sidecar。");
                _stdin = _process.StandardInput;
                _stdout = new BoundedTextLineReader(_process.StandardOutput);
                _stderr = new BoundedTextCollector(16 * 1024);
                _process.ErrorDataReceived += (_, args) => _stderr?.AppendLine(args.Data);
                _process.BeginErrorReadLine();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ex is not InfrastructureServiceException and not OperationCanceledException)
            {
                throw new InfrastructureServiceException("无法启动 Rust OCR Sidecar，运行包或本机原生依赖不可用。", ex);
            }
        }

        private string ResolveExecutable()
        {
            return FindExecutable(_paths);
        }

        internal static string FindOnnxRuntimeLibrary(IAppPathProvider paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            string name = OperatingSystem.IsWindows() ? "onnxruntime.dll" : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";
            // Keep startup deterministic and avoid walking a potentially large
            // installation tree on every sidecar restart. Packaging places the
            // native runtime in one of these bounded locations.
            foreach (string candidate in new[]
            {
                Path.Combine(paths.AppRoot, name),
                Path.Combine(paths.AppRoot, "sidecar", name),
                Path.Combine(paths.AppRoot, "sidecar", "ocr", name),
                Path.Combine(paths.AppRoot, "runtimes", name),
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
            Process? process = _process;
            StreamWriter? stdin = _stdin;
            try
            {
                if (process is { HasExited: false } && stdin != null)
                {
                    using var gracefulTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await stdin.WriteLineAsync(
                        "{\"id\":\"shutdown\",\"command\":\"shutdown\"}".AsMemory(),
                        gracefulTimeout.Token);
                    await stdin.FlushAsync(gracefulTimeout.Token);
                    if (!await process.WaitForExitAsync(TimeSpan.FromSeconds(2)))
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(TimeSpan.FromSeconds(5));
                    }
                }
            }
            catch
            {
                try
                {
                    if (process is { HasExited: false })
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(TimeSpan.FromSeconds(5));
                    }
                }
                catch
                {
                }
            }
            finally
            {
                _stdin?.Dispose();
                _stdout?.Dispose();
                process?.Dispose();
                _stdin = null;
                _stdout = null;
                _stderr = null;
                _process = null;
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _shutdownSource.Cancel();
            await _gate.WaitAsync();
            try { await StopAsync(); }
            finally
            {
                _gate.Release();
                _shutdownSource.Dispose();
                _gate.Dispose();
            }
        }

        private sealed record RustOcrResponse(string Id, bool Success, string FullText, List<RustOcrLine> Lines, string Error);
        private sealed record RustOcrLine(string Text, float Confidence, int X, int Y, int Width, int Height);
        private sealed class RustOcrRecognitionException(string error) : InfrastructureServiceException($"Rust OCR 识别失败：{error}");
    }

    internal sealed class BoundedTextCollector
    {
        private readonly int _maximumCharacters;
        private readonly StringBuilder _buffer = new();
        private readonly object _sync = new();

        public BoundedTextCollector(int maximumCharacters)
        {
            _maximumCharacters = maximumCharacters > 0
                ? maximumCharacters
                : throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        public void AppendLine(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            lock (_sync)
            {
                if (_buffer.Length >= _maximumCharacters)
                {
                    return;
                }

                if (_buffer.Length > 0)
                {
                    if (_maximumCharacters - _buffer.Length < 3)
                    {
                        return;
                    }
                    _buffer.Append(" | ");
                }

                int remaining = _maximumCharacters - _buffer.Length;
                string normalized = value.Trim();
                _buffer.Append(normalized.AsSpan(0, Math.Min(normalized.Length, remaining)));
            }
        }

        public string GetText()
        {
            lock (_sync)
            {
                return _buffer.ToString();
            }
        }
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

        public async Task<string?> ReadLineAsync(
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
