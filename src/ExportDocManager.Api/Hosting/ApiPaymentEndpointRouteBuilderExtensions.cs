using ExportDocManager.Models.Entities;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/payments", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentReadRepository paymentReadRepository,
                int? pageNumber,
                int? pageSize,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var result = await paymentReadRepository.QueryPageAsync(
                    new PaymentPageQuery
                    {
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50,
                        Keyword = keyword ?? string.Empty
                    },
                    cancellationToken);

                return Results.Ok(ApiPaymentDtoFactory.FromPagedPayments(result));
            })
            .WithName("ListPayments");

            endpoints.MapGet("/api/payments/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentDetailReadRepository paymentDetailReadRepository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款ID必须大于0。"));
                }

                var payment = await paymentDetailReadRepository.GetByIdAsync(id, cancellationToken);
                return payment == null
                    ? Results.NotFound()
                    : Results.Ok(ApiPaymentDtoFactory.FromPayment(payment));
            })
            .WithName("GetPayment");

            endpoints.MapPost("/api/payments", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentService paymentService,
                IPaymentDetailReadRepository paymentDetailReadRepository,
                ApiPaymentDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增付款不能包含已有ID。"));
                }

                Payment payment;
                int savedId;
                try
                {
                    payment = ApiPaymentDtoFactory.ToPaymentForSave(request);
                    payment.Id = 0;
                    payment.OwnerUserId = null;
                    payment.DepartmentId = string.Empty;
                    payment.CompanyScope = string.Empty;
                    savedId = await paymentService.SavePaymentAsync(payment);
                }
                catch (Exception ex)
                {
                    return WritePaymentFailure(context, ex, "create");
                }

                var savedPayment = await paymentDetailReadRepository.GetByIdAsync(savedId, cancellationToken);
                return Results.Created(
                    $"/api/payments/{savedId}",
                    new ApiPaymentSaveResponse(
                        true,
                        savedId,
                        ApiPaymentDtoFactory.FromPayment(savedPayment ?? payment)));
            })
            .WithName("CreatePayment");

            endpoints.MapPut("/api/payments/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentService paymentService,
                IPaymentDetailReadRepository paymentDetailReadRepository,
                int id,
                ApiPaymentDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款ID必须大于0。"));
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体付款ID与路径ID不一致。"));
                }

                if (string.IsNullOrWhiteSpace(request.RowVersion))
                {
                    return Results.Conflict(new ApiErrorResponse("付款记录缺少版本号，请刷新后重试。"));
                }

                var existing = await paymentDetailReadRepository.GetByIdAsync(id, cancellationToken);
                if (existing == null)
                {
                    return Results.NotFound();
                }

                Payment payment;
                int savedId;
                try
                {
                    payment = ApiPaymentDtoFactory.ToPaymentForSave(request);
                    payment.Id = id;
                    ApiPaymentDtoFactory.PreserveExistingOwnership(payment, existing);
                    savedId = await paymentService.SavePaymentAsync(payment);
                }
                catch (Exception ex)
                {
                    return WritePaymentFailure(context, ex, "update");
                }

                var savedPayment = await paymentDetailReadRepository.GetByIdAsync(savedId, cancellationToken);
                return Results.Ok(new ApiPaymentSaveResponse(
                    true,
                    savedId,
                    ApiPaymentDtoFactory.FromPayment(savedPayment ?? payment)));
            })
            .WithName("UpdatePayment");

            endpoints.MapDelete("/api/payments/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentService paymentService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款ID必须大于0。"));
                }

                bool deleted;
                try
                {
                    deleted = await paymentService.DeletePaymentAsync(id);
                }
                catch (Exception ex)
                {
                    return WritePaymentFailure(context, ex, "delete");
                }

                return deleted
                    ? Results.Ok(new ApiCommandResponse(true, "付款已删除。"))
                    : Results.NotFound();
            })
            .WithName("DeletePayment");
        }

        private static IResult WritePaymentFailure(HttpContext context, Exception exception, string operation)
        {
            return exception switch
            {
                ArgumentException argumentException =>
                    Results.BadRequest(new ApiErrorResponse(argumentException.Message)),
                UnauthorizedAccessException unauthorizedAccessException =>
                    Results.Json(
                        new ApiErrorResponse(unauthorizedAccessException.Message),
                        statusCode: StatusCodes.Status403Forbidden),
                BusinessConcurrencyException concurrencyException =>
                    Results.Conflict(new ApiErrorResponse(concurrencyException.Message)),
                _ => WriteUnexpectedPaymentFailure(context, exception, operation)
            };
        }

        private static IResult WriteUnexpectedPaymentFailure(
            HttpContext context,
            Exception exception,
            string operation)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ExportDocManager.Api.Payments")
                .LogError(exception, "Unexpected payment {Operation} failure.", operation);
            return Results.Json(
                new ApiErrorResponse("付款操作未完成，请稍后重试；若问题持续，请联系管理员查看服务日志。"),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
