using System.Globalization;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed class ExporterSealService : IExporterSealService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IAppPathProvider _pathProvider;
        private readonly BusinessDataAccessScope _accessScope;

        public ExporterSealService(
            IDbContextFactory<AppDbContext> contextFactory,
            IAppPathProvider pathProvider,
            BusinessDataAccessScope accessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _accessScope = accessScope ?? new BusinessDataAccessScope(new DatabaseConnectionSettings());
        }

        public async Task<Exporter> SaveSealAsync(
            int exporterId,
            ExporterSealKind sealKind,
            string originalFileName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            if (exporterId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exporterId), "出口商ID必须大于0。");
            }

            if (content.IsEmpty || content.Length > ExporterSealFilePolicy.MaximumBytes)
            {
                throw new InvalidDataException("印章图片不能为空且不能超过 5 MB。");
            }

            string imageExtension = DetectImageExtension(content.Span);
            ValidateOriginalExtension(originalFileName, imageExtension);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var exporter = await _accessScope.ApplyExporterScope(context.Exporters)
                .SingleOrDefaultAsync(item => item.Id == exporterId, cancellationToken);
            if (exporter == null)
            {
                throw new KeyNotFoundException("出口商不存在。");
            }

            string sealRoot = GetExporterSealRoot(exporterId, createDirectory: true);
            string prefix = sealKind == ExporterSealKind.Document ? "document" : "customs";
            string managedPath = Path.Combine(sealRoot, $"{prefix}-{Guid.NewGuid():N}{imageExtension}");
            string previousPath = sealKind == ExporterSealKind.Document
                ? exporter.DocSealPath
                : exporter.CustomsSealPath;

            try
            {
                await AtomicFileHelper.WriteFileAtomicAsync(
                    managedPath,
                    (tempPath, token) => File.WriteAllBytesAsync(tempPath, content.ToArray(), token),
                    cancellationToken);

                if (sealKind == ExporterSealKind.Document)
                {
                    exporter.DocSealPath = managedPath;
                }
                else
                {
                    exporter.CustomsSealPath = managedPath;
                }

                await context.SaveChangesAsync(cancellationToken);
                DeleteReplacedManagedSeal(exporterId, previousPath, managedPath);

                return exporter;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                AtomicFileHelper.TryDeleteFile(managedPath);
                throw new InvalidOperationException("该出口商数据已被其他用户修改，请刷新后重试。", ex);
            }
            catch
            {
                AtomicFileHelper.TryDeleteFile(managedPath);
                throw;
            }
        }

        public void DeleteReplacedManagedSeal(
            int exporterId,
            string previousPath,
            string currentPath)
        {
            if (exporterId <= 0 || string.IsNullOrWhiteSpace(previousPath))
            {
                return;
            }

            try
            {
                string sealRoot = GetExporterSealRoot(exporterId, createDirectory: false);
                string fullPreviousPath = Path.GetFullPath(previousPath);
                string fullCurrentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? string.Empty
                    : Path.GetFullPath(currentPath);
                if (!string.Equals(fullPreviousPath, fullCurrentPath, PathBoundaryHelper.PathComparison) &&
                    PathBoundaryHelper.IsWithinRoot(fullPreviousPath, sealRoot))
                {
                    AtomicFileHelper.TryDeleteFile(fullPreviousPath);
                }
            }
            catch (Exception)
            {
                // A legacy external path is never deleted by managed seal replacement.
            }
        }

        public void DeleteAllManagedSeals(int exporterId)
        {
            if (exporterId <= 0)
            {
                return;
            }

            AtomicFileHelper.TryDeleteDirectory(GetExporterSealRoot(exporterId, createDirectory: false));
        }

        private string GetExporterSealRoot(int exporterId, bool createDirectory)
        {
            string root = Path.GetFullPath(Path.Combine(
                _pathProvider.FileRoot,
                "Seals",
                "Exporters",
                exporterId.ToString(CultureInfo.InvariantCulture)));
            if (createDirectory)
            {
                Directory.CreateDirectory(root);
            }

            return root;
        }

        private static void ValidateOriginalExtension(string originalFileName, string detectedExtension)
        {
            string extension = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
            bool matches = detectedExtension switch
            {
                ".png" => extension == ".png",
                ".jpg" => extension is ".jpg" or ".jpeg",
                ".gif" => extension == ".gif",
                ".webp" => extension == ".webp",
                _ => false
            };

            if (!matches)
            {
                throw new InvalidDataException("印章图片扩展名与实际图片格式不一致。");
            }
        }

        private static string DetectImageExtension(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return ".png";
            }

            if (bytes.Length >= 4 &&
                bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF &&
                bytes[^2] == 0xFF && bytes[^1] == 0xD9)
            {
                return ".jpg";
            }

            if (bytes.Length >= 6 &&
                (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
            {
                return ".gif";
            }

            if (bytes.Length >= 12 &&
                bytes[..4].SequenceEqual("RIFF"u8) &&
                bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            {
                return ".webp";
            }

            throw new InvalidDataException("印章图片格式无效；仅支持 PNG、JPEG、GIF 或 WebP。");
        }
    }
}
