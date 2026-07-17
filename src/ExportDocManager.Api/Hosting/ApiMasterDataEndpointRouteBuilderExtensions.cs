using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            MapCustomerMasterDataEndpoints(endpoints);
            MapExporterMasterDataEndpoints(endpoints);
            MapPayeeMasterDataEndpoints(endpoints);
            MapProductMasterDataEndpoints(endpoints);
            MapPortMasterDataEndpoints(endpoints);
            MapUnitMasterDataEndpoints(endpoints);
            MapHsCodeMasterDataEndpoints(endpoints);
        }

        private static IResult BadMasterDataId(string name)
        {
            return Results.BadRequest(new ApiErrorResponse($"{name}ID必须大于0。"));
        }

        private static IResult BadRowVersion(string name)
        {
            return Results.BadRequest(new ApiErrorResponse($"{name} rowVersion 必须是有效的 Base64 字符串。"));
        }
    }
}
