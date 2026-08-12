using ExportDocManager.Models.DTOs;
using Microsoft.AspNetCore.Http.HttpResults;

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

        private static BadRequest<ApiErrorResponse> BadMasterDataId(string name)
        {
            return TypedResults.BadRequest(new ApiErrorResponse($"{name}ID必须大于0。"));
        }

        private static BadRequest<ApiErrorResponse> BadRowVersion(string name)
        {
            return TypedResults.BadRequest(new ApiErrorResponse($"{name} rowVersion 必须是有效的 Base64 字符串。"));
        }
    }
}
