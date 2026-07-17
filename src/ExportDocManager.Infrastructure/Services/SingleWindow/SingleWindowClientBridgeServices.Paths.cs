namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        private static SingleWindowClientFolderLayout ResolveBusinessLayout(
            string rootPath,
            bool createDirectories)
        {
            string normalizedRoot = NormalizeClientRootPath(rootPath);
            string bizRoot = normalizedRoot;

            string outBox = Path.Combine(bizRoot, "OutBox");
            string sentBox = Path.Combine(bizRoot, "SentBox");
            string inBox = Path.Combine(bizRoot, "InBox");
            string failBox = Path.Combine(bizRoot, "FailBox");

            if (createDirectories)
            {
                Directory.CreateDirectory(bizRoot);
                Directory.CreateDirectory(outBox);
                Directory.CreateDirectory(sentBox);
                Directory.CreateDirectory(inBox);
                Directory.CreateDirectory(failBox);
            }

            return new SingleWindowClientFolderLayout
            {
                BizRoot = bizRoot,
                OutBox = outBox,
                SentBox = sentBox,
                InBox = inBox,
                FailBox = failBox
            };
        }

        private static string NormalizeClientRootPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return string.Empty;
            }

            string normalized = rootPath.Trim();
            string leafName = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (IsClientBoxDirectoryName(leafName))
            {
                return Directory.GetParent(normalized)?.FullName ?? normalized;
            }

            return normalized;
        }

        private static bool IsClientBoxDirectoryName(string directoryName)
        {
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return false;
            }

            return string.Equals(directoryName, "OutBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "SentBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "InBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "FailBox", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class SingleWindowClientFolderLayout
        {
            public string BizRoot { get; init; } = string.Empty;

            public string OutBox { get; init; } = string.Empty;

            public string SentBox { get; init; } = string.Empty;

            public string InBox { get; init; } = string.Empty;

            public string FailBox { get; init; } = string.Empty;
        }
    }
}
