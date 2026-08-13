using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Infrastructure.PdfOcr;

namespace ExportDocManager.Services.Tools;

public sealed class OcrRuntimeDiagnosticContributor : IRuntimeDependencyDiagnosticContributor
{
    private readonly IAppPathProvider _pathProvider;

    public OcrRuntimeDiagnosticContributor(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public IReadOnlyList<RuntimeDependencyDiagnostic> Inspect() => [InspectOcrRuntime()];

    private RuntimeDependencyDiagnostic InspectOcrRuntime()
    {
        string rustExecutable = RustOcrSidecarHost.FindExecutable(_pathProvider);
        string modelRoot = Path.Combine(_pathProvider.OcrModelRoot, "PaddleOCR", "V6");
        if (!OcrRuntimeOptions.IsEnabled())
        {
            return new RuntimeDependencyDiagnostic(
                "ocr-runtime", "智能 OCR", "optional", "disabled", false,
                Path.GetFullPath(modelRoot),
                "OCR 已通过运行配置关闭；不影响其它业务功能。");
        }

        string[] requiredModels =
        [
            Path.Combine(modelRoot, "det", "inference.onnx"),
            Path.Combine(modelRoot, "rec", "inference.onnx"),
            Path.Combine(modelRoot, "rec", "inference.yml")
        ];
        if (!File.Exists(rustExecutable))
        {
            return new RuntimeDependencyDiagnostic(
                "ocr-runtime", "智能 OCR", "optional", "missing", false,
                Path.GetFullPath(Path.Combine(_pathProvider.AppRoot, "sidecar", "ocr")),
                "未找到 Rust PP-OCRv6 Sidecar；OCR 不可用，其它业务功能不受影响。");
        }
        if (requiredModels.Any(path => !File.Exists(path)))
        {
            return new RuntimeDependencyDiagnostic(
                "ocr-runtime", "智能 OCR", "optional", "incomplete", false,
                Path.GetFullPath(modelRoot),
                "Rust OCR Sidecar 已安装，但 PP-OCRv6 模型文件不完整。");
        }

        string onnxRuntime = RustOcrSidecarHost.FindOnnxRuntimeLibrary(_pathProvider);
        if (string.IsNullOrWhiteSpace(onnxRuntime) || !File.Exists(onnxRuntime))
        {
            return new RuntimeDependencyDiagnostic(
                "ocr-runtime", "智能 OCR", "optional", "incomplete", false,
                Path.GetFullPath(_pathProvider.AppRoot),
                "Rust OCR Sidecar 和模型已安装，但缺少 ONNX Runtime 原生库。");
        }

        return new RuntimeDependencyDiagnostic(
            "ocr-runtime", "智能 OCR", "optional", "ready", true,
            Path.GetFullPath(rustExecutable),
            "Rust PP-OCRv6 Sidecar 已就绪；使用 ONNX Runtime 和纯 Rust 图像处理，不依赖 OpenCV。");
    }
}
