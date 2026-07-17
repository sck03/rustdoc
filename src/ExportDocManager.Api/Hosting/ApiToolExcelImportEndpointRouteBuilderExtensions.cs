using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExcelImportPreviewEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/excel/import-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService,
                IExcelImportService excelImportService,
                ApiExcelImportPreviewRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var validation = ValidateExcelSourcePath(
                    request?.FilePath,
                    "Excel 导入源文件",
                    out string sourcePath);
                if (validation != null)
                {
                    return validation;
                }

                try
                {
                    await settingsService.LoadAsync();
                    var result = await excelImportService.ImportFromExcelAsync(sourcePath, context.RequestAborted);
                    return Results.Ok(ApiExcelDtoFactory.FromImportResult(sourcePath, result));
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewExcelImport");
        }
    }
}
