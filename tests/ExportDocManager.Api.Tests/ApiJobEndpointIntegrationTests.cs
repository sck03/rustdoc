using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiJobEndpointIntegrationTests
    {
        [Fact]
        public async Task JobEndpoints_ShouldRequireAuthenticationAndPreserveEmptyQueueBehavior()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-jobs",
                "api-jobs.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/jobs");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var listResponse = await adminClient.GetAsync("/api/jobs?pageNumber=2&pageSize=5&status=Running&keyword=missing");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using (var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
            {
                var root = listDocument.RootElement;
                Assert.Equal(0, root.GetProperty("totalCount").GetInt32());
                Assert.Equal(2, root.GetProperty("pageNumber").GetInt32());
                Assert.Equal(5, root.GetProperty("pageSize").GetInt32());
                Assert.Empty(root.GetProperty("items").EnumerateArray());
            }

            var getMissingResponse = await adminClient.GetAsync("/api/jobs/missing-job");
            Assert.Equal(HttpStatusCode.NotFound, getMissingResponse.StatusCode);

            var cancelMissingResponse = await adminClient.PostAsync("/api/jobs/missing-job/cancel", content: null);
            Assert.Equal(HttpStatusCode.Conflict, cancelMissingResponse.StatusCode);

            var retryMissingResponse = await adminClient.PostAsync("/api/jobs/missing-job/retry", content: null);
            Assert.Equal(HttpStatusCode.NotFound, retryMissingResponse.StatusCode);
        }

        [Fact]
        public async Task JobEndpoints_ShouldScopeRegularUsersToOwnJobs()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-jobs-scope",
                "api-jobs-scope.db");
            CreateExcelImportTemplate(harness.AppRoot);
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);
            await CreateUserAsync(adminClient, "job-alice", "alice-pass");
            await CreateUserAsync(adminClient, "job-bob", "bob-pass");

            var aliceLogin = await harness.LoginAsync(anonymousClient, "job-alice", "alice-pass");
            var bobLogin = await harness.LoginAsync(anonymousClient, "job-bob", "bob-pass");
            using var aliceClient = harness.CreateClient(aliceLogin.AccessToken);
            using var bobClient = harness.CreateClient(bobLogin.AccessToken);

            string outputRoot = Path.Combine(harness.DataRoot, "JobScopeOutputs");
            Directory.CreateDirectory(outputRoot);
            var adminJob = await StartTemplateExportJobAsync(adminClient, Path.Combine(outputRoot, "admin-template.xlsx"));
            var aliceJob = await StartTemplateExportJobAsync(aliceClient, Path.Combine(outputRoot, "alice-template.xlsx"));
            var bobJob = await StartTemplateExportJobAsync(bobClient, Path.Combine(outputRoot, "bob-template.xlsx"));

            var adminJobs = await ReadJobsAsync(adminClient);
            Assert.Contains(adminJobs, job => job.JobId == adminJob.JobId);
            Assert.Contains(adminJobs, job => job.JobId == aliceJob.JobId);
            Assert.Contains(adminJobs, job => job.JobId == bobJob.JobId);

            var aliceJobs = await ReadJobsAsync(aliceClient);
            Assert.Contains(aliceJobs, job => job.JobId == aliceJob.JobId);
            Assert.DoesNotContain(aliceJobs, job => job.JobId == adminJob.JobId);
            Assert.DoesNotContain(aliceJobs, job => job.JobId == bobJob.JobId);

            var bobJobs = await ReadJobsAsync(bobClient);
            Assert.Contains(bobJobs, job => job.JobId == bobJob.JobId);
            Assert.DoesNotContain(bobJobs, job => job.JobId == adminJob.JobId);
            Assert.DoesNotContain(bobJobs, job => job.JobId == aliceJob.JobId);

            var aliceOwnResponse = await aliceClient.GetAsync($"/api/jobs/{aliceJob.JobId}");
            Assert.Equal(HttpStatusCode.OK, aliceOwnResponse.StatusCode);

            var completedAliceJob = await WaitForTerminalJobAsync(aliceClient, aliceJob.JobId);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, completedAliceJob.Status);
            Assert.Equal("导入数据模板.xlsx", completedAliceJob.OutputPath);
            Assert.False(Path.IsPathRooted(completedAliceJob.OutputPath));
            var aliceDownloadResponse = await aliceClient.GetAsync($"/api/jobs/{aliceJob.JobId}/download");
            Assert.Equal(HttpStatusCode.OK, aliceDownloadResponse.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", aliceDownloadResponse.Content.Headers.ContentType?.MediaType);

            var aliceForeignResponse = await aliceClient.GetAsync($"/api/jobs/{bobJob.JobId}");
            Assert.Equal(HttpStatusCode.NotFound, aliceForeignResponse.StatusCode);
            var aliceForeignDownloadResponse = await aliceClient.GetAsync($"/api/jobs/{bobJob.JobId}/download");
            Assert.Equal(HttpStatusCode.NotFound, aliceForeignDownloadResponse.StatusCode);

            var bobRetryAliceResponse = await bobClient.PostAsync($"/api/jobs/{aliceJob.JobId}/retry", content: null);
            Assert.Equal(HttpStatusCode.NotFound, bobRetryAliceResponse.StatusCode);
        }

        [Fact]
        public async Task BackgroundJobService_ShouldFilterAndClearByRequestedBy()
        {
            var service = new ApiBackgroundJobService();
            service.Upsert(CreateJob("job-admin", "admin", BackgroundJobStatusCatalog.Succeeded));
            service.Upsert(CreateJob("job-alice", "job-alice", BackgroundJobStatusCatalog.Succeeded));
            service.Upsert(CreateJob("job-bob", "job-bob", BackgroundJobStatusCatalog.Succeeded));
            service.Upsert(CreateJob("job-alice-running", "job-alice", BackgroundJobStatusCatalog.Running));

            var alicePage = await service.QueryAsync(new BackgroundJobQuery
            {
                RequestedBy = "job-alice",
                PageNumber = 1,
                PageSize = 10
            });

            Assert.Equal(2, alicePage.TotalCount);
            Assert.All(alicePage.Items, job => Assert.Equal("job-alice", job.RequestedBy));

            int cleared = await service.ClearTerminalAsync("job-alice");
            Assert.Equal(1, cleared);

            var allJobs = await service.QueryAsync(new BackgroundJobQuery { PageNumber = 1, PageSize = 10 });
            Assert.Contains(allJobs.Items, job => job.JobId == "job-admin");
            Assert.Contains(allJobs.Items, job => job.JobId == "job-bob");
            Assert.Contains(allJobs.Items, job => job.JobId == "job-alice-running");
            Assert.DoesNotContain(allJobs.Items, job => job.JobId == "job-alice");
        }

        private static BackgroundJobSnapshot CreateJob(string jobId, string requestedBy, string status)
        {
            return new BackgroundJobSnapshot
            {
                JobId = jobId,
                Kind = "Test",
                Title = jobId,
                RequestedBy = requestedBy,
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static async Task CreateUserAsync(HttpClient adminClient, string username, string password)
        {
            var response = await adminClient.PostAsJsonAsync(
                "/api/users",
                new ApiUserSaveRequest(
                    username,
                    username,
                    UserRoleCatalog.User,
                    null,
                    string.Empty,
                    string.Empty,
                    true,
                    password));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task<BackgroundJobSnapshot> StartTemplateExportJobAsync(
            HttpClient client,
            string destinationPath)
        {
            var response = await client.PostAsJsonAsync(
                "/api/tools/excel/template/download",
                new { });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(response);
        }

        private static async Task<IReadOnlyList<BackgroundJobSnapshot>> ReadJobsAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/jobs?pageNumber=1&pageSize=20");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .Select(item => new BackgroundJobSnapshot
                {
                    JobId = item.GetProperty("jobId").GetString() ?? string.Empty,
                    RequestedBy = item.GetProperty("requestedBy").GetString() ?? string.Empty
                })
                .ToArray();
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

        private static void CreateExcelImportTemplate(string appRoot)
        {
            string templatePath = Path.Combine(appRoot, "Resources", "ExcelTemplates", "invoice-import-template.xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
            using var workbook = new XLWorkbook();
            workbook.AddWorksheet("Invoice");
            workbook.SaveAs(templatePath);
        }
    }
}
