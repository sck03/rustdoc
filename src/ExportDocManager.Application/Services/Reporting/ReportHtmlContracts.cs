using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Reporting
{
    public enum ReportDocumentType
    {
        ExportDocument,
        PaymentVoucher
    }

    public sealed class ReportTemplateDescriptor
    {
        public ReportDocumentType ReportType { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public string TemplatePath { get; init; } = string.Empty;

        public bool WithSealDefault { get; init; }
    }

    public sealed class ReportHtmlRenderResult
    {
        public ReportDocumentType ReportType { get; init; }

        public int SourceId { get; init; }

        public string TemplatePath { get; init; } = string.Empty;

        public bool WithSeal { get; init; }

        public string Html { get; init; } = string.Empty;
    }

    public sealed class ReportTemplateContentResult
    {
        public ReportDocumentType ReportType { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public string TemplatePath { get; init; } = string.Empty;

        public bool WithSealDefault { get; init; }

        public string Content { get; init; } = string.Empty;

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ReportTemplatePreviewResult
    {
        public ReportDocumentType ReportType { get; init; }

        public bool WithSeal { get; init; }

        public string Html { get; init; } = string.Empty;
    }

    public sealed class ReportTemplateCommandResult
    {
        public ReportDocumentType ReportType { get; init; }

        public string TemplatePath { get; init; } = string.Empty;

        public string StoragePolicy { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;
    }

    public sealed class ReportTemplateStorageStatus
    {
        public string TemplateRoot { get; init; } = string.Empty;

        public bool Exists { get; init; }

        public bool Writable { get; init; }

        public string Message { get; init; } = string.Empty;

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public enum ReportTemplateImportStrategy
    {
        Overwrite = 0,
        Merge = 1,
        AddOnly = 2
    }

    public sealed class ReportTemplatePackageExportResult
    {
        public string PackagePath { get; init; } = string.Empty;

        public int TemplateCount { get; init; }

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ReportTemplatePackageImportResult
    {
        public int TemplateCount { get; init; }

        public string PackageVersion { get; init; } = "1.0";

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ReportTemplateFieldDescriptor
    {
        public ReportDocumentType ReportType { get; init; }

        public string Category { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;
    }

    public sealed class ReportTemplateFieldCatalog
    {
        public ReportDocumentType ReportType { get; init; }

        public IReadOnlyList<string> CategoryOrder { get; init; } = Array.Empty<string>();

        public IReadOnlyList<ReportTemplateFieldDescriptor> Fields { get; init; } = Array.Empty<ReportTemplateFieldDescriptor>();
    }

    public interface IReportHtmlService
    {
        Task<IReadOnlyList<ReportTemplateDescriptor>> GetAvailableTemplatesAsync(
            ReportDocumentType reportType,
            CancellationToken cancellationToken = default);

        Task<ReportHtmlRenderResult> RenderInvoiceReportAsync(
            int invoiceId,
            ReportDocumentType reportType,
            string templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default);

        Task<ReportHtmlRenderResult> RenderInvoiceReportDraftAsync(
            Invoice invoice,
            ReportDocumentType reportType,
            string templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default);

        Task<ReportHtmlRenderResult> RenderPaymentVoucherAsync(
            int paymentId,
            string templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default);

        Task<ReportHtmlRenderResult> RenderPaymentVoucherDraftAsync(
            Payment payment,
            string templatePath = null,
            bool withSeal = true,
            CancellationToken cancellationToken = default);
    }

    public interface IReportTemplateService
    {
        Task<ReportTemplateContentResult> CreateTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            string displayName = null,
            CancellationToken cancellationToken = default);

        Task<ReportTemplateContentResult> GetTemplateContentAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default);

        Task<ReportTemplateContentResult> SaveTemplateContentAsync(
            ReportDocumentType reportType,
            string templatePath,
            string content,
            CancellationToken cancellationToken = default);

        Task<ReportTemplateContentResult> RenameTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            string newTemplatePath,
            CancellationToken cancellationToken = default);

        Task<ReportTemplateCommandResult> DeleteTemplateAsync(
            ReportDocumentType reportType,
            string templatePath,
            CancellationToken cancellationToken = default);

        Task<ReportTemplatePreviewResult> PreviewTemplateContentAsync(
            ReportDocumentType reportType,
            string content,
            bool withSeal = true,
            CancellationToken cancellationToken = default);
    }

    public interface IReportTemplateStorageDiagnosticsService
    {
        Task<ReportTemplateStorageStatus> CheckAsync(CancellationToken cancellationToken = default);
    }

    public interface IReportTemplatePackageService
    {
        Task<ReportTemplatePackageExportResult> ExportAsync(
            string packagePath,
            IProgress<OperationProgressUpdate> progress = null,
            CancellationToken cancellationToken = default);

        Task<ReportTemplatePackageImportResult> ImportAsync(
            string packagePath,
            ReportTemplateImportStrategy strategy = ReportTemplateImportStrategy.Overwrite,
            IProgress<OperationProgressUpdate> progress = null,
            CancellationToken cancellationToken = default);
    }

    public interface IReportTemplateFieldCatalogService
    {
        ReportTemplateFieldCatalog GetFieldCatalog(ReportDocumentType reportType);
    }
}
