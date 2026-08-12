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
        private static void MapPayeeMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/payees", async Task<Results<
                Ok<IReadOnlyList<ApiPayeeDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeReadRepository repository,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new PayeeReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPayees(rows));
            })
            .WithName("ListPayees");

            endpoints.MapGet("/api/master-data/payees/page", async Task<Results<
                Ok<ApiPagedResponse<ApiPayeeDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeReadRepository repository,
                int pageNumber,
                int pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return TypedResults.Unauthorized();
                var page = await repository.QueryPageAsync(new PayeeReadQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Keyword = keyword ?? string.Empty
                }, cancellationToken);
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPage(page, ApiMasterDataDtoFactory.FromPayees));
            })
            .WithName("ListPayeesPage");

            endpoints.MapGet("/api/master-data/payees/{id:int}", async Task<Results<
                Ok<ApiPayeeDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("收款对象");
                }

                var payee = await FindPayeeByIdAsync(repository, id, cancellationToken);
                return payee == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiMasterDataDtoFactory.FromPayee(payee));
            })
            .WithName("GetPayee");

            endpoints.MapPost("/api/master-data/payees", async Task<Results<
                Created<ApiPayeeDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeService payeeService,
                IPayeeReadRepository repository,
                ApiPayeeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("收款对象请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增收款对象不能包含已有ID。"));
                }

                Payee payee;
                try
                {
                    payee = ApiMasterDataDtoFactory.ToPayeeForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("收款对象");
                }
                payee.Id = 0;
                payee.RowVersion = null;

                int savedId = await payeeService.SavePayeeAsync(payee, cancellationToken);
                var saved = await FindPayeeByIdAsync(repository, savedId, cancellationToken) ?? payee;
                return TypedResults.Created(
                    $"/api/master-data/payees/{savedId}",
                    ApiMasterDataDtoFactory.FromPayee(saved));
            })
            .WithName("CreatePayee")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/master-data/payees/{id:int}", async Task<Results<
                Ok<ApiPayeeDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeService payeeService,
                IPayeeReadRepository repository,
                int id,
                ApiPayeeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("收款对象");
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("收款对象请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体收款对象ID与路径ID不一致。"));
                }

                if (await FindPayeeByIdAsync(repository, id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                Payee payee;
                try
                {
                    payee = ApiMasterDataDtoFactory.ToPayeeForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("收款对象");
                }
                payee.Id = id;

                int savedId = await payeeService.SavePayeeAsync(payee, cancellationToken);
                var saved = await FindPayeeByIdAsync(repository, savedId, cancellationToken) ?? payee;
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPayee(saved));
            })
            .WithName("UpdatePayee")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/master-data/payees/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeService payeeService,
                IPayeeReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("收款对象");
                }

                if (await FindPayeeByIdAsync(repository, id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                await payeeService.DeletePayeeAsync(id, cancellationToken);
                return TypedResults.Ok(new ApiCommandResponse(true, "收款对象已删除。"));
            })
            .WithName("DeletePayee")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}
