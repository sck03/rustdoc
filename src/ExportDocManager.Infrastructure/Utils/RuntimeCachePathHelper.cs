using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Utils
{
    public static class RuntimeCachePathHelper
    {
        public static string CreateUniqueDirectory(
            IAppPathProvider pathProvider,
            string category,
            string prefix)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

            string directory = Path.Combine(
                pathProvider.CacheRoot,
                NormalizePathSegment(category),
                $"{NormalizePathSegment(prefix)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string NormalizePathSegment(string value)
        {
            return CrossPlatformFileNamePolicy.SanitizeFileNamePart(value, '-', "Cache");
        }
    }
}
