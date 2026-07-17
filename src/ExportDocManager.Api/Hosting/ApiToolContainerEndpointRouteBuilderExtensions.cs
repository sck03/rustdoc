using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapContainerPackingToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/container-packing/analyze", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerPackingEngine packingEngine,
                ApiContainerPackingAnalyzeRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var validation = ValidateContainerPackingAnalyzeRequest(request, out var packingRequest);
                if (validation != null)
                {
                    return validation;
                }

                try
                {
                    var analysis = packingEngine.Analyze(packingRequest, cancellationToken);
                    return Results.Ok(ApiContainerPackingDtoFactory.FromAnalysis(analysis));
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(
                        new ApiErrorResponse("装箱分析已取消。"),
                        statusCode: StatusCodes.Status499ClientClosedRequest);
                }
            })
            .WithName("AnalyzeContainerPacking");

            endpoints.MapGet("/api/tools/container-packing/projects", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var projects = await containerLoadingService.GetAllProjectsAsync();
                return Results.Ok(new ApiContainerPackingProjectListResponse(
                    projects.Select(ApiContainerPackingProjectDtoFactory.FromProjectSummary).ToList(),
                    ApiContainerPackingProjectDtoFactory.StoragePolicy));
            })
            .WithName("ListContainerPackingProjects");

            endpoints.MapGet("/api/tools/container-packing/projects/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("装柜方案 ID 无效。"));
                }

                var project = await containerLoadingService.GetProjectAsync(id);
                if (project == null)
                {
                    return Results.NotFound(new ApiErrorResponse("装柜方案不存在或已删除。"));
                }

                var items = await containerLoadingService.GetProjectItemsAsync(id);
                return Results.Ok(new ApiContainerPackingProjectResponse(
                    ApiContainerPackingProjectDtoFactory.FromProject(project, items),
                    ApiContainerPackingProjectDtoFactory.StoragePolicy));
            })
            .WithName("GetContainerPackingProject");

            endpoints.MapPost("/api/tools/container-packing/projects", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                ApiContainerPackingProjectSaveRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var validation = ValidateContainerPackingProjectSaveRequest(request);
                if (validation != null)
                {
                    return validation;
                }

                var project = ApiContainerPackingProjectDtoFactory.ToProject(request);
                var items = ApiContainerPackingProjectDtoFactory.ToProjectItems(request);

                try
                {
                    await containerLoadingService.SaveProjectAsync(project, items);
                    var savedItems = await containerLoadingService.GetProjectItemsAsync(project.Id);
                    return Results.Ok(new ApiContainerPackingProjectSaveResponse(
                        true,
                        project.Id,
                        ApiContainerPackingProjectDtoFactory.FromProject(project, savedItems),
                        "装柜方案已保存。",
                        ApiContainerPackingProjectDtoFactory.StoragePolicy));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("SaveContainerPackingProject");

            endpoints.MapDelete("/api/tools/container-packing/projects/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("装柜方案 ID 无效。"));
                }

                var project = await containerLoadingService.GetProjectAsync(id);
                if (project == null)
                {
                    return Results.NotFound(new ApiErrorResponse("装柜方案不存在或已删除。"));
                }

                await containerLoadingService.DeleteProjectAsync(id);
                return Results.Ok(new ApiCommandResponse(true, "装柜方案已删除。"));
            })
            .WithName("DeleteContainerPackingProject");

            endpoints.MapGet("/api/tools/container-packing/container-types", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var containerTypes = await containerLoadingService.GetContainerTypesAsync();
                return Results.Ok(new ApiContainerTypeListResponse(
                    containerTypes.Select(ApiContainerPackingProjectDtoFactory.FromContainerType).ToList(),
                    ApiContainerPackingProjectDtoFactory.StoragePolicy));
            })
            .WithName("ListContainerPackingContainerTypes");

            endpoints.MapPost("/api/tools/container-packing/container-types", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                ApiContainerTypeSaveRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var validation = ValidateContainerTypeSaveRequest(request);
                if (validation != null)
                {
                    return validation;
                }

                var containerType = ApiContainerPackingProjectDtoFactory.ToContainerType(request);
                try
                {
                    await containerLoadingService.SaveContainerTypeAsync(containerType);
                    return Results.Ok(new ApiContainerTypeSaveResponse(
                        true,
                        containerType.Id,
                        ApiContainerPackingProjectDtoFactory.FromContainerType(containerType),
                        "柜型已保存。",
                        ApiContainerPackingProjectDtoFactory.StoragePolicy));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("SaveContainerPackingContainerType");

            endpoints.MapDelete("/api/tools/container-packing/container-types/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IContainerLoadingService containerLoadingService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("柜型 ID 无效。"));
                }

                var containerType = (await containerLoadingService.GetContainerTypesAsync())
                    .FirstOrDefault(type => type.Id == id);
                if (containerType == null)
                {
                    return Results.NotFound(new ApiErrorResponse("柜型不存在或已删除。"));
                }

                if (containerType.IsSystemDefault)
                {
                    return Results.Conflict(new ApiErrorResponse("系统默认柜型不支持删除。"));
                }

                await containerLoadingService.DeleteContainerTypeAsync(id);
                return Results.Ok(new ApiCommandResponse(true, "柜型已删除。"));
            })
            .WithName("DeleteContainerPackingContainerType");
        }

        private static IResult ValidateContainerPackingAnalyzeRequest(
            ApiContainerPackingAnalyzeRequest request,
            out ContainerPackingRequest packingRequest)
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
            return null;
        }

        private static IResult ValidateContainerPackingProjectSaveRequest(
            ApiContainerPackingProjectSaveRequest request)
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

            return null;
        }

        private static IResult ValidateContainerTypeSaveRequest(ApiContainerTypeSaveRequest request)
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

            return null;
        }

        private static IResult ValidateContainerDimensions(ApiContainerDimensionsDto container)
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

        private static bool IsValidCargoItem(ApiContainerPackingCargoInputDto item)
        {
            return item != null &&
                item.Quantity > 0 &&
                item.Length > 0 &&
                item.Width > 0 &&
                item.Height > 0;
        }
    }
}
