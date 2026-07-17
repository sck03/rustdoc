using ExportDocManager.Services.Infrastructure;

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

            if (!OperatingSystem.IsWindows())
            {
                return new OcrRuntimeAvailability(
                    false,
                    "unsupported",
                    false,
                    modelBasePath,
                    "当前平台尚未提供经过验收的 OCR 原生运行包；程序不会静默调用系统 OCR。");
            }

            try
            {
                var bundle = new PaddleOcrOnnxModelBundle(modelBasePath);
                bundle.EnsureModelBundle();
                _ = bundle.LoadRecognitionLabels();

                return new OcrRuntimeAvailability(
                    true,
                    "ready",
                    true,
                    modelBasePath,
                    "OCR 模型和识别字典已就绪。");
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
        }
    }
}
