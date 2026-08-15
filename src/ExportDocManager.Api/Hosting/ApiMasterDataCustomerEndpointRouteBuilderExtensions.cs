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
        private static void MapCustomerMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/customers", async Task<Results<
                Ok<IReadOnlyList<ApiCustomerDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerReadRepository repository,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                var rows = await repository.QueryAsync(
                    new CustomerReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return TypedResults.Ok(ApiMasterDataDtoFactory.FromCustomers(rows));
            })
            .WithName("ListCustomers");

            endpoints.MapGet("/api/master-data/customers/page", async Task<Results<
                Ok<ApiPagedResponse<ApiCustomerDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerReadRepository repository,
                int pageNumber,
                int pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var page = await repository.QueryPageAsync(new CustomerReadQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Keyword = keyword ?? string.Empty
                }, cancellationToken);
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPage(page, ApiMasterDataDtoFactory.FromCustomers));
            })
            .WithName("ListCustomersPage");

            endpoints.MapGet("/api/master-data/customers/{id:int}", async Task<Results<
                Ok<ApiCustomerDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("客户");
                }

                var customer = await customerService.GetCustomerByIdAsync(id, cancellationToken);
                return customer == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiMasterDataDtoFactory.FromCustomer(customer));
            })
            .WithName("GetCustomer");

            endpoints.MapPost("/api/master-data/customers", async Task<Results<
                Created<ApiCustomerDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                ApiCustomerDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("客户请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增客户不能包含已有ID。"));
                }

                Customer customer;
                try
                {
                    customer = ApiMasterDataDtoFactory.ToCustomerForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("客户");
                }

                customer.Id = 0;
                customer.RowVersion = null;

                int savedId = await customerService.SaveCustomerAsync(customer, cancellationToken);
                var saved = await customerService.GetCustomerByIdAsync(savedId, cancellationToken) ?? customer;
                return TypedResults.Created(
                    $"/api/master-data/customers/{savedId}",
                    ApiMasterDataDtoFactory.FromCustomer(saved));
            })
            .WithName("CreateCustomer")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/master-data/customers/{id:int}", async Task<Results<
                Ok<ApiCustomerDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                int id,
                ApiCustomerDto request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("客户");
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("客户请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体客户ID与路径ID不一致。"));
                }

                if (await customerService.GetCustomerByIdAsync(id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                Customer customer;
                try
                {
                    customer = ApiMasterDataDtoFactory.ToCustomerForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("客户");
                }

                customer.Id = id;

                int savedId = await customerService.SaveCustomerAsync(customer, cancellationToken);
                var saved = await customerService.GetCustomerByIdAsync(savedId, cancellationToken) ?? customer;
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromCustomer(saved));
            })
            .WithName("UpdateCustomer")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/master-data/customers/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("客户");
                }

                if (await customerService.GetCustomerByIdAsync(id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                await customerService.DeleteCustomerAsync(id, cancellationToken);
                return TypedResults.Ok(new ApiCommandResponse(true, "客户已删除。"));
            })
            .WithName("DeleteCustomer")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}
