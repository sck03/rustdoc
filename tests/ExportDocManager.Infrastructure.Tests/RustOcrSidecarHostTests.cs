using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class RustOcrSidecarHostTests
{
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
