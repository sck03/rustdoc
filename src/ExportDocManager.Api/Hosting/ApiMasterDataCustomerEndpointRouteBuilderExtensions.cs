using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapCustomerMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/customers", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerReadRepository repository,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new CustomerReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromCustomers(rows));
            })
            .WithName("ListCustomers");

            endpoints.MapGet("/api/master-data/customers/page", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerReadRepository repository,
                int pageNumber,
                int pageSize,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                var page = await repository.QueryPageAsync(new CustomerReadQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Keyword = keyword ?? string.Empty
                }, cancellationToken);
                return Results.Ok(ApiMasterDataDtoFactory.FromPage(page, ApiMasterDataDtoFactory.FromCustomers));
            })
            .WithName("ListCustomersPage");

            endpoints.MapGet("/api/master-data/customers/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("客户");
                }

                var customer = await customerService.GetCustomerByIdAsync(id, cancellationToken);
                return customer == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromCustomer(customer));
            })
            .WithName("GetCustomer");

            endpoints.MapPost("/api/master-data/customers", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                ApiCustomerDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("客户请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增客户不能包含已有ID。"));
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
                return Results.Created(
                    $"/api/master-data/customers/{savedId}",
                    ApiMasterDataDtoFactory.FromCustomer(saved));
            })
            .WithName("CreateCustomer");

            endpoints.MapPut("/api/master-data/customers/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                int id,
                ApiCustomerDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("客户");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("客户请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体客户ID与路径ID不一致。"));
                }

                if (await customerService.GetCustomerByIdAsync(id, cancellationToken) == null)
                {
                    return Results.NotFound();
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
                return Results.Ok(ApiMasterDataDtoFactory.FromCustomer(saved));
            })
            .WithName("UpdateCustomer");

            endpoints.MapDelete("/api/master-data/customers/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomerService customerService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("客户");
                }

                if (await customerService.GetCustomerByIdAsync(id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                await customerService.DeleteCustomerAsync(id, cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, "客户已删除。"));
            })
            .WithName("DeleteCustomer");
        }
    }
}
