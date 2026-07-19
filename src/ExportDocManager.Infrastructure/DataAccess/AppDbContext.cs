using System;
using ExportDocManager.Models;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.DataAccess
{
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Exporter> Exporters { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<CustomOption> CustomOptions { get; set; }
        public DbSet<Payee> Payees { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<HsCode> HsCodes { get; set; }
        public DbSet<Port> Ports { get; set; }
        public DbSet<Unit> Units { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<ApiUserSession> ApiUserSessions { get; set; }
        public DbSet<ApiBackgroundJobRecord> ApiBackgroundJobs { get; set; }
        public DbSet<PermissionTemplate> PermissionTemplates { get; set; }
        public DbSet<PermissionTemplateModule> PermissionTemplateModules { get; set; }
        public DbSet<CrmCustomer> CrmCustomers { get; set; }
        public DbSet<CrmContact> CrmContacts { get; set; }
        public DbSet<CrmFollowUp> CrmFollowUps { get; set; }
        public DbSet<SupplierCompany> SupplierCompanies { get; set; }
        public DbSet<SupplierContact> SupplierContacts { get; set; }
        public DbSet<SupplierProductLink> SupplierProductLinks { get; set; }
        public DbSet<SupplierAssessment> SupplierAssessments { get; set; }
        public DbSet<EmailTemplate> EmailTemplates { get; set; }
        public DbSet<EmailTemplateVersion> EmailTemplateVersions { get; set; }
        public DbSet<UserReportTemplate> UserReportTemplates { get; set; }
        public DbSet<UserReportTemplateVersion> UserReportTemplateVersions { get; set; }
        public DbSet<SalesOpportunity> SalesOpportunities { get; set; }
        public DbSet<SalesOpportunityHistory> SalesOpportunityHistories { get; set; }

        public DbSet<ContainerProject> ContainerProjects { get; set; }
        public DbSet<ContainerProjectItem> ContainerProjectItems { get; set; }
        public DbSet<ContainerTypeDefinition> ContainerTypeDefinitions { get; set; }
        public DbSet<SwClientProfile> SwClientProfiles { get; set; }
        public DbSet<SwOperatorWorkstation> SwOperatorWorkstations { get; set; }
        public DbSet<SwOperationTicket> SwOperationTickets { get; set; }
        public DbSet<CustomsCooDocument> CustomsCooDocuments { get; set; }
        public DbSet<CustomsCooItem> CustomsCooItems { get; set; }
        public DbSet<CustomsCooNonpartyCorp> CustomsCooNonpartyCorps { get; set; }
        public DbSet<CustomsCooAttachment> CustomsCooAttachments { get; set; }
        public DbSet<CustomsCooProducerProfile> CustomsCooProducerProfiles { get; set; }
        public DbSet<AgentConsignmentDocument> AgentConsignmentDocuments { get; set; }
        public DbSet<SwSubmissionBatch> SwSubmissionBatches { get; set; }
        public DbSet<SwReceiptLog> SwReceiptLogs { get; set; }
        public DbSet<SwHandoffPackageRecord> SwHandoffPackageRecords { get; set; }

        public AppDbContext()
        {
        }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Items)
                .WithOne()
                .HasForeignKey(i => i.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => new { i.InvoiceNo, i.Type })
                .IsUnique();

            // 添加性能优化索引
            modelBuilder.Entity<Invoice>().HasIndex(i => i.InvoiceDate);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.ContractNo);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.CustomerId);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.ExporterId);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.OwnerUserId);
            modelBuilder.Entity<Invoice>().HasIndex(i => new { i.CompanyScope, i.DepartmentId });

            modelBuilder.Entity<Item>().HasIndex(i => i.InvoiceId);
            modelBuilder.Entity<Item>().HasIndex(i => i.StyleNo); // Frequently searched

            modelBuilder.Entity<Customer>().HasIndex(c => c.CustomerNameEN);
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
                .HasOne<SupplierCompany>()
                .WithMany()
                .HasForeignKey(item => item.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SupplierProductLink>()
                .HasIndex(item => new { item.SupplierCompanyId, item.ProductId })
                .IsUnique();
            modelBuilder.Entity<SupplierProductLink>().HasIndex(item => item.ProductId);
            modelBuilder.Entity<SupplierProductLink>().Property(item => item.ReferencePrice).HasPrecision(18, 4);
            modelBuilder.Entity<SupplierProductLink>()
                .HasOne<SupplierCompany>()
                .WithMany()
                .HasForeignKey(item => item.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SupplierProductLink>()
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SupplierAssessment>()
                .HasIndex(item => new { item.SupplierCompanyId, item.AssessedAt });
            modelBuilder.Entity<SupplierAssessment>()
                .HasOne<SupplierCompany>()
                .WithMany()
                .HasForeignKey(item => item.SupplierCompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EmailTemplate>().HasIndex(item => new { item.OwnerUserId, item.Category, item.Name });
            modelBuilder.Entity<EmailTemplate>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<EmailTemplate>().HasIndex(item => new { item.IsShared, item.IsActive });
            modelBuilder.Entity<UserReportTemplate>().HasIndex(item => new { item.ReportType, item.Name, item.OwnerUserId });
            modelBuilder.Entity<UserReportTemplate>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<UserReportTemplate>().HasIndex(item => new { item.IsShared, item.IsActive, item.ReportType });
            modelBuilder.Entity<UserReportTemplateVersion>()
                .HasIndex(item => new { item.UserReportTemplateId, item.VersionNumber })
                .IsUnique();
            modelBuilder.Entity<UserReportTemplateVersion>()
                .HasOne(item => item.Template)
                .WithMany()
                .HasForeignKey(item => item.UserReportTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EmailTemplateVersion>()
                .HasIndex(item => new { item.EmailTemplateId, item.VersionNumber })
                .IsUnique();
            modelBuilder.Entity<EmailTemplateVersion>()
                .HasOne(item => item.Template)
                .WithMany()
                .HasForeignKey(item => item.EmailTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => item.CrmCustomerId);
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => item.ProductId);
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => new { item.OwnerUserId, item.Stage });
            modelBuilder.Entity<SalesOpportunity>().HasIndex(item => new { item.CompanyScope, item.DepartmentId });
            modelBuilder.Entity<SalesOpportunity>().Property(item => item.EstimatedAmount).HasPrecision(18, 4);
            modelBuilder.Entity<SalesOpportunity>().HasOne<CrmCustomer>().WithMany()
                .HasForeignKey(item => item.CrmCustomerId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SalesOpportunity>().HasOne<Product>().WithMany()
                .HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<SalesOpportunityHistory>().HasIndex(item => new { item.SalesOpportunityId, item.VersionNumber }).IsUnique();
            modelBuilder.Entity<SalesOpportunityHistory>().Property(item => item.EstimatedAmount).HasPrecision(18, 4);
            modelBuilder.Entity<SalesOpportunityHistory>().HasOne(item => item.Opportunity).WithMany()
                .HasForeignKey(item => item.SalesOpportunityId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Exporter>().HasIndex(e => e.ExporterNameEN);
            
            modelBuilder.Entity<Product>().HasIndex(p => p.ProductCode);
            modelBuilder.Entity<Product>().HasIndex(p => p.NameEN);

            modelBuilder.Entity<HsCode>().HasIndex(h => h.Code);
            modelBuilder.Entity<HsCode>().HasIndex(h => h.NormalizedCode);
            modelBuilder.Entity<HsCode>().HasIndex(h => h.Name);
            modelBuilder.Entity<HsCode>().HasIndex(h => h.Status);
            modelBuilder.Entity<HsCode>().HasIndex(h => new { h.EffectiveYear, h.Status });

            modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.PermissionTemplateId);
            modelBuilder.Entity<User>().Property(u => u.DepartmentId).HasMaxLength(50);
            modelBuilder.Entity<User>().Property(u => u.CompanyScope).HasMaxLength(50);
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
            modelBuilder.Entity<PermissionTemplate>().HasIndex(template => template.Code).IsUnique();
            modelBuilder.Entity<PermissionTemplate>().HasIndex(template => new { template.IsActive, template.Name });
            modelBuilder.Entity<PermissionTemplateModule>()
                .HasIndex(module => new { module.PermissionTemplateId, module.ModuleKey })
                .IsUnique();
            modelBuilder.Entity<PermissionTemplateModule>()
                .HasOne(module => module.PermissionTemplate)
                .WithMany(template => template.Modules)
                .HasForeignKey(module => module.PermissionTemplateId)
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
                .HasIndex(cp => cp.Name);
            modelBuilder.Entity<ContainerProject>()
                .HasIndex(cp => cp.CreatedAt);
            
            modelBuilder.Entity<ContainerProject>()
                .HasMany(p => p.Items)
                .WithOne()
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            var containerProjectEntity = modelBuilder.Entity<ContainerProject>();
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

            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => profile.ProfileName)
                .IsUnique();
            modelBuilder.Entity<SwClientProfile>()
                .HasIndex(profile => new { profile.IsEnabled, profile.MachineName });

            modelBuilder.Entity<SwOperatorWorkstation>()
                .HasIndex(workstation => workstation.MachineName)
                .IsUnique();
            modelBuilder.Entity<SwOperatorWorkstation>()
                .HasIndex(workstation => new { workstation.IsEnabled, workstation.ProfileId });

            modelBuilder.Entity<SwOperationTicket>()
                .HasIndex(ticket => new { ticket.BusinessType, ticket.Status, ticket.Priority, ticket.RequestedAt });
            modelBuilder.Entity<SwOperationTicket>()
                .HasIndex(ticket => new { ticket.SourceInvoiceId, ticket.DocumentId, ticket.BusinessType });

            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => batch.BatchReference)
                .IsUnique();
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => new { batch.BusinessType, batch.InvoiceNo, batch.CreatedAt });
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => new { batch.SourceInvoiceId, batch.BusinessType, batch.CreatedAt });
            modelBuilder.Entity<SwSubmissionBatch>()
                .HasIndex(batch => new { batch.SourceInvoiceId, batch.BusinessType, batch.SubmissionVersion });
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

            modelBuilder.Entity<SwReceiptLog>()
                .HasIndex(log => new { log.BatchId, log.ImportedAt });
            modelBuilder.Entity<SwReceiptLog>()
                .HasIndex(log => new { log.ReferenceNo, log.ReceiptCode, log.SourceFileName });

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

            var itemEntity = modelBuilder.Entity<Item>();
            itemEntity.Property(i => i.Quantity).HasColumnType("decimal(18, 2)"); // Keep some precision for pieces
            itemEntity.Property(i => i.UnitPrice).HasColumnType("decimal(18, 4)");
            itemEntity.Property(i => i.TotalPrice).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.Cartons).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.NWTotal).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.GWTotal).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.Volume).HasColumnType("decimal(18, 3)");
            itemEntity.Property(i => i.TaxRebateRate).HasColumnType("decimal(18, 2)");
            itemEntity.Property(i => i.PurchasePrice).HasColumnType("decimal(18, 4)");
            itemEntity.Property(i => i.PurchaseTotal).HasColumnType("decimal(18, 2)");

            paymentEntity.Property(p => p.DepartmentId).HasMaxLength(50);
            paymentEntity.Property(p => p.CompanyScope).HasMaxLength(50);
        }
    }
}
