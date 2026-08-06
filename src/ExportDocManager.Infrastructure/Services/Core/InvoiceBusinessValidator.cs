using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    internal static class InvoiceBusinessValidator
    {
        private const int MaximumItemCount = 2000;
        private const decimal MaximumBusinessNumber = 1_000_000_000_000m;

        public static async Task ValidateNormalizeAndCalculateAsync(
            AppDbContext context,
            Invoice invoice,
            IReadOnlyList<Item> items,
            bool isNew,
            string existingStatus,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(invoice);

            NormalizeInvoice(invoice);
            ValidateInvoiceHeader(invoice, isNew, existingStatus);

            var normalizedItems = (items ?? Array.Empty<Item>())
                .Where(item => item != null)
                .ToList();
            if (normalizedItems.Count > MaximumItemCount)
            {
                throw new InvoiceValidationException($"单张发票最多允许 {MaximumItemCount} 行商品明细。");
            }

            foreach (var item in normalizedItems)
            {
                NormalizeAndValidateItem(item, invoice.Id);
                RecalculateItem(item);
            }

            await ValidateHsCodesAsync(context, normalizedItems, cancellationToken).ConfigureAwait(false);
            invoice.Items = normalizedItems;
            RecalculateInvoice(invoice);
        }

        public static void ValidateForStatusTransition(Invoice invoice, string targetStatus)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            string target = InvoiceStatusCatalog.Normalize(targetStatus);

            if (string.Equals(target, InvoiceStatusCatalog.Verified, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(invoice.CustomerNameEN) ||
                    string.IsNullOrWhiteSpace(invoice.ExporterNameEN))
                {
                    throw new InvoiceValidationException("提交核对前必须填写客户和出口商信息。");
                }

                if (invoice.Items == null || invoice.Items.Count == 0)
                {
                    throw new InvoiceValidationException("提交核对前至少需要一行商品明细。");
                }

                if (invoice.Items.Any(item =>
                        string.IsNullOrWhiteSpace(item.StyleName) && string.IsNullOrWhiteSpace(item.StyleNameCN) ||
                        item.Quantity <= 0))
                {
                    throw new InvoiceValidationException("提交核对前，每行商品必须填写品名且数量大于 0。");
                }
            }

            if (string.Equals(target, InvoiceStatusCatalog.Shipped, StringComparison.OrdinalIgnoreCase) &&
                invoice.ShipmentDate.Year is < 1900 or > 2100)
            {
                throw new InvoiceValidationException("确认出运前必须填写有效的出运日期。");
            }
        }

        private static void NormalizeInvoice(Invoice invoice)
        {
            invoice.InvoiceNo = NormalizeText(invoice.InvoiceNo, 100, "发票号", required: true);
            invoice.ContractNo = NormalizeText(invoice.ContractNo, 100, "合同号");
            invoice.LetterOfCreditNo = NormalizeText(invoice.LetterOfCreditNo, 100, "信用证号");
            invoice.LetterOfCreditSourcePath = NormalizeText(invoice.LetterOfCreditSourcePath, 1000, "信用证来源路径");
            invoice.LetterOfCreditContent = NormalizeText(invoice.LetterOfCreditContent, 2_000_000, "信用证内容");
            invoice.IssuingBank = NormalizeText(invoice.IssuingBank, 300, "开证行");
            invoice.CustomsBrokerName = NormalizeText(invoice.CustomsBrokerName, 300, "报关行名称");
            invoice.CustomsBrokerCode = NormalizeText(invoice.CustomsBrokerCode, 100, "报关行编码");
            invoice.Spare1 = NormalizeText(invoice.Spare1, 500, "备用字段1");
            invoice.Spare2 = NormalizeText(invoice.Spare2, 500, "备用字段2");
            invoice.Spare3 = NormalizeText(invoice.Spare3, 500, "备用字段3");
            invoice.CustomFieldsJson = NormalizeJson(invoice.CustomFieldsJson, 100_000, "发票扩展字段");
            invoice.PaymentTerms = NormalizeText(invoice.PaymentTerms, 300, "付款条款");
            invoice.PortOfLoading = NormalizeText(invoice.PortOfLoading, 200, "装运港");
            invoice.PortOfDestination = NormalizeText(invoice.PortOfDestination, 200, "目的港");
            invoice.DestinationCountry = NormalizeText(invoice.DestinationCountry, 200, "目的国");
            invoice.ShippingMarks = NormalizeText(invoice.ShippingMarks, 20_000, "唛头");
            invoice.ShippingMarksType = NormalizeText(invoice.ShippingMarksType, 20, "唛头类型");
            if (string.IsNullOrWhiteSpace(invoice.ShippingMarksType))
            {
                invoice.ShippingMarksType = "Text";
            }
            invoice.ShippingMarksImage = NormalizeText(invoice.ShippingMarksImage, 1000, "唛头图片路径");
            invoice.TradeTerms = NormalizeText(invoice.TradeTerms, 100, "贸易条款");
            invoice.TransportMode = NormalizeText(invoice.TransportMode, 100, "运输方式");
            invoice.Currency = NormalizeText(invoice.Currency, 10, "币种").ToUpperInvariant();
            invoice.SpecialTerms = NormalizeText(invoice.SpecialTerms, 20_000, "特殊条款");
            invoice.Type = InvoiceTypeCatalog.Normalize(invoice.Type);
            invoice.Status = InvoiceStatusCatalog.Normalize(invoice.Status);
            invoice.SupervisionMode = NormalizeText(invoice.SupervisionMode, 100, "监管方式");
            invoice.CustomerNameEN = NormalizeText(invoice.CustomerNameEN, 500, "客户英文名称");
            invoice.CustomerAddressEN = NormalizeText(invoice.CustomerAddressEN, 2000, "客户英文地址");
            invoice.NotifyPartyName = NormalizeText(invoice.NotifyPartyName, 500, "通知方名称");
            invoice.NotifyPartyAddress = NormalizeText(invoice.NotifyPartyAddress, 2000, "通知方地址");
            invoice.ExporterNameEN = NormalizeText(invoice.ExporterNameEN, 500, "出口商英文名称");
            invoice.ExporterNameCN = NormalizeText(invoice.ExporterNameCN, 500, "出口商中文名称");
            invoice.ExporterAddressEN = NormalizeText(invoice.ExporterAddressEN, 2000, "出口商英文地址");
            invoice.ExporterAddressCN = NormalizeText(invoice.ExporterAddressCN, 2000, "出口商中文地址");
            invoice.ExporterCreditCode = NormalizeText(invoice.ExporterCreditCode, 100, "统一信用代码");
            invoice.ExporterCustomsCode = NormalizeText(invoice.ExporterCustomsCode, 100, "出口商海关编码");
            invoice.BankName = NormalizeText(invoice.BankName, 300, "银行名称");
            invoice.BankAccount = NormalizeText(invoice.BankAccount, 200, "银行账号");
            invoice.SwiftCode = NormalizeText(invoice.SwiftCode, 50, "SWIFT编码").ToUpperInvariant();
            invoice.DepartmentId = NormalizeText(invoice.DepartmentId, 50, "部门范围");
            invoice.CompanyScope = NormalizeText(invoice.CompanyScope, 50, "公司范围");
            if (invoice.ExchangeRate.HasValue)
            {
                if (invoice.ExchangeRate.Value < 0 || invoice.ExchangeRate.Value > 1_000_000m)
                {
                    throw new InvoiceValidationException("汇率必须大于 0 且不能超过 1,000,000。");
                }

                if (invoice.ExchangeRate.Value == 0)
                {
                    invoice.ExchangeRate = null;
                }
            }
        }

        private static void ValidateInvoiceHeader(Invoice invoice, bool isNew, string existingStatus)
        {
            if (!InvoiceTypeCatalog.IsKnown(invoice.Type))
            {
                throw new InvoiceValidationException("业务类型只能是“实际数据”或“报关数据”。");
            }

            if (!InvoiceStatusCatalog.IsKnown(invoice.Status))
            {
                throw new InvoiceValidationException("发票状态无效，请刷新后重试。");
            }

            if (isNew && !string.Equals(invoice.Status, InvoiceStatusCatalog.Draft, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvoiceValidationException("新建发票必须先保存为草稿，再通过状态操作提交核对。");
            }

            if (!isNew && !string.Equals(
                    InvoiceStatusCatalog.Normalize(existingStatus),
                    invoice.Status,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvoiceValidationException("不能通过普通保存直接修改发票状态，请使用状态流转操作。");
            }

            if (invoice.InvoiceDate.Year is < 1900 or > 2100 || invoice.ShipmentDate.Year is < 1900 or > 2100)
            {
                throw new InvoiceValidationException("发票日期和出运日期必须在 1900—2100 年之间。");
            }

            if (!string.Equals(invoice.ShippingMarksType, "Text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(invoice.ShippingMarksType, "Image", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvoiceValidationException("唛头类型只能是文本或图片。");
            }
            if (string.Equals(invoice.ShippingMarksType, "Image", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    invoice.ShippingMarksImage = ManagedDataPathResolver.NormalizeStoredPath(
                        invoice.ShippingMarksImage,
                        "Marks");
                }
                catch (InvalidDataException ex)
                {
                    throw new InvoiceValidationException(ex.Message);
                }
            }
            else
            {
                invoice.ShippingMarksImage = string.Empty;
            }
        }

        private static void NormalizeAndValidateItem(Item item, int invoiceId)
        {
            if (item.InvoiceId != 0 && invoiceId > 0 && item.InvoiceId != invoiceId)
            {
                throw new InvoiceValidationException("商品明细所属发票与当前发票不一致。");
            }

            item.PoNumber = NormalizeText(item.PoNumber, 100, "PO号");
            item.StyleNo = NormalizeText(item.StyleNo, 200, "款号");
            item.StyleName = NormalizeText(item.StyleName, 500, "英文品名");
            item.FabricComposition = NormalizeText(item.FabricComposition, 1000, "成分");
            item.StyleNameCN = NormalizeText(item.StyleNameCN, 500, "中文品名");
            item.Brand = NormalizeText(item.Brand, 200, "品牌");
            item.HSCode = HsCodeTextHelper.NormalizeCode(item.HSCode);
            item.Origin = NormalizeText(item.Origin, 200, "原产地");
            item.UnitEN = NormalizeText(item.UnitEN, 50, "英文单位").ToUpperInvariant();
            item.UnitCN = NormalizeText(item.UnitCN, 50, "中文单位");
            item.CtnUnitEN = NormalizeText(item.CtnUnitEN, 50, "英文包装单位").ToUpperInvariant();
            item.CtnUnitCN = NormalizeText(item.CtnUnitCN, 50, "中文包装单位");
            item.Spare1 = NormalizeText(item.Spare1, 500, "明细备用字段1");
            item.Spare2 = NormalizeText(item.Spare2, 500, "明细备用字段2");
            item.Spare3 = NormalizeText(item.Spare3, 500, "明细备用字段3");
            item.CustomFieldsJson = NormalizeJson(item.CustomFieldsJson, 50_000, "明细扩展字段");

            // Keep the persisted invoice measurement precision stable before
            // validation and recalculation. This also normalizes rows imported
            // from spreadsheets that contain more display digits than the UI
            // supports.
            item.Volume = ItemMeasurementPrecisionPolicy.RoundVolume(item.Volume);
            item.GWPerCtn = ItemMeasurementPrecisionPolicy.RoundWeight(item.GWPerCtn);
            item.NWPerCtn = ItemMeasurementPrecisionPolicy.RoundWeight(item.NWPerCtn);
            item.GWTotal = ItemMeasurementPrecisionPolicy.RoundWeight(item.GWTotal);
            item.NWTotal = ItemMeasurementPrecisionPolicy.RoundWeight(item.NWTotal);

            if (!string.IsNullOrWhiteSpace(item.HSCode) &&
                (item.HSCode.Length is < 6 or > 20 || !item.HSCode.All(char.IsDigit)))
            {
                throw new InvoiceValidationException($"HS 编码“{item.HSCode}”格式无效，应为 6—20 位数字。");
            }

            ValidateNonNegative(item.Quantity, "数量");
            ValidateNonNegative(item.PcsPerCtn, "每箱数量");
            ValidateNonNegative(item.Cartons, "箱数");
            ValidateNonNegative(item.Length, "长度");
            ValidateNonNegative(item.Width, "宽度");
            ValidateNonNegative(item.Height, "高度");
            ValidateNonNegative(item.Volume, "体积");
            ValidateNonNegative(item.GWPerCtn, "每箱毛重");
            ValidateNonNegative(item.NWPerCtn, "每箱净重");
            ValidateNonNegative(item.GWTotal, "总毛重");
            ValidateNonNegative(item.NWTotal, "总净重");
            item.PriceCalculationMode = ItemPriceCalculationModeCatalog.Normalize(item.PriceCalculationMode);
            if (!ItemPriceCalculationModeCatalog.IsKnown(item.PriceCalculationMode))
            {
                throw new InvoiceValidationException("明细价格核算方式无效。请刷新页面后重试。");
            }
            ValidateNonNegative(item.UnitPrice, "销售单价");
            ValidateNonNegative(item.TotalPrice, "销售金额");
            ValidateNonNegative(item.PurchasePrice, "采购单价");
            if (item.TaxRebateRate is < 0 or > 100)
            {
                throw new InvoiceValidationException("退税率必须在 0—100 之间。");
            }
        }

        private static async Task ValidateHsCodesAsync(
            AppDbContext context,
            IReadOnlyCollection<Item> items,
            CancellationToken cancellationToken)
        {
            string[] codes = items
                .Select(item => item.HSCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (codes.Length == 0)
            {
                return;
            }

            bool hasTrustedCatalog = await context.HsCodes
                .AsNoTracking()
                .AnyAsync(item =>
                    item.Status == HsCodeValidityPolicy.ActiveStatus &&
                    item.SourceName != null && item.SourceName != string.Empty &&
                    item.EffectiveYear >= 2000 && item.EffectiveYear <= 2100 &&
                    item.LastVerifiedAt != null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!hasTrustedCatalog)
            {
                return;
            }

            var trustedCodes = await context.HsCodes
                .AsNoTracking()
                .Where(item => codes.Contains(item.NormalizedCode) &&
                    item.Status == HsCodeValidityPolicy.ActiveStatus &&
                    item.SourceName != null && item.SourceName != string.Empty &&
                    item.EffectiveYear >= 2000 && item.EffectiveYear <= 2100 &&
                    item.LastVerifiedAt != null)
                .Select(item => item.NormalizedCode)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var trustedSet = trustedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] invalidCodes = codes.Where(code => !trustedSet.Contains(code)).ToArray();
            if (invalidCodes.Length > 0)
            {
                throw new InvoiceValidationException(
                    $"以下 HS 编码不在当前已验证年度税则中或已经失效：{string.Join("、", invalidCodes)}。");
            }
        }

        private static void RecalculateItem(Item item)
        {
            if (item.Quantity > 0 && item.PcsPerCtn > 0)
            {
                item.Cartons = Math.Ceiling(item.Quantity / item.PcsPerCtn);
            }

            item.PriceCalculationMode = ItemPriceCalculationModeCatalog.Normalize(item.PriceCalculationMode);
            if (string.Equals(
                    item.PriceCalculationMode,
                    ItemPriceCalculationModeCatalog.LineAmountDriven,
                    StringComparison.Ordinal))
            {
                item.TotalPrice = RoundMoney(item.TotalPrice);
                item.UnitPrice = item.Quantity > 0
                    ? RoundUnitPrice(item.TotalPrice / item.Quantity)
                    : 0m;
            }
            else
            {
                item.UnitPrice = RoundUnitPrice(item.UnitPrice);
                item.TotalPrice = RoundMoney(item.Quantity * item.UnitPrice);
            }
            item.PurchaseTotal = RoundMoney(item.Quantity * item.PurchasePrice);

            item.Volume = item.Length > 0 && item.Width > 0 && item.Height > 0 && item.Cartons > 0
                ? RoundMeasure(item.Length * item.Width * item.Height * item.Cartons / 1_000_000m)
                : 0m;
            item.GWTotal = item.GWPerCtn > 0 && item.Cartons > 0
                ? ItemMeasurementPrecisionPolicy.RoundWeight(item.GWPerCtn * item.Cartons)
                : 0m;
            item.NWTotal = item.NWPerCtn > 0 && item.Cartons > 0
                ? ItemMeasurementPrecisionPolicy.RoundWeight(item.NWPerCtn * item.Cartons)
                : 0m;
        }

        private static void RecalculateInvoice(Invoice invoice)
        {
            var items = invoice.Items ?? [];
            invoice.TotalCartons = RoundMoney(items.Sum(item => item.Cartons));
            invoice.TotalQuantity = RoundMoney(items.Sum(item => item.Quantity));
            invoice.TotalGrossWeight = ItemMeasurementPrecisionPolicy.RoundWeight(items.Sum(item => item.GWTotal));
            invoice.TotalNetWeight = ItemMeasurementPrecisionPolicy.RoundWeight(items.Sum(item => item.NWTotal));
            invoice.TotalVolume = ItemMeasurementPrecisionPolicy.RoundVolume(items.Sum(item => item.Volume));
            invoice.TotalAmount = RoundMoney(items.Sum(item => item.TotalPrice));
            invoice.TotalPurchaseAmount = RoundMoney(items.Sum(item => item.PurchaseTotal));
            invoice.TotalTaxRefundAmount = RoundMoney(items.Sum(item => item.TaxRefundAmount));

            decimal? effectiveRate = invoice.ExchangeRate;
            if (!effectiveRate.HasValue && string.Equals(invoice.Currency, "CNY", StringComparison.OrdinalIgnoreCase))
            {
                effectiveRate = 1m;
            }

            invoice.TotalProfit = effectiveRate is > 0
                ? RoundMoney(invoice.TotalAmount * effectiveRate.Value - invoice.TotalPurchaseAmount + invoice.TotalTaxRefundAmount)
                : 0m;
        }

        private static string NormalizeText(string value, int maximumLength, string fieldName, bool required = false)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (required && string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvoiceValidationException($"{fieldName}不能为空。");
            }

            if (normalized.Length > maximumLength)
            {
                throw new InvoiceValidationException($"{fieldName}不能超过 {maximumLength} 个字符。");
            }

            return normalized;
        }

        private static string NormalizeJson(string value, int maximumLength, string fieldName)
        {
            string normalized = NormalizeText(value, maximumLength, fieldName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            try
            {
                using var _ = JsonDocument.Parse(normalized);
                return normalized;
            }
            catch (JsonException)
            {
                throw new InvoiceValidationException($"{fieldName}必须是有效的 JSON。");
            }
        }

        private static void ValidateNonNegative(decimal value, string fieldName)
        {
            if (value < 0 || value > MaximumBusinessNumber)
            {
                throw new InvoiceValidationException($"{fieldName}必须在 0—{MaximumBusinessNumber:N0} 之间。");
            }
        }

        private static decimal RoundMoney(decimal value) =>
            decimal.Round(value, 2, MidpointRounding.AwayFromZero);

        private static decimal RoundUnitPrice(decimal value) =>
            ItemPricePrecisionPolicy.Round(value);

        private static decimal RoundMeasure(decimal value) =>
            ItemMeasurementPrecisionPolicy.RoundVolume(value);
    }
}
