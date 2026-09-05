using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Services.Security
{
    /// <summary>
    /// Maps each report-template domain to the business data that its fields
    /// describe. Template capabilities and source-data access are evaluated
    /// independently so a generic designer grant cannot cross business domains.
    /// </summary>
    public static class ReportDocumentAccessCatalog
    {
        public static string GetSourceResource(ReportDocumentType reportType) => reportType switch
        {
            ReportDocumentType.ExportDocument => PermissionModuleCatalog.DocumentInvoices,
            ReportDocumentType.PaymentVoucher => PermissionModuleCatalog.DocumentPayments,
            _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, "报表类型无效。")
        };
    }
}
