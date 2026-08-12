using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowPackageEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/single-window/packages/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                ApiSingleWindowImportPackageRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("导入本机提交包仅支持桌面版；浏览器版请上传提交包。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("提交包只能导入独立 SQLite 持卡机。");
                }

                return await ImportSingleWindowPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    SingleWindowPackageType.SubmitPackage,
                    request,
                    cancellationToken);
            })
            .WithName("ImportSingleWindowSubmitPackage")
            .Produces<ApiSingleWindowImportedPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/receipts/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                ApiSingleWindowImportPackageRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("导入本机回执包仅支持桌面版；浏览器版请上传回执包。");
                }

                return await ImportSingleWindowPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    SingleWindowPackageType.ReceiptPackage,
                    request,
                    cancellationToken);
            })
            .WithName("ImportSingleWindowReceiptPackage")
            .Produces<ApiSingleWindowImportedPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/packages/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                string? fileName,
                string? workingDirectory,
                bool? keepWorkingDirectory,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("提交包只能由受控持卡机导入。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("提交包只能导入独立 SQLite 持卡机。");
                }

                return await ImportSingleWindowUploadedPackageAsync(
                    context,
                    handoffPackageService,
                    pathProvider,
                    SingleWindowPackageType.SubmitPackage,
                    fileName,
                    workingDirectory,
                    keepWorkingDirectory ?? false,
                    cancellationToken);
            })
            .Accepts<IFormFile>("application/octet-stream")
            .WithName("UploadSingleWindowSubmitPackage")
            .Produces<ApiSingleWindowImportedPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/receipts/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                string? fileName,
                string? workingDirectory,
                bool? keepWorkingDirectory,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await ImportSingleWindowUploadedPackageAsync(
                    context,
                    handoffPackageService,
                    pathProvider,
                    SingleWindowPackageType.ReceiptPackage,
                    fileName,
                    workingDirectory,
                    keepWorkingDirectory ?? false,
                    cancellationToken);
            })
            .Accepts<IFormFile>("application/octet-stream")
            .WithName("UploadSingleWindowReceiptPackage")
            .Produces<ApiSingleWindowImportedPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/receipts/save-package-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                ApiSingleWindowReceiptPackageExportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机保存操作仅支持桌面版；浏览器版请下载回执包。");
                }

                return await ExportSingleWindowReceiptPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    request,
                    cancellationToken);
            })
            .WithName("SaveSingleWindowReceiptPackageToPath")
            .Produces<ApiSingleWindowHandoffPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/receipts/download-package", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                ApiSingleWindowReceiptPackageExportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("本机回执文件打包只能由受控操作机执行；浏览器版请上传回执文件。");
                }

                return await DownloadSingleWindowReceiptPackageAsync(
                    context,
                    handoffPackageService,
                    pathProvider,
                    request,
                    cancellationToken);
            })
            .WithName("DownloadSingleWindowReceiptPackage")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);
        }
    }
}
