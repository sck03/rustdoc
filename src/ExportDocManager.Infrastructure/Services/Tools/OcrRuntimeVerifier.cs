using ExportDocManager.Services.Infrastructure;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace ExportDocManager.Services.Tools
{
    public sealed record OcrRuntimeVerificationResult(
        string Platform,
        string OpenCvVersion,
        string RecognizedText);

    public static class OcrRuntimeVerifier
    {
        public static async Task<OcrRuntimeVerificationResult> VerifyAsync(
            IAppPathProvider pathProvider,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            var availability = OcrRuntimeAvailabilityInspector.Inspect(pathProvider);
            if (!availability.Ready)
            {
                throw new InvalidOperationException(availability.Message);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var image = new Mat(new Size(1280, 320), MatType.CV_8UC3, Scalar.White);
            Cv2.PutText(
                image,
                "EXPORT DOC 2026",
                new Point(45, 205),
                HersheyFonts.HersheySimplex,
                3.0,
                Scalar.Black,
                7,
                LineTypes.AntiAlias);
            Cv2.ImEncode(".png", image, out byte[] encoded);
            await using var stream = new MemoryStream(encoded, writable: false);
            var result = await new PaddleOcrService(pathProvider).RecognizeAsync(stream).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result.FullText))
            {
                throw new InvalidOperationException("PP-OCRv6 运行验证未识别出测试文本。");
            }

            return new OcrRuntimeVerificationResult(
                $"{RuntimeInformation.RuntimeIdentifier}/{RuntimeInformation.ProcessArchitecture}",
                Cv2.GetVersionString(),
                result.FullText.Trim());
        }
    }
}
