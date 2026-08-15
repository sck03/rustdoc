using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapProductMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/products", async Task<Results<
                Ok<ApiPagedResponse<ApiProductDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductReadRepository repository,
                string? keyword,
                int? pageNumber,
                int? pageSize,
                CancellationToken cancellationToken) =>
            {

                var result = await repository.QueryPageAsync(
                    new ProductReadQuery
                    {
                        Keyword = keyword ?? string.Empty,
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50
                    },
                    cancellationToken);

                return TypedResults.Ok(new ApiPagedResponse<ApiProductDto>(
                    result.Items.Select(ApiMasterDataDtoFactory.FromProduct).ToArray(),
                    result.TotalCount,
                    result.PageNumber,
                    result.PageSize,
                    result.TotalPages,
                    result.HasPreviousPage,
                    result.HasNextPage));
            })
            .WithName("ListProducts");

            endpoints.MapGet("/api/master-data/products/{id:int}", async Task<Results<
                Ok<ApiProductDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("商品");
                }

                var product = await productService.GetByIdAsync(id, cancellationToken);
                return product == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiMasterDataDtoFactory.FromProduct(product));
            })
            .WithName("GetProduct");

            endpoints.MapPost("/api/master-data/products", async Task<Results<
                Created<ApiProductDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                ApiProductDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("商品请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增商品不能包含已有ID。"));
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
                return TypedResults.Created(
                    $"/api/master-data/products/{savedId}",
                    ApiMasterDataDtoFactory.FromProduct(saved));
            })
            .WithName("CreateProduct")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/master-data/products/{id:int}", async Task<Results<
                Ok<ApiProductDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                int id,
                ApiProductDto request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("商品");
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("商品请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体商品ID与路径ID不一致。"));
                }

                var existing = await productService.GetByIdAsync(id, cancellationToken);
                if (existing == null)
                {
                    return TypedResults.NotFound();
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
                    return TypedResults.NotFound();
                }

                var updated = await productService.GetByIdAsync(id, cancellationToken) ?? product;
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromProduct(updated));
            })
            .WithName("UpdateProduct")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/master-data/products/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IProductService productService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("商品");
                }

                return await productService.DeleteAsync(id, cancellationToken)
                    ? TypedResults.Ok(new ApiCommandResponse(true, "商品已删除。"))
                    : TypedResults.NotFound();
            })
            .WithName("DeleteProduct")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}
