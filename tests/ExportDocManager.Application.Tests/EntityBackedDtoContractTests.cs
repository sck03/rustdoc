using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Application.Tests
{
    public class EntityBackedDtoContractTests
    {
        [Fact]
        public void ImportResult_ShouldReportSuccessFromErrorCollection()
        {
            var result = new ImportResult
            {
                Invoice = new Invoice { InvoiceNo = "INV001" },
                Customer = new Customer { CustomerNameEN = "Buyer" },
                Exporter = new Exporter { ExporterNameEN = "Seller" }
            };

            Assert.True(result.Success);

            result.Errors.Add("缺少商品明细");

            Assert.False(result.Success);
        }

        [Fact]
        public void InvoiceTransferPackage_ShouldKeepEntityPayload()
        {
            var package = new InvoiceTransferPackage
            {
                SchemaVersion = "1",
                Invoice = new Invoice { InvoiceNo = "INV003" },
                Items = [new Item { StyleNo = "A" }],
                Customer = new Customer { CustomerNameEN = "Buyer" },
                Exporter = new Exporter { ExporterNameEN = "Seller" }
            };

            Assert.Equal("INV003", package.Invoice.InvoiceNo);
            Assert.Single(package.Items);
        }
    }
}
