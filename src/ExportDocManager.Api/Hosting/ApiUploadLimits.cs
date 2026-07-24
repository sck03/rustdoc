using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiUploadLimits
    {
        public const long CrmImportBytes = 10L * 1024L * 1024L;
        public const long SupplierImportBytes = 10L * 1024L * 1024L;
        public const long ExcelImportBytes = 25L * 1024L * 1024L;
        public const long PackageImportBytes = 50L * 1024L * 1024L;
        public const long PdfMergeBytes = 100L * 1024L * 1024L;
        public const long MaximumRequestBodyBytes = 128L * 1024L * 1024L;

        public static Task<long> CopyRequestBodyAsync(
            HttpRequest request,
            Stream destination,
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ContentLength is > 0 && request.ContentLength.Value > maximumBytes)
            {
                throw new PayloadLimitExceededException(maximumBytes);
            }

            return BoundedStreamHelper.CopyToAsync(
                request.Body,
                destination,
                maximumBytes,
                cancellationToken);
        }

        public static async Task<long> CopyFormFileAsync(
            IFormFile file,
            Stream destination,
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            if (file.Length > maximumBytes)
            {
                throw new PayloadLimitExceededException(maximumBytes);
            }

            await using var source = file.OpenReadStream();
            return await BoundedStreamHelper.CopyToAsync(
                source,
                destination,
                maximumBytes,
                cancellationToken);
        }
    }
}
