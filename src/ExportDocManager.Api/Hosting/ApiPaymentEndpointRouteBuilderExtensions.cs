using ExportDocManager.Models.Entities;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/payments", async Task<Results<
                Ok<ApiPagedResponse<ApiPaymentDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentReadRepository paymentReadRepository,
                int? pageNumber,
                int? pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                var result = await paymentReadRepository.QueryPageAsync(
                    new PaymentPageQuery
                    {
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50,
                        Keyword = keyword ?? string.Empty
                    },
                    cancellationToken);

                return TypedResults.Ok(ApiPaymentDtoFactory.FromPagedPayments(result));
            })
            .WithName("ListPayments");

            endpoints.MapGet("/api/payments/{id:int}", async Task<Results<
                Ok<ApiPaymentDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentDetailReadRepository paymentDetailReadRepository,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("付款ID必须大于0。"));
                }

                var payment = await paymentDetailReadRepository.GetByIdAsync(id, cancellationToken);
                return payment == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiPaymentDtoFactory.FromPayment(payment));
            })
            .WithName("GetPayment");

            endpoints.MapPost("/api/payments", async Task<Results<
                Created<ApiPaymentSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentService paymentService,
                IPaymentDetailReadRepository paymentDetailReadRepository,
                ApiPaymentDto request,
                CancellationToken cancellationToken) =>
            {

                if (request is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("付款请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增付款不能包含已有ID。"));
                }

                Payment payment = ApiPaymentDtoFactory.ToPaymentForSave(request);
                payment.Id = 0;
                payment.OwnerUserId = null;
                payment.DepartmentId = string.Empty;
                payment.CompanyScope = string.Empty;
                int savedId = await paymentService.SavePaymentAsync(payment, cancellationToken);

                var savedPayment = await paymentDetailReadRepository.GetByIdAsync(savedId, cancellationToken);
                return TypedResults.Created(
                    $"/api/payments/{savedId}",
                    new ApiPaymentSaveResponse(
                        true,
                        savedId,
                        ApiPaymentDtoFactory.FromPayment(savedPayment ?? payment)));
            })
            .WithName("CreatePayment")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/payments/{id:int}", async Task<Results<
                Ok<ApiPaymentSaveResponse>,
                BadRequest<ApiErrorResponse>,
                Conflict<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentService paymentService,
                IPaymentDetailReadRepository paymentDetailReadRepository,
                int id,
                ApiPaymentDto request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("付款ID必须大于0。"));
                }

                if (request is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("付款请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体付款ID与路径ID不一致。"));
                }

                if (string.IsNullOrWhiteSpace(request.RowVersion))
                {
                    return TypedResults.Conflict(new ApiErrorResponse("付款记录缺少版本号，请刷新后重试。"));
                }

                var existing = await paymentDetailReadRepository.GetByIdAsync(id, cancellationToken);
                if (existing == null)
                {
                    return TypedResults.NotFound();
                }

                Payment payment = ApiPaymentDtoFactory.ToPaymentForSave(request);
                payment.Id = id;
                ApiPaymentDtoFactory.PreserveExistingOwnership(payment, existing);
                int savedId = await paymentService.SavePaymentAsync(payment, cancellationToken);

                var savedPayment = await paymentDetailReadRepository.GetByIdAsync(savedId, cancellationToken);
                return TypedResults.Ok(new ApiPaymentSaveResponse(
                    true,
                    savedId,
                    ApiPaymentDtoFactory.FromPayment(savedPayment ?? payment)));
            })
            .WithName("UpdatePayment")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/payments/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPaymentService paymentService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("付款ID必须大于0。"));
                }

                bool deleted = await paymentService.DeletePaymentAsync(id, cancellationToken);

                return deleted
                    ? TypedResults.Ok(new ApiCommandResponse(true, "付款已删除。"))
                    : TypedResults.NotFound();
            })
            .WithName("DeletePayment");
        }

    }
}
