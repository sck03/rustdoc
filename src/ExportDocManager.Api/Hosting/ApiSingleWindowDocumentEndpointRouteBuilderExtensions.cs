using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Time;

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
            .WithName("GetCustomsCooDocument")
            .Produces<ApiCustomsCooDocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/single-window/coo/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiCustomsCooDocumentDto request,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("SaveCustomsCooDocument")
            .Produces<ApiCustomsCooDocumentSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/build-defaults", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("BuildCustomsCooDefaults")
            .Produces<ApiCustomsCooDocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapGet("/api/single-window/coo/{invoiceId:int}/locked-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("GetCustomsCooLockedFields")
            .Produces<ApiSingleWindowLockedFieldsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/unlock-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiSingleWindowUnlockFieldsRequest request,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("UnlockCustomsCooFields")
            .Produces<ApiCustomsCooUnlockFieldsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/submit-package/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                IBusinessClock clock,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机保存操作仅支持桌面版；浏览器版请下载提交包。");
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
                    clock,
                    SingleWindowBusinessType.CustomsCoo,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .Accepts<ApiSingleWindowSubmitPackageRequest>("application/json")
            .WithName("SaveCustomsCooSubmitPackageToPath")
            .Produces<ApiSingleWindowHandoffPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/submit-package/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                IBusinessClock clock,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

                ApiSingleWindowSubmitPackageRequest request;
                try
                {
                    request = await ReadSingleWindowSubmitPackageRequestAsync(context, cancellationToken);
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }

                return await DownloadSingleWindowSubmitPackageAsync(
                    context,
                    handoffPackageService,
                    settingsService,
                    pathProvider,
                    clock,
                    SingleWindowBusinessType.CustomsCoo,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .Accepts<ApiSingleWindowSubmitPackageRequest>("application/json")
            .WithName("DownloadCustomsCooSubmitPackage")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapGet("/api/single-window/acd/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("GetAgentConsignmentDocument")
            .Produces<ApiAgentConsignmentDocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/single-window/acd/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiAgentConsignmentDocumentDto request,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("SaveAgentConsignmentDocument")
            .Produces<ApiAgentConsignmentDocumentSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/build-defaults", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("BuildAgentConsignmentDefaults")
            .Produces<ApiAgentConsignmentDocumentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapGet("/api/single-window/acd/{invoiceId:int}/locked-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("GetAgentConsignmentLockedFields")
            .Produces<ApiSingleWindowLockedFieldsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/unlock-fields", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAgentConsignmentDocumentService documentService,
                ISettingsService settingsService,
                int invoiceId,
                ApiSingleWindowUnlockFieldsRequest request,
                CancellationToken cancellationToken) =>
            {

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
            .WithName("UnlockAgentConsignmentFields")
            .Produces<ApiAgentConsignmentUnlockFieldsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/submit-package/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                IBusinessClock clock,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机保存操作仅支持桌面版；浏览器版请下载提交包。");
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
                    clock,
                    SingleWindowBusinessType.AgentConsignment,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .Accepts<ApiSingleWindowSubmitPackageRequest>("application/json")
            .WithName("SaveAgentConsignmentSubmitPackageToPath")
            .Produces<ApiSingleWindowHandoffPackageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/submit-package/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowHandoffPackageService handoffPackageService,
                ISettingsService settingsService,
                IAppPathProvider pathProvider,
                IBusinessClock clock,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {

                ApiSingleWindowSubmitPackageRequest request;
                try
                {
                    request = await ReadSingleWindowSubmitPackageRequestAsync(context, cancellationToken);
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }

                return await DownloadSingleWindowSubmitPackageAsync(
                    context,
                    handoffPackageService,
                    settingsService,
                    pathProvider,
                    clock,
                    SingleWindowBusinessType.AgentConsignment,
                    invoiceId,
                    request,
                    cancellationToken);
            })
            .Accepts<ApiSingleWindowSubmitPackageRequest>("application/json")
            .WithName("DownloadAgentConsignmentSubmitPackage")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        }
    }
}
