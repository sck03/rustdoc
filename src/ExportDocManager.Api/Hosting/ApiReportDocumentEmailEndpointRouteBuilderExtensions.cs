using System.Net.Mail;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string InvoiceDocumentEmailStoragePolicy =
            "单据邮件发送只读取当前发票/报关数据域、客户邮箱和程序根 appsettings.json SMTP 配置；临时 PDF 写运行数据根 Cache/ReportDocumentEmails 后自动清理，不读取付款/报销表，不创建默认附件或导出目录。";

        private static void MapInvoiceDocumentEmailEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/document-email", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService,
                ApiBackgroundJobRunner jobRunner,
                int invoiceId,
                ApiInvoiceDocumentEmailRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var validation = ValidateInvoiceDocumentEmailRequest(
                    invoiceId,
                    request,
                    out var normalizedItems,
                    out bool includeMergedPdf,
                    out string toAddress,
                    out string subject,
                    out string body);
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

                return AcceptedBackgroundJob(EnqueueInvoiceDocumentEmailJob(
                    jobRunner,
                    user.Username,
                    invoiceId,
                    normalizedItems,
                    includeMergedPdf,
                    toAddress,
                    subject,
                    body));
            })
            .WithName("StartInvoiceDocumentEmailJob");
        }

        internal static IResult ValidateInvoiceDocumentEmailRequest(
            int invoiceId,
            ApiInvoiceDocumentEmailRequest request,
            out IReadOnlyList<ApiInvoiceDocumentPackageItemRequest> normalizedItems,
            out bool includeMergedPdf,
            out string toAddress,
            out string subject,
            out string body)
        {
            normalizedItems = Array.Empty<ApiInvoiceDocumentPackageItemRequest>();
            includeMergedPdf = false;
            toAddress = string.Empty;
            subject = string.Empty;
            body = string.Empty;

            if (invoiceId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
            }

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("单据邮件请求体不能为空。"));
            }

            var itemValidation = ValidateInvoiceDocumentItemRequests(request.Items, out normalizedItems);
            if (itemValidation != null)
            {
                return itemValidation;
            }

            toAddress = request.ToAddress?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(toAddress))
            {
                try
                {
                    toAddress = new MailAddress(toAddress).Address;
                }
                catch (FormatException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse($"收件人地址无效：{ex.Message}"));
                }
            }

            includeMergedPdf = request.IncludeMergedPdf;
            subject = request.Subject?.Trim() ?? string.Empty;
            body = string.IsNullOrWhiteSpace(request.Body) ? string.Empty : request.Body;
            return null;
        }

        internal static BackgroundJobSnapshot EnqueueInvoiceDocumentEmailJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            int invoiceId,
            IReadOnlyList<ApiInvoiceDocumentPackageItemRequest> items,
            bool includeMergedPdf,
            string toAddress,
            string subject,
            string body)
        {
            return jobRunner.Enqueue(
                "ReportDocumentEmail",
                "发票单据邮件发送",
                requestedBy,
                async (provider, jobContext) =>
                {
                    var pathProvider = provider.GetRequiredService<IAppPathProvider>();
                    string tempRoot = Path.Combine(
                        pathProvider.CacheRoot,
                        "ReportDocumentEmails",
                        jobContext.JobId);

                    try
                    {
                        var documentSet = await GenerateInvoiceDocumentPdfFilesAsync(
                                provider,
                                jobContext,
                                invoiceId,
                                items,
                                tempRoot,
                                includeMergedPdf,
                                8,
                                78,
                                string.Empty)
                            .ConfigureAwait(false);

                        var attachments = documentSet.Entries
                            .Select(entry => entry.SourcePath)
                            .Where(path => !string.IsNullOrWhiteSpace(path))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (attachments.Count == 0)
                        {
                            throw new InvalidOperationException("未能生成任何单据附件。");
                        }

                        string recipient = await ResolveInvoiceDocumentEmailRecipientAsync(
                                provider,
                                documentSet.CustomerId,
                                toAddress)
                            .ConfigureAwait(false);
                        var jobSettingsService = provider.GetRequiredService<ISettingsService>();
                        var emailConfig = jobSettingsService.Settings?.Email ?? new EmailConfig();
                        string emailSubject = BuildInvoiceDocumentEmailSubject(subject, emailConfig, documentSet);
                        string emailBody = BuildInvoiceDocumentEmailBody(body, emailConfig, documentSet);

                        jobContext.Report(
                            84,
                            "正在发送单据邮件",
                            recipient);

                        var emailService = provider.GetRequiredService<IEmailService>();
                        await emailService.SendEmailAsync(
                                recipient,
                                emailSubject,
                                emailBody,
                                attachments)
                            .ConfigureAwait(false);

                        jobContext.Report(
                            98,
                            "单据邮件已发送",
                            $"{recipient} / {attachments.Count} 个附件");

                        return string.Empty;
                    }
                    finally
                    {
                        AtomicFileHelper.TryDeleteDirectory(tempRoot);
                    }
                },
                retryOperation: "StartInvoiceDocumentEmailJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiInvoiceDocumentEmailJobRetryRequest
                {
                    InvoiceId = invoiceId,
                    Body = new ApiInvoiceDocumentEmailRequest
                    {
                        Items = items.ToList(),
                        IncludeMergedPdf = includeMergedPdf,
                        ToAddress = toAddress,
                        Subject = subject,
                        Body = body
                    }
                }));
        }

        private static async Task<string> ResolveInvoiceDocumentEmailRecipientAsync(
            IServiceProvider provider,
            int customerId,
            string requestedToAddress)
        {
            if (!string.IsNullOrWhiteSpace(requestedToAddress))
            {
                return NormalizeInvoiceDocumentEmailRecipient(requestedToAddress, "收件人地址");
            }

            if (customerId > 0)
            {
                var customerService = provider.GetRequiredService<ICustomerService>();
                var customer = await customerService.GetCustomerByIdAsync(customerId).ConfigureAwait(false);
                string customerEmail = customer?.Email?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(customerEmail))
                {
                    return NormalizeInvoiceDocumentEmailRecipient(customerEmail, "客户邮箱");
                }
            }

            throw new InvalidOperationException("收件人地址不能为空，且当前发票客户档案没有邮箱。");
        }

        internal static string BuildInvoiceDocumentEmailSubject(
            string requestedSubject,
            EmailConfig emailConfig,
            ApiInvoiceGeneratedDocumentSet documentSet)
        {
            if (!string.IsNullOrWhiteSpace(requestedSubject))
            {
                return requestedSubject.Trim();
            }

            return ApplyInvoiceDocumentEmailTemplate(
                emailConfig?.DocumentEmailSubjectTemplate,
                EmailConfig.DefaultDocumentEmailSubjectTemplate,
                documentSet);
        }

        internal static string BuildInvoiceDocumentEmailBody(
            string requestedBody,
            EmailConfig emailConfig,
            ApiInvoiceGeneratedDocumentSet documentSet)
        {
            if (!string.IsNullOrWhiteSpace(requestedBody))
            {
                return requestedBody;
            }

            return ApplyInvoiceDocumentEmailTemplate(
                emailConfig?.DocumentEmailBodyTemplate,
                EmailConfig.DefaultDocumentEmailBodyTemplate,
                documentSet);
        }

        private static string ApplyInvoiceDocumentEmailTemplate(
            string configuredTemplate,
            string fallbackTemplate,
            ApiInvoiceGeneratedDocumentSet documentSet)
        {
            string template = string.IsNullOrWhiteSpace(configuredTemplate)
                ? fallbackTemplate
                : configuredTemplate;
            string invoiceNo = documentSet?.InvoiceNo ?? string.Empty;
            string customerName = documentSet?.CustomerName ?? string.Empty;
            string dateText = (documentSet?.ExportDate ?? DateTime.Now).ToString("yyyyMMdd");

            return template
                .Replace("{InvoiceNo}", invoiceNo, StringComparison.OrdinalIgnoreCase)
                .Replace("{Customer}", customerName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Date}", dateText, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeInvoiceDocumentEmailRecipient(
            string address,
            string label)
        {
            try
            {
                return new MailAddress(address?.Trim() ?? string.Empty).Address;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"{label}无效：{ex.Message}", ex);
            }
        }

        internal sealed class ApiInvoiceDocumentEmailJobRetryRequest
        {
            public int InvoiceId { get; set; }

            public ApiInvoiceDocumentEmailRequest Body { get; set; } = new();
        }
    }
}
