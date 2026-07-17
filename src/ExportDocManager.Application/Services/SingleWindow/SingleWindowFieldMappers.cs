using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using static ExportDocManager.Services.SingleWindow.SingleWindowFieldMapperHelpers;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class CustomsCooFieldMapper : ICustomsCooFieldMapper
    {
        public CooMappedDocument Map(CooSourceSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var invoice = snapshot.Invoice ?? new Invoice();
            var exporter = snapshot.Exporter;
            var customer = snapshot.Customer;
            var existing = snapshot.ExistingDocument;
            var warnings = new List<string>();
            string firstOrigin = snapshot.Items.FirstOrDefault()?.Origin;
            string exporterCreditCode = CustomsCooTextFormatter.ResolveExporterCreditCode(invoice, exporter);
            string exporterNameEnglish = invoice.ExporterNameEN ?? exporter?.ExporterNameEN;
            string exporterAddressEnglish = invoice.ExporterAddressEN ?? exporter?.AddressEN;
            string customerNameEnglish = invoice.CustomerNameEN ?? customer?.CustomerNameEN;
            string customerAddressEnglish = invoice.CustomerAddressEN ?? customer?.AddressEN;
            string existingOrgCode = NormalizeText(existing?.OrgCode);
            string existingFetchPlace = NormalizeText(existing?.FetchPlace);
            string derivedApplicationAddress = FirstNonEmpty(
                CustomsCooIssuingAuthorityCatalog.ResolveApplicationAddress(existingOrgCode),
                CustomsCooIssuingAuthorityCatalog.ResolveApplicationAddress(existingFetchPlace));

            var goods = BuildGoodsItems(snapshot, invoice);

            if (goods.Count == 0)
            {
                warnings.Add("当前发票没有商品明细，导出的海关原产地证 XML 仅为骨架。");
            }

            string mappedOriginCountryCode = NormalizeRecognizedCountryCode(firstOrigin);
            string mappedOriginCountryName = NormalizeRecognizedCountryNameEnglish(firstOrigin);

            var document = new CooMappedDocument
            {
                CertNo = string.Empty,
                ApplyType = PreferValue(existing?.ApplyType, "0"),
                CertStatus = PreferValue(existing?.CertStatus, "0"),
                CertType = PreferValue(existing?.CertType, "C"),
                EntMgrNo = PreferValue(existing?.EntMgrNo, string.Empty),
                CiqRegNo = PreferValue(existing?.CiqRegNo, exporterCreditCode),
                AplRegNo = PreferValue(existing?.AplRegNo, exporterCreditCode),
                EtpsName = PreferValue(existing?.EtpsName, NormalizeText(invoice.ExporterNameCN ?? exporter?.ExporterNameCN)),
                ApplName = PreferValue(existing?.ApplName, string.Empty),
                Applicant = PreferValue(existing?.Applicant, string.Empty),
                ApplTel = PreferValue(existing?.ApplTel, string.Empty),
                OrgCode = PreferValue(existing?.OrgCode, string.Empty),
                FetchPlace = PreferValue(existing?.FetchPlace, existingOrgCode),
                AplAdd = PreferValue(existing?.AplAdd, derivedApplicationAddress),
                InvDate = PreferValue(
                    existing?.InvDate,
                    invoice.InvoiceDate > DateTime.MinValue ? invoice.InvoiceDate.ToString("yyyy-MM-dd") : string.Empty),
                InvNo = PreferValue(existing?.InvNo, NormalizeText(invoice.InvoiceNo)),
                AplDate = PreferValue(existing?.AplDate, DateTime.Today.ToString("yyyy-MM-dd")),
                DestCountry = PreferValue(existing?.DestCountry, NormalizeCountryNameEnglish(invoice.DestinationCountry)),
                DestCountryCode = PreferValue(existing?.DestCountryCode, NormalizeCountryCode(invoice.DestinationCountry)),
                DestCountryName = PreferValue(existing?.DestCountryName, NormalizeCountryNameChinese(invoice.DestinationCountry)),
                Exporter = PreferPartyBlock(
                    existing?.Exporter,
                    CustomsCooTextFormatter.BuildPartyBlock(exporterNameEnglish, exporterAddressEnglish)),
                Consignee = PreferPartyBlock(
                    existing?.Consignee,
                    CustomsCooTextFormatter.BuildPartyBlock(customerNameEnglish, customerAddressEnglish)),
                GoodsSpecClause = PreferValue(existing?.GoodsSpecClause, NormalizeText(invoice.SpecialTerms)),
                Mark = PreferValue(existing?.Mark, string.IsNullOrWhiteSpace(invoice.ShippingMarks) ? "N/M" : invoice.ShippingMarks.Trim()),
                LoadPort = PreferValue(existing?.LoadPort, NormalizePort(invoice.PortOfLoading)),
                UnloadPort = PreferValue(existing?.UnloadPort, NormalizePort(invoice.PortOfDestination)),
                TransMeans = PreferValue(existing?.TransMeans, NormalizeTransportMode(invoice.TransportMode)),
                TransName = PreferValue(existing?.TransName, string.Empty),
                TransCountryCode = PreferValue(existing?.TransCountryCode, NormalizeCountryCode(existing?.TransCountryName)),
                TransCountryName = PreferValue(existing?.TransCountryName, NormalizeCountryNameChinese(existing?.TransCountryCode)),
                TransPort = PreferValue(existing?.TransPort, NormalizePort(existing?.TransPort)),
                DestPort = PreferValue(existing?.DestPort, NormalizePort(invoice.PortOfDestination)),
                TransDetails = PreferValue(
                    existing?.TransDetails,
                    BuildCooTransportDetails(
                        invoice.PortOfLoading,
                        invoice.PortOfDestination,
                        existing?.TransPort,
                        invoice.TransportMode)),
                IntendExpDate = PreferValue(existing?.IntendExpDate, invoice.ShipmentDate > DateTime.MinValue ? invoice.ShipmentDate.ToString("yyyy-MM-dd") : string.Empty),
                TradeModeCode = PreferCooTradeModeCode(existing?.TradeModeCode, invoice.SupervisionMode),
                FobValue = PreferValue(existing?.FobValue, FormatDecimal(invoice.TotalAmount, 5)),
                TotalAmt = PreferValue(existing?.TotalAmt, FormatDecimal(invoice.TotalAmount, 5)),
                Note = PreferValue(existing?.Note, NormalizeText(invoice.SpecialTerms)),
                ContractNo = PreferValue(existing?.ContractNo, NormalizeText(invoice.ContractNo)),
                LcNo = PreferValue(existing?.LcNo, NormalizeText(invoice.LetterOfCreditNo)),
                SpecInvTerms = PreferValue(existing?.SpecInvTerms, NormalizeText(invoice.SpecialTerms)),
                PriceTerms = PreferValue(NormalizePriceTerms(existing?.PriceTerms), NormalizePriceTerms(invoice.TradeTerms)),
                Curr = PreferValue(existing?.Curr, NormalizeCurrencyText(invoice.Currency)),
                Remark = PreferValue(existing?.Remark, NormalizeText(invoice.SpecialTerms)),
                Producer = PreferValue(existing?.Producer, string.Empty),
                ProducerSertFlag = PreferValue(existing?.ProducerSertFlag, string.Empty),
                ExhibitFlag = PreferValue(existing?.ExhibitFlag, string.Empty),
                ThirdPartyInvFlag = PreferValue(existing?.ThirdPartyInvFlag, string.Empty),
                ExporterTel = PreferValue(existing?.ExporterTel, NormalizePhone(exporter?.Phone)),
                ExporterFax = PreferValue(existing?.ExporterFax, string.Empty),
                ExporterEmail = PreferValue(existing?.ExporterEmail, string.Empty),
                ConsigneeTel = PreferValue(existing?.ConsigneeTel, NormalizePhone(customer?.Phone)),
                ConsigneeFax = PreferValue(existing?.ConsigneeFax, string.Empty),
                ConsigneeEmail = PreferValue(existing?.ConsigneeEmail, NormalizeText(customer?.Email)),
                PredictFlag = PreferValue(existing?.PredictFlag, string.Empty),
                ExpDeclDate = PreferValue(existing?.ExpDeclDate, string.Empty),
                OriCountryCode = PreferValue(existing?.OriCountryCode, mappedOriginCountryCode),
                OriCountry = PreferRecognizedCountryNameEnglish(existing?.OriCountry, mappedOriginCountryName, firstOrigin),
                ChkValidDate = PreferValue(existing?.ChkValidDate, string.Empty),
                EtpsConcEr = PreferValue(existing?.EtpsConcEr, NormalizeText(exporter?.ContactPerson)),
                EtpsTel = PreferValue(existing?.EtpsTel, NormalizePhone(exporter?.Phone)),
                EntryId = PreferValue(existing?.EntryId, string.Empty),
                PrcsAssembly = PreferValue(existing?.PrcsAssembly, string.Empty),
                OldCertNo = PreferValue(existing?.OldCertNo, string.Empty),
                ModReason = PreferValue(existing?.ModReason, string.Empty),
                ModColm = PreferValue(existing?.ModColm, string.Empty),
                OldSituDesc = PreferValue(existing?.OldSituDesc, string.Empty),
                ModSituDesc = PreferValue(existing?.ModSituDesc, string.Empty),
                OldDeclDate = PreferValue(existing?.OldDeclDate, string.Empty),
                OldIssueDate = PreferValue(existing?.OldIssueDate, string.Empty),
                AplPromiseCode = PreferValue(existing?.AplPromiseCode, "1"),
                Goods = goods,
                NonpartyCorps = existing?.NonpartyCorps?
                    .OrderBy(item => item.SortNo)
                    .Select(item => new CooMappedNonpartyCorp
                    {
                        SortNo = item.SortNo,
                        EntName = item.EntName ?? string.Empty,
                        EntAddr = item.EntAddr ?? string.Empty,
                        EntCountryCode = item.EntCountryCode ?? string.Empty,
                        EntCountryName = item.EntCountryName ?? string.Empty
                    })
                    .ToList() ?? [],
                Attachments = snapshot.Attachments ?? [],
                Warnings = warnings
            };

            document.CertNo = CustomsCooCertNoGenerator.NormalizeOrGenerate(
                existing?.CertNo,
                document.CertType,
                document.CiqRegNo,
                document.AplRegNo,
                document.AplDate);

            AddIfMissing(document.CiqRegNo, "出口商代码(CiqRegNo)缺失。", warnings);
            AddIfMissing(document.EtpsName, "企业名称(EtpsName)缺失。", warnings);
            AddIfMissing(document.Exporter, "出口商(Exporter)缺失。", warnings);
            AddIfMissing(document.Consignee, "收货人(Consignee)缺失。", warnings);
            AddIfMissing(document.Mark, "唛头(Mark)缺失。", warnings);
            AddIfMissing(document.DestCountry, "进口国/地区(DestCountry)缺失。", warnings);
            AddIfMissing(document.DestCountryCode, "进口国/地区编码(DestCountryCode)缺失。", warnings);
            AddIfMissing(document.DestCountryName, "进口国/地区中文名(DestCountryName)缺失。", warnings);
            AddIfMissing(document.AplRegNo, "录入企业代码(AplRegNo)缺失。", warnings);
            AddIfMissing(document.ApplName, "申报员姓名(ApplName)缺失。", warnings);
            AddIfMissing(document.Applicant, "申报员身份证号(Applicant)缺失。", warnings);
            AddIfMissing(document.ApplTel, "申报员联系电话(ApplTel)缺失。", warnings);
            AddIfMissing(document.OrgCode, "签证机构代码(OrgCode)缺失。", warnings);
            AddIfMissing(document.FetchPlace, "领证机构代码(FetchPlace)缺失。", warnings);
            AddIfMissing(document.AplAdd, "申请地址(AplAdd)缺失。", warnings);
            AddIfMissing(document.AplPromiseCode, "申请企业承诺代码(AplPromiseCode)缺失。", warnings);
            AddIfMissing(document.Curr, "币制(Curr)缺失或未识别。", warnings);

            return document;
        }

        private static IReadOnlyList<CooMappedGoodsItem> BuildGoodsItems(CooSourceSnapshot snapshot, Invoice invoice)
        {
            var sourceItems = snapshot.Items?.Where(item => item != null).ToList() ?? [];
            var existingItems = snapshot.ExistingDocument?.Items?
                .OrderBy(item => item.GNo)
                .ToList() ?? [];

            if (sourceItems.Count == 0 && existingItems.Count > 0)
            {
                return existingItems
                    .Select(item => NormalizeGoodsItem(
                        MapExistingGoodsItem(item, invoice, item.GNo <= 0 ? 1 : item.GNo),
                        snapshot.ExistingDocument?.CertType))
                    .ToList();
            }

            var existingIndex = ExistingGoodsItemIndex.Create(existingItems);
            return sourceItems
                .Select((item, index) =>
                {
                    var existing = existingIndex.Find(item, index + 1);

                    var mappedItem = existing == null
                        ? MapGoodsItem(item, invoice, snapshot.Exporter, index + 1)
                        : MergeGoodsItem(item, invoice, snapshot.Exporter, existing, index + 1);

                    return NormalizeGoodsItem(
                        mappedItem,
                        snapshot.ExistingDocument?.CertType);
                })
                .ToList();
        }

        private static CooMappedGoodsItem MapGoodsItem(Item item, Invoice invoice, Exporter exporter, int lineNo)
        {
            return new CooMappedGoodsItem
            {
                GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.DefaultCode,
                GNo = lineNo,
                HSCode = NormalizeText(item.HSCode),
                GoodsName = NormalizeText(item.StyleNameCN),
                GoodsNameE = NormalizeText(item.StyleName),
                PackQty = FormatDecimal(item.Cartons, 2),
                PackUnit = NormalizeUnitEnglish(item.CtnUnitEN),
                GoodsQty = FormatDecimal(item.Quantity, 2),
                GoodsQtyRef = FormatDecimal(item.Quantity, 2),
                GoodsUnitE = NormalizeUnitEnglish(item.UnitEN),
                GoodsUnit = NormalizeUnitChinese(string.IsNullOrWhiteSpace(item.UnitCN) ? item.UnitEN : item.UnitCN),
                GoodsUnitRef = NormalizeUnitEnglish(item.UnitEN),
                SecdGoodsQtyRef = FormatDecimal(item.Cartons, 2),
                SecdGoodsUnitRef = NormalizeUnitEnglish(item.CtnUnitEN),
                GrossWt = FormatDecimal(item.GWTotal, 2),
                NetWt = FormatDecimal(item.NWTotal, 2),
                WtUnit = "KGS",
                InvPrice = FormatDecimal(item.UnitPrice, 4),
                InvValue = FormatDecimal(item.TotalPrice, 2),
                FobValue = FormatDecimal(item.TotalPrice, 2),
                ICompPrpr = string.Empty,
                GoodsDesc = string.Empty,
                GoodsOriginCountry = NormalizeRecognizedCountryCode(item.Origin),
                GoodsOriginCountryEn = NormalizeRecognizedCountryNameEnglish(item.Origin),
                Producer = string.Empty,
                ProducerTel = string.Empty,
                ProducerFax = string.Empty,
                ProducerEmail = string.Empty,
                CiqRegNo = string.Empty,
                PrdcEtpsName = NormalizeText(invoice.ExporterNameCN ?? exporter?.ExporterNameCN),
                PrdcEtpsConcEr = NormalizeText(exporter?.ContactPerson),
                PrdcEtpsTel = NormalizePhone(exporter?.Phone),
                OriCriteriaSub = string.Empty,
                InvNo = NormalizeText(invoice.InvoiceNo),
                PackType = CustomsCooPackTypeCatalog.DefaultCode,
                GoodsTaxRate = string.Empty
            };
        }

        private static CooMappedGoodsItem MergeGoodsItem(Item item, Invoice invoice, Exporter exporter, CustomsCooItem existing, int lineNo)
        {
            var fallback = MapGoodsItem(item, invoice, exporter, lineNo);
            fallback.GNo = lineNo;
            fallback.GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(PreferValue(existing.GoodsItemFlag, fallback.GoodsItemFlag));
            fallback.HSCode = PreferValue(existing.HSCode, fallback.HSCode);
            fallback.GoodsName = PreferValue(existing.GoodsName, fallback.GoodsName);
            fallback.GoodsNameE = PreferValue(existing.GoodsNameE, fallback.GoodsNameE);
            fallback.PackQty = PreferValue(existing.PackQty, fallback.PackQty);
            fallback.PackUnit = PreferValue(existing.PackUnit, fallback.PackUnit);
            fallback.GoodsQty = PreferValue(existing.GoodsQty, fallback.GoodsQty);
            fallback.GoodsQtyRef = PreferValue(existing.GoodsQtyRef, fallback.GoodsQtyRef);
            fallback.GoodsUnitE = PreferValue(existing.GoodsUnitE, fallback.GoodsUnitE);
            fallback.GoodsUnit = PreferValue(existing.GoodsUnit, fallback.GoodsUnit);
            fallback.GoodsUnitRef = PreferValue(existing.GoodsUnitRef, fallback.GoodsUnitRef);
            fallback.SecdGoodsQtyRef = PreferValue(existing.SecdGoodsQtyRef, fallback.SecdGoodsQtyRef);
            fallback.SecdGoodsUnitRef = PreferValue(existing.SecdGoodsUnitRef, fallback.SecdGoodsUnitRef);
            fallback.GrossWt = PreferValue(existing.GrossWt, fallback.GrossWt);
            fallback.NetWt = PreferValue(existing.NetWt, fallback.NetWt);
            fallback.WtUnit = PreferValue(existing.WtUnit, fallback.WtUnit);
            fallback.InvPrice = PreferValue(existing.InvPrice, fallback.InvPrice);
            fallback.InvValue = PreferValue(existing.InvValue, fallback.InvValue);
            fallback.FobValue = PreferValue(existing.FobValue, fallback.FobValue);
            fallback.ICompPrpr = PreferValue(existing.ICompPrpr, fallback.ICompPrpr);
            fallback.GoodsDesc = PreferValue(existing.GoodsDesc, fallback.GoodsDesc);
            fallback.OriCriteria = PreferValue(existing.OriCriteria, fallback.OriCriteria);
            fallback.OriCriteriaRef = PreferValue(existing.OriCriteriaRef, fallback.OriCriteriaRef);
            fallback.GoodsOriginCountry = PreferValue(existing.GoodsOriginCountry, fallback.GoodsOriginCountry);
            fallback.GoodsOriginCountryEn = PreferRecognizedCountryNameEnglish(existing.GoodsOriginCountryEn, fallback.GoodsOriginCountryEn, item?.Origin);
            fallback.Producer = PreferValue(existing.Producer, fallback.Producer);
            fallback.ProducerTel = PreferValue(existing.ProducerTel, fallback.ProducerTel);
            fallback.ProducerFax = PreferValue(existing.ProducerFax, fallback.ProducerFax);
            fallback.ProducerEmail = PreferValue(existing.ProducerEmail, fallback.ProducerEmail);
            fallback.CiqRegNo = PreferValue(existing.CiqRegNo, fallback.CiqRegNo);
            fallback.PrdcEtpsName = PreferValue(existing.PrdcEtpsName, fallback.PrdcEtpsName);
            fallback.PrdcEtpsConcEr = PreferValue(existing.PrdcEtpsConcEr, fallback.PrdcEtpsConcEr);
            fallback.PrdcEtpsTel = PreferValue(existing.PrdcEtpsTel, fallback.PrdcEtpsTel);
            fallback.ProducerSertFlag = PreferValue(existing.ProducerSertFlag, fallback.ProducerSertFlag);
            fallback.OriCriteriaSub = PreferValue(existing.OriCriteriaSub, fallback.OriCriteriaSub);
            fallback.InvNo = PreferValue(existing.InvNo, fallback.InvNo);
            fallback.PackType = CustomsCooPackTypeCatalog.NormalizeOrDefault(PreferValue(existing.PackType, fallback.PackType));
            fallback.GoodsTaxRate = PreferValue(existing.GoodsTaxRate, fallback.GoodsTaxRate);
            return fallback;
        }

        private static CooMappedGoodsItem MapExistingGoodsItem(CustomsCooItem existing, Invoice invoice, int lineNo)
        {
            return new CooMappedGoodsItem
            {
                GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(PreferValue(existing.GoodsItemFlag, CustomsCooGoodsItemFlagCatalog.DefaultCode)),
                GNo = lineNo,
                HSCode = NormalizeText(existing.HSCode),
                GoodsName = NormalizeText(existing.GoodsName),
                GoodsNameE = NormalizeText(existing.GoodsNameE),
                PackQty = NormalizeText(existing.PackQty),
                PackUnit = NormalizeText(existing.PackUnit),
                GoodsQty = NormalizeText(existing.GoodsQty),
                GoodsQtyRef = NormalizeText(existing.GoodsQtyRef),
                GoodsUnitE = NormalizeText(existing.GoodsUnitE),
                GoodsUnit = NormalizeText(existing.GoodsUnit),
                GoodsUnitRef = NormalizeText(existing.GoodsUnitRef),
                SecdGoodsQtyRef = NormalizeText(existing.SecdGoodsQtyRef),
                SecdGoodsUnitRef = NormalizeText(existing.SecdGoodsUnitRef),
                GrossWt = NormalizeText(existing.GrossWt),
                NetWt = NormalizeText(existing.NetWt),
                WtUnit = NormalizeText(existing.WtUnit),
                InvPrice = NormalizeText(existing.InvPrice),
                InvValue = NormalizeText(existing.InvValue),
                FobValue = NormalizeText(existing.FobValue),
                ICompPrpr = NormalizeText(existing.ICompPrpr),
                GoodsDesc = NormalizeText(existing.GoodsDesc),
                OriCriteria = NormalizeText(existing.OriCriteria),
                OriCriteriaRef = NormalizeText(existing.OriCriteriaRef),
                GoodsOriginCountry = NormalizeText(existing.GoodsOriginCountry),
                GoodsOriginCountryEn = NormalizeText(existing.GoodsOriginCountryEn),
                Producer = NormalizeText(existing.Producer),
                ProducerTel = NormalizeText(existing.ProducerTel),
                ProducerFax = NormalizeText(existing.ProducerFax),
                ProducerEmail = NormalizeText(existing.ProducerEmail),
                CiqRegNo = NormalizeText(existing.CiqRegNo),
                PrdcEtpsName = NormalizeText(existing.PrdcEtpsName),
                PrdcEtpsConcEr = NormalizeText(existing.PrdcEtpsConcEr),
                PrdcEtpsTel = NormalizeText(existing.PrdcEtpsTel),
                ProducerSertFlag = NormalizeText(existing.ProducerSertFlag),
                OriCriteriaSub = NormalizeText(existing.OriCriteriaSub),
                InvNo = PreferValue(existing.InvNo, NormalizeText(invoice.InvoiceNo)),
                PackType = CustomsCooPackTypeCatalog.NormalizeOrDefault(existing.PackType),
                GoodsTaxRate = NormalizeText(existing.GoodsTaxRate)
            };
        }

        private sealed class ExistingGoodsItemIndex
        {
            private readonly Dictionary<int, CustomsCooItem> _bySourceItemId;
            private readonly Dictionary<string, CustomsCooItem> _bySourceStyleNo;
            private readonly Dictionary<int, CustomsCooItem> _byLineNo;

            private ExistingGoodsItemIndex(
                Dictionary<int, CustomsCooItem> bySourceItemId,
                Dictionary<string, CustomsCooItem> bySourceStyleNo,
                Dictionary<int, CustomsCooItem> byLineNo)
            {
                _bySourceItemId = bySourceItemId;
                _bySourceStyleNo = bySourceStyleNo;
                _byLineNo = byLineNo;
            }

            public static ExistingGoodsItemIndex Create(IEnumerable<CustomsCooItem> items)
            {
                var bySourceItemId = new Dictionary<int, CustomsCooItem>();
                var bySourceStyleNo = new Dictionary<string, CustomsCooItem>(StringComparer.OrdinalIgnoreCase);
                var byLineNo = new Dictionary<int, CustomsCooItem>();

                foreach (var item in items.Where(item => item != null))
                {
                    if (item.SourceItemId > 0)
                    {
                        bySourceItemId.TryAdd(item.SourceItemId, item);
                    }

                    string sourceStyleNo = NormalizeText(item.SourceStyleNo);
                    if (!string.IsNullOrWhiteSpace(sourceStyleNo))
                    {
                        bySourceStyleNo.TryAdd(sourceStyleNo, item);
                    }

                    if (item.GNo > 0)
                    {
                        byLineNo.TryAdd(item.GNo, item);
                    }
                }

                return new ExistingGoodsItemIndex(bySourceItemId, bySourceStyleNo, byLineNo);
            }

            public CustomsCooItem Find(Item item, int lineNo)
            {
                if (item != null && item.Id > 0 && _bySourceItemId.TryGetValue(item.Id, out var bySourceItemId))
                {
                    return bySourceItemId;
                }

                string styleNo = NormalizeText(item?.StyleNo);
                if (!string.IsNullOrWhiteSpace(styleNo) && _bySourceStyleNo.TryGetValue(styleNo, out var bySourceStyleNo))
                {
                    return bySourceStyleNo;
                }

                return _byLineNo.TryGetValue(lineNo, out var byLineNo)
                    ? byLineNo
                    : null;
            }
        }

        private static CooMappedGoodsItem NormalizeGoodsItem(CooMappedGoodsItem item, string certType)
        {
            if (item == null)
            {
                return new CooMappedGoodsItem();
            }

            item.GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(item.GoodsItemFlag);
            item.PackType = CustomsCooPackTypeCatalog.NormalizeOrDefault(item.PackType);
            item.OriCriteriaSub = CustomsCooOriginCriteriaCatalog.NormalizeOriginCriteriaSubInput(
                certType,
                item.OriCriteria,
                item.OriCriteriaSub);

            return item;
        }

        private static string PreferPartyBlock(string existingValue, string fallbackValue)
        {
            string normalizedExisting = CustomsCooTextFormatter.DecodeXmlMultiline(existingValue);
            return string.IsNullOrWhiteSpace(normalizedExisting)
                ? fallbackValue
                : normalizedExisting;
        }

        private static string PreferRecognizedCountryNameEnglish(string existingValue, string fallbackValue, string rawOriginValue)
        {
            string normalizedExisting = NormalizeText(existingValue);
            if (string.IsNullOrWhiteSpace(normalizedExisting))
            {
                return fallbackValue;
            }

            if (string.IsNullOrWhiteSpace(fallbackValue) && !TryResolveCountry(rawOriginValue, out _))
            {
                string rawText = NormalizeText(rawOriginValue);
                string rawUpperText = NormalizeUpperText(rawOriginValue);
                if (string.Equals(normalizedExisting, rawText, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedExisting, rawUpperText, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }
            }

            return normalizedExisting;
        }

        private static string NormalizeRecognizedCountryCode(string value)
        {
            return TryResolveCountry(value, out var entry)
                ? entry.Code
                : string.Empty;
        }

        private static string NormalizeRecognizedCountryNameEnglish(string value)
        {
            return TryResolveCountry(value, out var entry)
                ? entry.EnglishName
                : string.Empty;
        }
    }

    public sealed class AgentConsignmentFieldMapper : IAgentConsignmentFieldMapper
    {
        public AcdMappedDocument Map(AcdSourceSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var invoice = snapshot.Invoice ?? new Invoice();
            var exporter = snapshot.Exporter;
            var firstItem = snapshot.Items.FirstOrDefault();
            var existing = snapshot.ExistingDocument;
            var warnings = new List<string>();
            var enterpriseCustomsCode = ResolveEnterpriseCustomsCode(invoice, exporter);
            var agentCustomsCode = NormalizeCustomsCode(invoice.CustomsBrokerCode);

            var document = new AcdMappedDocument
            {
                CopCusCode = PreferValue(existing?.CopCusCode, enterpriseCustomsCode),
                Sign = PreferValue(existing?.Sign, string.Empty),
                OperType = PreferValue(existing?.OperType, "1"),
                GName = PreferValue(existing?.GName, NormalizeText(firstItem?.StyleNameCN ?? firstItem?.StyleName)),
                CodeTS = PreferValue(existing?.CodeTS, NormalizeText(firstItem?.HSCode)),
                DeclTotal = PreferValue(existing?.DeclTotal, FormatDecimal(invoice.TotalAmount, 4)),
                IEDate = PreferValue(existing?.IEDate, ResolveAcdDate(invoice)),
                ListNo = PreferValue(existing?.ListNo, string.Empty),
                TradeMode = PreferValue(existing?.TradeMode, NormalizeTradeModeCode(invoice.SupervisionMode)),
                OriCountry = PreferValue(existing?.OriCountry, ResolveAcdOriginCountryCode(firstItem?.Origin)),
                TradeCode = PreferValue(existing?.TradeCode, enterpriseCustomsCode),
                AgentCode = PreferValue(existing?.AgentCode, agentCustomsCode),
                Curr = PreferValue(existing?.Curr, NormalizeCurrencyCode(invoice.Currency)),
                QtyOrWeight = PreferValue(existing?.QtyOrWeight, FormatDecimal(invoice.TotalGrossWeight > 0 ? invoice.TotalGrossWeight : invoice.TotalQuantity, 2)),
                PackingCondition = PreferValue(existing?.PackingCondition, NormalizeText(invoice.SpecialTerms)),
                OtherNote = PreferValue(existing?.OtherNote, NormalizeText(invoice.SpecialTerms)),
                ConsignTele = PreferValue(existing?.ConsignTele, NormalizePhone(exporter?.Phone)),
                EntryId = PreferValue(existing?.EntryId, string.Empty),
                ReceiveDate = PreferValue(existing?.ReceiveDate, DateTime.Today.ToString("yyyyMMdd")),
                PaperInfo = PreferValue(existing?.PaperInfo, string.Empty),
                OtherRecInfo = PreferValue(existing?.OtherRecInfo, string.Empty),
                DeclarePrice = PreferValue(existing?.DeclarePrice, string.Empty),
                PromiseNote = PreferValue(existing?.PromiseNote, string.Empty),
                DeclTele = PreferValue(existing?.DeclTele, string.Empty),
                Warnings = warnings
            };

            if (string.IsNullOrWhiteSpace(document.CopCusCode))
            {
                warnings.Add("企业内部编号/经营单位海关10位编码缺失，请先维护出口商的海关10位编码。");
            }
            AddIfMissing(document.GName, "主要货物名称(GName)缺失。", warnings);
            AddIfMissing(document.CodeTS, "HS编码(CodeTS)缺失。", warnings);
            AddIfMissing(document.TradeMode, "贸易方式代码(TradeMode)缺失。", warnings);
            AddIfMissing(document.AgentCode, "申报单位(被委托方)海关10位编码缺失或格式不正确。", warnings);
            AddIfMissing(document.Curr, "币制代码(Curr)缺失或未识别。", warnings);
            AddIfMissing(document.OriCountry, "原产地/货源地(OriCountry)缺失或未识别。", warnings);

            return document;
        }

        private static string ResolveAcdDate(Invoice invoice)
        {
            if (invoice.ShipmentDate > DateTime.MinValue)
            {
                return invoice.ShipmentDate.ToString("yyyyMMdd");
            }

            return invoice.InvoiceDate > DateTime.MinValue
                ? invoice.InvoiceDate.ToString("yyyyMMdd")
                : string.Empty;
        }
    }
}

