namespace ExportDocManager.Services.Suppliers
{
    public sealed record SupplierAssessmentRecord(
        int Id, int SupplierCompanyId, DateTimeOffset AssessedAt, string AssessmentKind,
        int QualityScore, int DeliveryScore, int ServiceScore, int PriceScore,
        decimal AverageScore, string Conclusion, string Notes, string AssessedBy,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int VersionNumber);

    public sealed record SupplierAssessmentSaveRequest(
        int Id, int SupplierCompanyId, DateTimeOffset AssessedAt, string AssessmentKind,
        int QualityScore, int DeliveryScore, int ServiceScore, int PriceScore,
        string Conclusion, string Notes, int ExpectedVersion = 0);

    public sealed record SupplierAssessmentOverviewItem(
        int SupplierCompanyId, string SupplierName, string SupplierStatus, string Category,
        int AssessmentCount, DateTimeOffset LatestAssessedAt, string LatestAssessmentKind,
        int QualityScore, int DeliveryScore, int ServiceScore, int PriceScore,
        decimal AverageScore, string Conclusion, string Notes);

    public sealed record SupplierAssessmentOverview(
        int TotalSuppliers, int AssessedSuppliers, int UnassessedSuppliers,
        int PreferredCount, int QualifiedCount, int WatchCount, int PausedCount,
        decimal AverageQualityScore, decimal AverageDeliveryScore,
        decimal AverageServiceScore, decimal AveragePriceScore,
        IReadOnlyList<SupplierAssessmentOverviewItem> Items);

    public interface ISupplierAssessmentService
    {
        Task<IReadOnlyList<SupplierAssessmentRecord>> ListAsync(
            int supplierCompanyId, CancellationToken cancellationToken = default);

        Task<SupplierAssessmentOverview> GetOverviewAsync(
            CancellationToken cancellationToken = default);

        Task<SupplierAssessmentRecord> SaveAsync(
            SupplierAssessmentSaveRequest request, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(
            int supplierCompanyId, int id, CancellationToken cancellationToken = default);
    }
}
