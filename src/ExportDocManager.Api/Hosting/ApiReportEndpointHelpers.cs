using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static bool TryParseReportDocumentType(string? reportType, out ReportDocumentType parsedReportType)
        {
            if (string.IsNullOrWhiteSpace(reportType))
            {
                parsedReportType = ReportDocumentType.ExportDocument;
                return true;
            }

            return Enum.TryParse(reportType.Trim(), true, out parsedReportType);
        }

        private static bool CanUseReportDocumentDomain(
            HttpContext context,
            ApiAuthorizationService authorizationService,
            ReportDocumentType reportType) =>
            authorizationService.CanUsePermission(
                ApiEndpointAuth.GetRequiredUser(context),
                ReportDocumentAccessCatalog.GetSourceResource(reportType),
                PermissionAction.View);
    }
}
