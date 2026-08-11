using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class RustOcrSidecarHostTests
{
    [Fact]
    public void RecognitionTimeouts_ShouldSeparateBusinessAndReleaseVerificationBudgets()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), RustOcrSidecarHost.DefaultRecognitionTimeout);
        Assert.Equal(TimeSpan.FromMinutes(3), OcrRuntimeVerifier.RuntimeVerificationTimeout);
        Assert.True(OcrRuntimeVerifier.RuntimeVerificationTimeout > RustOcrSidecarHost.DefaultRecognitionTimeout);
        Assert.True(OcrRuntimeVerifier.RuntimeVerificationTimeout <= RustOcrSidecarHost.MaximumRecognitionTimeout);
    }

    [Fact]
    public async Task RecognizeAsync_ShouldRejectUnboundedInternalTimeout()
    {
        string root = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", Guid.NewGuid().ToString("N"));
        var paths = new RuntimeAppPathProvider(Path.Combine(root, "resources"), Path.Combine(root, "data"));
        await using var host = new RustOcrSidecarHost(paths);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            host.RecognizeAsync(
                Path.Combine(root, "verification.png"),
                RustOcrSidecarHost.MaximumRecognitionTimeout + TimeSpan.FromSeconds(1)));

        Assert.Equal("recognitionTimeout", exception.ParamName);
    }

    [Fact]
    public void FindOnnxRuntimeLibrary_ShouldResolvePackagedSidecarRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", Guid.NewGuid().ToString("N"));
        string appRoot = Path.Combine(root, "resources");
        string dataRoot = Path.Combine(root, "data");
        string libraryName = OperatingSystem.IsWindows()
            ? "onnxruntime.dll"
            : OperatingSystem.IsMacOS()
                ? "libonnxruntime.dylib"
                : "libonnxruntime.so";
        string expected = Path.Combine(appRoot, "sidecar", libraryName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllBytes(expected, [0x01]);
            var paths = new RuntimeAppPathProvider(appRoot, dataRoot);

            Assert.Equal(expected, RustOcrSidecarHost.FindOnnxRuntimeLibrary(paths));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
