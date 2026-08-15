using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowProducerProfileEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/coo/producer-profiles", async Task<Results<
                Ok<ApiCustomsCooProducerProfileListResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                var profiles = await producerProfileService
                    .SearchAsync(keyword ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);

                return TypedResults.Ok(ApiSingleWindowDtoFactory.FromCustomsCooProducerProfileList(profiles));
            })
            .WithName("ListCustomsCooProducerProfiles");

            endpoints.MapGet("/api/single-window/coo/producer-profiles/{id:int}", async Task<Results<
                Ok<ApiCustomsCooProducerProfileResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料 ID 必须大于 0。"));
                }

                var profile = await producerProfileService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
                return profile == null
                    ? TypedResults.NotFound(new ApiErrorResponse("生产企业资料不存在。"))
                    : TypedResults.Ok(ApiSingleWindowDtoFactory.FromCustomsCooProducerProfileResponse(profile));
            })
            .WithName("GetCustomsCooProducerProfile");

            endpoints.MapPost("/api/single-window/coo/producer-profiles", async Task<Results<
                Ok<ApiCustomsCooProducerProfileSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                ApiCustomsCooProducerProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {

                ApiCustomsCooProducerProfileInputDto? profile = request?.Profile;
                var validationErrors = ValidateCustomsCooProducerProfile(profile);
                if (validationErrors.Count > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料校验失败：" + string.Join("；", validationErrors)));
                }

                if (profile is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料请求体不能为空。"));
                }

                var saved = await producerProfileService.SaveOrUpdateAsync(
                    ApiSingleWindowDtoFactory.ToCustomsCooProducerProfileInput(profile),
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(ApiSingleWindowDtoFactory.FromSavedCustomsCooProducerProfile(
                    saved,
                    "生产企业资料已保存，后续可直接回填到 COO 商品行。"));
            })
            .WithName("CreateCustomsCooProducerProfile")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/single-window/coo/producer-profiles/{id:int}", async Task<Results<
                Ok<ApiCustomsCooProducerProfileSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                int id,
                ApiCustomsCooProducerProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料 ID 必须大于 0。"));
                }

                ApiCustomsCooProducerProfileInputDto? profile = request?.Profile;
                var validationErrors = ValidateCustomsCooProducerProfile(profile);
                if (validationErrors.Count > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料校验失败：" + string.Join("；", validationErrors)));
                }

                if (profile is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料请求体不能为空。"));
                }

                var existing = await producerProfileService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    return TypedResults.NotFound(new ApiErrorResponse("生产企业资料不存在。"));
                }

                int savedId = await producerProfileService.SaveAsync(
                    ApiSingleWindowDtoFactory.ToCustomsCooProducerProfileInput(profile),
                    id,
                    cancellationToken).ConfigureAwait(false);
                var updated = await producerProfileService.GetByIdAsync(savedId, cancellationToken).ConfigureAwait(false);

                if (updated is null)
                {
                    return TypedResults.NotFound(new ApiErrorResponse("已保存的生产企业资料不存在。"));
                }

                return TypedResults.Ok(ApiSingleWindowDtoFactory.FromSavedCustomsCooProducerProfile(
                    updated,
                    "生产企业资料已更新。"));
            })
            .WithName("UpdateCustomsCooProducerProfile")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/single-window/coo/producer-profiles/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("生产企业资料 ID 必须大于 0。"));
                }

                bool deleted = await producerProfileService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                return deleted
                    ? TypedResults.Ok(new ApiCommandResponse(true, "生产企业资料已删除。"))
                    : TypedResults.NotFound(new ApiErrorResponse("生产企业资料不存在。"));
            })
            .WithName("DeleteCustomsCooProducerProfile")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }

        private static IReadOnlyList<string> ValidateCustomsCooProducerProfile(
            ApiCustomsCooProducerProfileInputDto? profile)
        {
            var errors = new List<string>();
            if (profile == null)
            {
                errors.Add("请求体不能为空");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(profile.CiqRegNo) &&
                string.IsNullOrWhiteSpace(profile.PrdcEtpsName))
            {
                errors.Add("生产企业代码或生产企业名称至少填写一个");
            }

            AddMaxLengthError(errors, profile.CiqRegNo, 20, "生产企业代码");
            AddMaxLengthError(errors, profile.PrdcEtpsName, 400, "生产企业名称");
            AddMaxLengthError(errors, profile.PrdcEtpsConcEr, 80, "生产企业联系人");
            AddMaxLengthError(errors, profile.PrdcEtpsTel, 80, "联系电话");
            AddMaxLengthError(errors, profile.Producer, 1000, "生产商描述");
            AddMaxLengthError(errors, profile.ProducerTel, 80, "生产商电话");
            AddMaxLengthError(errors, profile.ProducerFax, 80, "生产商传真");
            AddMaxLengthError(errors, profile.ProducerEmail, 120, "生产商邮箱");
            AddMaxLengthError(errors, profile.ProducerSertFlag, 10, "生产商保密标记");
            AddMaxLengthError(errors, profile.LastInvoiceNo, 80, "最近发票号");
            AddMaxLengthError(errors, profile.LastContractNo, 80, "最近合同号");
            AddMaxLengthError(errors, profile.LastSourceStyleNo, 80, "最近款号");

            string secretFlag = profile.ProducerSertFlag?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(secretFlag) &&
                !string.Equals(secretFlag, "Y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(secretFlag, "N", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("生产商保密标记只能是 Y 或 N");
            }

            string email = profile.ProducerEmail?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(email) &&
                (!email.Contains('@', StringComparison.Ordinal) || email.StartsWith("@", StringComparison.Ordinal) || email.EndsWith("@", StringComparison.Ordinal)))
            {
                errors.Add("生产商邮箱格式不正确");
            }

            return errors;
        }

        private static void AddMaxLengthError(
            ICollection<string> errors,
            string? value,
            int maxLength,
            string displayName)
        {
            if ((value?.Trim().Length ?? 0) > maxLength)
            {
                errors.Add($"{displayName}不能超过 {maxLength} 个字符");
            }
        }
    }
}
