using ExportDocManager.Services.Core;
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
            byte[] bytes = DecodePngDataUrl(imageDataUrl);
            string marksRoot = GetMarksRoot();
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
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("唛头图片不存在。", fullPath);
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaxImageBytes)
            {
                throw new InvalidDataException("唛头图片大小无效或超过限制。");
            }

            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            string contentType = DetectImageContentType(bytes, fullPath);
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
            Directory.CreateDirectory(root);
            RuntimeFilePermissionHelper.RestrictDirectory(root);
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

        private static byte[] DecodePngDataUrl(string imageDataUrl)
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

            string header = trimmed[..commaIndex].Trim().ToLowerInvariant();
            if (!header.StartsWith("data:image/png", StringComparison.Ordinal) ||
                !header.Contains(";base64", StringComparison.Ordinal))
            {
                throw new FormatException("唛头图片仅支持 PNG data URL。");
            }

            string base64 = trimmed[(commaIndex + 1)..].Trim();
            if (base64.Length == 0 || base64.Length > MaxImageBytes * 2)
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

            if (bytes.Length == 0 || bytes.Length > MaxImageBytes || !IsPng(bytes))
            {
                throw new FormatException("唛头图片必须是有效 PNG，且不能超过 5 MB。");
            }

            return bytes;
        }

        private static string DetectImageContentType(byte[] bytes, string imagePath)
        {
            if (IsPng(bytes))
            {
                return "image/png";
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }

            string extension = Path.GetExtension(imagePath);
            if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return "image/jpeg";
            }

            throw new FormatException("唛头图片仅支持 PNG 或 JPEG 预览。");
        }

        private static bool IsPng(byte[] bytes)
        {
            return bytes.Length >= 8 &&
                   bytes[0] == 0x89 &&
                   bytes[1] == 0x50 &&
                   bytes[2] == 0x4E &&
                   bytes[3] == 0x47 &&
                   bytes[4] == 0x0D &&
                   bytes[5] == 0x0A &&
                   bytes[6] == 0x1A &&
                   bytes[7] == 0x0A;
        }

    }
}
