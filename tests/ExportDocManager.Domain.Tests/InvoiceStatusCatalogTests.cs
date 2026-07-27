using ExportDocManager.Models.Entities;

namespace ExportDocManager.Domain.Tests
{
    public sealed class InvoiceStatusCatalogTests
    {
        [Fact]
        public void DeletionAndUnverifyRules_ShouldKeepCancelledInvoicesLockedForAudit()
        {
            Assert.True(InvoiceStatusCatalog.IsEditable(InvoiceStatusCatalog.Draft));
            Assert.False(InvoiceStatusCatalog.IsEditable(InvoiceStatusCatalog.Verified));
            Assert.False(InvoiceStatusCatalog.IsEditable(InvoiceStatusCatalog.Cancelled));
            Assert.True(InvoiceStatusCatalog.CanUnverify(InvoiceStatusCatalog.Verified));
            Assert.True(InvoiceStatusCatalog.CanUnverify(InvoiceStatusCatalog.Shipped));
            Assert.True(InvoiceStatusCatalog.CanUnverify(InvoiceStatusCatalog.Completed));
            Assert.False(InvoiceStatusCatalog.CanUnverify(InvoiceStatusCatalog.Cancelled));
        }
    }
}
