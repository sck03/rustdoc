using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static IResult BadSingleWindowBusinessType()
        {
            return Results.BadRequest(new ApiErrorResponse("单一窗口业务类型必须是 CustomsCoo 或 AgentConsignment。"));
        }

        private static bool TryParseSingleWindowBusinessType(
            string value,
            out SingleWindowBusinessType businessType)
        {
            businessType = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (Enum.TryParse(trimmed, ignoreCase: true, out businessType) &&
                Enum.IsDefined(typeof(SingleWindowBusinessType), businessType))
            {
                return true;
            }

            string normalized = trimmed
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();

            if (normalized is "CUSTOMSCOO" or "COO")
            {
                businessType = SingleWindowBusinessType.CustomsCoo;
                return true;
            }

            if (normalized is "AGENTCONSIGNMENT" or "ACD")
            {
                businessType = SingleWindowBusinessType.AgentConsignment;
                return true;
            }

            return false;
        }

        private static bool IsSingleWindowMissingSource(InvalidOperationException ex)
        {
            return ex.Message.Contains("未找到", StringComparison.Ordinal);
        }
    }
}
