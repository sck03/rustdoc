using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Mvc;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string EmailToolStoragePolicy =
            "邮件工具读取运行数据根 Config/appsettings.json 中的 SMTP 配置，并在当前业务数据库保存投递状态和幂等键；任意本地附件路径只允许带桌面可信令牌的 Tauri 请求使用，局域网/容器浏览器不得读取服务器文件路径。发送过程不创建默认附件目录，也不把发票/报关数据域与付款/报销数据域按编号合并。";

        private const string EmailServerSuggestionStoragePolicy =
            "邮件服务器配置推断只在内存中返回建议，不保存 appsettings.json、不写数据库、不创建目录。";

        private static void MapEmailToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/tools/email/deliveries", async (
                IEmailDeliveryStore deliveryStore,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                var rows = await deliveryStore.ListRecentAsync(limit ?? 50, cancellationToken).ConfigureAwait(false);
                return Results.Ok(rows.Select(item => new ApiEmailDeliveryDto(
                    item.DeliveryId,
                    item.JobId,
                    item.Kind,
                    item.Recipient,
                    item.Subject,
                    item.AttachmentCount,
                    item.Status,
                    item.ErrorMessage,
                    item.CreatedAt,
                    item.SentAt,
                    item.UpdatedAt)));
            })
            .WithName("ListEmailDeliveries")
            .WithApiCapability(PermissionResourceCatalog.EmailDelivery, PermissionAction.ViewDelivery)
            .Produces<IReadOnlyList<ApiEmailDeliveryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/tools/email/status", async (
                HttpContext context,
                ISettingsService settingsService) =>
            {

                await settingsService.LoadAsync();
                var email = settingsService.Settings?.Email ?? new EmailConfig();
                string fromAddress = ResolveEmailFromAddress(email);

                return Results.Ok(new ApiEmailStatusResponse
                {
                    IsConfigured = !string.IsNullOrWhiteSpace(email.SmtpHost) &&
                        !string.IsNullOrWhiteSpace(fromAddress),
                    SmtpHost = email.SmtpHost?.Trim() ?? string.Empty,
                    SmtpPort = email.SmtpPort,
                    EnableSsl = email.EnableSsl,
                    FromAddress = fromAddress,
                    FromDisplayName = email.FromDisplayName?.Trim() ?? string.Empty,
                    StoragePolicy = EmailToolStoragePolicy
                });
            })
            .WithName("GetEmailToolStatus")
            .WithApiCapability(PermissionResourceCatalog.EmailDelivery, PermissionAction.ViewDelivery)
            .Produces<ApiEmailStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/tools/email/server-suggestion", (
                HttpContext context,
                ApiEmailServerSuggestionRequest request) =>
            {

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("邮箱地址不能为空。"));
                }

                string emailAddress = NormalizeEmailAddress(request.EmailAddress);
                if (string.IsNullOrWhiteSpace(emailAddress))
                {
                    return Results.BadRequest(new ApiErrorResponse("邮箱地址无效。"));
                }

                var suggestion = MailServerHelper.GetServerConfig(emailAddress);
                if (!suggestion.HasValue)
                {
                    return Results.BadRequest(new ApiErrorResponse("邮箱地址无效。"));
                }

                return Results.Ok(new ApiEmailServerSuggestionResponse
                {
                    Success = true,
                    Message = $"已根据 {emailAddress.Split('@')[1]} 推断 SMTP 配置。",
                    EmailAddress = emailAddress,
                    SmtpHost = suggestion.Value.SmtpHost,
                    SmtpPort = suggestion.Value.Port,
                    EnableSsl = suggestion.Value.Ssl,
                    StoragePolicy = EmailServerSuggestionStoragePolicy
                });
            })
            .WithName("SuggestEmailServerConfig")
            .WithApiCapability(PermissionResourceCatalog.EmailPolicy, PermissionAction.Configure)
            .Produces<ApiEmailServerSuggestionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/tools/email/send", async (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                IEmailService emailService,
                IEmailDeliveryStore deliveryStore,
                ISettingsService settingsService,
                ApiEmailSendRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                CancellationToken cancellationToken) =>
            {

                bool allowAttachmentPaths = ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions);
                var validation = ValidateEmailSendRequest(request, allowAttachmentPaths, out var normalizedRequest);
                if (validation != null)
                {
                    return validation;
                }

                if (normalizedRequest is null)
                {
                    return Results.BadRequest(new ApiErrorResponse("邮件发送请求体无效。"));
                }

                await settingsService.LoadAsync();
                var email = settingsService.Settings?.Email ?? new EmailConfig();
                if (string.IsNullOrWhiteSpace(email.SmtpHost) ||
                    string.IsNullOrWhiteSpace(ResolveEmailFromAddress(email)))
                {
                    return WriteValidation("邮件服务尚未配置，请先在设置中填写 SMTP 服务器和发件人。");
                }

                try
                {
                    string recipient = EmailRecipientPolicy.ValidateAndNormalize(
                        normalizedRequest.ToAddress,
                        email);
                    string deliveryId = string.IsNullOrWhiteSpace(idempotencyKey)
                        ? Guid.NewGuid().ToString("N")
                        : idempotencyKey.Trim();
                    if (deliveryId.Length is < 8 or > 120)
                    {
                        return Results.BadRequest(new ApiErrorResponse("Idempotency-Key 长度无效。"));
                    }

                    var delivery = await deliveryStore.BeginAsync(
                            deliveryId,
                            EmailDeliveryFingerprint.Create([
                                "EmailTool",
                                recipient,
                                normalizedRequest.Subject,
                                normalizedRequest.Body,
                                .. normalizedRequest.AttachmentPaths
                            ]),
                            string.Empty,
                            "EmailTool",
                            recipient,
                            normalizedRequest.Subject,
                            normalizedRequest.AttachmentPaths.Count,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!delivery.ShouldSend)
                    {
                        return delivery.AlreadySent
                            ? Results.Ok(new ApiEmailSendResponse
                            {
                                Success = true,
                                Message = "邮件已发送（幂等请求）。",
                                ToAddress = recipient,
                                Subject = normalizedRequest.Subject,
                                AttachmentCount = normalizedRequest.AttachmentPaths.Count,
                                StoragePolicy = EmailToolStoragePolicy
                            })
                            : Results.Conflict(new ApiErrorResponse(delivery.ErrorMessage ?? "邮件已尝试投递，结果不确定。"));
                    }

                    try
                    {
                        await emailService.SendEmailAsync(
                            recipient,
                            normalizedRequest.Subject,
                            normalizedRequest.Body,
                            normalizedRequest.AttachmentPaths.ToList(),
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await deliveryStore.MarkUncertainAsync(deliveryId, ex.Message, CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }

                    await deliveryStore.MarkSentAsync(deliveryId, CancellationToken.None).ConfigureAwait(false);

                    return Results.Ok(new ApiEmailSendResponse
                    {
                        Success = true,
                        Message = "邮件已发送。",
                        ToAddress = recipient,
                        Subject = normalizedRequest.Subject,
                        AttachmentCount = normalizedRequest.AttachmentPaths.Count,
                        StoragePolicy = EmailToolStoragePolicy
                    });
                }
                catch (ServiceException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("SendEmail")
            .WithApiCapability(PermissionResourceCatalog.EmailDelivery, PermissionAction.Send)
            .WithApiResourceProfile(ApiResourceProfile.EmailDelivery)
            .WithApiSecurityAudit("email-delivery")
            .Produces<ApiEmailSendResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/tools/email/test-connection", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IEmailService emailService,
                ISettingsService settingsService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以测试 SMTP 连接。");
                }

                await settingsService.LoadAsync();
                var email = settingsService.Settings?.Email ?? new EmailConfig();
                string fromAddress = ResolveEmailFromAddress(email);
                if (string.IsNullOrWhiteSpace(email.SmtpHost) ||
                    string.IsNullOrWhiteSpace(fromAddress))
                {
                    return WriteValidation("邮件服务尚未配置，请先保存 SMTP 服务器和发件人。");
                }

                try
                {
                    await emailService.TestConnectionAsync(email, cancellationToken);

                    return Results.Ok(new ApiEmailTestResponse
                    {
                        Success = true,
                        Message = "邮件连接测试成功，测试邮件已发送到发件人地址。",
                        FromAddress = fromAddress,
                        SmtpHost = email.SmtpHost?.Trim() ?? string.Empty,
                        StoragePolicy = EmailToolStoragePolicy
                    });
                }
                catch (ServiceException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("TestEmailConnection")
            .WithApiCapability(PermissionResourceCatalog.EmailPolicy, PermissionAction.Configure)
            .Produces<ApiEmailTestResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        }

        private static IResult? ValidateEmailSendRequest(
            ApiEmailSendRequest? request,
            bool allowAttachmentPaths,
            out ApiEmailSendRequest? normalizedRequest)
        {
            normalizedRequest = null;

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("邮件发送请求体不能为空。"));
            }

            string toAddress = request.ToAddress?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toAddress))
            {
                return Results.BadRequest(new ApiErrorResponse("收件人地址不能为空。"));
            }

            if (!ApiEmailAddressPolicy.TryNormalize(toAddress, out string normalizedToAddress))
            {
                return Results.BadRequest(new ApiErrorResponse("收件人地址无效。"));
            }

            var requestedAttachmentPaths = (request.AttachmentPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            if (requestedAttachmentPaths.Length > 0 && !allowAttachmentPaths)
            {
                return WriteForbidden("局域网或容器浏览器不能读取服务器文件路径作为附件，请从受控单据输出入口发送附件。");
            }

            var attachmentPaths = EmailAttachmentPolicy.ValidateAndNormalize(requestedAttachmentPaths);

            normalizedRequest = new ApiEmailSendRequest
            {
                ToAddress = normalizedToAddress,
                Subject = request.Subject?.Trim() ?? string.Empty,
                Body = string.IsNullOrWhiteSpace(request.Body)
                    ? "Dear Customer,\r\n\r\nPlease find the attached export documents.\r\n\r\nBest regards,"
                    : request.Body,
                AttachmentPaths = attachmentPaths
            };
            return null;
        }

        private static string ResolveEmailFromAddress(EmailConfig email)
        {
            if (!string.IsNullOrWhiteSpace(email.FromAddress))
            {
                return email.FromAddress.Trim();
            }

            return email.UserName?.Trim() ?? string.Empty;
        }

        private static string NormalizeEmailAddress(string emailAddress)
        {
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                return string.Empty;
            }

            return ApiEmailAddressPolicy.TryNormalize(emailAddress, out string normalized)
                ? normalized
                : string.Empty;
        }
    }
}
