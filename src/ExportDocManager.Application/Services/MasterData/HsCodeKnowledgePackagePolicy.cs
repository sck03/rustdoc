namespace ExportDocManager.Services.MasterData;

public static class HsCodeKnowledgePackagePolicy
{
    public const long MaximumPackageBytes = 100L * 1024L * 1024L;
    public const long MaximumManifestBytes = 256L * 1024L;
    public const long MaximumEntryBytes = 100L * 1024L * 1024L;
    public const long MaximumExpandedBytes = 300L * 1024L * 1024L;
}
