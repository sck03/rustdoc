using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapProductMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/products", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductReadRepository repository,
                string? keyword,
                int? pageNumber,
                int? pageSize,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var result = await repository.QueryPageAsync(
                    new ProductReadQuery
                    {
                        Keyword = keyword ?? string.Empty,
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50
                    },
                    cancellationToken);

                return Results.Ok(new ExportDocManager.Models.PagedResult<ApiProductDto>(
                    result.Items.Select(ApiMasterDataDtoFactory.FromProduct).ToList(),
                    result.TotalCount,
                    result.PageNumber,
                    result.PageSize));
            })
            .WithName("ListProducts");

            endpoints.MapGet("/api/master-data/products/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("商品");
                }

                var product = await productService.GetByIdAsync(id, cancellationToken);
                return product == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromProduct(product));
            })
            .WithName("GetProduct");

            endpoints.MapPost("/api/master-data/products", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                ApiProductDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("商品请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增商品不能包含已有ID。"));
                }

                Product product;
                try
                {
                    product = ApiMasterDataDtoFactory.ToProductForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("商品");
                }
                product.Id = 0;
                product.RowVersion = null;

                int savedId = await productService.AddProductAsync(product, cancellationToken);
                var saved = await productService.GetByIdAsync(savedId, cancellationToken) ?? product;
                return Results.Created(
                    $"/api/master-data/products/{savedId}",
                    ApiMasterDataDtoFactory.FromProduct(saved));
            })
            .WithName("CreateProduct");

            endpoints.MapPut("/api/master-data/products/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                int id,
                ApiProductDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("商品");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("商品请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体商品ID与路径ID不一致。"));
                }

                var existing = await productService.GetByIdAsync(id, cancellationToken);
                if (existing == null)
                {
                    return Results.NotFound();
                }

                Product product;
                try
                {
                    product = ApiMasterDataDtoFactory.ToProductForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("商品");
                }
                product.Id = id;
                product.CreatedAt = existing.CreatedAt;

                if (!await productService.UpdateProductAsync(product, cancellationToken))
                {
                    return Results.NotFound();
                }

                var updated = await productService.GetByIdAsync(id, cancellationToken) ?? product;
                return Results.Ok(ApiMasterDataDtoFactory.FromProduct(updated));
            })
            .WithName("UpdateProduct");

            endpoints.MapDelete("/api/master-data/products/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("商品");
                }

                return await productService.DeleteAsync(id, cancellationToken)
                    ? Results.Ok(new ApiCommandResponse(true, "商品已删除。"))
                    : Results.NotFound();
            })
            .WithName("DeleteProduct");
        }
    }
}
