using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        public async Task<SwClientProfile> GetDefaultAsync(CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var profile = await context.SwClientProfiles
                .AsNoTracking()
                .Where(item => item.IsEnabled)
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return profile ?? new SwClientProfile
            {
                ProfileName = DefaultProfileName,
                ImportRootPath = GetBuiltInClientRoot(null),
                ReceiptRootPath = GetBuiltInClientRoot(null)
            };
        }

        public async Task<int> SaveDefaultAsync(
            string importRootPath,
            string receiptRootPath = "",
            SingleWindowBusinessType? businessType = null,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var profile = await context.SwClientProfiles
                .FirstOrDefaultAsync(item => item.ProfileName == DefaultProfileName, cancellationToken);

            profile ??= new SwClientProfile
            {
                ProfileName = DefaultProfileName,
                ImportRootPath = GetBuiltInClientRoot(null),
                ReceiptRootPath = GetBuiltInClientRoot(null)
            };

            profile.MachineName = Environment.MachineName;
            if (businessType.HasValue)
            {
                SingleWindowClientProfilePathResolver.UpdateBusinessOverride(
                    profile,
                    businessType.Value,
                    importRootPath,
                    receiptRootPath);
                EnsureClientFolderStructure(importRootPath);
                EnsureClientFolderStructure(receiptRootPath);
            }
            else
            {
                profile.ImportRootPath = ResolveUpdatedPath(profile.ImportRootPath, importRootPath);
                profile.ReceiptRootPath = ResolveUpdatedPath(profile.ReceiptRootPath, receiptRootPath);
                EnsureClientFolderStructure(profile.ImportRootPath);
                EnsureClientFolderStructure(profile.ReceiptRootPath);
            }

            profile.IsEnabled = true;
            profile.UpdatedAt = DateTime.Now;

            if (profile.Id <= 0)
            {
                await context.SwClientProfiles.AddAsync(profile, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            return profile.Id;
        }

        private static string ResolveUpdatedPath(string currentValue, string newValue)
        {
            return string.IsNullOrWhiteSpace(newValue)
                ? currentValue?.Trim() ?? string.Empty
                : newValue.Trim();
        }

        private string GetBuiltInClientRoot(SingleWindowBusinessType? businessType)
        {
            return SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                _pathProvider.SingleWindowRoot,
                businessType);
        }

        private static void EnsureClientFolderStructure(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            string normalizedRoot = NormalizeClientRootPath(rootPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(normalizedRoot);
                Directory.CreateDirectory(Path.Combine(normalizedRoot, "OutBox"));
                Directory.CreateDirectory(Path.Combine(normalizedRoot, "SentBox"));
                Directory.CreateDirectory(Path.Combine(normalizedRoot, "InBox"));
                Directory.CreateDirectory(Path.Combine(normalizedRoot, "FailBox"));
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException ||
                ex is IOException ||
                ex is NotSupportedException ||
                ex is ArgumentException)
            {
                Serilog.Log.Warning(ex, "创建单一窗口客户端目录骨架失败: {RootPath}", normalizedRoot);
            }
        }
    }
}
