using ExportDocManager.Services.Infrastructure;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace ExportDocManager.Services.Tools
{
    public sealed record OcrRuntimeAvailability(
        bool UsePaddleOcr,
        string Status,
        bool Ready,
        string ModelBasePath,
        string Message);

    public static class OcrRuntimeAvailabilityInspector
    {
        public const string RuntimeEnvironmentVariable = "EXPORTDOCMANAGER_OCR_RUNTIME";

        public static OcrRuntimeAvailability Inspect(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string modelBasePath = Path.Combine(pathProvider.OcrModelRoot, "PaddleOCR", "V6");
            string mode = (Environment.GetEnvironmentVariable(RuntimeEnvironmentVariable) ?? "auto")
                .Trim()
                .ToLowerInvariant();
            bool explicitlyEnabled = mode is "1" or "true" or "enabled" or "paddle" or "paddleocr";
            bool explicitlyDisabled = mode is "0" or "false" or "disabled" or "off" or "none" or "unsupported";

            if (explicitlyDisabled)
            {
                return new OcrRuntimeAvailability(
                    false,
                    "disabled",
                    false,
                    modelBasePath,
                    "OCR 已通过运行配置关闭；不影响其它业务功能。");
            }

            if (!IsSupportedPlatform())
            {
                return new OcrRuntimeAvailability(
                    false,
                    "unsupported",
                    false,
                    modelBasePath,
                    "当前平台或处理器架构尚未提供经过验收的 OCR 原生运行包；当前支持 Windows 和 Linux x64。");
            }

            try
            {
                var bundle = new PaddleOcrOnnxModelBundle(modelBasePath);
                bundle.EnsureModelBundle();
                _ = bundle.LoadRecognitionLabels();
                _ = Cv2.GetVersionString();
                using var sessionOptions = new SessionOptions();

                return new OcrRuntimeAvailability(
                    true,
                    "ready",
                    true,
                    modelBasePath,
                    OperatingSystem.IsLinux()
                        ? "PP-OCRv6 模型、ONNX Runtime 与 Linux x64 OpenCV 原生运行库已就绪。"
                        : "PP-OCRv6 模型、ONNX Runtime 与 OpenCV 原生运行库已就绪。");
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException || ex is FileNotFoundException || ex is InvalidDataException)
            {
                return new OcrRuntimeAvailability(
                    false,
                    explicitlyEnabled ? "incomplete" : "missing",
                    false,
                    modelBasePath,
                    explicitlyEnabled
                        ? "OCR 已启用，但模型文件不完整或识别字典无效。"
                        : "未安装完整 OCR 模型；不使用 OCR 时可忽略。");
            }
            catch (Exception ex)
            {
                return new OcrRuntimeAvailability(
                    false,
                    "runtime-error",
                    false,
                    modelBasePath,
                    $"OCR 模型存在，但原生运行库无法加载：{ex.GetType().Name}: {ex.Message}");
            }
        }

        public static bool IsSupportedPlatform() =>
            (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64) ||
            (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
    }
}
