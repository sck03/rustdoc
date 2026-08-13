namespace ExportDocManager.Services.Infrastructure;

public sealed record OcrRuntimeVerificationResult(
    string Platform,
    string Engine,
    string RecognizedText);

public interface IOcrRuntimeVerifier
{
    Task<OcrRuntimeVerificationResult> VerifyAsync(
        CancellationToken cancellationToken = default);
}
