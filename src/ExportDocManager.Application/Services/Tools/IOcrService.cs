namespace ExportDocManager.Services.Tools
{
    public interface IOcrService
    {
        /// <summary>
        /// Recognizes text from an image stream.
        /// 从图片流中识别文本。
        /// </summary>
        /// <param name="imageStream">The image stream.</param>
        /// <returns>OCR result containing text and structure.</returns>
        Task<OcrResult> RecognizeAsync(Stream imageStream);
    }
}
