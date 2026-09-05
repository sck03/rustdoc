using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Suppliers;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Services
{
    internal static class UnsupportedCapability
    {
        public static InfrastructureServiceException Excel() =>
            new("当前产品未安装 Excel 能力模块。");

        public static InfrastructureServiceException Browser() =>
            new("当前产品未安装浏览器能力模块，无法生成浏览器 PDF。");

        public static InfrastructureServiceException PdfOcr() =>
            new("当前产品未安装 PDF/OCR 能力模块。");
    }
}

namespace ExportDocManager.Services.Reporting
{
    public sealed class UnsupportedHtmlToPdfService : IHtmlToPdfService
    {
        public Task<HtmlToPdfRenderResult> RenderAsync(
            string html,
            string destinationPath,
            HtmlToPdfRenderOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<HtmlToPdfRenderResult>(UnsupportedCapability.Browser());
    }

    public sealed class UnsupportedPdfMergeService : IPdfMergeService
    {
        public void Merge(
            IReadOnlyCollection<string> sourceFiles,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            throw UnsupportedCapability.PdfOcr();
    }
}

namespace ExportDocManager.Services.Tools
{
    public sealed class UnsupportedLetterOfCreditDocumentService : ILetterOfCreditDocumentService
    {
        public Task<LetterOfCreditDocumentImportResult> ImportAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromException<LetterOfCreditDocumentImportResult>(UnsupportedCapability.PdfOcr());
    }
}

namespace ExportDocManager.Services.Data
{
    public sealed class UnsupportedExcelImportAnalyzer : IExcelImportAnalyzer
    {
        public Task<ExcelImportAnalysisReport> AnalyzeAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ExcelImportAnalysisReport>(UnsupportedCapability.Excel());
    }

    public sealed class UnsupportedExcelImportService : IExcelImportService
    {
        public Task<ImportResult> ImportFromExcelAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ImportResult>(UnsupportedCapability.Excel());
    }

    public sealed class UnsupportedExcelImportTemplateService : IExcelImportTemplateService
    {
        public ExcelImportTemplateInfo GetDefaultTemplate() => throw UnsupportedCapability.Excel();
        public string EnsureDefaultTemplateAvailable() => throw UnsupportedCapability.Excel();
        public Task<string> ExportDefaultTemplateAsync(string targetFilePath, bool overwrite = true, CancellationToken cancellationToken = default) => Task.FromException<string>(UnsupportedCapability.Excel());
        public Task<string> ExportBlankBookingSheetAsync(string targetFilePath, bool overwrite = true, CancellationToken cancellationToken = default) => Task.FromException<string>(UnsupportedCapability.Excel());
        public string ExportBookingSheet(string sourceFilePath, string targetFilePath, bool overwrite = true) => throw UnsupportedCapability.Excel();
        public string ExportBookingSheetFromInvoice(Invoice invoice, string targetFilePath, bool overwrite = true) => throw UnsupportedCapability.Excel();
    }
}

namespace ExportDocManager.Services.Crm
{
    public sealed class UnsupportedCrmCustomerImportService : ICrmCustomerImportService
    {
        public Task<CrmCustomerImportPreview> PreviewAsync(Stream input, string fileName, CancellationToken cancellationToken = default) => Task.FromException<CrmCustomerImportPreview>(UnsupportedCapability.Excel());
        public Task<CrmCustomerImportResult> ImportAsync(string previewId, CancellationToken cancellationToken = default) => Task.FromException<CrmCustomerImportResult>(UnsupportedCapability.Excel());
    }

    public sealed class UnsupportedCrmCustomerExportService : ICrmCustomerExportService
    {
        public Task<byte[]> ExportAsync(string? keyword, string? status, CancellationToken cancellationToken = default) => Task.FromException<byte[]>(UnsupportedCapability.Excel());
    }
}

namespace ExportDocManager.Services.Suppliers
{
    public sealed class UnsupportedSupplierFileService : ISupplierFileService
    {
        public Task<SupplierImportPreview> PreviewAsync(Stream input, string fileName, CancellationToken cancellationToken = default) => Task.FromException<SupplierImportPreview>(UnsupportedCapability.Excel());
        public Task<SupplierImportResult> ImportAsync(string previewId, CancellationToken cancellationToken = default) => Task.FromException<SupplierImportResult>(UnsupportedCapability.Excel());
        public Task<byte[]> ExportAsync(string? keyword, string? status, CancellationToken cancellationToken = default) => Task.FromException<byte[]>(UnsupportedCapability.Excel());
    }
}

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class UnsupportedQueryResultExportService : IQueryResultExportService
    {
        public Task<QueryResultExportResult> ExportToExcelAsync(QueryPageQuery query, string filePath, IProgress<OperationProgressUpdate>? progress = null, CancellationToken cancellationToken = default) => Task.FromException<QueryResultExportResult>(UnsupportedCapability.Excel());
    }
}

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class UnsupportedSingleWindowReferenceCatalogExcelImportService : ISingleWindowReferenceCatalogExcelImportService
    {
        public Task<SingleWindowReferenceCatalogExcelImportPreview> PreviewImportAsync(Stream workbookStream, SingleWindowReferenceCatalogExcelImportOptions options, CancellationToken cancellationToken = default) => Task.FromException<SingleWindowReferenceCatalogExcelImportPreview>(UnsupportedCapability.Excel());
    }
}
