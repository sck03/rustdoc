using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowDocumentPersistenceService
    {
        private static async Task DeletePersistedCustomsCooChildrenAsync(
            AppDbContext context,
            int documentId,
            CancellationToken cancellationToken)
        {
            await context.CustomsCooItems
                .Where(item => item.DocumentId == documentId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await context.CustomsCooNonpartyCorps
                .Where(item => item.DocumentId == documentId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await context.CustomsCooAttachments
                .Where(item => item.DocumentId == documentId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private static List<CustomsCooItem> BuildPersistedCustomsCooItems(IEnumerable<CustomsCooItem> sourceItems, int documentId)
        {
            return (sourceItems ?? [])
                .Where(item => item != null)
                .OrderBy(item => item.GNo)
                .Select(item => new CustomsCooItem
                {
                    DocumentId = documentId,
                    SourceItemId = item.SourceItemId,
                    SourceStyleNo = NormalizePersistedValue(item.SourceStyleNo),
                    GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(item.GoodsItemFlag),
                    GNo = item.GNo,
                    HSCode = NormalizePersistedValue(item.HSCode),
                    GoodsName = NormalizePersistedValue(item.GoodsName),
                    GoodsNameE = NormalizePersistedValue(item.GoodsNameE),
                    PackQty = NormalizePersistedValue(item.PackQty),
                    PackUnit = NormalizePersistedValue(item.PackUnit),
                    GoodsQty = NormalizePersistedValue(item.GoodsQty),
                    GoodsQtyRef = NormalizePersistedValue(item.GoodsQtyRef),
                    GoodsUnitE = NormalizePersistedValue(item.GoodsUnitE),
                    GoodsUnit = NormalizePersistedValue(item.GoodsUnit),
                    GoodsUnitRef = NormalizePersistedValue(item.GoodsUnitRef),
                    SecdGoodsQtyRef = NormalizePersistedValue(item.SecdGoodsQtyRef),
                    SecdGoodsUnitRef = NormalizePersistedValue(item.SecdGoodsUnitRef),
                    GrossWt = NormalizePersistedValue(item.GrossWt),
                    NetWt = NormalizePersistedValue(item.NetWt),
                    WtUnit = NormalizePersistedValue(item.WtUnit),
                    InvPrice = NormalizePersistedValue(item.InvPrice),
                    InvValue = NormalizePersistedValue(item.InvValue),
                    FobValue = NormalizePersistedValue(item.FobValue),
                    ICompPrpr = NormalizePersistedValue(item.ICompPrpr),
                    GoodsDesc = NormalizePersistedValue(item.GoodsDesc),
                    OriCriteria = NormalizePersistedValue(item.OriCriteria),
                    OriCriteriaRef = NormalizePersistedValue(item.OriCriteriaRef),
                    GoodsOriginCountry = NormalizePersistedValue(item.GoodsOriginCountry),
                    GoodsOriginCountryEn = NormalizePersistedValue(item.GoodsOriginCountryEn),
                    Producer = NormalizePersistedValue(item.Producer),
                    ProducerTel = NormalizePersistedValue(item.ProducerTel),
                    ProducerFax = NormalizePersistedValue(item.ProducerFax),
                    ProducerEmail = NormalizePersistedValue(item.ProducerEmail),
                    CiqRegNo = NormalizePersistedValue(item.CiqRegNo),
                    PrdcEtpsName = NormalizePersistedValue(item.PrdcEtpsName),
                    PrdcEtpsConcEr = NormalizePersistedValue(item.PrdcEtpsConcEr),
                    PrdcEtpsTel = NormalizePersistedValue(item.PrdcEtpsTel),
                    ProducerSertFlag = NormalizePersistedValue(item.ProducerSertFlag),
                    OriCriteriaSub = NormalizePersistedValue(item.OriCriteriaSub),
                    InvNo = NormalizePersistedValue(item.InvNo),
                    PackType = CustomsCooPackTypeCatalog.NormalizeOrDefault(item.PackType),
                    GoodsTaxRate = NormalizePersistedValue(item.GoodsTaxRate)
                })
                .ToList();
        }

        private static List<CustomsCooItem> BuildPersistedCustomsCooItems(
            IReadOnlyList<Item> sourceItems,
            IReadOnlyList<CooMappedGoodsItem> mappedGoods,
            int documentId)
        {
            var goods = mappedGoods ?? [];
            return (sourceItems ?? [])
                .Where(item => item != null)
                .Select((item, index) =>
                {
                    var mapped = index < goods.Count ? goods[index] : null;
                    return new CustomsCooItem
                    {
                        DocumentId = documentId,
                        SourceItemId = item.Id,
                        SourceStyleNo = NormalizePersistedValue(item.StyleNo),
                        GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(mapped?.GoodsItemFlag),
                        GNo = index + 1,
                        HSCode = NormalizePersistedValue(mapped?.HSCode),
                        GoodsName = NormalizePersistedValue(mapped?.GoodsName),
                        GoodsNameE = NormalizePersistedValue(mapped?.GoodsNameE),
                        PackQty = NormalizePersistedValue(mapped?.PackQty),
                        PackUnit = NormalizePersistedValue(mapped?.PackUnit),
                        GoodsQty = NormalizePersistedValue(mapped?.GoodsQty),
                        GoodsQtyRef = NormalizePersistedValue(mapped?.GoodsQtyRef),
                        GoodsUnitE = NormalizePersistedValue(mapped?.GoodsUnitE),
                        GoodsUnit = NormalizePersistedValue(mapped?.GoodsUnit),
                        GoodsUnitRef = NormalizePersistedValue(mapped?.GoodsUnitRef),
                        SecdGoodsQtyRef = NormalizePersistedValue(mapped?.SecdGoodsQtyRef),
                        SecdGoodsUnitRef = NormalizePersistedValue(mapped?.SecdGoodsUnitRef),
                        GrossWt = NormalizePersistedValue(mapped?.GrossWt),
                        NetWt = NormalizePersistedValue(mapped?.NetWt),
                        WtUnit = NormalizePersistedValue(mapped?.WtUnit),
                        InvPrice = NormalizePersistedValue(mapped?.InvPrice),
                        InvValue = NormalizePersistedValue(mapped?.InvValue),
                        FobValue = NormalizePersistedValue(mapped?.FobValue),
                        ICompPrpr = NormalizePersistedValue(mapped?.ICompPrpr),
                        GoodsDesc = NormalizePersistedValue(mapped?.GoodsDesc),
                        OriCriteria = NormalizePersistedValue(mapped?.OriCriteria),
                        OriCriteriaRef = NormalizePersistedValue(mapped?.OriCriteriaRef),
                        GoodsOriginCountry = NormalizePersistedValue(mapped?.GoodsOriginCountry),
                        GoodsOriginCountryEn = NormalizePersistedValue(mapped?.GoodsOriginCountryEn),
                        Producer = NormalizePersistedValue(mapped?.Producer),
                        ProducerTel = NormalizePersistedValue(mapped?.ProducerTel),
                        ProducerFax = NormalizePersistedValue(mapped?.ProducerFax),
                        ProducerEmail = NormalizePersistedValue(mapped?.ProducerEmail),
                        CiqRegNo = NormalizePersistedValue(mapped?.CiqRegNo),
                        PrdcEtpsName = NormalizePersistedValue(mapped?.PrdcEtpsName),
                        PrdcEtpsConcEr = NormalizePersistedValue(mapped?.PrdcEtpsConcEr),
                        PrdcEtpsTel = NormalizePersistedValue(mapped?.PrdcEtpsTel),
                        ProducerSertFlag = NormalizePersistedValue(mapped?.ProducerSertFlag),
                        OriCriteriaSub = NormalizePersistedValue(mapped?.OriCriteriaSub),
                        InvNo = NormalizePersistedValue(mapped?.InvNo),
                        PackType = CustomsCooPackTypeCatalog.NormalizeOrDefault(mapped?.PackType),
                        GoodsTaxRate = NormalizePersistedValue(mapped?.GoodsTaxRate)
                    };
                })
                .ToList();
        }

        private static List<CustomsCooNonpartyCorp> BuildPersistedCustomsCooNonpartyCorps(
            IEnumerable<CustomsCooNonpartyCorp> sourceNonpartyCorps,
            int documentId)
        {
            return (sourceNonpartyCorps ?? [])
                .Where(item => item != null)
                .Select(item => new CustomsCooNonpartyCorp
                {
                    DocumentId = documentId,
                    SortNo = item.SortNo,
                    EntName = NormalizePersistedValue(item.EntName),
                    EntAddr = NormalizePersistedValue(item.EntAddr),
                    EntCountryCode = NormalizePersistedValue(item.EntCountryCode),
                    EntCountryName = NormalizePersistedValue(item.EntCountryName)
                })
                .ToList();
        }

        private static List<CustomsCooNonpartyCorp> BuildPersistedCustomsCooNonpartyCorps(
            IReadOnlyList<CooMappedNonpartyCorp> sourceNonpartyCorps,
            int documentId)
        {
            return (sourceNonpartyCorps ?? [])
                .Where(item => item != null)
                .Select((item, index) => new CustomsCooNonpartyCorp
                {
                    DocumentId = documentId,
                    SortNo = item.SortNo > 0 ? item.SortNo : index + 1,
                    EntName = NormalizePersistedValue(item.EntName),
                    EntAddr = NormalizePersistedValue(item.EntAddr),
                    EntCountryCode = NormalizePersistedValue(item.EntCountryCode),
                    EntCountryName = NormalizePersistedValue(item.EntCountryName)
                })
                .ToList();
        }

        private static List<CustomsCooAttachment> BuildPersistedCustomsCooAttachments(
            IEnumerable<CustomsCooAttachment> sourceAttachments,
            int documentId)
        {
            return (sourceAttachments ?? [])
                .Where(item => item != null)
                .Select(item => new CustomsCooAttachment
                {
                    DocumentId = documentId,
                    SortOrder = item.SortOrder,
                    FileType = NormalizePersistedValue(item.FileType),
                    DocType = NormalizePersistedValue(item.DocType),
                    FileName = NormalizePersistedValue(item.FileName),
                    FilePath = NormalizePersistedValue(item.FilePath),
                    MediaType = NormalizePersistedValue(item.MediaType),
                    Description = NormalizePersistedValue(item.Description),
                    CertNo = NormalizePersistedValue(item.CertNo),
                    CertType = NormalizePersistedValue(item.CertType),
                    AplRegNo = NormalizePersistedValue(item.AplRegNo),
                    CiqRegNo = NormalizePersistedValue(item.CiqRegNo),
                    IsDelay = item.IsDelay,
                    FileExistsAtBuild = item.FileExistsAtBuild
                })
                .ToList();
        }

        private static List<CustomsCooAttachment> BuildPersistedCustomsCooAttachments(
            IReadOnlyList<SingleWindowAttachmentSource> sourceAttachments,
            int documentId)
        {
            return (sourceAttachments ?? [])
                .Where(item => item != null)
                .Select((item, index) => new CustomsCooAttachment
                {
                    DocumentId = documentId,
                    SortOrder = index + 1,
                    FileType = NormalizePersistedValue(item.FileType),
                    DocType = NormalizePersistedValue(item.DocType),
                    FileName = NormalizePersistedValue(item.FileName),
                    FilePath = NormalizePersistedValue(item.FilePath),
                    MediaType = NormalizePersistedValue(item.MediaType),
                    Description = NormalizePersistedValue(item.Description),
                    CertNo = NormalizePersistedValue(item.CertNo),
                    CertType = NormalizePersistedValue(item.CertType),
                    AplRegNo = NormalizePersistedValue(item.AplRegNo),
                    CiqRegNo = NormalizePersistedValue(item.CiqRegNo),
                    IsDelay = item.IsDelay,
                    FileExistsAtBuild = item.Exists
                })
                .ToList();
        }
    }
}
