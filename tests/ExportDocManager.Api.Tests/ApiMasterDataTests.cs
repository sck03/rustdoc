using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Tests
{
    public class ApiMasterDataTests
    {
        [Fact]
        public void MasterDataDtoFactory_ShouldMapCustomerAndExporterRowVersions()
        {
            var customer = new Customer
            {
                Id = 1,
                CustomerNameEN = "Buyer",
                NotifyPartyName = "Notify",
                AddressEN = "Buyer Address",
                NotifyPartyAddress = "Notify Address",
                ContactPerson = "Alice",
                Phone = "123",
                Email = "a@example.test",
                TaxId = "TAX",
                Notes = "Note",
                RowVersion = new byte[] { 1, 2, 3 }
            };
            var exporter = new Exporter
            {
                Id = 2,
                ExporterNameEN = "Exporter EN",
                ExporterNameCN = "Exporter CN",
                CreditCode = "CREDIT",
                CustomsCode = "CUSTOMS",
                RowVersion = new byte[] { 4, 5, 6 }
            };

            var customerDto = Assert.Single(ApiMasterDataDtoFactory.FromCustomers([customer]));
            var exporterDto = Assert.Single(ApiMasterDataDtoFactory.FromExporters([exporter]));

            Assert.Equal(1, customerDto.Id);
            Assert.Equal("Buyer", customerDto.CustomerNameEN);
            Assert.Equal("Buyer (Notify)", customerDto.DisplayName);
            Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), customerDto.RowVersion);
            Assert.Equal(2, exporterDto.Id);
            Assert.Equal("Exporter EN", exporterDto.ExporterNameEN);
            Assert.Equal("Exporter CN", exporterDto.ExporterNameCN);
            Assert.Equal("CREDIT", exporterDto.CreditCode);
            Assert.Equal(Convert.ToBase64String(new byte[] { 4, 5, 6 }), exporterDto.RowVersion);
        }

        [Fact]
        public void MasterDataDtoFactory_ShouldMapProductPortUnitAndPayee()
        {
            var product = new Product
            {
                Id = 10,
                ProductCode = "SKU-1",
                NameEN = "Shirt",
                NameCN = "Shirt CN",
                HSCode = "6109",
                TaxRebateRate = 13m,
                UnitEN = "PCS",
                UnitCN = "Piece CN",
                DefaultPrice = 9.99m,
                RowVersion = new byte[] { 10, 11 }
            };
            var port = new Port { Id = 11, NameEN = "Shanghai", NameCN = "Shanghai CN", Country = "CN", Code = "CNSHA", RowVersion = new byte[] { 12, 13 } };
            var unit = new Unit { Id = 12, NameEN = "PCS", NameCN = "Piece CN", Code = "PCE", RowVersion = new byte[] { 14, 15 } };
            var payee = new Payee { Id = 13, Category = "Factory", Name = "Payee", BankName = "Bank", RowVersion = new byte[] { 16, 17 } };

            var productDto = Assert.Single(ApiMasterDataDtoFactory.FromProducts([product]));
            var portDto = Assert.Single(ApiMasterDataDtoFactory.FromPorts([port]));
            var unitDto = Assert.Single(ApiMasterDataDtoFactory.FromUnits([unit]));
            var payeeDto = Assert.Single(ApiMasterDataDtoFactory.FromPayees([payee]));

            Assert.Equal("SKU-1", productDto.ProductCode);
            Assert.Equal("6109", productDto.HSCode);
            Assert.Equal(13m, productDto.TaxRebateRate);
            Assert.Equal(9.99m, productDto.DefaultPrice);
            Assert.Equal(Convert.ToBase64String(new byte[] { 10, 11 }), productDto.RowVersion);
            Assert.Equal("CNSHA", portDto.Code);
            Assert.Equal(Convert.ToBase64String(new byte[] { 12, 13 }), portDto.RowVersion);
            Assert.Equal("PCE", unitDto.Code);
            Assert.Equal(Convert.ToBase64String(new byte[] { 14, 15 }), unitDto.RowVersion);
            Assert.Equal("Factory", payeeDto.Category);
            Assert.Equal("Payee", payeeDto.Name);
            Assert.Equal(Convert.ToBase64String(new byte[] { 16, 17 }), payeeDto.RowVersion);
        }

        [Fact]
        public void MasterDataDtoFactory_ShouldConvertDtosForSave()
        {
            string customerRowVersion = Convert.ToBase64String(new byte[] { 7, 8, 9 });
            var customer = ApiMasterDataDtoFactory.ToCustomerForSave(new ApiCustomerDto(
                5,
                " Buyer ",
                string.Empty,
                "Notify",
                "Address",
                "Notify Address",
                "Alice",
                "123",
                "a@example.test",
                "TAX",
                "Note",
                customerRowVersion));
            var exporter = ApiMasterDataDtoFactory.ToExporterForSave(new ApiExporterDto(
                6,
                "Exporter EN",
                "Exporter CN",
                "Address EN",
                "Address CN",
                "Bob",
                "Credit",
                "Customs",
                "456",
                "Bank",
                "Account",
                "Swift",
                "Notes",
                "doc.png",
                "customs.png",
                Convert.ToBase64String(new byte[] { 1, 2 })));
            var payee = ApiMasterDataDtoFactory.ToPayeeForSave(new ApiPayeeDto(
                7,
                "Factory",
                "Payee",
                "Bank",
                "RMB",
                "USD",
                "Cindy",
                "789",
                "Note",
                Convert.ToBase64String(new byte[] { 3, 4 })));
            var product = ApiMasterDataDtoFactory.ToProductForSave(new ApiProductDto(
                8,
                "SKU",
                "Name EN",
                "Name CN",
                "Desc",
                "6201",
                "Elements",
                "A",
                "M",
                13m,
                "Cotton",
                "Brand",
                "CN",
                "PCS",
                "件",
                1m,
                2m,
                3m,
                4m,
                5m,
                6m,
                "CTN",
                "箱",
                9.99m,
                new DateTime(2026, 1, 2),
                new DateTime(2026, 1, 3),
                Convert.ToBase64String(new byte[] { 5, 6 })));
            var port = ApiMasterDataDtoFactory.ToPortForSave(new ApiPortDto(9, "Shanghai", "上海", "CN", "CNSHA", Convert.ToBase64String(new byte[] { 7, 8 })));
            var unit = ApiMasterDataDtoFactory.ToUnitForSave(new ApiUnitDto(10, "PCS", "件", "PCE", Convert.ToBase64String(new byte[] { 9, 10 })));
            var hsCode = ApiMasterDataDtoFactory.ToHsCodeForSave(new ApiHsCodeDto(
                11,
                "6201.9300",
                string.Empty,
                "Jackets",
                "KG",
                "Desc",
                "Elements",
                "A",
                "M",
                "13%",
                new DateTime(2026, 6, 23),
                "https://example.test/hs",
                RowVersion: Convert.ToBase64String(new byte[] { 11, 12 })));

            Assert.Equal(5, customer.Id);
            Assert.Equal(" Buyer ", customer.CustomerNameEN);
            Assert.Equal(new byte[] { 7, 8, 9 }, customer.RowVersion);
            Assert.Equal("Exporter EN", exporter.ExporterNameEN);
            Assert.Equal(new byte[] { 1, 2 }, exporter.RowVersion);
            Assert.Equal("Payee", payee.Name);
            Assert.Equal(new byte[] { 3, 4 }, payee.RowVersion);
            Assert.Equal("SKU", product.ProductCode);
            Assert.Equal(9.99m, product.DefaultPrice);
            Assert.Equal(new byte[] { 5, 6 }, product.RowVersion);
            Assert.Equal("CNSHA", port.Code);
            Assert.Equal(new byte[] { 7, 8 }, port.RowVersion);
            Assert.Equal("PCE", unit.Code);
            Assert.Equal(new byte[] { 9, 10 }, unit.RowVersion);
            Assert.Equal("62019300", hsCode.NormalizedCode);
            Assert.Equal("Jackets", hsCode.Name);
            Assert.Equal(new byte[] { 11, 12 }, hsCode.RowVersion);
        }

        [Fact]
        public void MasterDataDtoFactory_ShouldRejectInvalidRowVersion()
        {
            var customer = new ApiCustomerDto(
                1,
                "Buyer",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "not-base64");

            Assert.Throws<FormatException>(() => ApiMasterDataDtoFactory.ToCustomerForSave(customer));
        }

        [Fact]
        public void MasterDataDtoFactory_ShouldMapPagedHsCodes()
        {
            var result = new PagedResult<HsCode>(
                new List<HsCode>
                {
                    new()
                    {
                        Id = 21,
                        Code = "6201.9300",
                        Name = "Jackets",
                        Unit = "KG",
                        RebateRate = "13%",
                        UpdateTime = new DateTime(2026, 6, 23)
                    }
                },
                totalCount: 3,
                pageNumber: 2,
                pageSize: 1);

            var dto = ApiMasterDataDtoFactory.FromPagedHsCodes(result);
            var item = Assert.Single(dto.Items);

            Assert.Equal(3, dto.TotalCount);
            Assert.Equal(2, dto.PageNumber);
            Assert.Equal(1, dto.PageSize);
            Assert.Equal("6201.9300", item.Code);
            Assert.Equal("62019300", item.NormalizedCode);
            Assert.Equal("Jackets", item.Name);
            Assert.Equal("KG", item.Unit);
            Assert.Equal("13%", item.RebateRate);
        }
    }
}
