using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public enum ExporterSealKind
    {
        Document,
        Customs
    }

    public static class ExporterSealFilePolicy
    {
        public const int MaximumBytes = 5 * 1024 * 1024;
    }

    public interface IExporterSealService
    {
        Task<Exporter> SaveSealAsync(
            int exporterId,
            ExporterSealKind sealKind,
            string originalFileName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default);

        void DeleteReplacedManagedSeal(
            int exporterId,
            string? previousPath,
            string? currentPath);

        void DeleteAllManagedSeals(int exporterId);
    }
}
