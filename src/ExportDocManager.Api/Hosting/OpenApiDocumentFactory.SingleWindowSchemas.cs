namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static object SingleWindowCustomsCooDocumentSchema()
        {
            var properties = SchemaProperties(
                stringProperties:
                [
                    "InvoiceNo",
                    "ContractNo",
                    "Status",
                    "CertNo",
                    "ApplyType",
                    "CertStatus",
                    "CertType",
                    "EntMgrNo",
                    "CiqRegNo",
                    "AplRegNo",
                    "EtpsName",
                    "ApplName",
                    "Applicant",
                    "ApplTel",
                    "OrgCode",
                    "FetchPlace",
                    "AplAdd",
                    "InvDate",
                    "InvNo",
                    "AplDate",
                    "DestCountry",
                    "DestCountryCode",
                    "DestCountryName",
                    "Exporter",
                    "Consignee",
                    "GoodsSpecClause",
                    "Mark",
                    "LoadPort",
                    "UnloadPort",
                    "TransMeans",
                    "TransName",
                    "TransCountryCode",
                    "TransCountryName",
                    "TransPort",
                    "DestPort",
                    "TransDetails",
                    "IntendExpDate",
                    "TradeModeCode",
                    "FobValue",
                    "TotalAmt",
                    "Note",
                    "LcNo",
                    "SpecInvTerms",
                    "PriceTerms",
                    "Curr",
                    "Remark",
                    "Producer",
                    "ProducerSertFlag",
                    "ExhibitFlag",
                    "ThirdPartyInvFlag",
                    "ExporterTel",
                    "ExporterFax",
                    "ExporterEmail",
                    "ConsigneeTel",
                    "ConsigneeFax",
                    "ConsigneeEmail",
                    "PredictFlag",
                    "ExpDeclDate",
                    "OriCountryCode",
                    "OriCountry",
                    "ChkValidDate",
                    "EtpsConcEr",
                    "EtpsTel",
                    "EntryId",
                    "PrcsAssembly",
                    "OldCertNo",
                    "ModReason",
                    "ModColm",
                    "OldSituDesc",
                    "ModSituDesc",
                    "OldDeclDate",
                    "OldIssueDate",
                    "AplPromiseCode",
                    "WarningSummary",
                    "SourceDiffSummary"
                ],
                integerProperties:
                [
                    "Id",
                    "SourceInvoiceId",
                    "WarningCount",
                    "DraftRevision",
                    "SourceDiffCount",
                    "ManualLockedFieldCount"
                ],
                dateTimeProperties: ["LastGeneratedAt"]);

            properties["items"] = RefArraySchema("ApiCustomsCooItemDto");
            properties["nonpartyCorps"] = RefArraySchema("ApiCustomsCooNonpartyCorpDto");
            properties["attachments"] = RefArraySchema("ApiCustomsCooAttachmentDto");
            return ObjectSchema(properties);
        }

        private static object SingleWindowCustomsCooItemSchema()
        {
            var properties = SchemaProperties(
                stringProperties:
                [
                    "SourceStyleNo",
                    "GoodsItemFlag",
                    "HSCode",
                    "GoodsName",
                    "GoodsNameE",
                    "PackQty",
                    "PackUnit",
                    "GoodsQty",
                    "GoodsQtyRef",
                    "GoodsUnitE",
                    "GoodsUnit",
                    "GoodsUnitRef",
                    "SecdGoodsQtyRef",
                    "SecdGoodsUnitRef",
                    "GrossWt",
                    "NetWt",
                    "WtUnit",
                    "InvPrice",
                    "InvValue",
                    "FobValue",
                    "ICompPrpr",
                    "GoodsDesc",
                    "OriCriteria",
                    "OriCriteriaRef",
                    "GoodsOriginCountry",
                    "GoodsOriginCountryEn",
                    "Producer",
                    "ProducerTel",
                    "ProducerFax",
                    "ProducerEmail",
                    "CiqRegNo",
                    "PrdcEtpsName",
                    "PrdcEtpsConcEr",
                    "PrdcEtpsTel",
                    "ProducerSertFlag",
                    "OriCriteriaSub",
                    "InvNo",
                    "PackType",
                    "GoodsTaxRate"
                ],
                integerProperties:
                [
                    "Id",
                    "DocumentId",
                    "SourceItemId",
                    "GNo"
                ]);

            return ObjectSchema(properties);
        }

        private static object SingleWindowCustomsCooNonpartyCorpSchema()
        {
            return ObjectSchema(SchemaProperties(
                stringProperties:
                [
                    "EntName",
                    "EntAddr",
                    "EntCountryCode",
                    "EntCountryName"
                ],
                integerProperties:
                [
                    "Id",
                    "DocumentId",
                    "SortNo"
                ]));
        }

        private static object SingleWindowCustomsCooAttachmentSchema()
        {
            return ObjectSchema(SchemaProperties(
                stringProperties:
                [
                    "CertNo",
                    "CertType",
                    "AplRegNo",
                    "CiqRegNo",
                    "FileType",
                    "FileName",
                    "FilePath",
                    "MediaType",
                    "Description",
                    "DocType"
                ],
                integerProperties:
                [
                    "Id",
                    "DocumentId",
                    "SortOrder"
                ],
                booleanProperties:
                [
                    "IsDelay",
                    "FileExistsAtBuild"
                ]));
        }

        private static object SingleWindowAgentConsignmentDocumentSchema()
        {
            return ObjectSchema(SchemaProperties(
                stringProperties:
                [
                    "InvoiceNo",
                    "ContractNo",
                    "Status",
                    "CounterpartyStatus",
                    "CopCusCode",
                    "Sign",
                    "OperType",
                    "GName",
                    "CodeTS",
                    "DeclTotal",
                    "IEDate",
                    "ListNo",
                    "TradeMode",
                    "OriCountry",
                    "TradeCode",
                    "AgentCode",
                    "Curr",
                    "QtyOrWeight",
                    "PackingCondition",
                    "OtherNote",
                    "ConsignTele",
                    "EntryId",
                    "ReceiveDate",
                    "PaperInfo",
                    "OtherRecInfo",
                    "DeclarePrice",
                    "PromiseNote",
                    "DeclTele",
                    "ConsignNo",
                    "WarningSummary",
                    "SourceDiffSummary"
                ],
                integerProperties:
                [
                    "Id",
                    "SourceInvoiceId",
                    "WarningCount",
                    "DraftRevision",
                    "SourceDiffCount",
                    "ManualLockedFieldCount"
                ],
                dateTimeProperties: ["LastGeneratedAt"]));
        }

    }
}
