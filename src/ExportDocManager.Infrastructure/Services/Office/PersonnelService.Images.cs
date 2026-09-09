using System.Security.Cryptography;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class PersonnelService
{
    public Task<PersonnelImageFile> ReadImageAsync(int id, PersonnelImageKind kind, CancellationToken cancellationToken = default)
    {
        ImageLabel(kind);
        string action = kind == PersonnelImageKind.Avatar ? PermissionAction.View : PermissionAction.ViewDetails;
        return office.RunAsync(Resource, action, false, async (db, actor, token) =>
        {
            var employee = await EmployeeAsync(db, id, actor, action, false, token);
            if (kind == PersonnelImageKind.Avatar && employee.Status == EmploymentStatus.Departed)
                office.DemandRecord(employee, actor, Resource, PermissionAction.ViewDetails);
            var image = await db.PersonnelImages.AsNoTracking()
                .SingleOrDefaultAsync(item => item.EmployeeId == id && item.Kind == kind && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("尚未上传该图片。");
            return new PersonnelImageFile(image.Content, image.ContentType);
        }, cancellationToken);
    }

    public async Task<PersonnelRecord> SaveImageAsync(int id, PersonnelImageKind kind, int expectedVersion, Stream source,
        string contentType, CancellationToken cancellationToken = default)
    {
        string label = ImageLabel(kind);
        ArgumentNullException.ThrowIfNull(source);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var buffer = new MemoryStream();
            try { await BoundedStreamHelper.CopyToAsync(source, buffer, PersonnelImageLimits.MaxBytes, timeout.Token); }
            catch (PayloadLimitExceededException) { throw new ServiceValidationException("图片不能超过 5 MB。"); }
            byte[] content = buffer.ToArray();
            string mediaType = PersonnelImagePolicy.Validate(content, contentType);
            string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            return await office.RunAsync(Resource, PermissionAction.Edit, true, async (db, actor, token) =>
            {
                var employee = await EditableImageOwnerAsync(db, id, actor, expectedVersion, token);
                var image = await db.PersonnelImages.SingleOrDefaultAsync(item => item.EmployeeId == id && item.Kind == kind, token);
                if (image?.ContentHash == hash) return await RecordAsync(db, employee, actor, token);
                if (image == null)
                {
                    image = new PersonnelImage { EmployeeId = id, CompanyScope = employee.CompanyScope, Kind = kind };
                    db.PersonnelImages.Add(image);
                }
                image.Content = content;
                image.ContentType = mediaType;
                image.ContentHash = hash;
                image.ByteLength = content.Length;
                image.UpdatedAt = office.Clock.UtcNow;
                db.Entry(employee).Property(item => item.UpdatedAt).IsModified = true;
                AddEvent(db, employee, actor, "Image", office.Clock.Today, "更新：" + label, "");
                await db.SaveChangesAsync(token);
                return await RecordAsync(db, employee, actor, token);
            }, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new ServiceTimeoutException("图片上传超时，请刷新后核对结果。", exception); }
    }

    public Task<PersonnelRecord> DeleteImageAsync(int id, PersonnelImageKind kind, int expectedVersion, CancellationToken cancellationToken = default)
    {
        string label = ImageLabel(kind);
        return office.RunAsync(Resource, PermissionAction.Edit, true, async (db, actor, token) =>
        {
            var employee = await EditableImageOwnerAsync(db, id, actor, expectedVersion, token);
            var image = await db.PersonnelImages.SingleOrDefaultAsync(item => item.EmployeeId == id && item.Kind == kind, token);
            if (image != null)
            {
                db.PersonnelImages.Remove(image);
                db.Entry(employee).Property(item => item.UpdatedAt).IsModified = true;
                AddEvent(db, employee, actor, "Image", office.Clock.Today, "移除：" + label, "");
                await db.SaveChangesAsync(token);
            }
            return await RecordAsync(db, employee, actor, token);
        }, cancellationToken);
    }

    private async Task<PersonnelEmployee> EditableImageOwnerAsync(ExportDocManager.DataAccess.AppDbContext db, int id, User actor,
        int expectedVersion, CancellationToken token)
    {
        var employee = await EmployeeAsync(db, id, actor, PermissionAction.Edit, true, token);
        Version(expectedVersion, employee.VersionNumber);
        if (employee.Status == EmploymentStatus.Departed) throw new ResourceConflictException("离职档案已归档；返聘后可继续维护图片。");
        return employee;
    }

    private static string ImageLabel(PersonnelImageKind kind) => kind switch
    {
        PersonnelImageKind.Avatar => "人员头像",
        PersonnelImageKind.IdentityFront => "身份证人像面",
        PersonnelImageKind.IdentityBack => "身份证国徽面",
        _ => throw new ServiceValidationException("未知的人员图片类型。")
    };
}
