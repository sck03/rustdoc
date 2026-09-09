using ExportDocManager.Services.Attachments;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapBusinessAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/business-attachments", async (IServiceProvider services, int? invoiceId, string? keyword,
            bool? includeArchived, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).QueryAsync(new BusinessAttachmentQuery(invoiceId, keyword,
                includeArchived ?? false, pageNumber ?? 1, pageSize ?? 20), cancellationToken)))
            .WithName("ListBusinessAttachments").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View);
        endpoints.MapGet("/api/business-attachments/{id:int:min(1)}", async (IServiceProvider services, int id, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).GetAsync(id, cancellationToken)))
            .WithName("GetBusinessAttachment").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View);
        endpoints.MapPost("/api/invoices/{invoiceId:int:min(1)}/attachments", async (IServiceProvider services, HttpContext context,
            int invoiceId, [FromForm] ApiAttachmentUploadForm form, CancellationToken cancellationToken) =>
        {
            var service = AttachmentService(services);
            if (context.Request.Form.Files.Count != 1) throw new ServiceValidationException("每次只能上传一个文件。");
            var file = form.File ?? throw new ServiceValidationException("请选择需要归档的文件。");
            await using var content = file.OpenReadStream();
            return TypedResults.Ok(await service.UploadAsync(invoiceId, new BusinessAttachmentUpload(form.AttachmentId,
                form.ExpectedVersion, form.UploadKey, form.Title, form.CategoryId, form.PoNumber, form.StyleNo, file.FileName, form.Note), content, cancellationToken));
        })
            .WithName("UploadBusinessAttachment").DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(BusinessAttachmentLimits.FileBytes + 64 * 1024))
            .WithFormOptions(bufferBody: true, memoryBufferThreshold: BusinessAttachmentLimits.FileBytes + 64 * 1024,
                bufferBodyLengthLimit: BusinessAttachmentLimits.FileBytes + 64 * 1024, valueCountLimit: 10,
                keyLengthLimit: 64, valueLengthLimit: 2048, multipartBodyLengthLimit: BusinessAttachmentLimits.FileBytes + 64 * 1024)
            .WithApiResourceProfile(ApiResourceProfile.Workload)
            .WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Operate);
        endpoints.MapPut("/api/business-attachments/{id:int:min(1)}", async (IServiceProvider services, int id,
            BusinessAttachmentUpdate request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).UpdateAsync(id, request, cancellationToken)))
            .WithName("UpdateBusinessAttachment").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Operate);
        endpoints.MapPut("/api/business-attachments/{id:int:min(1)}/metadata", async (IServiceProvider services, int id,
            BusinessAttachmentMetadataUpdate request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).EditMetadataAsync(id, request, cancellationToken)))
            .WithName("EditBusinessAttachmentMetadata").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Operate);
        endpoints.MapDelete("/api/business-attachments/{id:int:min(1)}", async (IServiceProvider services, int id,
            [FromBody] BusinessAttachmentDelete request, CancellationToken cancellationToken) =>
        {
            await AttachmentService(services).DeleteAsync(id, request, cancellationToken);
            return TypedResults.Ok(new ApiCommandResponse(true, "业务资料及全部版本已删除。"));
        }).WithName("DeleteBusinessAttachment").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Manage);
        MapBusinessAttachmentCategoryEndpoints(endpoints);
        endpoints.MapGet("/api/business-attachments/{id:int:min(1)}/revisions/{revision:int:min(1)}/content", async (
            IServiceProvider services, HttpContext context, int id, int revision, CancellationToken cancellationToken) =>
        {
            var file = await AttachmentService(services).ReadAsync(id, revision, cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return TypedResults.File(file.Content, file.ContentType, file.FileName);
        }).WithName("DownloadBusinessAttachment").Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View);
        endpoints.MapPost("/api/business-attachments/{id:int:min(1)}/revisions/{revision:int:min(1)}/save-to-path", async (
            IServiceProvider services, HttpContext context, ApiDesktopAccessOptions desktopAccess, int id, int revision,
            ApiAttachmentSaveRequest request, CancellationToken cancellationToken) =>
        {
            if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccess))
                throw new PermissionDeniedException("本机保存仅支持有效桌面通道；浏览器请下载原文件。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                var file = await AttachmentService(services).ReadAsync(id, revision, timeout.Token);
                string path = request.DestinationPath;
                if (!Path.IsPathFullyQualified(path) || !CrossPlatformFileNamePolicy.IsSafeFileName(Path.GetFileName(path)) ||
                    !string.Equals(Path.GetExtension(path), Path.GetExtension(file.FileName), StringComparison.OrdinalIgnoreCase))
                    throw new ServiceValidationException("请选择有效的绝对保存路径，并保留原文件扩展名。");
                PathBoundaryHelper.EnsureNoLinkLikeComponents(path, "保存路径不能经过符号链接或目录联接。");
                await AtomicFileHelper.WriteFileAtomicAsync(path, (temp, token) => File.WriteAllBytesAsync(temp, file.Content, token), timeout.Token);
                return TypedResults.Ok(new ApiCommandResponse(true, "归档文件已保存。"));
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            { throw new ServiceTimeoutException("保存归档文件超时。", ex); }
        }).WithName("SaveBusinessAttachmentToPath").RequireDesktopApi()
            .WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View);
    }

    private static IBusinessAttachmentService AttachmentService(IServiceProvider services) =>
        services.GetService<IBusinessAttachmentService>() ?? throw new UserVisibleInfrastructureException("当前产品未安装或已关闭业务资料归档模块。");
}

public sealed record ApiAttachmentSaveRequest(string DestinationPath);

public sealed class ApiAttachmentUploadForm
{
    public IFormFile? File { get; init; }
    public int? AttachmentId { get; init; }
    public int ExpectedVersion { get; init; }
    public Guid UploadKey { get; init; }
    public string Title { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public string PoNumber { get; init; } = string.Empty;
    public string StyleNo { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}
