using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class CustomsCooItem
    {
        public int Id { get; set; }

        public int DocumentId { get; set; }

        public int SourceItemId { get; set; }

        [MaxLength(80)]
        public string SourceStyleNo { get; set; } = string.Empty;

        [MaxLength(10)]
        public string GoodsItemFlag { get; set; } = "N";

        public int GNo { get; set; }

        [MaxLength(20)]
        public string HSCode { get; set; } = string.Empty;

        [MaxLength(300)]
        public string GoodsName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string GoodsNameE { get; set; } = string.Empty;

        [MaxLength(40)]
        public string PackQty { get; set; } = string.Empty;

        [MaxLength(40)]
        public string PackUnit { get; set; } = string.Empty;

        [MaxLength(40)]
        public string GoodsQty { get; set; } = string.Empty;

        [MaxLength(40)]
        public string GoodsQtyRef { get; set; } = string.Empty;

        [MaxLength(40)]
        public string GoodsUnitE { get; set; } = string.Empty;

        [MaxLength(40)]
        public string GoodsUnit { get; set; } = string.Empty;

        [MaxLength(40)]
        public string GoodsUnitRef { get; set; } = string.Empty;

        [MaxLength(40)]
        public string SecdGoodsQtyRef { get; set; } = string.Empty;

        [MaxLength(40)]
        public string SecdGoodsUnitRef { get; set; } = string.Empty;

        [MaxLength(40)]
        public string GrossWt { get; set; } = string.Empty;

        [MaxLength(40)]
        public string NetWt { get; set; } = string.Empty;

        [MaxLength(20)]
        public string WtUnit { get; set; } = string.Empty;

        [MaxLength(40)]
        public string InvPrice { get; set; } = string.Empty;

        [MaxLength(40)]
        public string InvValue { get; set; } = string.Empty;

        [MaxLength(40)]
        public string FobValue { get; set; } = string.Empty;

        [MaxLength(20)]
        public string ICompPrpr { get; set; } = string.Empty;

        public string GoodsDesc { get; set; } = string.Empty;

        [MaxLength(40)]
        public string OriCriteria { get; set; } = string.Empty;

        [MaxLength(40)]
        public string OriCriteriaRef { get; set; } = string.Empty;

        [MaxLength(20)]
        public string GoodsOriginCountry { get; set; } = string.Empty;

        [MaxLength(80)]
        public string GoodsOriginCountryEn { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Producer { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ProducerTel { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ProducerFax { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ProducerEmail { get; set; } = string.Empty;

        [MaxLength(20)]
        public string CiqRegNo { get; set; } = string.Empty;

        [MaxLength(400)]
        public string PrdcEtpsName { get; set; } = string.Empty;

        [MaxLength(80)]
        public string PrdcEtpsConcEr { get; set; } = string.Empty;

        [MaxLength(80)]
        public string PrdcEtpsTel { get; set; } = string.Empty;

        [MaxLength(10)]
        public string ProducerSertFlag { get; set; } = string.Empty;

        [MaxLength(10)]
        public string OriCriteriaSub { get; set; } = string.Empty;

        [MaxLength(80)]
        public string InvNo { get; set; } = string.Empty;

        [MaxLength(10)]
        public string PackType { get; set; } = "1";

        [MaxLength(10)]
        public string GoodsTaxRate { get; set; } = string.Empty;

        public CustomsCooDocument Document { get; set; }
    }
}
