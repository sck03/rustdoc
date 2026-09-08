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
        attachment.Property(item => item.Category).HasConversion<string>().HasMaxLength(30);
        attachment.HasMany(item => item.Revisions).WithOne().HasForeignKey(item => item.BusinessAttachmentId).OnDelete(DeleteBehavior.Cascade);
        var revision = model.Entity<BusinessAttachmentRevision>();
        revision.HasIndex(item => new { item.BusinessAttachmentId, item.Revision }).IsUnique();
        revision.HasIndex(item => new { item.UploadedByUserId, item.UploadKey }).IsUnique();
        var history = model.Entity<BusinessAttachmentEvent>();
        history.HasOne<BusinessAttachment>().WithMany().HasForeignKey(item => item.BusinessAttachmentId).OnDelete(DeleteBehavior.Cascade);
        history.HasIndex(item => new { item.BusinessAttachmentId, item.Id });
    }
}
