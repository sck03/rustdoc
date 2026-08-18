using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;


namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const int MaximumHsCodeBatchDeleteCount = 5_000;

        private static void MapHsCodeMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            MapHsCodeImportsEndpoints(endpoints);
            MapHsCodeRemoteEndpoints(endpoints);
            MapHsCodeCrudEndpoints(endpoints);
            MapHsCodeKnowledgeEndpoints(endpoints);
        }
    }
}
