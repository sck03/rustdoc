using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Tests
{
    public class ApiInvoiceTests
    {
        [Fact]
        public void InvoiceDtoFactory_ShouldMapPagedInvoiceList()
        {
            var invoiceDate = new DateTime(2026, 6, 23);
            var result = new PagedResult<Invoice>(
                new List<Invoice>
                {
                    new()
                    {
                        Id = 12,
                        InvoiceNo = "INV-001",
                        ContractNo = "CON-001",
                        InvoiceDate = invoiceDate,
                        CustomerNameEN = "Buyer Co",
                        ExporterNameCN = "CN Exporter",
                        DestinationCountry = "Germany",
                        PortOfLoading = "Shanghai",
                        PortOfDestination = "Hamburg",
                        Currency = "USD",
                        TotalAmount = 123.45m,
                        Type = "Commercial",
                        Status = "Draft"
                    }
                },
                totalCount: 3,
                pageNumber: 2,
                pageSize: 1);

            var dto = ApiInvoiceDtoFactory.FromPagedInvoices(result);
            var item = Assert.Single(dto.Items);

            Assert.Equal(3, dto.TotalCount);
            Assert.Equal(2, dto.PageNumber);
            Assert.Equal(1, dto.PageSize);
            Assert.Equal(3, dto.TotalPages);
            Assert.True(dto.HasPreviousPage);
            Assert.True(dto.HasNextPage);
            Assert.Equal(12, item.Id);
            Assert.Equal("INV-001", item.InvoiceNo);
            Assert.Equal("CON-001", item.ContractNo);
            Assert.Equal(invoiceDate, item.InvoiceDate);
            Assert.Equal("Buyer Co", item.CustomerName);
            Assert.Equal("CN Exporter", item.ExporterName);
            Assert.Equal("Germany", item.DestinationCountry);
            Assert.Equal("Shanghai", item.PortOfLoading);
            Assert.Equal("Hamburg", item.PortOfDestination);
            Assert.Equal("USD", item.Currency);
            Assert.Equal(123.45m, item.TotalAmount);
            Assert.Equal("Commercial", item.Type);
            Assert.Equal("Draft", item.Status);
        }

        [Fact]
        public void InvoiceDtoFactory_ShouldNormalizeNullableTextFields()
        {
            var result = new PagedResult<Invoice>(
                new List<Invoice>
                {
                    new()
                    {
                        InvoiceDate = DateTime.UtcNow,
                        Status = null
                    }
                },
                totalCount: 1,
                pageNumber: 1,
                pageSize: 50);

            var item = Assert.Single(ApiInvoiceDtoFactory.FromPagedInvoices(result).Items);

            Assert.Equal(string.Empty, item.InvoiceNo);
            Assert.Equal(string.Empty, item.ContractNo);
            Assert.Equal(string.Empty, item.CustomerName);
            Assert.Equal(string.Empty, item.ExporterName);
            Assert.Equal(string.Empty, item.DestinationCountry);
            Assert.Equal(string.Empty, item.PortOfLoading);
            Assert.Equal(string.Empty, item.PortOfDestination);
            Assert.Equal(string.Empty, item.Currency);
            Assert.Equal(string.Empty, item.Type);
            Assert.Equal(string.Empty, item.Status);
        }

        [Fact]
        public void InvoiceDtoFactory_ShouldMapInvoiceDetailWithItems()
        {
            var invoice = new Invoice
            {
                Id = 42,
                OwnerUserId = 7,
                DepartmentId = "D1",
                CompanyScope = "C1",
                InvoiceNo = "INV-DETAIL",
                ContractNo = "CON-DETAIL",
                InvoiceDate = new DateTime(2026, 6, 1),
                ShipmentDate = new DateTime(2026, 7, 1),
                CustomerNameEN = "Detail Buyer",
                ExporterNameEN = "Detail Exporter",
                Currency = "EUR",
                TotalAmount = 2500m,
                Status = "Verified",
                RowVersion = new byte[] { 1, 2, 3 },
                Items =
                [
                    new Item
                    {
                        Id = 5,
                        InvoiceId = 42,
                        PoNumber = "PO-1",
                        StyleNo = "ST-1",
                        StyleName = "Jacket",
                        HSCode = "6201",
                        Quantity = 10m,
                        UnitEN = "PCS",
                        Cartons = 2m,
                        UnitPrice = 25m,
                        TotalPrice = 250m,
                        PurchaseTotal = 100m,
                        TaxRebateRate = 13m
                    }
                ]
            };

            var detail = ApiInvoiceDtoFactory.FromInvoiceDetail(invoice);
            var item = Assert.Single(detail.Items);

            Assert.Equal(42, detail.Id);
            Assert.Equal(7, detail.OwnerUserId);
            Assert.Equal("D1", detail.DepartmentId);
            Assert.Equal("INV-DETAIL", detail.InvoiceNo);
            Assert.Equal("Detail Buyer", detail.CustomerNameEN);
            Assert.Equal("Detail Exporter", detail.ExporterNameEN);
            Assert.Equal("EUR", detail.Currency);
            Assert.Equal(2500m, detail.TotalAmount);
            Assert.Equal("Verified", detail.Status);
            Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), detail.RowVersion);
            Assert.Equal(5, item.Id);
            Assert.Equal(42, item.InvoiceId);
            Assert.Equal("PO-1", item.PoNumber);
            Assert.Equal("ST-1", item.StyleNo);
            Assert.Equal("Jacket", item.StyleName);
            Assert.Equal("6201", item.HSCode);
            Assert.Equal(10m, item.Quantity);
            Assert.Equal(25m, item.UnitPrice);
            Assert.Equal(250m, item.TotalPrice);
            Assert.Equal(100m / 1.13m * 0.13m, item.TaxRefundAmount);
        }

        [Fact]
        public void InvoiceDtoFactory_ToInvoiceForSave_ShouldMapWritableFieldsAndIgnoreRequestOwnership()
        {
            var rowVersion = Convert.ToBase64String(new byte[] { 9, 8, 7 });
            var request = new ApiInvoiceDetailDto
            {
                Id = 9,
                OwnerUserId = 99,
                DepartmentId = "External",
                CompanyScope = "External",
                InvoiceNo = "INV-SAVE",
                ContractNo = "CON-SAVE",
                InvoiceDate = new DateTime(2026, 6, 23),
                ShipmentDate = new DateTime(2026, 7, 1),
                CustomerNameEN = "New Buyer",
                CustomerAddressEN = "Buyer Address",
                ExporterNameEN = "New Exporter",
                ExporterNameCN = "出口商",
                ExporterCreditCode = "913000",
                Currency = "USD",
                Status = string.Empty,
                RowVersion = rowVersion,
                Items =
                [
                    new ApiInvoiceItemDto
                    {
                        Id = 3,
                        InvoiceId = 9,
                        StyleNo = "ST-API",
                        StyleName = "API Jacket",
                        Quantity = 12m,
                        UnitEN = "PCS",
                        UnitPrice = 5m,
                        TotalPrice = 60m
                    }
                ]
            };

            var invoice = ApiInvoiceDtoFactory.ToInvoiceForSave(request);
            var customer = ApiInvoiceDtoFactory.CreateCustomerForAutoCreation(invoice);
            var exporter = ApiInvoiceDtoFactory.CreateExporterForAutoCreation(invoice);

            Assert.Equal(9, invoice.Id);
            Assert.Null(invoice.OwnerUserId);
            Assert.Equal(string.Empty, invoice.DepartmentId);
            Assert.Equal(string.Empty, invoice.CompanyScope);
            Assert.Equal("INV-SAVE", invoice.InvoiceNo);
            Assert.Equal("CON-SAVE", invoice.ContractNo);
            Assert.Equal(InvoiceStatusCatalog.Draft, invoice.Status);
            Assert.Equal(new byte[] { 9, 8, 7 }, invoice.RowVersion);
            var item = Assert.Single(invoice.Items);
            Assert.Equal(3, item.Id);
            Assert.Equal("ST-API", item.StyleNo);
            Assert.Equal(60m, item.TotalPrice);
            Assert.NotNull(customer);
            Assert.Equal("New Buyer", customer.CustomerNameEN);
            Assert.NotNull(exporter);
            Assert.Equal("New Exporter", exporter.ExporterNameEN);
            Assert.Equal("913000", exporter.CreditCode);
        }

        [Fact]
        public void InvoiceDtoFactory_ToInvoiceForSave_ShouldNormalizeInvoiceType()
        {
            var defaultInvoice = ApiInvoiceDtoFactory.ToInvoiceForSave(new ApiInvoiceDetailDto());
            var customsInvoice = ApiInvoiceDtoFactory.ToInvoiceForSave(new ApiInvoiceDetailDto
            {
                Type = "  报关数据  "
            });

            Assert.Equal("实际数据", defaultInvoice.Type);
            Assert.Equal("报关数据", customsInvoice.Type);
        }

        [Fact]
        public void InvoiceDtoFactory_PreserveExistingOwnership_ShouldKeepStoredScopeAndFallbackRowVersion()
        {
            var target = new Invoice { InvoiceNo = "INV-TARGET" };
            var existing = new Invoice
            {
                OwnerUserId = 7,
                DepartmentId = "Doc",
                CompanyScope = "CN",
                RowVersion = new byte[] { 1, 2, 3 }
            };

            ApiInvoiceDtoFactory.PreserveExistingOwnership(target, existing);

            Assert.Equal(7, target.OwnerUserId);
            Assert.Equal("Doc", target.DepartmentId);
            Assert.Equal("CN", target.CompanyScope);
            Assert.Equal(new byte[] { 1, 2, 3 }, target.RowVersion);
        }
    }
}
