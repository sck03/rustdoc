namespace ExportDocManager.Services.Core
{
    public interface IShippingMarkImageService
    {
        Task<ShippingMarkImageSaveResult> SavePngDataUrlAsync(string imageDataUrl, CancellationToken cancellationToken = default);

        Task<ShippingMarkImagePreviewResult> ReadImageAsDataUrlAsync(string imagePath, CancellationToken cancellationToken = default);
    }

    public sealed record ShippingMarkImageSaveResult(
        string ImagePath,
        string FileName,
        string ContentType,
        long SizeBytes,
        string StoragePolicy);

    public sealed record ShippingMarkImagePreviewResult(
        string ImagePath,
        string FileName,
        string ContentType,
        long SizeBytes,
        string DataUrl,
        string StoragePolicy);
}
