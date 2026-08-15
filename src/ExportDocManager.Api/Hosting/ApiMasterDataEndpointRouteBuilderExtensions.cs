using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var referenceData = endpoints.MapPermissionGroup(
                PermissionModuleCatalog.DocumentReferenceData,
                PermissionModuleCatalog.DocumentMasterData);
            MapCustomerMasterDataEndpoints(referenceData);
            MapExporterMasterDataEndpoints(referenceData);
            MapPayeeMasterDataEndpoints(referenceData);
            MapUnitMasterDataEndpoints(referenceData);
            MapProductMasterDataEndpoints(endpoints.MapPermissionGroup(
                PermissionModuleCatalog.CommonProductReference,
                PermissionModuleCatalog.DocumentMasterData));
            MapPortMasterDataEndpoints(endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentMasterData));
            MapHsCodeMasterDataEndpoints(endpoints.MapPermissionGroup(
                PermissionModuleCatalog.DocumentHsKnowledge,
                writeAccessLevel: PermissionAccessLevel.Manage));
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
