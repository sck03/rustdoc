namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        private static SingleWindowClientFolderLayout ResolveBusinessLayout(
            string rootPath,
            bool createDirectories)
        {
            string normalizedRoot = SingleWindowClientProfilePathResolver.EnsureClientRoot(
                rootPath,
                createDirectories,
                requireWritable: createDirectories);
            string bizRoot = normalizedRoot;

            string outBox = Path.Combine(bizRoot, "OutBox");
            string sentBox = Path.Combine(bizRoot, "SentBox");
            string inBox = Path.Combine(bizRoot, "InBox");
            string failBox = Path.Combine(bizRoot, "FailBox");

            return new SingleWindowClientFolderLayout
            {
                BizRoot = bizRoot,
                OutBox = outBox,
                SentBox = sentBox,
                InBox = inBox,
                FailBox = failBox
            };
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
