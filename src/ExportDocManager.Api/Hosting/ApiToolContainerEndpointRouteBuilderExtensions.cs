using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapContainerPackingToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/container-packing/analyze", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerPackingEngine packingEngine,
                ApiContainerPackingAnalyzeRequest request,
                CancellationToken cancellationToken) =>
            {

                var validation = ValidateContainerPackingAnalyzeRequest(request, out var packingRequest);
                if (validation != null)
                {
                    return validation;
                }

                if (packingRequest is null)
                {
                    return Results.BadRequest(new ApiErrorResponse("装箱分析请求体无效。"));
                }

                try
                {
                    var analysis = await packingEngine
                        .AnalyzeAsync(packingRequest, cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Ok(ApiContainerPackingDtoFactory.FromAnalysis(analysis));
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(
                        new ApiErrorResponse("装箱分析已取消。"),
                        statusCode: StatusCodes.Status499ClientClosedRequest);
                }
                catch (ServiceException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("AnalyzeContainerPacking")
            .Produces<ApiContainerPackingAnalyzeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/tools/container-packing/projects", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int? limit,
                CancellationToken cancellationToken) =>
            {

                var projects = await containerLoadingService.GetRecentProjectsAsync(
                    limit ?? 100,
                    cancellationToken);
                return Results.Ok(new ApiContainerPackingProjectListResponse(
                    projects.Select(ApiContainerPackingProjectDtoFactory.FromProjectSummary).ToList(),
                    ApiContainerPackingProjectDtoFactory.StoragePolicy));
            })
            .WithName("ListContainerPackingProjects")
            .Produces<ApiContainerPackingProjectListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/tools/container-packing/projects/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("装柜方案 ID 无效。"));
                }

                var project = await containerLoadingService.GetProjectAsync(id, cancellationToken);
                if (project == null)
                {
                    return Results.NotFound(new ApiErrorResponse("装柜方案不存在或已删除。"));
                }

                var items = await containerLoadingService.GetProjectItemsAsync(id, cancellationToken);
                return Results.Ok(new ApiContainerPackingProjectResponse(
                    ApiContainerPackingProjectDtoFactory.FromProject(project, items),
                    ApiContainerPackingProjectDtoFactory.StoragePolicy));
            })
            .WithName("GetContainerPackingProject")
            .Produces<ApiContainerPackingProjectResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/tools/container-packing/projects", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                ApiContainerPackingProjectSaveRequest request,
                CancellationToken cancellationToken) =>
            {

                var validation = ValidateContainerPackingProjectSaveRequest(request);
                if (validation != null)
                {
                    return validation;
                }

                var project = ApiContainerPackingProjectDtoFactory.ToProject(request);
                var items = ApiContainerPackingProjectDtoFactory.ToProjectItems(request);

                try
                {
                    await containerLoadingService.SaveProjectAsync(project, items, cancellationToken);
                    var savedItems = await containerLoadingService.GetProjectItemsAsync(project.Id, cancellationToken);
                    return Results.Ok(new ApiContainerPackingProjectSaveResponse(
                        true,
                        project.Id,
                        ApiContainerPackingProjectDtoFactory.FromProject(project, savedItems),
                        "装柜方案已保存。",
                        ApiContainerPackingProjectDtoFactory.StoragePolicy));
                }
                catch (ServiceException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("SaveContainerPackingProject")
            .Produces<ApiContainerPackingProjectSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/tools/container-packing/projects/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("装柜方案 ID 无效。"));
                }

                var project = await containerLoadingService.GetProjectAsync(id, cancellationToken);
                if (project == null)
                {
                    return Results.NotFound(new ApiErrorResponse("装柜方案不存在或已删除。"));
                }

                await containerLoadingService.DeleteProjectAsync(id, cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, "装柜方案已删除。"));
            })
            .WithName("DeleteContainerPackingProject")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/api/tools/container-packing/container-types", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                CancellationToken cancellationToken) =>
            {

                var containerTypes = await containerLoadingService.GetContainerTypesAsync(cancellationToken);
                return Results.Ok(new ApiContainerTypeListResponse(
                    containerTypes.Select(ApiContainerPackingProjectDtoFactory.FromContainerType).ToList(),
                    ApiContainerPackingProjectDtoFactory.StoragePolicy));
            })
            .WithName("ListContainerPackingContainerTypes")
            .Produces<ApiContainerTypeListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/tools/container-packing/container-types", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                ApiContainerTypeSaveRequest request,
                CancellationToken cancellationToken) =>
            {

                var validation = ValidateContainerTypeSaveRequest(request);
                if (validation != null)
                {
                    return validation;
                }

                var containerType = ApiContainerPackingProjectDtoFactory.ToContainerType(request);
                try
                {
                    await containerLoadingService.SaveContainerTypeAsync(containerType, cancellationToken);
                    return Results.Ok(new ApiContainerTypeSaveResponse(
                        true,
                        containerType.Id,
                        ApiContainerPackingProjectDtoFactory.FromContainerType(containerType),
                        "柜型已保存。",
                        ApiContainerPackingProjectDtoFactory.StoragePolicy));
                }
                catch (ServiceException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("SaveContainerPackingContainerType")
            .WithApiPermission(
                PermissionModuleCatalog.DocumentContainerPacking,
                writeAccessLevel: PermissionAccessLevel.Manage)
            .Produces<ApiContainerTypeSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/tools/container-packing/container-types/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("柜型 ID 无效。"));
                }

                var containerType = (await containerLoadingService.GetContainerTypesAsync(cancellationToken))
                    .FirstOrDefault(type => type.Id == id);
                if (containerType == null)
                {
                    return Results.NotFound(new ApiErrorResponse("柜型不存在或已删除。"));
                }

                if (containerType.IsSystemDefault)
                {
                    return Results.Conflict(new ApiErrorResponse("系统默认柜型不支持删除。"));
                }

                await containerLoadingService.DeleteContainerTypeAsync(id, cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, "柜型已删除。"));
            })
            .WithName("DeleteContainerPackingContainerType")
            .WithApiPermission(
                PermissionModuleCatalog.DocumentContainerPacking,
                writeAccessLevel: PermissionAccessLevel.Manage)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        }

        private static IResult? ValidateContainerPackingAnalyzeRequest(
            ApiContainerPackingAnalyzeRequest? request,
            out ContainerPackingRequest? packingRequest)
        {
            packingRequest = null;

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("装箱分析请求体不能为空。"));
            }

            if (request.Container == null)
            {
                return Results.BadRequest(new ApiErrorResponse("集装箱尺寸不能为空。"));
            }

            if (request.Container.Length <= 0 || request.Container.Width <= 0 || request.Container.Height <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("集装箱长、宽、高必须大于 0。"));
            }

            if (request.CargoItems == null || request.CargoItems.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("至少需要一行货物。"));
            }

            bool hasValidCargo = request.CargoItems.Any(item =>
                item != null &&
                item.Quantity > 0 &&
                item.Length > 0 &&
                item.Width > 0 &&
                item.Height > 0);
            if (!hasValidCargo)
            {
                return Results.BadRequest(new ApiErrorResponse("至少需要一行尺寸和箱数有效的货物。"));
            }

            foreach (var item in request.CargoItems.Where(item => item != null))
            {
                if (!string.IsNullOrWhiteSpace(item.PreferredZone) &&
                    !Enum.TryParse<ContainerCargoZone>(item.PreferredZone, ignoreCase: true, out _))
                {
                    return Results.BadRequest(new ApiErrorResponse($"装载区域无效：{item.PreferredZone}"));
                }
            }

            packingRequest = ApiContainerPackingDtoFactory.ToRequest(request);
            try
            {
                ContainerPackingResourcePolicy.Validate(packingRequest);
            }
            catch (ServiceValidationException ex)
            {
                packingRequest = null;
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            return null;
        }

        private static IResult? ValidateContainerPackingProjectSaveRequest(
            ApiContainerPackingProjectSaveRequest? request)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("装柜方案请求体不能为空。"));
            }

            if (request.Id < 0)
            {
                return Results.BadRequest(new ApiErrorResponse("装柜方案 ID 无效。"));
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ApiErrorResponse("装柜方案名称不能为空。"));
            }

            var containerValidation = ValidateContainerDimensions(request.Container);
            if (containerValidation != null)
            {
                return containerValidation;
            }

            if (request.CargoItems == null || request.CargoItems.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("至少需要一行货物才能保存装柜方案。"));
            }

            bool hasValidCargo = request.CargoItems.Any(IsValidCargoItem);
            if (!hasValidCargo)
            {
                return Results.BadRequest(new ApiErrorResponse("至少需要一行尺寸和箱数有效的货物。"));
            }

            foreach (var item in request.CargoItems.Where(item => item != null))
            {
                if (!string.IsNullOrWhiteSpace(item.PreferredZone) &&
                    !Enum.TryParse<ContainerCargoZone>(item.PreferredZone, ignoreCase: true, out _))
                {
                    return Results.BadRequest(new ApiErrorResponse($"装载区域无效：{item.PreferredZone}"));
                }
            }

            try
            {
                ContainerPackingResourcePolicy.Validate(
                    ApiContainerPackingDtoFactory.ToRequest(
                        request.Container,
                        request.CargoItems,
                        request.Rules));
            }
            catch (ServiceValidationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }

            return null;
        }

        private static IResult? ValidateContainerTypeSaveRequest(ApiContainerTypeSaveRequest? request)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("柜型请求体不能为空。"));
            }

            if (request.Id < 0)
            {
                return Results.BadRequest(new ApiErrorResponse("柜型 ID 无效。"));
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ApiErrorResponse("柜型名称不能为空。"));
            }

            if (request.Length <= 0 || request.Width <= 0 || request.Height <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("柜型长、宽、高必须大于 0。"));
            }

            try
            {
                ContainerPackingResourcePolicy.ValidateContainer(new ContainerDimensions(
                    request.Length,
                    request.Width,
                    request.Height,
                    request.MaxVolume,
                    request.MaxWeight));
            }
            catch (ServiceValidationException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }

            return null;
        }

        private static IResult? ValidateContainerDimensions(ApiContainerDimensionsDto? container)
        {
            if (container == null)
            {
                return Results.BadRequest(new ApiErrorResponse("集装箱尺寸不能为空。"));
            }

            if (container.Length <= 0 || container.Width <= 0 || container.Height <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("集装箱长、宽、高必须大于 0。"));
            }

            return null;
        }

        private static bool IsValidCargoItem(ApiContainerPackingCargoInputDto? item)
        {
            return item != null &&
                item.Quantity > 0 &&
                item.Length > 0 &&
                item.Width > 0 &&
                item.Height > 0;
        }
    }
}
