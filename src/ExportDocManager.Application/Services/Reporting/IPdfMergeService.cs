namespace ExportDocManager.Services.Reporting
{
    public interface IPdfMergeService
    {
        void Merge(
            IReadOnlyCollection<string> sourceFiles,
            string destinationPath,
            CancellationToken cancellationToken = default);
    }
}
