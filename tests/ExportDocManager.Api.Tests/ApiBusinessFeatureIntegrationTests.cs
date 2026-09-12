using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Attachments;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Worklist;

namespace ExportDocManager.Api.Tests;

public sealed class ApiBusinessFeatureIntegrationTests
{
    [Fact]
    public async Task InvoiceDraftReviewAndAttachments_ShouldUseOneContractAndRetainArchivedFiles()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("business-features", "features.db");
        using var anonymous = harness.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/worklist")).StatusCode);
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        Assert.Contains("business-attachments", login.User.Capabilities.AvailableFeatures);
        Assert.Contains("worklist", login.User.Capabilities.AvailableFeatures);
        var draft = new ApiInvoiceDetailDto
        {
            InvoiceNo = "NEW-CUSTOMER-STYLE",
            InvoiceDate = new(2026, 9, 8),
            ShipmentDate = new(2026, 9, 8),
            NotifyPartyMode = NotifyPartyMode.Separate,
            Items = [new ApiInvoiceItemDto { StyleNo = "PO-STYLE-NEW", Quantity = 0 }]
        };
        var savedResponse = await client.PostAsJsonAsync("/api/invoices", draft);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(savedResponse);
        var review = await ApiIntegrationTestHarness.ReadJsonAsync<InvoiceReviewResult>(await client.PostAsJsonAsync("/api/invoices/review", draft));
        Assert.False(review.Ready);
        Assert.Contains(review.Issues, issue => issue.RowNumber == 1 && issue.Field == "quantity");
        Assert.Contains(review.Issues, issue => issue.Field == "notifyPartyName");
        var worklist = await ApiIntegrationTestHarness.ReadJsonAsync<WorklistPage>(await client.GetAsync("/api/worklist?source=invoice-review"));
        Assert.Equal(saved.Id, Assert.Single(worklist.Page.Items).RecordId);
        var url = $"/api/invoices/{saved.Id}/attachments";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(Guid.NewGuid().ToString()), "uploadKey");
        content.Add(new StringContent("Source"), "title");
        content.Add(new StringContent("1"), "categoryId");
        content.Add(new StringContent(new string('样', 500)), "note");
        content.Add(new ByteArrayContent("archived bytes"u8.ToArray()), "file", "source.txt");
        var uploadedResponse = await client.PostAsync(url, content);
        Assert.True(uploadedResponse.IsSuccessStatusCode, await uploadedResponse.Content.ReadAsStringAsync());
        var uploaded = await ApiIntegrationTestHarness.ReadJsonAsync<BusinessAttachmentRecord>(uploadedResponse);
        Assert.Null(uploaded.CurrentRevision);
        var detailsResponse = await client.GetAsync($"/api/business-attachments/{uploaded.Id}");
        Assert.DoesNotContain("archived bytes", await detailsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var downloaded = await client.GetAsync($"/api/business-attachments/{uploaded.Id}/revisions/1/content");
        Assert.Equal("archived bytes", await downloaded.Content.ReadAsStringAsync());
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(downloaded.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/business-attachments/{uploaded.Id}/revisions/1/save-to-path",
            new ApiAttachmentSaveRequest("unused.txt"))).StatusCode);
        var deleted = await client.DeleteAsync($"/api/invoices/{saved.Id}?rowVersion={Uri.EscapeDataString(saved.Invoice.RowVersion!)}");
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Contains("归档资料", await deleted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProductEditionCatalog.Document, true, false)]
    [InlineData(ProductEditionCatalog.Sales, false, true)]
    [InlineData(ProductEditionCatalog.Full, true, true)]
    [InlineData(ProductEditionCatalog.Administration, false, false)]
    public async Task Editions_ShouldStartAndExposeOnlyTheirAvailableWorklistSources(string edition, bool documents, bool sales)
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("feature-edition", "features.db", productEdition: edition);
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        var response = await client.GetAsync("/api/worklist");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var page = await ApiIntegrationTestHarness.ReadJsonAsync<WorklistPage>(response);
        Assert.Equal(documents, page.Sources.Any(source => source.Key == "invoice-review"));
        Assert.Equal(sales, page.Sources.Any(source => source.Key == "customer-follow-up"));
        Assert.Equal(documents, login.User.Capabilities.AvailableFeatures.Contains("business-attachments"));
        foreach (string source in new[] { "meeting-collection", "meeting-return", "supply-collection", "supply-return", "probation-end", "contract-end" })
            Assert.Equal(edition is ProductEditionCatalog.Full or ProductEditionCatalog.Administration, page.Sources.Any(item => item.Key == source));
        Assert.DoesNotContain(page.Sources, source => source.Key is "meeting-approval" or "supply-approval");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("excel")]
    [InlineData("browser")]
    [InlineData("pdf-ocr")]
    [InlineData(CapabilityModuleKeys.BusinessAttachments)]
    [InlineData(CapabilityModuleKeys.Worklist)]
    public async Task DisablingOptionalModules_ShouldLeaveCoreLoginInvoicesAndHealthOperational(string? module)
    {
        IReadOnlyList<string> disabled = module == null ? CapabilityModuleKeys.All : [module];
        await using var harness = await ApiIntegrationTestHarness.StartAsync("disabled-features", "features.db", disabledCapabilityModules: disabled);
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        Assert.Equal(!disabled.Contains("excel"), login.User.Capabilities.EnabledModules.Contains(PermissionModuleCatalog.DocumentExcel));
        Assert.Equal(!disabled.Contains("pdf-ocr"), login.User.Capabilities.EnabledModules.Contains(PermissionModuleCatalog.DocumentOcr));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/invoices?pageNumber=1&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/livez")).StatusCode);
        foreach (string feature in new[] { CapabilityModuleKeys.Worklist, CapabilityModuleKeys.BusinessAttachments })
        {
            bool available = !disabled.Contains(feature);
            Assert.Equal(available, login.User.Capabilities.AvailableFeatures.Contains(feature));
            var response = await client.GetAsync("/api/" + feature);
            Assert.Equal(available ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            if (!available) Assert.Contains("未安装或已关闭", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DesktopDownload_ShouldWriteOnlyAnExplicitPathWithTheOriginalExtension()
    {
        const string desktopToken = "isolated-attachment-desktop-test";
        await using var harness = await ApiIntegrationTestHarness.StartAsync("attachment-desktop", "features.db", desktopAccessToken: desktopToken);
        using var anonymous = harness.CreateClient(desktopAccessToken: desktopToken);
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken, desktopToken);
        var attachment = await CreateAttachmentAsync(client);
        string endpoint = $"/api/business-attachments/{attachment.Id}/revisions/1/save-to-path";
        string path = Path.Combine(harness.DataRoot, "ChosenDownloads", "source.txt");
        var response = await client.PostAsJsonAsync(endpoint, new ApiAttachmentSaveRequest(path));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, new ApiAttachmentSaveRequest("relative.txt"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, new ApiAttachmentSaveRequest(Path.ChangeExtension(path, ".exe")))).StatusCode);
    }

    [Fact]
    public async Task UploadLimit_ShouldReturn413AndLeaveTheArchiveUnchanged()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("attachment-limits", "features.db");
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        var attachment = await CreateAttachmentAsync(client);
        using var form = UploadForm(new byte[BusinessAttachmentLimits.FileBytes + 1]);
        var response = await client.PostAsync($"/api/invoices/{attachment.InvoiceId}/attachments", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var page = await ApiIntegrationTestHarness.ReadJsonAsync<BusinessAttachmentPage>(await client.GetAsync("/api/business-attachments"));
        Assert.Single(page.Page.Items);
    }

    private static async Task<BusinessAttachmentRecord> CreateAttachmentAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync("/api/invoices", new ApiInvoiceDetailDto { InvoiceNo = "ATT-TEST", InvoiceDate = new(2026, 9, 8), ShipmentDate = new(2026, 9, 8) });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var invoice = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(created);
        using var form = UploadForm("original"u8.ToArray());
        var response = await client.PostAsync($"/api/invoices/{invoice.Id}/attachments", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await ApiIntegrationTestHarness.ReadJsonAsync<BusinessAttachmentRecord>(response);
    }

    [Fact]
    public async Task AttachmentManagement_ShouldExposeCategoryMetadataAndVersionedDeleteContracts()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("attachment-management", "features.db");
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        var attachment = await CreateAttachmentAsync(client);
        var catalog = await ApiIntegrationTestHarness.ReadJsonAsync<BusinessAttachmentCategoryCatalog>(await client.GetAsync("/api/business-attachment-categories"));
        Assert.True(catalog.CanManage);
        Assert.Equal(3, catalog.Items.Count);
        var category = await ApiIntegrationTestHarness.ReadJsonAsync<BusinessAttachmentCategoryRecord>(await client.PostAsJsonAsync(
            "/api/business-attachment-categories", new BusinessAttachmentCategoryCreate(catalog.CompanyScope, "质检报告")));
        var editRequest = new BusinessAttachmentMetadataUpdate(attachment.VersionNumber, "已核对的资料", category.Id, "PO-NEW", "STYLE-NEW", "修正信息");
        var edited = await ApiIntegrationTestHarness.ReadJsonAsync<BusinessAttachmentRecord>(await client.PutAsJsonAsync(
            $"/api/business-attachments/{attachment.Id}/metadata", editRequest));
        Assert.Equal("质检报告", edited.CategoryName);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/business-attachments/{attachment.Id}/metadata", editRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/business-attachment-categories/{category.Id}?expectedVersion={category.VersionNumber}")).StatusCode);
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, $"/api/business-attachments/{attachment.Id}")
        { Content = JsonContent.Create(new BusinessAttachmentDelete(edited.VersionNumber, "误传后清理")) };
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(deletion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/business-attachments/{attachment.Id}/revisions/1/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/business-attachment-categories/{category.Id}?expectedVersion={category.VersionNumber}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/business-attachment-categories")).StatusCode);
    }

    private static MultipartFormDataContent UploadForm(byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(Guid.NewGuid().ToString()), "uploadKey");
        form.Add(new StringContent("客户资料"), "title");
        form.Add(new StringContent("1"), "categoryId");
        form.Add(new ByteArrayContent(bytes), "file", "source.txt");
        return form;
    }
}
