using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.DataAccess;

internal static class BusinessAttachmentModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var attachment = model.Entity<BusinessAttachment>();
        attachment.HasOne(item => item.Invoice).WithMany().HasForeignKey(item => item.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        attachment.HasIndex(item => new { item.InvoiceId, item.IsArchived, item.Id });
        attachment.HasOne(item => item.Category).WithMany().HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.Restrict);
        var category = model.Entity<BusinessAttachmentCategory>();
        category.HasIndex(item => new { item.CompanyScope, item.NameNormalized }).IsUnique();
        attachment.HasMany(item => item.Revisions).WithOne().HasForeignKey(item => item.BusinessAttachmentId).OnDelete(DeleteBehavior.Cascade);
        var revision = model.Entity<BusinessAttachmentRevision>();
        revision.HasIndex(item => new { item.BusinessAttachmentId, item.Revision }).IsUnique();
        revision.HasIndex(item => new { item.UploadedByUserId, item.UploadKey }).IsUnique();
        var history = model.Entity<BusinessAttachmentEvent>();
        history.HasOne<BusinessAttachment>().WithMany().HasForeignKey(item => item.BusinessAttachmentId).OnDelete(DeleteBehavior.Cascade);
        history.HasIndex(item => new { item.BusinessAttachmentId, item.Id });
    }
}
