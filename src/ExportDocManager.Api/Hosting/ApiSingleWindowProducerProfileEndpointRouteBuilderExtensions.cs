using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowProducerProfileEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/coo/producer-profiles", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string keyword = context.Request.Query["keyword"].ToString();
                var profiles = string.IsNullOrWhiteSpace(keyword)
                    ? await producerProfileService.GetAllAsync(cancellationToken).ConfigureAwait(false)
                    : await producerProfileService.SearchAsync(keyword, cancellationToken).ConfigureAwait(false);

                return Results.Ok(ApiSingleWindowDtoFactory.FromCustomsCooProducerProfileList(profiles));
            })
            .WithName("ListCustomsCooProducerProfiles");

            endpoints.MapGet("/api/single-window/coo/producer-profiles/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("生产企业资料 ID 必须大于 0。"));
                }

                var profile = await producerProfileService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
                return profile == null
                    ? Results.NotFound(new ApiErrorResponse("生产企业资料不存在。"))
                    : Results.Ok(ApiSingleWindowDtoFactory.FromCustomsCooProducerProfileResponse(profile));
            })
            .WithName("GetCustomsCooProducerProfile");

            endpoints.MapPost("/api/single-window/coo/producer-profiles", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                ApiCustomsCooProducerProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var validationErrors = ValidateCustomsCooProducerProfile(request?.Profile);
                if (validationErrors.Count > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("生产企业资料校验失败：" + string.Join("；", validationErrors)));
                }

                try
                {
                    var saved = await producerProfileService.SaveOrUpdateAsync(
                        ApiSingleWindowDtoFactory.ToCustomsCooProducerProfileInput(request.Profile),
                        cancellationToken).ConfigureAwait(false);

                    return Results.Ok(ApiSingleWindowDtoFactory.FromSavedCustomsCooProducerProfile(
                        saved,
                        "生产企业资料已保存，后续可直接回填到 COO 商品行。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreateCustomsCooProducerProfile");

            endpoints.MapPut("/api/single-window/coo/producer-profiles/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                int id,
                ApiCustomsCooProducerProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("生产企业资料 ID 必须大于 0。"));
                }

                var validationErrors = ValidateCustomsCooProducerProfile(request?.Profile);
                if (validationErrors.Count > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("生产企业资料校验失败：" + string.Join("；", validationErrors)));
                }

                var existing = await producerProfileService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    return Results.NotFound(new ApiErrorResponse("生产企业资料不存在。"));
                }

                try
                {
                    int savedId = await producerProfileService.SaveAsync(
                        ApiSingleWindowDtoFactory.ToCustomsCooProducerProfileInput(request.Profile),
                        id,
                        cancellationToken).ConfigureAwait(false);
                    var saved = await producerProfileService.GetByIdAsync(savedId, cancellationToken).ConfigureAwait(false);

                    return Results.Ok(ApiSingleWindowDtoFactory.FromSavedCustomsCooProducerProfile(
                        saved,
                        "生产企业资料已更新。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdateCustomsCooProducerProfile");

            endpoints.MapDelete("/api/single-window/coo/producer-profiles/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ICustomsCooProducerProfileService producerProfileService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("生产企业资料 ID 必须大于 0。"));
                }

                try
                {
                    bool deleted = await producerProfileService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                    return deleted
                        ? Results.Ok(new ApiCommandResponse(true, "生产企业资料已删除。"))
                        : Results.NotFound(new ApiErrorResponse("生产企业资料不存在。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteCustomsCooProducerProfile");
        }

        private static IReadOnlyList<string> ValidateCustomsCooProducerProfile(
            ApiCustomsCooProducerProfileInputDto profile)
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
            string value,
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
