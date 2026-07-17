using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static bool TryParseReportDocumentType(string reportType, out ReportDocumentType parsedReportType)
        {
            if (string.IsNullOrWhiteSpace(reportType))
            {
                parsedReportType = ReportDocumentType.ExportDocument;
                return true;
            }

            return Enum.TryParse(reportType.Trim(), true, out parsedReportType);
        }
    }
}
