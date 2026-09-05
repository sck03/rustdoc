using System;
using System.Text;
using ExportDocManager.Models;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExportDocManager.DataAccess
{
    public class AppDbContext : DbContext
    {
        private readonly TimeProvider _timeProvider;

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Exporter> Exporters { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceStatusHistory> InvoiceStatusHistories { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<CustomOption> CustomOptions { get; set; }
        public DbSet<Payee> Payees { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<HsCode> HsCodes { get; set; }
        public DbSet<HsCodeDeclarationExample> HsCodeDeclarationExamples { get; set; }
        public DbSet<HsCodeReplacementRelation> HsCodeReplacementRelations { get; set; }
        public DbSet<HsCodeSearchFeedback> HsCodeSearchFeedback { get; set; }
        public DbSet<HsCodeRemoteCandidate> HsCodeRemoteCandidates { get; set; }
        public DbSet<Port> Ports { get; set; }
        public DbSet<Unit> Units { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<OrganizationCompany> OrganizationCompanies { get; set; }
        public DbSet<OrganizationDepartment> OrganizationDepartments { get; set; }
        public DbSet<ApiUserSession> ApiUserSessions { get; set; }
        public DbSet<ApiBackgroundJobRecord> ApiBackgroundJobs { get; set; }
        public DbSet<PermissionTemplate> PermissionTemplates { get; set; }
        public DbSet<PermissionTemplateGrant> PermissionTemplateGrants { get; set; }
        public DbSet<BusinessImportPreview> BusinessImportPreviews { get; set; }
        public DbSet<CrmCustomer> CrmCustomers { get; set; }
        public DbSet<CrmContact> CrmContacts { get; set; }
        public DbSet<CrmFollowUp> CrmFollowUps { get; set; }
        public DbSet<SupplierCompany> SupplierCompanies { get; set; }
        public DbSet<SupplierContact> SupplierContacts { get; set; }
        public DbSet<SupplierProductLink> SupplierProductLinks { get; set; }
        public DbSet<SupplierAssessment> SupplierAssessments { get; set; }
        public DbSet<EmailTemplate> EmailTemplates { get; set; }
        public DbSet<EmailTemplateVersion> EmailTemplateVersions { get; set; }
        public DbSet<EmailDeliveryRecord> EmailDeliveryRecords { get; set; }
        public DbSet<UserReportTemplate> UserReportTemplates { get; set; }
        public DbSet<UserReportTemplateVersion> UserReportTemplateVersions { get; set; }
        public DbSet<ReportTemplateImageResourceEntry> ReportTemplateImageResources { get; set; }
        public DbSet<ReportTemplateImageResourceUploadClaim> ReportTemplateImageResourceUploadClaims { get; set; }
        public DbSet<UserReportTemplateResourceReference> UserReportTemplateResourceReferences { get; set; }
        public DbSet<UserReportTemplateVersionResourceReference> UserReportTemplateVersionResourceReferences { get; set; }
        public DbSet<SalesOpportunity> SalesOpportunities { get; set; }
        public DbSet<SalesOpportunityHistory> SalesOpportunityHistories { get; set; }

        public DbSet<ContainerProject> ContainerProjects { get; set; }
        public DbSet<ContainerProjectItem> ContainerProjectItems { get; set; }
        public DbSet<ContainerTypeDefinition> ContainerTypeDefinitions { get; set; }
        public DbSet<SwClientProfile> SwClientProfiles { get; set; }
        public DbSet<CustomsCooDocument> CustomsCooDocuments { get; set; }
        public DbSet<CustomsCooItem> CustomsCooItems { get; set; }
        public DbSet<CustomsCooNonpartyCorp> CustomsCooNonpartyCorps { get; set; }
        public DbSet<CustomsCooAttachment> CustomsCooAttachments { get; set; }
        public DbSet<CustomsCooProducerProfile> CustomsCooProducerProfiles { get; set; }
        public DbSet<AgentConsignmentDocument> AgentConsignmentDocuments { get; set; }
        public DbSet<SwSubmissionBatch> SwSubmissionBatches { get; set; }
        public DbSet<SwReceiptLog> SwReceiptLogs { get; set; }
        public DbSet<SwHandoffPackageRecord> SwHandoffPackageRecords { get; set; }
        public DbSet<SwSubmitPackageArchive> SwSubmitPackageArchives { get; set; }

        internal AppDbContext(DbContextOptions<AppDbContext> options)
            : this(options, TimeProvider.System)
        {
        }

        public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
            : base(options)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplyPersistenceTimestamps();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            ApplyPersistenceTimestamps();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplyPersistenceTimestamps()
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (var entry in ChangeTracker.Entries()
                         .Where(item => item.State is EntityState.Added or EntityState.Modified))
            {
                var created = entry.Properties.FirstOrDefault(property =>
                    property.Metadata.Name == "CreatedAt" &&
                    property.Metadata.ClrType == typeof(DateTimeOffset));
                if (entry.State == EntityState.Added && created?.CurrentValue is DateTimeOffset createdAt && createdAt == default)
                {
                    created.CurrentValue = now;
                }

                var updated = entry.Properties.FirstOrDefault(property =>
                    property.Metadata.Name == "UpdatedAt" &&
                    property.Metadata.ClrType == typeof(DateTimeOffset));
                if (updated != null &&
                    (entry.State == EntityState.Added && updated.CurrentValue is DateTimeOffset updatedAt && updatedAt == default ||
                     entry.State == EntityState.Modified && !updated.IsModified))
                {
                    updated.CurrentValue = now;
                }

                // Every aggregate that exposes VersionNumber participates in
                // the same optimistic-concurrency contract.  Service methods
                // may explicitly advance the value when several rows are
                // changed in one operation; direct seed/import paths still get
                // a deterministic initial value and ordinary edits cannot
                // accidentally leave the token unchanged.
                var version = entry.Properties.FirstOrDefault(property =>
                    property.Metadata.Name == "VersionNumber" &&
                    property.Metadata.ClrType == typeof(int));
                if (version != null)
                {
                    if (entry.State == EntityState.Added && version.CurrentValue is int initial && initial <= 0)
                    {
                        version.CurrentValue = 1;
                    }
                    else if (entry.State == EntityState.Modified &&
                             version.CurrentValue is int current &&
                             version.OriginalValue is int original &&
                             current == original &&
                             original < int.MaxValue)
                    {
                        version.CurrentValue = original + 1;
                    }
                }

                // Canonical keys are maintained at the persistence boundary so
                // direct imports/seeding and all service paths share the same
                // cross-provider uniqueness semantics.
                switch (entry.Entity)
                {
                    case User user:
                        user.UsernameNormalized = CanonicalKey(user.Username);
                        break;
                    case PermissionTemplate template:
                        template.CodeNormalized = CanonicalKey(template.Code);
                        break;
                    case OrganizationCompany company when entry.State == EntityState.Added:
                        company.Code = CanonicalKey(company.Code);
                        break;
                    case OrganizationDepartment department when entry.State == EntityState.Added:
                        department.Code = CanonicalKey(department.Code);
                        department.CompanyCode = CanonicalKey(department.CompanyCode);
                        break;
                    case SalesOpportunity opportunity:
                        opportunity.QuotationNoNormalized = CanonicalNullableKey(opportunity.QuotationNo);
                        break;
                }
            }
        }

        private static string CanonicalKey(string? value) =>
            (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();

        private static string? CanonicalNullableKey(string? value)
        {
            string key = CanonicalKey(value);
            return key.Length == 0 ? null : key;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Items)
                .WithOne()
                .HasForeignKey(i => i.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => new { i.CompanyScope, i.InvoiceNo, i.Type })
                .IsUnique();

            modelBuilder.Entity<Invoice>()
                .Property(i => i.NotifyPartyMode)
                .HasConversion<string>()
                .HasMaxLength(32);
            modelBuilder.Entity<Invoice>().Property(i => i.InvoiceDate).HasColumnType("date");
            modelBuilder.Entity<Invoice>().Property(i => i.ShipmentDate).HasColumnType("date");
            modelBuilder.Entity<Payment>().Property(i => i.PaymentDate).HasColumnType("date");
            modelBuilder.Entity<Payment>().Property(i => i.ShipmentDate).HasColumnType("date");
            modelBuilder.Entity<Payment>().Property(i => i.ReceiptDate).HasColumnType("date");
            modelBuilder.Entity<Customer>()
                .Property(i => i.NotifyPartyMode)
                .HasConversion<string>()
                .HasMaxLength(32);

            modelBuilder.Entity<InvoiceStatusHistory>()
                .HasIndex(item => new { item.InvoiceId, item.ChangedAt });
            modelBuilder.Entity<InvoiceStatusHistory>()
                .HasOne<Invoice>()
                .WithMany()
                .HasForeignKey(item => item.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // 添加性能优化索引
            modelBuilder.Entity<Invoice>().HasIndex(i => i.InvoiceDate);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.ContractNo);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.CustomerId);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.ExporterId);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.OwnerUserId);
            modelBuilder.Entity<Invoice>().HasIndex(i => new { i.CompanyScope, i.DepartmentId });
            modelBuilder.Entity<Invoice>().HasIndex(i => new { i.OwnerUserId, i.InvoiceDate, i.Id });
            modelBuilder.Entity<Invoice>().HasIndex(i => new { i.CompanyScope, i.DepartmentId, i.InvoiceDate, i.Id });

            modelBuilder.Entity<Item>().HasIndex(i => i.InvoiceId);
            modelBuilder.Entity<Item>().HasIndex(i => i.StyleNo); // Frequently searched
            modelBuilder.Entity<Item>().HasIndex(i => i.HSCode);
            modelBuilder.Entity<Item>().HasIndex(i => new { i.InvoiceId, i.Id });
            modelBuilder.Entity<Item>().HasIndex(i => new { i.InvoiceId, i.StyleNo });
            modelBuilder.Entity<Item>().HasIndex(i => new { i.InvoiceId, i.StyleName });
            modelBuilder.Entity<Item>().HasIndex(i => new { i.InvoiceId, i.HSCode });

            modelBuilder.Entity<Customer>().HasIndex(c => c.CustomerNameEN);
            modelBuilder.Entity<Customer>().HasIndex(c => c.OwnerUserId);
            modelBuilder.Entity<Customer>().HasIndex(c => new { c.CompanyScope, c.DepartmentId });
            modelBuilder.Entity<Payee>().HasIndex(item => item.OwnerUserId);
            modelBuilder.Entity<Payee>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<AuditLog>().HasIndex(log => new { log.Timestamp, log.Id });
            modelBuilder.Entity<CrmCustomer>().HasIndex(item => item.Name);
            modelBuilder.Entity<CrmCustomer>().HasIndex(item => item.OwnerUserId);
            modelBuilder.Entity<CrmCustomer>().HasIndex(item => item.LinkedDocumentCustomerId);
            modelBuilder.Entity<CrmCustomer>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<CrmCustomer>()
                .HasOne<Customer>()
                .WithMany()
                .HasForeignKey(item => item.LinkedDocumentCustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<CrmContact>().HasIndex(item => new { item.CrmCustomerId, item.Name });
            modelBuilder.Entity<CrmContact>()
                .HasIndex(item => item.CrmCustomerId)
                .IsUnique()
                .HasFilter(Database.IsSqlite() ? "\"IsPrimary\" = 1" : "\"IsPrimary\" = TRUE");
            modelBuilder.Entity<CrmContact>()
                .HasOne<CrmCustomer>()
                .WithMany()
                .HasForeignKey(item => item.CrmCustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CrmFollowUp>().HasIndex(item => item.CrmCustomerId);
            modelBuilder.Entity<CrmFollowUp>().HasIndex(item => item.OwnerUserId);
            modelBuilder.Entity<CrmFollowUp>().HasIndex(item => new { item.IsCompleted, item.NextFollowUpAt });
            modelBuilder.Entity<CrmFollowUp>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<CrmFollowUp>()
                .HasOne<CrmCustomer>()
                .WithMany()
                .HasForeignKey(item => item.CrmCustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CrmFollowUp>()
                .HasOne<CrmContact>()
                .WithMany()
                .HasForeignKey(item => item.CrmContactId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<SupplierCompany>().HasIndex(item => item.Name);
            modelBuilder.Entity<SupplierCompany>().HasIndex(item => item.OwnerUserId);
            modelBuilder.Entity<SupplierCompany>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<SupplierContact>().HasIndex(item => new { item.SupplierCompanyId, item.Name });
            modelBuilder.Entity<SupplierContact>()
                .HasIndex(item => item.SupplierCompanyId)
                .IsUnique()
                .HasFilter(Database.IsSqlite() ? "\"IsPrimary\" = 1" : "\"IsPrimary\" = TRUE");
            modelBuilder.Entity<SupplierContact>()
                .HasOne<SupplierCompany>()
                .WithMany()
                .HasForeignKey(item => item.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupplierProductLink>()
                .HasIndex(item => new { item.SupplierCompanyId, item.ProductId })
                .IsUnique();
            modelBuilder.Entity<SupplierProductLink>().HasIndex(item => item.ProductId);
            modelBuilder.Entity<SupplierProductLink>().Property(item => item.ReferencePrice).HasPrecision(18, 4);
            modelBuilder.Entity<SupplierProductLink>()
                .HasOne<SupplierCompany>()
                .WithMany()
                .HasForeignKey(item => item.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupplierProductLink>()
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupplierAssessment>()
                .HasIndex(item => new { item.SupplierCompanyId, item.AssessmentDate, item.Id });
            modelBuilder.Entity<SupplierAssessment>()
                .HasOne<SupplierCompany>()
                .WithMany()
                .HasForeignKey(item => item.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupplierAssessment>()
                .HasIndex(item => new { item.SupplierCompanyId, item.Status, item.AssessmentDate });
            modelBuilder.Entity<EmailTemplate>().HasIndex(item => new { item.OwnerUserId, item.Category, item.Name });
            modelBuilder.Entity<EmailTemplate>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<EmailTemplate>().HasIndex(item => new { item.Status, item.ShareScope });
            modelBuilder.Entity<UserReportTemplate>().HasIndex(item => new { item.ReportType, item.Name, item.OwnerUserId });
            modelBuilder.Entity<UserReportTemplate>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<UserReportTemplate>().HasIndex(item => new { item.Status, item.ShareScope, item.ReportType });
            modelBuilder.Entity<UserReportTemplateVersion>()
                .HasIndex(item => new { item.UserReportTemplateId, item.VersionNumber })
                .IsUnique();
            modelBuilder.Entity<UserReportTemplateVersion>()
                .HasOne(item => item.Template)
                .WithMany()
                .HasForeignKey(item => item.UserReportTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ReportTemplateImageResourceEntry>()
                .HasIndex(item => item.Sha256)
                .IsUnique();
            modelBuilder.Entity<ReportTemplateImageResourceEntry>()
                .HasIndex(item => new { item.RecycledAt, item.CreatedAt });
            modelBuilder.Entity<ReportTemplateImageResourceUploadClaim>()
                .HasKey(item => new { item.ResourceId, item.UserId });
            modelBuilder.Entity<ReportTemplateImageResourceUploadClaim>()
                .HasOne(item => item.Resource)
                .WithMany()
                .HasForeignKey(item => item.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserReportTemplateResourceReference>()
                .HasKey(item => new { item.UserReportTemplateId, item.ResourceId, item.ReferenceKind });
            modelBuilder.Entity<UserReportTemplateResourceReference>()
                .HasIndex(item => new { item.ResourceId, item.ReferenceKind });
            modelBuilder.Entity<UserReportTemplateResourceReference>()
                .HasOne(item => item.Template)
                .WithMany()
                .HasForeignKey(item => item.UserReportTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserReportTemplateResourceReference>()
                .HasOne(item => item.Resource)
                .WithMany()
                .HasForeignKey(item => item.ResourceId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<UserReportTemplateVersionResourceReference>()
                .HasKey(item => new { item.UserReportTemplateVersionId, item.ResourceId });
            modelBuilder.Entity<UserReportTemplateVersionResourceReference>()
                .HasIndex(item => item.ResourceId);
            modelBuilder.Entity<UserReportTemplateVersionResourceReference>()
                .HasOne(item => item.Version)
                .WithMany()
                .HasForeignKey(item => item.UserReportTemplateVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserReportTemplateVersionResourceReference>()
                .HasOne(item => item.Resource)
                .WithMany()
                .HasForeignKey(item => item.ResourceId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<EmailTemplateVersion>()
                .HasIndex(item => new { item.EmailTemplateId, item.VersionNumber })
                .IsUnique();
            modelBuilder.Entity<EmailTemplateVersion>()
                .HasOne(item => item.Template)
                .WithMany()
                .HasForeignKey(item => item.EmailTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EmailDeliveryRecord>().HasKey(item => new { item.OwnerUserId, item.DeliveryId });
            modelBuilder.Entity<EmailDeliveryRecord>().HasIndex(item => new { item.OwnerUserId, item.CreatedAt });
            modelBuilder.Entity<EmailDeliveryRecord>().HasIndex(item => new { item.CompanyScope, item.DepartmentId, item.CreatedAt });
            modelBuilder.Entity<EmailDeliveryRecord>().HasIndex(item => new { item.Status, item.UpdatedAt });
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => item.CrmCustomerId);
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => item.ProductId);
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => new { item.OwnerUserId, item.Stage });
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<SalesOpportunity>().Property(item => item.EstimatedAmount).HasPrecision(18, 4);
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => item.QuotationNoNormalized).IsUnique();
            modelBuilder.Entity<SalesOpportunity>().HasOne<CrmCustomer>().WithMany()
                .HasForeignKey(item => item.CrmCustomerId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SalesOpportunity>().HasOne<Product>().WithMany()
                .HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<SalesOpportunityHistory>().HasIndex(item => new { item.SalesOpportunityId, item.VersionNumber }).IsUnique();
            modelBuilder.Entity<SalesOpportunityHistory>().Property(item => item.EstimatedAmount).HasPrecision(18, 4);
            modelBuilder.Entity<SalesOpportunityHistory>().HasOne(item => item.Opportunity).WithMany()
                .HasForeignKey(item => item.SalesOpportunityId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Exporter>().HasIndex(e => e.ExporterNameEN);
            modelBuilder.Entity<Exporter>().HasIndex(e => e.OwnerUserId);
            modelBuilder.Entity<Exporter>().HasIndex(e => new { e.CompanyScope, e.DepartmentId });

            modelBuilder.Entity<Product>().HasIndex(p => p.ProductCode);
            modelBuilder.Entity<Product>().HasIndex(p => p.NameEN);
            modelBuilder.Entity<Product>().HasIndex(p => p.HSCode);
            modelBuilder.Entity<Product>().HasIndex(p => new { p.UpdatedAt, p.Id });
            modelBuilder.Entity<Product>().HasIndex(p => new { p.ProductCode, p.NameEN, p.UpdatedAt, p.Id });

            modelBuilder.Entity<HsCode>().HasIndex(h => h.Code);
            modelBuilder.Entity<HsCode>().HasIndex(h => h.NormalizedCode);
            modelBuilder.Entity<HsCode>().HasIndex(h => h.Name);
            modelBuilder.Entity<HsCode>().HasIndex(h => h.Status);
            modelBuilder.Entity<HsCode>().HasIndex(h => new { h.EffectiveYear, h.Status });
            modelBuilder.Entity<HsCodeDeclarationExample>().HasIndex(item => item.Fingerprint).IsUnique();
            modelBuilder.Entity<HsCodeDeclarationExample>().HasIndex(item => item.RawReportedHsCode);
            modelBuilder.Entity<HsCodeDeclarationExample>().HasIndex(item => item.ResolvedCurrentHsCode);
            modelBuilder.Entity<HsCodeDeclarationExample>().HasIndex(item => new { item.ResolutionStatus, item.UpdatedAt });
            modelBuilder.Entity<HsCodeDeclarationExample>().HasIndex(item => new { item.IsManuallyVerified, item.UpdatedAt });
            modelBuilder.Entity<HsCodeReplacementRelation>()
                .HasIndex(item => new { item.OldCode, item.NewCode, item.EffectiveYear })
                .IsUnique();
            modelBuilder.Entity<HsCodeReplacementRelation>().HasIndex(item => item.OldCode);
            modelBuilder.Entity<HsCodeSearchFeedback>().HasIndex(item => item.Fingerprint).IsUnique();
            modelBuilder.Entity<HsCodeSearchFeedback>().HasIndex(item => item.CandidateCode);
            modelBuilder.Entity<HsCodeRemoteCandidate>().HasIndex(item => item.Fingerprint).IsUnique();
            modelBuilder.Entity<HsCodeRemoteCandidate>().HasIndex(item => new { item.ReviewStatus, item.LastSeenAt });
            modelBuilder.Entity<HsCodeRemoteCandidate>().HasIndex(item => item.RawReportedHsCode);

            modelBuilder.Entity<User>().HasIndex(u => u.UsernameNormalized).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.PermissionTemplateId);
            modelBuilder.Entity<User>().HasIndex(u => u.CompanyScope);
            modelBuilder.Entity<User>().HasIndex(u => u.DepartmentId);
            modelBuilder.Entity<User>().Property(u => u.DepartmentId).HasMaxLength(50);
            modelBuilder.Entity<User>().Property(u => u.CompanyScope).HasMaxLength(50);
            modelBuilder.Entity<OrganizationCompany>().HasKey(item => item.Code);
            modelBuilder.Entity<OrganizationCompany>().HasIndex(item => new { item.IsActive, item.Name });
            modelBuilder.Entity<OrganizationDepartment>().HasKey(item => item.Code);
            modelBuilder.Entity<OrganizationDepartment>().HasIndex(item => new { item.CompanyCode, item.IsActive, item.Name });
            modelBuilder.Entity<OrganizationDepartment>()
                .HasOne(item => item.Company)
                .WithMany()
                .HasForeignKey(item => item.CompanyCode)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<User>()
                .HasOne(user => user.PermissionTemplate)
                .WithMany()
                .HasForeignKey(user => user.PermissionTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ApiUserSession>().HasIndex(session => session.TokenHash).IsUnique();
            modelBuilder.Entity<ApiUserSession>().HasIndex(session => new { session.UserId, session.ExpiresAt });
            modelBuilder.Entity<ApiUserSession>().HasIndex(session => session.ExpiresAt);
            modelBuilder.Entity<ApiUserSession>().HasIndex(session => session.RevokedAt);
            modelBuilder.Entity<ApiUserSession>().Property(session => session.TokenHash).HasMaxLength(64);
            modelBuilder.Entity<ApiUserSession>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ApiBackgroundJobRecord>().HasKey(job => job.JobId);
            modelBuilder.Entity<ApiBackgroundJobRecord>().Property(job => job.JobId).HasMaxLength(120);
            modelBuilder.Entity<ApiBackgroundJobRecord>().Property(job => job.Kind).HasMaxLength(80);
            modelBuilder.Entity<ApiBackgroundJobRecord>().Property(job => job.Status).HasMaxLength(30);
            modelBuilder.Entity<ApiBackgroundJobRecord>().Property(job => job.RequestedBy).HasMaxLength(100);
            modelBuilder.Entity<ApiBackgroundJobRecord>().HasIndex(job => job.RequestedByUserId);
            modelBuilder.Entity<ApiBackgroundJobRecord>().HasIndex(job => new { job.RequestedBy, job.CreatedAt });
            modelBuilder.Entity<ApiBackgroundJobRecord>().HasIndex(job => new { job.Status, job.CreatedAt });
            modelBuilder.Entity<PermissionTemplate>().HasIndex(template => template.CodeNormalized).IsUnique();
            modelBuilder.Entity<PermissionTemplate>().HasIndex(template => new { template.IsActive, template.Name });
            modelBuilder.Entity<BusinessImportPreview>()
                .HasIndex(preview => new { preview.OwnerUserId, preview.Kind, preview.ExpiresAt });
            modelBuilder.Entity<BusinessImportPreview>()
                .HasIndex(preview => preview.ExpiresAt);
            modelBuilder.Entity<PermissionTemplateGrant>()
                .HasIndex(grant => new { grant.PermissionTemplateId, grant.ResourceKey, grant.Action })
                .IsUnique();
            modelBuilder.Entity<PermissionTemplateGrant>()
                .HasOne(grant => grant.PermissionTemplate)
                .WithMany(template => template.Grants)
                .HasForeignKey(grant => grant.PermissionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Payment Indexes
            modelBuilder.Entity<Payment>().HasIndex(p => p.PaymentDate);
            modelBuilder.Entity<Payment>().HasIndex(p => p.InvoiceNo);
            modelBuilder.Entity<Payment>().HasIndex(p => p.PayeeName);
            modelBuilder.Entity<Payment>().HasIndex(p => p.OwnerUserId);
            modelBuilder.Entity<Payment>().HasIndex(p => new { p.CompanyScope, p.DepartmentId });

            // Define precision for Payment decimal properties
            var paymentEntity = modelBuilder.Entity<Payment>();
            paymentEntity.Property(p => p.USDAmount).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.CNYAmount).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.TravelExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.BusinessEntertainmentExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.TelephoneExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.OfficeExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.RepairExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.FreightMiscExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.InspectionExpense).HasColumnType("decimal(18, 2)");
            paymentEntity.Property(p => p.OtherExpense).HasColumnType("decimal(18, 2)");

            modelBuilder.Entity<ContainerProject>()
                .HasIndex(cp => new { cp.OwnerUserId, cp.UpdatedAt });
            modelBuilder.Entity<ContainerProject>()
                .HasIndex(cp => cp.Name);
            modelBuilder.Entity<ContainerProject>()
                .HasIndex(cp => cp.CreatedAt);

            modelBuilder.Entity<ContainerProject>()
                .HasMany(p => p.Items)
                .WithOne()
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            var containerProjectEntity = modelBuilder.Entity<ContainerProject>();
            containerProjectEntity.Property(project => project.VersionNumber).IsConcurrencyToken();
            containerProjectEntity.Property(project => project.ContainerMaxVolume).HasColumnType("decimal(18, 3)");
            containerProjectEntity.Property(project => project.ContainerMaxWeight).HasColumnType("decimal(18, 2)");
            containerProjectEntity.Property(project => project.DefaultPalletWeight).HasColumnType("decimal(18, 2)");
            containerProjectEntity.Property(project => project.CenterOfGravityTolerancePercent).HasColumnType("decimal(18, 2)");
            containerProjectEntity.Property(project => project.MinimumSupportAreaPercent).HasColumnType("decimal(18, 2)");

            var containerProjectItemEntity = modelBuilder.Entity<ContainerProjectItem>();
            containerProjectItemEntity.Property(item => item.Length).HasColumnType("decimal(18, 2)");
            containerProjectItemEntity.Property(item => item.Width).HasColumnType("decimal(18, 2)");
            containerProjectItemEntity.Property(item => item.Height).HasColumnType("decimal(18, 2)");
            containerProjectItemEntity.Property(item => item.Weight).HasColumnType("decimal(18, 2)");
            containerProjectItemEntity.Property(item => item.MaxTopLoadWeight).HasColumnType("decimal(18, 2)");

            modelBuilder.Entity<CustomsCooDocument>()
                .HasIndex(document => document.SourceInvoiceId)
                .IsUnique();
            modelBuilder.Entity<CustomsCooDocument>()
                .HasIndex(document => new { document.InvoiceNo, document.LastGeneratedAt });
            modelBuilder.Entity<CustomsCooDocument>()
                .HasIndex(document => new { document.SourceInvoiceId, document.DraftRevision });
            modelBuilder.Entity<CustomsCooDocument>()
                .Property(document => document.DraftRevision)
                .IsConcurrencyToken();
            modelBuilder.Entity<CustomsCooDocument>()
                .HasMany(document => document.Items)
                .WithOne(item => item.Document)
                .HasForeignKey(item => item.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CustomsCooDocument>()
                .HasMany(document => document.NonpartyCorps)
                .WithOne(item => item.Document)
                .HasForeignKey(item => item.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CustomsCooDocument>()
                .HasMany(document => document.Attachments)
                .WithOne(item => item.Document)
                .HasForeignKey(item => item.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomsCooItem>()
                .HasIndex(item => new { item.DocumentId, item.GNo });
            modelBuilder.Entity<CustomsCooItem>()
                .HasIndex(item => item.HSCode);
            modelBuilder.Entity<CustomsCooNonpartyCorp>()
                .HasIndex(item => new { item.DocumentId, item.SortNo });
            modelBuilder.Entity<CustomsCooAttachment>()
                .HasIndex(item => new { item.DocumentId, item.FileName });
            modelBuilder.Entity<CustomsCooProducerProfile>()
                .HasIndex(item => item.CiqRegNo);
            modelBuilder.Entity<CustomsCooProducerProfile>()
                .HasIndex(item => item.PrdcEtpsName);
            modelBuilder.Entity<CustomsCooProducerProfile>()
                .HasIndex(item => item.LastUsedAt);

            modelBuilder.Entity<AgentConsignmentDocument>()
                .HasIndex(document => document.SourceInvoiceId)
                .IsUnique();
            modelBuilder.Entity<AgentConsignmentDocument>()
                .HasIndex(document => new { document.InvoiceNo, document.LastGeneratedAt });
            modelBuilder.Entity<AgentConsignmentDocument>()
                .HasIndex(document => new { document.SourceInvoiceId, document.DraftRevision });
            modelBuilder.Entity<AgentConsignmentDocument>()
                .Property(document => document.DraftRevision)
                .IsConcurrencyToken();

            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => profile.ProfileKey)
                .IsUnique();
            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => profile.StationKey)
                .IsUnique(false);
            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => new { profile.StationKey, profile.ProfileName })
                .IsUnique();
            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => new { profile.StationKey, profile.CompanyScope, profile.CardIdentifier })
                .IsUnique();
            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => new { profile.StationKey, profile.IsActive, profile.IsEnabled });

            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => batch.BatchReference)
                .IsUnique();
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => new { batch.BusinessType, batch.InvoiceNo, batch.CreatedAt });
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => new { batch.SourceInvoiceId, batch.BusinessType, batch.CreatedAt });
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => new { batch.SourceInvoiceId, batch.BusinessType, batch.SubmissionVersion })
                .IsUnique();
            // A dispatch is a lease-based state machine.  Treat both lease and
            // operation identity as concurrency tokens so a stale completion or
            // failure callback can never overwrite recovery or a later retry.
            modelBuilder.Entity<SwSubmissionBatch>()
                .Property(batch => batch.ClientDispatchLeaseUntil)
                .IsConcurrencyToken();
            modelBuilder.Entity<SwSubmissionBatch>()
                .Property(batch => batch.ClientDispatchOperationId)
                .IsConcurrencyToken();
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasMany(batch => batch.ReceiptLogs)
                .WithOne(log => log.Batch)
                .HasForeignKey(log => log.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasMany(batch => batch.PackageRecords)
                .WithOne(record => record.Batch)
                .HasForeignKey(record => record.BatchId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasOne(batch => batch.SubmitPackageArchive)
                .WithOne(archive => archive.Batch)
                .HasForeignKey<SwSubmitPackageArchive>(archive => archive.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SwSubmitPackageArchive>()
                .HasIndex(archive => archive.BatchId)
                .IsUnique();
            modelBuilder.Entity<SwSubmitPackageArchive>()
                .HasIndex(archive => archive.Sha256);

            modelBuilder.Entity<SwReceiptLog>()
                .HasIndex(log => new { log.BatchId, log.ImportedAt });
            modelBuilder.Entity<SwReceiptLog>()
                .HasIndex(log => new { log.ReferenceNo, log.ReceiptCode, log.SourceFileName });
            modelBuilder.Entity<SwReceiptLog>()
                .HasIndex(log => new { log.BatchId, log.ContentSha256 })
                .IsUnique();

            modelBuilder.Entity<SwHandoffPackageRecord>()
                .HasIndex(record => new { record.BatchReference, record.PackageType, record.Direction, record.CreatedAt });
            modelBuilder.Entity<SwHandoffPackageRecord>()
                .HasIndex(record => new { record.SourceInvoiceId, record.BusinessType, record.CreatedAt });

            // Default values for newly created records.
            modelBuilder.Entity<Invoice>().Property(i => i.Status).HasDefaultValue(InvoiceStatusCatalog.Draft);
            modelBuilder.Entity<Invoice>().Property(i => i.TotalPurchaseAmount).HasDefaultValue(0m);
            modelBuilder.Entity<Invoice>().Property(i => i.TotalTaxRefundAmount).HasDefaultValue(0m);
            modelBuilder.Entity<Invoice>().Property(i => i.TotalProfit).HasDefaultValue(0m);

            modelBuilder.Entity<Item>().Property(i => i.TaxRebateRate).HasDefaultValue(0m);
            modelBuilder.Entity<Item>().Property(i => i.PurchasePrice).HasDefaultValue(0m);
            modelBuilder.Entity<Item>().Property(i => i.PurchaseTotal).HasDefaultValue(0m);

            modelBuilder.Entity<Product>().Property(p => p.TaxRebateRate).HasDefaultValue(0m);

            var productEntity = modelBuilder.Entity<Product>();
            productEntity.Property(p => p.GWPerCtn).HasColumnType("decimal(18, 2)");
            productEntity.Property(p => p.NWPerCtn).HasColumnType("decimal(18, 2)");

            // Define precision for Invoice and Item decimal properties
            var invoiceEntity = modelBuilder.Entity<Invoice>();
            invoiceEntity.Property(i => i.DepartmentId).HasMaxLength(50);
            invoiceEntity.Property(i => i.CompanyScope).HasMaxLength(50);
            invoiceEntity.Property(i => i.TotalAmount).HasColumnType("decimal(18, 2)");
            invoiceEntity.Property(i => i.TotalNetWeight).HasColumnType("decimal(18, 2)");
            invoiceEntity.Property(i => i.TotalGrossWeight).HasColumnType("decimal(18, 2)");
            invoiceEntity.Property(i => i.TotalVolume).HasColumnType("decimal(18, 3)");
            invoiceEntity.Property(i => i.TotalPurchaseAmount).HasColumnType("decimal(18, 2)");
            invoiceEntity.Property(i => i.TotalTaxRefundAmount).HasColumnType("decimal(18, 2)");
            invoiceEntity.Property(i => i.TotalProfit).HasColumnType("decimal(18, 2)");

            var invoiceStatusHistoryEntity = modelBuilder.Entity<InvoiceStatusHistory>();
            invoiceStatusHistoryEntity.Property(item => item.FromStatus).HasMaxLength(30);
            invoiceStatusHistoryEntity.Property(item => item.ToStatus).HasMaxLength(30);
            invoiceStatusHistoryEntity.Property(item => item.Note).HasMaxLength(500);
            invoiceStatusHistoryEntity.Property(item => item.ChangedByUsername).HasMaxLength(50);

            var itemEntity = modelBuilder.Entity<Item>();
            itemEntity.Property(i => i.PriceCalculationMode)
                .HasMaxLength(30)
                .HasDefaultValue(ItemPriceCalculationModeCatalog.UnitPriceDriven);
            itemEntity.Property(i => i.Quantity).HasColumnType("decimal(18, 2)"); // Keep some precision for pieces
            itemEntity.Property(i => i.UnitPrice).HasColumnType("decimal(18, 5)");
            itemEntity.Property(i => i.TotalPrice).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.Cartons).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.GWPerCtn).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.NWPerCtn).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.NWTotal).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.GWTotal).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.Volume).HasColumnType("decimal(18, 3)");
            itemEntity.Property(i => i.TaxRebateRate).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.PurchasePrice).HasColumnType("decimal(18, 4)");
            itemEntity.Property(i => i.PurchaseTotal).HasColumnType("decimal(18, 2)");

            paymentEntity.Property(p => p.DepartmentId).HasMaxLength(50);
            paymentEntity.Property(p => p.CompanyScope).HasMaxLength(50);

            ConfigureTemporalStorage(modelBuilder);
        }

        private void ConfigureTemporalStorage(ModelBuilder modelBuilder)
        {
            foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (IMutableProperty property in entityType.GetProperties())
                {
                    Type modelType = property.ClrType;
                    if (modelType == typeof(DateOnly) || modelType == typeof(DateOnly?))
                    {
                        property.SetColumnType("date");
                        continue;
                    }

                    if (!Database.IsSqlite())
                    {
                        continue;
                    }

                    if (modelType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(
                            value => value.UtcTicks,
                            value => new DateTimeOffset(value, TimeSpan.Zero)));
                    }
                    else if (modelType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(new ValueConverter<DateTimeOffset?, long?>(
                            value => value.HasValue ? value.Value.UtcTicks : null,
                            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null));
                    }
                }
            }
        }
    }
}
