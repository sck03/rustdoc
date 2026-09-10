using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiPaymentDtoFactory
    {
        public static ApiPagedResponse<ApiPaymentDto> FromPagedPayments(PagedResult<Payment> result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiPagedResponse<ApiPaymentDto>(
                result.Items.Select(FromPayment).ToList(),
                result.TotalCount,
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage);
        }

        public static ApiPaymentDto FromPayment(Payment payment)
        {
            ArgumentNullException.ThrowIfNull(payment);

            return new ApiPaymentDto
            {
                Id = payment.Id,
                OwnerUserId = payment.OwnerUserId,
                DepartmentId = payment.DepartmentId ?? string.Empty,
                CompanyScope = payment.CompanyScope ?? string.Empty,
                InvoiceNo = payment.InvoiceNo ?? string.Empty,
                VoucherNo = payment.VoucherNo ?? string.Empty,
                QuantityUnit = payment.QuantityUnit ?? string.Empty,
                TradeMethod = payment.TradeMethod ?? string.Empty,
                TaxRebateRate = payment.TaxRebateRate ?? string.Empty,
                Spare1 = payment.Spare1 ?? string.Empty,
                Spare2 = payment.Spare2 ?? string.Empty,
                Spare3 = payment.Spare3 ?? string.Empty,
                Spare4 = payment.Spare4 ?? string.Empty,
                Spare5 = payment.Spare5 ?? string.Empty,
                Spare6 = payment.Spare6 ?? string.Empty,
                Spare7 = payment.Spare7 ?? string.Empty,
                Spare8 = payment.Spare8 ?? string.Empty,
                Spare9 = payment.Spare9 ?? string.Empty,
                Spare10 = payment.Spare10 ?? string.Empty,
                ShipmentDate = payment.ShipmentDate,
                PayeeId = payment.PayeeId,
                Department = payment.Department ?? string.Empty,
                Project = payment.Project ?? string.Empty,
                USDAmount = payment.USDAmount,
                CNYAmount = payment.CNYAmount,
                PaymentMethod = payment.PaymentMethod ?? string.Empty,
                PayeeName = payment.PayeeName ?? string.Empty,
                PayerName = payment.PayerName ?? string.Empty,
                BankName = payment.BankName ?? string.Empty,
                AccountNo = payment.AccountNo ?? string.Empty,
                Notes = payment.Notes ?? string.Empty,
                PaymentDate = payment.PaymentDate,
                GoodsName = payment.GoodsName ?? string.Empty,
                Quantity = payment.Quantity ?? string.Empty,
                ShipmentCountry = payment.ShipmentCountry ?? string.Empty,
                ReceiptDate = payment.ReceiptDate,
                TravelExpense = payment.TravelExpense,
                BusinessEntertainmentExpense = payment.BusinessEntertainmentExpense,
                TelephoneExpense = payment.TelephoneExpense,
                OfficeExpense = payment.OfficeExpense,
                RepairExpense = payment.RepairExpense,
                FreightMiscExpense = payment.FreightMiscExpense,
                InspectionExpense = payment.InspectionExpense,
                OtherExpense = payment.OtherExpense,
                RowVersion = payment.RowVersion == null || payment.RowVersion.Length == 0
                    ? string.Empty
                    : Convert.ToBase64String(payment.RowVersion)
            };
        }

        public static Payment ToPaymentForSave(ApiPaymentDto request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new Payment
            {
                Id = request.Id,
                InvoiceNo = request.InvoiceNo,
                VoucherNo = request.VoucherNo,
                QuantityUnit = request.QuantityUnit,
                TradeMethod = request.TradeMethod,
                TaxRebateRate = request.TaxRebateRate,
                Spare1 = request.Spare1,
                Spare2 = request.Spare2,
                Spare3 = request.Spare3,
                Spare4 = request.Spare4,
                Spare5 = request.Spare5,
                Spare6 = request.Spare6,
                Spare7 = request.Spare7,
                Spare8 = request.Spare8,
                Spare9 = request.Spare9,
                Spare10 = request.Spare10,
                ShipmentDate = request.ShipmentDate,
                PayeeId = request.PayeeId,
                Department = request.Department,
                Project = request.Project,
                USDAmount = request.USDAmount,
                CNYAmount = request.CNYAmount,
                PaymentMethod = request.PaymentMethod,
                PayeeName = request.PayeeName,
                PayerName = request.PayerName,
                BankName = request.BankName,
                AccountNo = request.AccountNo,
                Notes = request.Notes,
                PaymentDate = request.PaymentDate,
                GoodsName = request.GoodsName,
                Quantity = request.Quantity,
                ShipmentCountry = request.ShipmentCountry,
                ReceiptDate = request.ReceiptDate,
                TravelExpense = request.TravelExpense,
                BusinessEntertainmentExpense = request.BusinessEntertainmentExpense,
                TelephoneExpense = request.TelephoneExpense,
                OfficeExpense = request.OfficeExpense,
                RepairExpense = request.RepairExpense,
                FreightMiscExpense = request.FreightMiscExpense,
                InspectionExpense = request.InspectionExpense,
                OtherExpense = request.OtherExpense,
                RowVersion = DecodeRowVersion(request.RowVersion)
            };
        }

        public static void PreserveExistingOwnership(Payment target, Payment existing)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(existing);

            target.OwnerUserId = existing.OwnerUserId;
            target.DepartmentId = existing.DepartmentId ?? string.Empty;
            target.CompanyScope = existing.CompanyScope ?? string.Empty;
        }

        private static byte[]? DecodeRowVersion(string? rowVersion)
        {
            if (string.IsNullOrWhiteSpace(rowVersion)) return null;
            try
            {
                return Convert.FromBase64String(rowVersion);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("付款记录版本号格式无效，请刷新后重试。", nameof(rowVersion), exception);
            }
        }
    }
}
