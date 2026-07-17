using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Tools
{
    public sealed class UnsupportedOcrService : IOcrService
    {
        private readonly IAppPathProvider _pathProvider;

        public UnsupportedOcrService(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public Task<OcrResult> RecognizeAsync(Stream imageStream)
        {
            throw new InvalidOperationException(
                $"当前 sidecar 未启用 OCR 运行时。OCR 模型仍应随程序放在 OcrModels/ 下，当前程序根 OCR 目录为：{_pathProvider.OcrModelRoot}。请启用 OCR 可选运行包后再导入扫描图片或扫描版 PDF。");
        }
    }
}
