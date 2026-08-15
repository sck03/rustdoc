using System.Net.Mail;
using ExportDocManager.Models;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string EmailToolStoragePolicy =
            "邮件工具只读取运行数据根 Config/appsettings.json 中的 SMTP 配置；任意本地附件路径只允许带桌面可信令牌的 Tauri 请求使用，局域网/容器浏览器不得读取服务器文件路径。发送过程不创建默认附件目录、不写数据库，也不把发票/报关数据域与付款/报销数据域按编号合并。";

        private const string EmailServerSuggestionStoragePolicy =
            "邮件服务器配置推断只在内存中返回建议，不保存 appsettings.json、不写数据库、不创建目录。";

        private static void MapEmailToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
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
            .Produces<ApiEmailServerSuggestionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/tools/email/send", async (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                IEmailService emailService,
                ISettingsService settingsService,
                ApiEmailSendRequest request,
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
                    await emailService.SendEmailAsync(
                        normalizedRequest.ToAddress,
                        normalizedRequest.Subject,
                        normalizedRequest.Body,
                        normalizedRequest.AttachmentPaths.ToList(),
                        cancellationToken);

                    return Results.Ok(new ApiEmailSendResponse
                    {
                        Success = true,
                        Message = "邮件已发送。",
                        ToAddress = normalizedRequest.ToAddress,
                        Subject = normalizedRequest.Subject,
                        AttachmentCount = normalizedRequest.AttachmentPaths.Count,
                        StoragePolicy = EmailToolStoragePolicy
                    });
                }
                catch (FormatException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
                catch (SmtpException ex)
                {
                    return WriteInfrastructureFailure("邮件发送服务暂时不可用，请稍后重试。", ex);
                }
            })
            .WithName("SendEmail")
            .Produces<ApiEmailSendResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
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
                catch (FormatException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
                catch (SmtpException ex)
                {
                    return WriteInfrastructureFailure("邮件连接服务暂时不可用，请稍后重试。", ex);
                }
            })
            .WithName("TestEmailConnection")
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

            try
            {
                _ = new MailAddress(toAddress);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new ApiErrorResponse($"收件人地址无效：{ex.Message}"));
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
                ToAddress = toAddress,
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

            try
            {
                return new MailAddress(emailAddress.Trim()).Address.Trim();
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }
    }
}
