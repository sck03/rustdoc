using System.Net.Mail;
using ExportDocManager.Models;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string EmailToolStoragePolicy =
            "邮件工具只读取程序根 appsettings.json 中的 SMTP 配置；任意本地附件路径只允许带桌面可信令牌的 Tauri 请求使用，局域网/容器浏览器不得读取服务器文件路径。发送过程不创建默认附件目录、不写数据库，也不把发票/报关数据域与付款/报销数据域按编号合并。";

        private const string EmailServerSuggestionStoragePolicy =
            "邮件服务器配置推断只在内存中返回建议，不保存 appsettings.json、不写数据库、不创建目录。";

        private static void MapEmailToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/tools/email/status", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
            .WithName("GetEmailToolStatus");

            endpoints.MapPost("/api/tools/email/server-suggestion", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiEmailServerSuggestionRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
            .WithName("SuggestEmailServerConfig");

            endpoints.MapPost("/api/tools/email/send", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IEmailService emailService,
                ISettingsService settingsService,
                ApiEmailSendRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                bool allowAttachmentPaths = ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions);
                var validation = ValidateEmailSendRequest(request, allowAttachmentPaths, out var normalizedRequest);
                if (validation != null)
                {
                    return validation;
                }

                await settingsService.LoadAsync();
                var email = settingsService.Settings?.Email ?? new EmailConfig();
                if (string.IsNullOrWhiteSpace(email.SmtpHost) ||
                    string.IsNullOrWhiteSpace(ResolveEmailFromAddress(email)))
                {
                    return WriteConflict("邮件服务尚未配置，请先在设置中填写 SMTP 服务器和发件人。");
                }

                try
                {
                    await emailService.SendEmailAsync(
                        normalizedRequest.ToAddress,
                        normalizedRequest.Subject,
                        normalizedRequest.Body,
                        normalizedRequest.AttachmentPaths.ToList());

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
                    return WriteConflict(ex.Message);
                }
                catch (SmtpException ex)
                {
                    return WriteConflict($"邮件发送失败：{ex.Message}");
                }
            })
            .WithName("SendEmail");

            endpoints.MapPost("/api/tools/email/test-connection", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IEmailService emailService,
                ISettingsService settingsService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

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
                    return WriteConflict("邮件服务尚未配置，请先保存 SMTP 服务器和发件人。");
                }

                try
                {
                    await emailService.TestConnectionAsync(email);

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
                    return WriteConflict(ex.Message);
                }
                catch (SmtpException ex)
                {
                    return WriteConflict($"邮件连接测试失败：{ex.Message}");
                }
            })
            .WithName("TestEmailConnection");
        }

        private static IResult ValidateEmailSendRequest(
            ApiEmailSendRequest request,
            bool allowAttachmentPaths,
            out ApiEmailSendRequest normalizedRequest)
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

            var attachmentPaths = new List<string>();
            foreach (string attachmentPath in requestedAttachmentPaths)
            {
                string trimmed = attachmentPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(trimmed);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return Results.BadRequest(new ApiErrorResponse($"附件路径无效：{ex.Message}"));
                }

                if (!File.Exists(fullPath))
                {
                    return Results.NotFound(new ApiErrorResponse($"附件文件不存在：{fullPath}"));
                }

                if (!attachmentPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    attachmentPaths.Add(fullPath);
                }
            }

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
