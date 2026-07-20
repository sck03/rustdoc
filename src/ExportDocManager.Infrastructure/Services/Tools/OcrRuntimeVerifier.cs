using System.Runtime.InteropServices;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Tools
{
    public sealed record OcrRuntimeVerificationResult(
        string Platform,
        string Engine,
        string RecognizedText);

    public static class OcrRuntimeVerifier
    {
        private const string VerificationImageBase64 = "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";

        public static async Task<OcrRuntimeVerificationResult> VerifyAsync(
            IAppPathProvider pathProvider,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            await using var host = new RustOcrSidecarHost(pathProvider);
            if (!host.IsAvailable(out _))
            {
                throw new InvalidOperationException("未找到Rust PP-OCRv6 Sidecar，无法执行跨平台OCR发布验证。");
            }

            string jobRoot = Path.Combine(pathProvider.CacheRoot, "OcrJobs", $"verify-{Guid.NewGuid():N}");
            string imagePath = Path.Combine(jobRoot, "verification.png");
            Directory.CreateDirectory(jobRoot);
            try
            {
                await File.WriteAllBytesAsync(
                    imagePath,
                    Convert.FromBase64String(VerificationImageBase64),
                    cancellationToken);
                OcrResult result = await host.RecognizeAsync(imagePath, cancellationToken);
                string text = result.FullText.Trim();
                if (!text.Contains("EXPORT DOC 2026", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Rust PP-OCRv6运行验证结果不符合预期：'{text}'。");
                }

                return new OcrRuntimeVerificationResult(
                    $"{RuntimeInformation.RuntimeIdentifier}/{RuntimeInformation.ProcessArchitecture}",
                    "rust-ort-ppocrv6",
                    text);
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(jobRoot);
            }
        }
    }
}
