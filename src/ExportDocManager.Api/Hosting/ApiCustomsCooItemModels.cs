namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCustomsCooItemDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int SourceItemId { get; set; }
        public string SourceStyleNo { get; set; } = string.Empty;
        public string GoodsItemFlag { get; set; } = string.Empty;
        public int GNo { get; set; }
        public string HSCode { get; set; } = string.Empty;
        public string GoodsName { get; set; } = string.Empty;
        public string GoodsNameE { get; set; } = string.Empty;
        public string PackQty { get; set; } = string.Empty;
        public string PackUnit { get; set; } = string.Empty;
        public string GoodsQty { get; set; } = string.Empty;
        public string GoodsQtyRef { get; set; } = string.Empty;
        public string GoodsUnitE { get; set; } = string.Empty;
        public string GoodsUnit { get; set; } = string.Empty;
        public string GoodsUnitRef { get; set; } = string.Empty;
        public string SecdGoodsQtyRef { get; set; } = string.Empty;
        public string SecdGoodsUnitRef { get; set; } = string.Empty;
        public string GrossWt { get; set; } = string.Empty;
        public string NetWt { get; set; } = string.Empty;
        public string WtUnit { get; set; } = string.Empty;
        public string InvPrice { get; set; } = string.Empty;
        public string InvValue { get; set; } = string.Empty;
        public string FobValue { get; set; } = string.Empty;
        public string ICompPrpr { get; set; } = string.Empty;
        public string GoodsDesc { get; set; } = string.Empty;
        public string OriCriteria { get; set; } = string.Empty;
        public string OriCriteriaRef { get; set; } = string.Empty;
        public string GoodsOriginCountry { get; set; } = string.Empty;
        public string GoodsOriginCountryEn { get; set; } = string.Empty;
        public string Producer { get; set; } = string.Empty;
        public string ProducerTel { get; set; } = string.Empty;
        public string ProducerFax { get; set; } = string.Empty;
        public string ProducerEmail { get; set; } = string.Empty;
        public string CiqRegNo { get; set; } = string.Empty;
        public string PrdcEtpsName { get; set; } = string.Empty;
        public string PrdcEtpsConcEr { get; set; } = string.Empty;
        public string PrdcEtpsTel { get; set; } = string.Empty;
        public string ProducerSertFlag { get; set; } = string.Empty;
        public string OriCriteriaSub { get; set; } = string.Empty;
        public string InvNo { get; set; } = string.Empty;
        public string PackType { get; set; } = string.Empty;
        public string GoodsTaxRate { get; set; } = string.Empty;
    }
}
