using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Core
{
    public sealed class ShippingMarkImageService : IShippingMarkImageService
    {
        private const int MaxImageBytes = 5 * 1024 * 1024;
        private const string StoragePolicy =
            "唛头图片只保存到运行数据根 Marks 目录，发票记录仅保存图片路径；预览也只读取该目录内图片，不读取付款/报销单据、不创建默认导出目录或系统盘默认落点。";

        private readonly IAppPathProvider _pathProvider;
        private readonly IBusinessClock _clock;

        public ShippingMarkImageService(IAppPathProvider pathProvider, IBusinessClock? clock = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<ShippingMarkImageSaveResult> SavePngDataUrlAsync(
            string imageDataUrl,
            CancellationToken cancellationToken = default)
        {
            byte[] bytes = DecodePngDataUrl(imageDataUrl, cancellationToken);
            string marksRoot = GetMarksRoot();
            Directory.CreateDirectory(marksRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(marksRoot);
            string fileName = $"Mark_{_clock.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.png";
            string imagePath = Path.Combine(marksRoot, fileName);

            await AtomicFileHelper.WriteFileAtomicAsync(
                imagePath,
                (tempPath, token) => File.WriteAllBytesAsync(tempPath, bytes, token),
                cancellationToken);
            RuntimeFilePermissionHelper.RestrictFile(imagePath);
            string storedPath = ManagedDataPathResolver.ToStoredPath(
                _pathProvider,
                imagePath,
                marksRoot,
                "Marks");

            return new ShippingMarkImageSaveResult(
                storedPath,
                fileName,
                "image/png",
                bytes.LongLength,
                StoragePolicy);
        }

        public async Task<ShippingMarkImagePreviewResult> ReadImageAsDataUrlAsync(
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            string fullPath = ResolveMarksImagePath(imagePath);
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > MaxImageBytes)
            {
                throw new UserVisibleInfrastructureException("唛头图片大小无效或超过 5 MB，请重新编辑并保存唛头图片。");
            }
            byte[] bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            string? contentType = InspectImage(bytes, cancellationToken)?.MediaType;
            if (contentType is not ("image/png" or "image/jpeg"))
                throw new UserVisibleInfrastructureException("唛头图片已损坏或不是有效的 PNG/JPEG，请重新编辑并保存唛头图片。");
            string dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
            string storedPath = ManagedDataPathResolver.ToStoredPath(
                _pathProvider,
                fullPath,
                GetMarksRoot(),
                "Marks");

            return new ShippingMarkImagePreviewResult(
                storedPath,
                Path.GetFileName(fullPath),
                contentType,
                bytes.LongLength,
                dataUrl,
                StoragePolicy);
        }

        private string GetMarksRoot()
        {
            string root = Path.GetFullPath(Path.Combine(_pathProvider.DataRoot, "Marks"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(root, _pathProvider.DataRoot,
                "唛头图片目录不能包含符号链接或重解析点。");
            return root;
        }

        private string ResolveMarksImagePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("唛头图片路径不能为空。", nameof(imagePath));
            }

            string marksRoot = GetMarksRoot();
            return ManagedDataPathResolver.ResolveStoredPath(
                _pathProvider,
                imagePath,
                marksRoot,
                "Marks");
        }

        private static byte[] DecodePngDataUrl(string imageDataUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imageDataUrl))
            {
                throw new ArgumentException("唛头图片内容不能为空。", nameof(imageDataUrl));
            }

            string trimmed = imageDataUrl.Trim();
            int commaIndex = trimmed.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex <= 0)
            {
                throw new FormatException("唛头图片必须是 PNG data URL。");
            }

            if (!string.Equals(trimmed[..commaIndex], "data:image/png;base64", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("唛头图片仅支持 PNG data URL。");
            }

            string base64 = trimmed[(commaIndex + 1)..].Trim();
            if (base64.Length == 0 || base64.Length > (MaxImageBytes + 2) / 3 * 4)
            {
                throw new FormatException("唛头图片内容大小无效。");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new FormatException("唛头图片 Base64 内容无效。", ex);
            }

            if (bytes.Length == 0 || bytes.Length > MaxImageBytes || InspectImage(bytes, cancellationToken)?.MediaType != "image/png")
            {
                throw new FormatException("唛头图片必须是有效 PNG，且不能超过 5 MB。");
            }

            return bytes;
        }

        private static RasterImageInspector.ImageInfo? InspectImage(byte[] bytes, CancellationToken cancellationToken) =>
            RasterImageInspector.Inspect(bytes, 8192, 32_000_000, cancellationToken);
    }
}
