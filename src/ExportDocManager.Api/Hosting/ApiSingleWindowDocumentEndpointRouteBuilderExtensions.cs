using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowDocumentEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/coo/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await GetCustomsCooDocumentAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    buildDefaults: false,
                    cancellationToken);
            })
            .WithName("GetCustomsCooDocument");

            endpoints.MapPut("/api/single-window/coo/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiCustomsCooDocumentDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await SaveCustomsCooDocumentAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .WithName("SaveCustomsCooDocument");

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/build-defaults", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await GetCustomsCooDocumentAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    buildDefaults: true,
                    cancellationToken);
            })
            .WithName("BuildCustomsCooDefaults");

            endpoints.MapGet("/api/single-window/coo/{invoiceId:int}/locked-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await GetCustomsCooLockedFieldsAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("GetCustomsCooLockedFields");

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/unlock-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiSingleWindowUnlockFieldsRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await UnlockCustomsCooFieldsAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .WithName("UnlockCustomsCooFields");

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/submit-package/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请下载提交包。");
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                ApiSingleWindowSubmitPackageRequest request;
                try
                {
                    request = await ReadSingleWindowSubmitPackageRequestAsync(context, cancellationToken);
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }

                return await ExportSingleWindowSubmitPackageAsync(
                    handoffPackageService,
                    settingsService,
                    pathProvider,
                    SingleWindowBusinessType.CustomsCoo,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .WithName("SaveCustomsCooSubmitPackageToPath");

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/submit-package/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await DownloadSingleWindowSubmitPackageAsync(
                    handoffPackageService,
                    settingsService,
                    pathProvider,
                    SingleWindowBusinessType.CustomsCoo,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("DownloadCustomsCooSubmitPackage");

            endpoints.MapGet("/api/single-window/acd/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await GetAgentConsignmentDocumentAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    buildDefaults: false,
                    cancellationToken);
            })
            .WithName("GetAgentConsignmentDocument");

            endpoints.MapPut("/api/single-window/acd/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiAgentConsignmentDocumentDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await SaveAgentConsignmentDocumentAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .WithName("SaveAgentConsignmentDocument");

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/build-defaults", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await GetAgentConsignmentDocumentAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    buildDefaults: true,
                    cancellationToken);
            })
            .WithName("BuildAgentConsignmentDefaults");

            endpoints.MapGet("/api/single-window/acd/{invoiceId:int}/locked-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await GetAgentConsignmentLockedFieldsAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("GetAgentConsignmentLockedFields");

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/unlock-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiSingleWindowUnlockFieldsRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await UnlockAgentConsignmentFieldsAsync(
                    documentService,
                    settingsService,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .WithName("UnlockAgentConsignmentFields");

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/submit-package/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请下载提交包。");
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                ApiSingleWindowSubmitPackageRequest request;
                try
                {
                    request = await ReadSingleWindowSubmitPackageRequestAsync(context, cancellationToken);
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }

                return await ExportSingleWindowSubmitPackageAsync(
                    handoffPackageService,
                    settingsService,
                    pathProvider,
                    SingleWindowBusinessType.AgentConsignment,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .WithName("SaveAgentConsignmentSubmitPackageToPath");

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/submit-package/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await DownloadSingleWindowSubmitPackageAsync(
                    handoffPackageService,
                    settingsService,
                    pathProvider,
                    SingleWindowBusinessType.AgentConsignment,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("DownloadAgentConsignmentSubmitPackage");
        }
    }
}
