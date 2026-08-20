namespace ExportDocManager.Utils
{
    /// <summary>Compares user-selected physical paths using current-platform semantics.</summary>
    public static class PhysicalPathComparison
    {
        public static StringComparer Comparer => PathBoundaryHelper.PathComparer;

        public static string Normalize(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return Path.GetFullPath(path.Trim());
        }

        public static bool AreSamePath(string firstPath, string secondPath) =>
            Comparer.Equals(Normalize(firstPath), Normalize(secondPath));

        public static bool CouldAddressSameExistingFile(string firstPath, string secondPath)
        {
            string first = Normalize(firstPath);
            string second = Normalize(secondPath);
            if (Comparer.Equals(first, second))
            {
                return true;
            }

            // On the usual case-insensitive APFS configuration, File.Exists
            // resolves a differently-cased spelling to the same entry. On a
            // case-sensitive APFS volume a new differently-cased destination
            // remains absent, so legitimate sibling files are preserved.
            return OperatingSystem.IsMacOS() &&
                   string.Equals(first, second, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(first) &&
                   File.Exists(second);
        }
    }
}
