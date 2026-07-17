using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Tests
{
    public class ApiPaymentTests
    {
        [Fact]
        public void PaymentDtoFactory_ShouldMapPagedPaymentList()
        {
            var result = new PagedResult<Payment>(
                new List<Payment>
                {
                    new()
                    {
                        Id = 8,
                        OwnerUserId = 7,
                        DepartmentId = "D1",
                        CompanyScope = "C1",
                        InvoiceNo = "INV-PAY",
                        ShipmentDate = new DateTime(2026, 5, 1),
                        PayeeId = 3,
                        Department = "Sales",
                        Project = "Spring",
                        USDAmount = 120.5m,
                        CNYAmount = 870.25m,
                        PaymentMethod = "TT",
                        PayeeName = "Factory",
                        PayerName = "Exporter",
                        BankName = "Bank",
                        AccountNo = "ACC",
                        Notes = "Note",
                        PaymentDate = new DateTime(2026, 5, 2),
                        GoodsName = "Jacket",
                        Quantity = "10 PCS",
                        ShipmentCountry = "Canada",
                        ReceiptDate = new DateTime(2026, 5, 3),
                        TravelExpense = 1m,
                        BusinessEntertainmentExpense = 2m,
                        TelephoneExpense = 3m,
                        OfficeExpense = 4m,
                        RepairExpense = 5m,
                        FreightMiscExpense = 6m,
                        InspectionExpense = 7m,
                        OtherExpense = 8m
                    }
                },
                totalCount: 2,
                pageNumber: 1,
                pageSize: 1);

            var dto = ApiPaymentDtoFactory.FromPagedPayments(result);
            var item = Assert.Single(dto.Items);

            Assert.Equal(2, dto.TotalCount);
            Assert.Equal(1, dto.PageNumber);
            Assert.Equal(1, dto.PageSize);
            Assert.Equal(8, item.Id);
            Assert.Equal(7, item.OwnerUserId);
            Assert.Equal("D1", item.DepartmentId);
            Assert.Equal("INV-PAY", item.InvoiceNo);
            Assert.Equal(3, item.PayeeId);
            Assert.Equal("Sales", item.Department);
            Assert.Equal("Spring", item.Project);
            Assert.Equal(120.5m, item.USDAmount);
            Assert.Equal(870.25m, item.CNYAmount);
            Assert.Equal("TT", item.PaymentMethod);
            Assert.Equal("Factory", item.PayeeName);
            Assert.Equal("Exporter", item.PayerName);
            Assert.Equal("Bank", item.BankName);
            Assert.Equal("ACC", item.AccountNo);
            Assert.Equal("Jacket", item.GoodsName);
            Assert.Equal("10 PCS", item.Quantity);
            Assert.Equal("Canada", item.ShipmentCountry);
            Assert.Equal(8m, item.OtherExpense);
        }

        [Fact]
        public void PaymentDtoFactory_ShouldNormalizeNullableTextFields()
        {
            var item = ApiPaymentDtoFactory.FromPayment(new Payment());

            Assert.Equal(string.Empty, item.DepartmentId);
            Assert.Equal(string.Empty, item.CompanyScope);
            Assert.Equal(string.Empty, item.InvoiceNo);
            Assert.Equal(string.Empty, item.Department);
            Assert.Equal(string.Empty, item.Project);
            Assert.Equal(string.Empty, item.PaymentMethod);
            Assert.Equal(string.Empty, item.PayeeName);
            Assert.Equal(string.Empty, item.PayerName);
            Assert.Equal(string.Empty, item.BankName);
            Assert.Equal(string.Empty, item.AccountNo);
            Assert.Equal(string.Empty, item.Notes);
            Assert.Equal(string.Empty, item.GoodsName);
            Assert.Equal(string.Empty, item.Quantity);
            Assert.Equal(string.Empty, item.ShipmentCountry);
        }

        [Fact]
        public void PaymentDtoFactory_ToPaymentForSave_ShouldMapWritableFieldsAndIgnoreRequestOwnership()
        {
            var request = new ApiPaymentDto
            {
                Id = 4,
                OwnerUserId = 99,
                DepartmentId = "External",
                CompanyScope = "External",
                InvoiceNo = "INV-PAY-SAVE",
                ShipmentDate = new DateTime(2026, 6, 1),
                PayeeId = 5,
                Department = "Sales",
                Project = "Autumn",
                USDAmount = 100m,
                CNYAmount = 720m,
                PaymentMethod = "TT",
                PayeeName = "Factory",
                PayerName = "Exporter",
                BankName = "Bank",
                AccountNo = "ACC",
                Notes = "Save",
                PaymentDate = new DateTime(2026, 6, 2),
                GoodsName = "Coat",
                Quantity = "20 PCS",
                ShipmentCountry = "US",
                ReceiptDate = new DateTime(2026, 6, 3),
                OtherExpense = 9m
            };

            var payment = ApiPaymentDtoFactory.ToPaymentForSave(request);

            Assert.Equal(4, payment.Id);
            Assert.Null(payment.OwnerUserId);
            Assert.Equal(string.Empty, payment.DepartmentId);
            Assert.Equal(string.Empty, payment.CompanyScope);
            Assert.Equal("INV-PAY-SAVE", payment.InvoiceNo);
            Assert.Equal("Autumn", payment.Project);
            Assert.Equal(720m, payment.CNYAmount);
            Assert.Equal("Factory", payment.PayeeName);
            Assert.Equal("Coat", payment.GoodsName);
            Assert.Equal(9m, payment.OtherExpense);
        }

        [Fact]
        public void PaymentDtoFactory_PreserveExistingOwnership_ShouldKeepStoredScope()
        {
            var target = new Payment { InvoiceNo = "PAY-TARGET" };
            var existing = new Payment
            {
                OwnerUserId = 7,
                DepartmentId = "Doc",
                CompanyScope = "CN"
            };

            ApiPaymentDtoFactory.PreserveExistingOwnership(target, existing);

            Assert.Equal(7, target.OwnerUserId);
            Assert.Equal("Doc", target.DepartmentId);
            Assert.Equal("CN", target.CompanyScope);
        }
    }
}
