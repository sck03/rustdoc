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
                    return WriteForbidden("服务器路径导入仅允许可信 Tauri 桌面端；浏览器请上传提交包。");
                }

                return await ImportSingleWindowPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    SingleWindowPackageType.SubmitPackage,
                    request,
                    cancellationToken);
            })
            .WithName("ImportSingleWindowSubmitPackage");

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
                    return WriteForbidden("服务器路径导入仅允许可信 Tauri 桌面端；浏览器请上传回执包。");
                }

                return await ImportSingleWindowPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    SingleWindowPackageType.ReceiptPackage,
                    request,
                    cancellationToken);
            })
            .WithName("ImportSingleWindowReceiptPackage");

            endpoints.MapPost("/api/single-window/packages/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                string fileName,
                string workingDirectory,
                bool keepWorkingDirectory,
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
                    SingleWindowPackageType.SubmitPackage,
                    fileName,
                    workingDirectory,
                    keepWorkingDirectory,
                    cancellationToken);
            })
            .WithName("UploadSingleWindowSubmitPackage");

            endpoints.MapPost("/api/single-window/receipts/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                string fileName,
                string workingDirectory,
                bool keepWorkingDirectory,
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
                    keepWorkingDirectory,
                    cancellationToken);
            })
            .WithName("UploadSingleWindowReceiptPackage");

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
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请下载回执包。");
                }

                return await ExportSingleWindowReceiptPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    request,
                    cancellationToken);
            })
            .WithName("SaveSingleWindowReceiptPackageToPath");

            endpoints.MapPost("/api/single-window/receipts/download-package", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                IAppPathProvider pathProvider,
                ApiSingleWindowReceiptPackageExportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await DownloadSingleWindowReceiptPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    request,
                    cancellationToken);
            })
            .WithName("DownloadSingleWindowReceiptPackage");
        }
    }
}
