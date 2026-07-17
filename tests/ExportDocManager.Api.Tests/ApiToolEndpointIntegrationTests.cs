using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PdfSharp.Pdf;

namespace ExportDocManager.Api.Tests
{
    public class ApiToolEndpointIntegrationTests
    {
        [Fact]
        public async Task ToolEndpoints_ShouldRequireAuthenticationAndPreservePathValidationBehavior()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-tools",
                "api-tools.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousPdfMergeResponse = await anonymousClient.PostAsJsonAsync(
                "/api/tools/pdf/merge/save-to-path",
                new
                {
                    sourceFiles = Array.Empty<string>(),
                    destinationPath = Path.Combine(harness.DataRoot, "merged.pdf")
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPdfMergeResponse.StatusCode);

            var anonymousExchangeRatesResponse = await anonymousClient.GetAsync("/api/tools/exchange-rates");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousExchangeRatesResponse.StatusCode);

            var anonymousEmailServerSuggestionResponse = await anonymousClient.PostAsJsonAsync(
                "/api/tools/email/server-suggestion",
                new
                {
                    emailAddress = "customer@example.com"
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousEmailServerSuggestionResponse.StatusCode);

            var anonymousEmailStatusResponse = await anonymousClient.GetAsync("/api/tools/email/status");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousEmailStatusResponse.StatusCode);

            var anonymousEmailSendResponse = await anonymousClient.PostAsJsonAsync(
                "/api/tools/email/send",
                new
                {
                    toAddress = "customer@example.com",
                    subject = "Documents",
                    body = "Body",
                    attachmentPaths = Array.Empty<string>()
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousEmailSendResponse.StatusCode);

            var anonymousEmailTestResponse = await anonymousClient.PostAsync(
                "/api/tools/email/test-connection",
                content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousEmailTestResponse.StatusCode);

            var anonymousOcrContentResponse = await anonymousClient.PostAsJsonAsync(
                "/api/tools/ocr/recognize-image-content",
                new
                {
                    imageContentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                    sourceName = "clipboard.png",
                    sourceMimeType = "image/png"
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousOcrContentResponse.StatusCode);

            var anonymousLetterOfCreditReviewResponse = await anonymousClient.PostAsJsonAsync(
                "/api/tools/letter-of-credit/review",
                new
                {
                    invoice = new
                    {
                        invoiceNo = "INV-AI-UNAUTHORIZED",
                        letterOfCreditNo = "LC-AI-UNAUTHORIZED"
                    }
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousLetterOfCreditReviewResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var emailStatusResponse = await adminClient.GetAsync("/api/tools/email/status");
            Assert.Equal(HttpStatusCode.OK, emailStatusResponse.StatusCode);
            var emailStatus = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailStatusResponse>(emailStatusResponse);
            Assert.False(emailStatus.IsConfigured);
            Assert.Contains("桌面可信令牌", emailStatus.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("局域网/容器浏览器不得读取服务器文件路径", emailStatus.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不写数据库", emailStatus.StoragePolicy, StringComparison.Ordinal);

            var invalidEmailSuggestionResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/email/server-suggestion",
                new
                {
                    emailAddress = "   "
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidEmailSuggestionResponse.StatusCode);

            var qqEmailSuggestionResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/email/server-suggestion",
                new
                {
                    emailAddress = "user@qq.com"
                });
            Assert.Equal(HttpStatusCode.OK, qqEmailSuggestionResponse.StatusCode);
            var qqEmailSuggestion = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailServerSuggestionResponse>(qqEmailSuggestionResponse);
            Assert.True(qqEmailSuggestion.Success);
            Assert.Equal("user@qq.com", qqEmailSuggestion.EmailAddress);
            Assert.Equal("smtp.qq.com", qqEmailSuggestion.SmtpHost);
            Assert.Equal(465, qqEmailSuggestion.SmtpPort);
            Assert.True(qqEmailSuggestion.EnableSsl);
            Assert.Contains("不保存 appsettings.json", qqEmailSuggestion.StoragePolicy, StringComparison.Ordinal);

            var enterpriseEmailSuggestionResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/email/server-suggestion",
                new
                {
                    emailAddress = "ops@example.com"
                });
            Assert.Equal(HttpStatusCode.OK, enterpriseEmailSuggestionResponse.StatusCode);
            var enterpriseEmailSuggestion = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailServerSuggestionResponse>(enterpriseEmailSuggestionResponse);
            Assert.Equal("smtp.example.com", enterpriseEmailSuggestion.SmtpHost);
            Assert.Equal(465, enterpriseEmailSuggestion.SmtpPort);
            Assert.True(enterpriseEmailSuggestion.EnableSsl);

            var invalidEmailRecipientResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/email/send",
                new
                {
                    toAddress = string.Empty,
                    subject = "Documents",
                    body = "Body",
                    attachmentPaths = Array.Empty<string>()
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidEmailRecipientResponse.StatusCode);

            var missingEmailAttachmentResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/email/send",
                new
                {
                    toAddress = "customer@example.com",
                    subject = "Documents",
                    body = "Body",
                    attachmentPaths = new[] { Path.Combine(harness.DataRoot, "missing.pdf") }
                });
            Assert.Equal(HttpStatusCode.Forbidden, missingEmailAttachmentResponse.StatusCode);

            var unconfiguredEmailResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/email/send",
                new
                {
                    toAddress = "customer@example.com",
                    subject = "Documents",
                    body = "Body",
                    attachmentPaths = Array.Empty<string>()
                });
            Assert.Equal(HttpStatusCode.Conflict, unconfiguredEmailResponse.StatusCode);

            var unconfiguredEmailTestResponse = await adminClient.PostAsync(
                "/api/tools/email/test-connection",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, unconfiguredEmailTestResponse.StatusCode);

            var createOperatorResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                new
                {
                    username = "email-operator",
                    fullName = "Email Operator",
                    role = "User",
                    departmentId = string.Empty,
                    companyScope = string.Empty,
                    isActive = true,
                    resetPassword = "email-pass"
                });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);
            var operatorLogin = await harness.LoginAsync(anonymousClient, "email-operator", "email-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);
            var forbiddenEmailTestResponse = await operatorClient.PostAsync(
                "/api/tools/email/test-connection",
                content: null);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenEmailTestResponse.StatusCode);

            var invalidPdfMergeResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/pdf/merge/save-to-path",
                new
                {
                    sourceFiles = Array.Empty<string>(),
                    destinationPath = Path.Combine(harness.DataRoot, "merged.pdf")
                });
            Assert.Equal(HttpStatusCode.Forbidden, invalidPdfMergeResponse.StatusCode);

            var invalidLetterOfCreditResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/letter-of-credit/import",
                new
                {
                    filePath = string.Empty
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidLetterOfCreditResponse.StatusCode);

            var invalidLetterOfCreditReviewResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/letter-of-credit/review",
                new
                {
                    invoice = new
                    {
                        invoiceNo = "INV-AI-MISSING-CONTEXT",
                        type = "实际数据",
                        letterOfCreditNo = string.Empty,
                        letterOfCreditContent = string.Empty,
                        specialTerms = string.Empty
                    }
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidLetterOfCreditReviewResponse.StatusCode);

            var invalidOcrResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/ocr/recognize-image",
                new
                {
                    filePath = string.Empty
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidOcrResponse.StatusCode);

            var invalidOcrContentResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/ocr/recognize-image-content",
                new
                {
                    imageContentBase64 = "not-base64",
                    sourceName = "clipboard.png",
                    sourceMimeType = "image/png"
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidOcrContentResponse.StatusCode);

            var invalidOcrMimeResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/ocr/recognize-image-content",
                new
                {
                    imageContentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                    sourceName = "clipboard.txt",
                    sourceMimeType = "text/plain"
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidOcrMimeResponse.StatusCode);

            var invalidContainerPackingResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/container-packing/analyze",
                new
                {
                    container = new
                    {
                        length = 0,
                        width = 1,
                        height = 1
                    },
                    cargoItems = new[]
                    {
                        new
                        {
                            quantity = 1,
                            length = 1,
                            width = 1,
                            height = 1,
                            preferredZone = "Front"
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidContainerPackingResponse.StatusCode);

            var invalidExcelImportResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/excel/import-preview",
                new
                {
                    filePath = Path.Combine(harness.DataRoot, "not-excel.txt")
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidExcelImportResponse.StatusCode);

            var invalidExcelExportResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/excel/template/save-to-path",
                new
                {
                    destinationPath = Path.Combine(harness.DataRoot, "template.txt")
                });
            Assert.Equal(HttpStatusCode.Forbidden, invalidExcelExportResponse.StatusCode);

            var invalidBookingSheetResponse = await adminClient.PostAsync(
                "/api/tools/excel/booking-sheet/from-invoice/0/download",
                content: null);
            Assert.Equal(HttpStatusCode.BadRequest, invalidBookingSheetResponse.StatusCode);
        }

        [Fact]
        public async Task EmailAttachmentPaths_ShouldRemainAvailableOnlyToTrustedDesktopRequests()
        {
            const string desktopToken = "email-desktop-token";
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-email-desktop",
                "api-email-desktop.db",
                desktopAccessToken: desktopToken);
            using var desktopClient = harness.CreateClient(desktopAccessToken: desktopToken);
            var login = await harness.LoginAsync(desktopClient, "admin", string.Empty);
            desktopClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);

            var missingAttachmentResponse = await desktopClient.PostAsJsonAsync(
                "/api/tools/email/send",
                new
                {
                    toAddress = "customer@example.com",
                    subject = "Documents",
                    body = "Body",
                    attachmentPaths = new[] { Path.Combine(harness.DataRoot, "missing.pdf") }
                });

            Assert.Equal(HttpStatusCode.NotFound, missingAttachmentResponse.StatusCode);
        }

        [Fact]
        public async Task PdfMergeUpload_ShouldValidateFilesDownloadResultAndCleanupUploadCache()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-pdf-merge-upload",
                "api-pdf-merge-upload.db");
            using var anonymousClient = harness.CreateClient();

            using (var anonymousForm = CreatePdfMergeForm(CreateTestPdfBytes(), CreateTestPdfBytes()))
            {
                var anonymousResponse = await anonymousClient.PostAsync("/api/tools/pdf/merge/upload", anonymousForm);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            }

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            using (var invalidForm = CreatePdfMergeForm(CreateTestPdfBytes()))
            {
                var invalidResponse = await adminClient.PostAsync("/api/tools/pdf/merge/upload", invalidForm);
                Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            }

            BackgroundJobSnapshot acceptedJob;
            using (var validForm = CreatePdfMergeForm(CreateTestPdfBytes(), CreateTestPdfBytes()))
            {
                var acceptedResponse = await adminClient.PostAsync("/api/tools/pdf/merge/upload", validForm);
                Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
                acceptedJob = await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(acceptedResponse);
            }

            var completedJob = await WaitForTerminalJobAsync(adminClient, acceptedJob.JobId);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, completedJob.Status);
            Assert.Equal("PdfMerge", completedJob.Kind);
            Assert.EndsWith(".pdf", completedJob.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFileName(completedJob.OutputPath), completedJob.OutputPath);

            var downloadResponse = await adminClient.GetAsync($"/api/jobs/{acceptedJob.JobId}/download");
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            Assert.Equal("application/pdf", downloadResponse.Content.Headers.ContentType?.MediaType);
            byte[] mergedPdf = await downloadResponse.Content.ReadAsByteArrayAsync();
            Assert.True(mergedPdf.Length > 4);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(mergedPdf, 0, 4));

            string uploadCacheRoot = Path.Combine(harness.DataRoot, "Cache", "BrowserUploads", "PdfMerge");
            if (Directory.Exists(uploadCacheRoot))
            {
                Assert.Empty(Directory.GetDirectories(uploadCacheRoot));
            }
        }

        [Fact]
        public async Task LetterOfCreditReviewEndpoint_ShouldReviewCurrentDraftThroughLoopbackAiOnly()
        {
            await using var aiServer = await FakeOpenAiServer.StartAsync("AI 审查报告：发现 1 项需复核条款。");
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-lc-review",
                "api-lc-review.db");
            File.WriteAllText(
                Path.Combine(harness.AppRoot, "appsettings.json"),
                $$"""
                {
                  "AI": {
                    "ApiEndpoint": "{{aiServer.Endpoint}}",
                    "ApiKey": "",
                    "ModelName": "fake-lc-review",
                    "SystemPrompt": "本地测试信用证审查助手"
                  }
                }
                """);

            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var response = await adminClient.PostAsJsonAsync(
                "/api/tools/letter-of-credit/review",
                new
                {
                    invoice = new
                    {
                        id = 0,
                        invoiceNo = "INV-AI-REVIEW-001",
                        contractNo = "CON-AI-REVIEW",
                        invoiceDate = DateTime.Today,
                        shipmentDate = DateTime.Today,
                        type = "实际数据",
                        letterOfCreditNo = "LC-AI-REVIEW-001",
                        letterOfCreditSourcePath = Path.Combine(harness.DataRoot, "lc-source.txt"),
                        letterOfCreditContent = "LC-CONTENT-MARKER: latest shipment date requires review.",
                        issuingBank = "Loopback Bank",
                        totalAmount = 1234.56m,
                        currency = "USD",
                        portOfLoading = "SHANGHAI",
                        portOfDestination = "HAMBURG",
                        paymentTerms = "L/C AT SIGHT",
                        tradeTerms = "FOB",
                        transportMode = "SEA",
                        specialTerms = "Documents must match L/C exactly."
                    }
                });

            string responseBody = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Expected OK but got {response.StatusCode}: {responseBody}");
            var result = JsonSerializer.Deserialize<ApiLetterOfCreditReviewResponse>(
                responseBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException($"无法解析 API 响应: {responseBody}");
            Assert.Contains("AI 审查报告", result.ReportText, StringComparison.Ordinal);
            Assert.Contains("INV-AI-REVIEW-001", result.ContextSummary, StringComparison.Ordinal);
            Assert.Contains("实际数据", result.ContextSummary, StringComparison.Ordinal);
            Assert.False(result.LetterOfCreditContentTruncated);
            Assert.Contains("当前请求", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取同号另一口径发票", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销单据", result.StoragePolicy, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\", result.StoragePolicy);

            using var aiRequestDocument = JsonDocument.Parse(aiServer.LastRequestBody);
            string aiUserContent = aiRequestDocument.RootElement
                .GetProperty("messages")[1]
                .GetProperty("content")
                .GetString() ?? string.Empty;
            Assert.Contains("LC-CONTENT-MARKER", aiUserContent, StringComparison.Ordinal);
            Assert.Contains("审查边界", aiUserContent, StringComparison.Ordinal);
            Assert.Contains("数据口径: 实际数据", aiUserContent, StringComparison.Ordinal);
            Assert.Contains("不要引用付款/报销单据", aiUserContent, StringComparison.Ordinal);
        }

        private sealed class FakeOpenAiServer : IAsyncDisposable
        {
            private readonly string _reportText;
            private WebApplication _app;

            private FakeOpenAiServer(string endpoint, string reportText)
            {
                Endpoint = endpoint;
                _reportText = reportText;
            }

            public string Endpoint { get; }

            public string LastRequestBody { get; private set; } = string.Empty;

            public static async Task<FakeOpenAiServer> StartAsync(string reportText)
            {
                int port = GetAvailablePort();
                string baseUrl = $"http://127.0.0.1:{port}";
                var server = new FakeOpenAiServer($"{baseUrl}/v1/chat/completions", reportText);
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls(baseUrl);
                var app = builder.Build();
                Func<HttpContext, Task<IResult>> handler = server.HandleAsync;
                app.MapPost("/v1/chat/completions", handler);
                server._app = app;
                await app.StartAsync();
                return server;
            }

            public async ValueTask DisposeAsync()
            {
                if (_app == null)
                {
                    return;
                }

                await _app.StopAsync();
                await _app.DisposeAsync();
            }

            private async Task<IResult> HandleAsync(HttpContext context)
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                LastRequestBody = await reader.ReadToEndAsync();

                return Results.Json(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = _reportText
                            }
                        }
                    }
                });
            }

            private static int GetAvailablePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }

        private static MultipartFormDataContent CreatePdfMergeForm(params byte[][] documents)
        {
            var form = new MultipartFormDataContent();
            for (int index = 0; index < documents.Length; index++)
            {
                var content = new ByteArrayContent(documents[index]);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                form.Add(content, "files", $"source-{index + 1}.pdf");
            }

            return form;
        }

        private static byte[] CreateTestPdfBytes()
        {
            using var document = new PdfDocument();
            document.AddPage();
            using var stream = new MemoryStream();
            document.Save(stream, closeStream: false);
            return stream.ToArray();
        }

        private static async Task<BackgroundJobSnapshot> WaitForTerminalJobAsync(HttpClient client, string jobId)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var response = await client.GetAsync($"/api/jobs/{jobId}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var job = await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(response);
                if (BackgroundJobStatusCatalog.IsTerminal(job.Status))
                {
                    return job;
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"Background job {jobId} did not finish in time.");
        }
    }
}
