using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Mvc;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapPersonnelImageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        const string resource = PermissionResourceCatalog.OfficePeople;
        endpoints.MapGet("/api/office/people/{id:int:min(1)}/avatar", (IPersonnelService service, HttpContext context, int id,
            CancellationToken cancellationToken) => PersonnelImageResultAsync(service, context, id, PersonnelImageKind.Avatar, cancellationToken))
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .OfficeEndpoint("GetPersonnelAvatar", resource, PermissionAction.View);
        endpoints.MapGet("/api/office/people/{id:int:min(1)}/images/{kind}", (IPersonnelService service, HttpContext context, int id,
            PersonnelImageKind kind, CancellationToken cancellationToken) => PersonnelImageResultAsync(service, context, id, kind, cancellationToken))
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .OfficeEndpoint("GetPersonnelImage", resource, PermissionAction.ViewDetails);
        endpoints.MapPost("/api/office/people/{id:int:min(1)}/images/{kind}", async (IPersonnelService service, int id,
            PersonnelImageKind kind, [FromForm] ApiPersonnelImageForm form, CancellationToken cancellationToken) =>
        {
            if (form.File is not { Length: > 0 and <= PersonnelImageLimits.MaxBytes })
                throw new ServiceValidationException("请选择不超过 5 MB 的 PNG 或 JPEG 图片。");
            await using var source = form.File.OpenReadStream();
            return TypedResults.Ok(await service.SaveImageAsync(id, kind, form.ExpectedVersion, source, form.File.ContentType, cancellationToken));
        }).DisableAntiforgery()
            .WithFormOptions(memoryBufferThreshold: PersonnelImageLimits.MaxBytes + 65536, multipartBodyLengthLimit: PersonnelImageLimits.MaxBytes + 65536)
            .WithMetadata(new RequestSizeLimitAttribute(PersonnelImageLimits.MaxBytes + 65536))
            .WithApiResourceProfile(ApiResourceProfile.Workload)
            .OfficeEndpoint("UploadPersonnelImage", resource, PermissionAction.Edit);
        endpoints.MapDelete("/api/office/people/{id:int:min(1)}/images/{kind}", async (IPersonnelService service, int id,
            PersonnelImageKind kind, int expectedVersion, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.DeleteImageAsync(id, kind, expectedVersion, cancellationToken)))
            .OfficeEndpoint("DeletePersonnelImage", resource, PermissionAction.Edit);
    }

    private static async Task<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult> PersonnelImageResultAsync(
        IPersonnelService service, HttpContext context, int id, PersonnelImageKind kind, CancellationToken token)
    {
        var file = await service.ReadImageAsync(id, kind, token);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(file.Content, file.ContentType);
    }
}

public sealed class ApiPersonnelImageForm
{
    public IFormFile? File { get; init; }
    public int ExpectedVersion { get; init; }
}
