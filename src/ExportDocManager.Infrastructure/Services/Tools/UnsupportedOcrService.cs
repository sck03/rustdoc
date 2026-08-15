using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Tools
{
    public sealed class UnsupportedOcrService : IOcrService
    {
        public Task<OcrResult> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new UserVisibleInfrastructureException(
                "当前安装未启用 OCR 能力，请安装或启用 OCR 可选模块后重试。");
        }
    }
}
