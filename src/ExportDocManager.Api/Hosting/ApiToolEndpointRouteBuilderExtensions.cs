namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            MapPdfToolEndpoints(endpoints);
            MapLetterOfCreditToolEndpoints(endpoints);
            MapOcrToolEndpoints(endpoints);
            MapExchangeRateToolEndpoints(endpoints);
            MapEmailToolEndpoints(endpoints);
            MapContainerPackingToolEndpoints(endpoints);
            MapExcelToolEndpoints(endpoints);
        }
    }
}
