using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPayeeMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/payees", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeReadRepository repository,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new PayeeReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromPayees(rows));
            })
            .WithName("ListPayees");

            endpoints.MapGet("/api/master-data/payees/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("收款对象");
                }

                var payee = await FindPayeeByIdAsync(repository, id, cancellationToken);
                return payee == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromPayee(payee));
            })
            .WithName("GetPayee");

            endpoints.MapPost("/api/master-data/payees", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeService payeeService,
                IPayeeReadRepository repository,
                ApiPayeeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("收款对象请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增收款对象不能包含已有ID。"));
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

                try
                {
                    int savedId = await payeeService.SavePayeeAsync(payee);
                    var saved = await FindPayeeByIdAsync(repository, savedId, cancellationToken) ?? payee;
                    return Results.Created(
                        $"/api/master-data/payees/{savedId}",
                        ApiMasterDataDtoFactory.FromPayee(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreatePayee");

            endpoints.MapPut("/api/master-data/payees/{id:int}", async (
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
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("收款对象");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("收款对象请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体收款对象ID与路径ID不一致。"));
                }

                if (await FindPayeeByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
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

                try
                {
                    int savedId = await payeeService.SavePayeeAsync(payee);
                    var saved = await FindPayeeByIdAsync(repository, savedId, cancellationToken) ?? payee;
                    return Results.Ok(ApiMasterDataDtoFactory.FromPayee(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdatePayee");

            endpoints.MapDelete("/api/master-data/payees/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPayeeService payeeService,
                IPayeeReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("收款对象");
                }

                if (await FindPayeeByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    await payeeService.DeletePayeeAsync(id);
                    return Results.Ok(new ApiCommandResponse(true, "收款对象已删除。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeletePayee");
        }
    }
}
