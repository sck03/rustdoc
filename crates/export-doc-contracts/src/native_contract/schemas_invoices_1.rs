//! Native declarations. Change these typed definitions, then regenerate both clients.
use super::model::*;
use std::collections::BTreeMap;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<(&'static str,Schema)> { vec![
    ("ApiAgentConsignmentDocumentDto",object_groups(vec![
        (&["id","sourceInvoiceId","warningCount","draftRevision","expectedDraftRevision","sourceDiffCount","manualLockedFieldCount"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["invoiceNo","contractNo","status","counterpartyStatus","copCusCode","sign","operType","gName","codeTS","declTotal","ieDate","listNo","tradeMode","oriCountry","tradeCode","agentCode","curr","qtyOrWeight","packingCondition","otherNote","consignTele","entryId","receiveDate","paperInfo","otherRecInfo","declarePrice","promiseNote","declTele","consignNo","warningSummary","sourceDiffSummary"], s()),
        (&["lastGeneratedAt"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
    ]).required(vec!["id","sourceInvoiceId","invoiceNo","contractNo","status","counterpartyStatus","copCusCode","sign","operType","gName","codeTS","declTotal","ieDate","listNo","tradeMode","oriCountry","tradeCode","agentCode","curr","qtyOrWeight","packingCondition","otherNote","consignTele","entryId","receiveDate","paperInfo","otherRecInfo","declarePrice","promiseNote","declTele","consignNo","warningCount","warningSummary","draftRevision","expectedDraftRevision","sourceDiffCount","sourceDiffSummary","manualLockedFieldCount"])),
    ("ApiAgentConsignmentDocumentSaveResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("document",r("ApiAgentConsignmentDocumentDto")),
        ("message",s())
    ]).required(vec!["success","id","document","message"])),
    ("ApiCrmCustomerBatchStatusRequest",o(vec![("ids",a(Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))),("status",s())]).required(vec!["ids","status"])),
    ("ApiCrmCustomerBatchStatusResult",o(vec![("affectedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("status",s())]).required(vec!["affectedCount","status"])),
    ("ApiCrmCustomerDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("countryRegion",s()),
        ("website",s()),("status",s()),("source",s()),
        ("notes",s()),("linkedDocumentCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","name","countryRegion","website","status","source","notes","versionNumber"])),
    ("ApiCrmCustomerImportPreviewDto",o(vec![
        ("totalRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("validRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("duplicateRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("rows",a(r("ApiCrmCustomerImportRowDto"))),("previewId",s())
    ]).required(vec!["totalRows","validRows","duplicateRows","rows","previewId"])),
    ("ApiCrmCustomerImportRequest",o(vec![("previewId",s())]).required(vec!["previewId"])),
    ("ApiCrmCustomerImportResultDto",o(vec![("createdCustomers",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("createdContacts",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("skippedDuplicates",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["createdCustomers","createdContacts","skippedDuplicates"])),
    ("ApiCrmCustomerImportRowDto",object_groups(vec![
        (&["rowNumber"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["name","countryRegion","website","status","source","notes","contactName","contactTitle","contactEmail","contactPhone","error"], s()),
        (&["isDuplicate"], b()),
    ]).required(vec!["rowNumber","name","countryRegion","website","status","source","notes","contactName","contactTitle","contactEmail","contactPhone","isDuplicate","error"])),
    ("ApiCrmCustomerSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("countryRegion",s()),
        ("website",s()),("source",s()),("notes",s()),
        ("linkedDocumentCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","name","countryRegion","website","source","notes"])),
    ("ApiCustomerDto",object_groups(vec![
        (&["id"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["customerNameEN","displayName","notifyPartyName","addressEN","notifyPartyAddress","contactPerson","phone","email","taxId","notes","rowVersion"], s()),
        (&["notifyPartyMode"], r("NotifyPartyMode")),
    ]).required(vec!["id","customerNameEN","displayName","notifyPartyName","addressEN","notifyPartyAddress","contactPerson","phone","email","taxId","notes","rowVersion","notifyPartyMode"])),
    ("ApiCustomsCooDocumentDto",object_groups(vec![
        (&["id","sourceInvoiceId","warningCount","draftRevision","expectedDraftRevision","sourceDiffCount","manualLockedFieldCount"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["invoiceNo","contractNo","status","certNo","applyType","certStatus","certType","entMgrNo","ciqRegNo","aplRegNo","etpsName","applName","applicant","applTel","orgCode","fetchPlace","aplAdd","invDate","invNo","aplDate","destCountry","destCountryCode","destCountryName","exporter","consignee","goodsSpecClause","mark","loadPort","unloadPort","transMeans","transName","transCountryCode","transCountryName","transPort","destPort","transDetails","intendExpDate","tradeModeCode","fobValue","totalAmt","note","lcNo","specInvTerms","priceTerms","curr","remark","producer","producerSertFlag","exhibitFlag","thirdPartyInvFlag","exporterTel","exporterFax","exporterEmail","consigneeTel","consigneeFax","consigneeEmail","predictFlag","expDeclDate","oriCountryCode","oriCountry","chkValidDate","etpsConcEr","etpsTel","entryId","prcsAssembly","oldCertNo","modReason","modColm","oldSituDesc","modSituDesc","oldDeclDate","oldIssueDate","aplPromiseCode","warningSummary","sourceDiffSummary"], s()),
        (&["lastGeneratedAt"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        (&["items"], a(r("ApiCustomsCooItemDto"))),
        (&["nonpartyCorps"], a(r("ApiCustomsCooNonpartyCorpDto"))),
        (&["attachments"], a(r("ApiCustomsCooAttachmentDto"))),
    ]).required(vec!["id","sourceInvoiceId","invoiceNo","contractNo","status","certNo","applyType","certStatus","certType","entMgrNo","ciqRegNo","aplRegNo","etpsName","applName","applicant","applTel","orgCode","fetchPlace","aplAdd","invDate","invNo","aplDate","destCountry","destCountryCode","destCountryName","exporter","consignee","goodsSpecClause","mark","loadPort","unloadPort","transMeans","transName","transCountryCode","transCountryName","transPort","destPort","transDetails","intendExpDate","tradeModeCode","fobValue","totalAmt","note","lcNo","specInvTerms","priceTerms","curr","remark","producer","producerSertFlag","exhibitFlag","thirdPartyInvFlag","exporterTel","exporterFax","exporterEmail","consigneeTel","consigneeFax","consigneeEmail","predictFlag","expDeclDate","oriCountryCode","oriCountry","chkValidDate","etpsConcEr","etpsTel","entryId","prcsAssembly","oldCertNo","modReason","modColm","oldSituDesc","modSituDesc","oldDeclDate","oldIssueDate","aplPromiseCode","warningCount","warningSummary","draftRevision","expectedDraftRevision","sourceDiffCount","sourceDiffSummary","manualLockedFieldCount","items","nonpartyCorps","attachments"])),
    ("ApiCustomsCooDocumentSaveResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("document",r("ApiCustomsCooDocumentDto")),
        ("message",s())
    ]).required(vec!["success","id","document","message"])),
    ("ApiDashboardRecentInvoiceDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceNo",s()),("status",s()),
        ("statusText",s()),("type",s()),("invoiceDate",s().format("date")),
        ("totalAmount",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("customerNameEN",s()),("currency",s())
    ]).required(vec!["id","invoiceNo","status","statusText","type","invoiceDate","totalAmount","customerNameEN"])),
    ("ApiExporterDto",object_groups(vec![
        (&["id"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["exporterNameEN","exporterNameCN","addressEN","addressCN","contactPerson","creditCode","customsCode","phone","bankName","bankAccount","swiftCode","notes","docSealPath","customsSealPath","rowVersion"], s()),
    ]).required(vec!["id","exporterNameEN","exporterNameCN","addressEN","addressCN","contactPerson","creditCode","customsCode","phone","bankName","bankAccount","swiftCode","notes","docSealPath","customsSealPath","rowVersion"])),
    ("ApiImportedCustomerDto",Schema::default().properties(BTreeMap::from([
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("customerNameEN",s()),("displayName",s()),
        ("notifyPartyMode",r("NotifyPartyMode")),("notifyPartyName",s()),("addressEN",s()),
        ("notifyPartyAddress",s()),("contactPerson",s()),("phone",s()),
        ("email",s()),("taxId",s()),("notes",s())
    ])).kind(Types::Many(vec![Primitive::Null,Primitive::Object])).required(vec!["id","customerNameEN","displayName","notifyPartyMode","notifyPartyName","addressEN","notifyPartyAddress","contactPerson","phone","email","taxId","notes"])),
    ("ApiImportedExporterDto",Schema::default().properties(BTreeMap::from([
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("exporterNameEN",s()),("exporterNameCN",s()),
        ("addressEN",s()),("addressCN",s()),("contactPerson",s()),
        ("creditCode",s()),("customsCode",s()),("phone",s()),
        ("bankName",s()),("bankAccount",s()),("swiftCode",s()),
        ("notes",s()),("docSealPath",s()),("customsSealPath",s())
    ])).kind(Types::Many(vec![Primitive::Null,Primitive::Object])).required(vec!["id","exporterNameEN","exporterNameCN","addressEN","addressCN","contactPerson","creditCode","customsCode","phone","bankName","bankAccount","swiftCode","notes","docSealPath","customsSealPath"])),
    ("ApiInvoiceBookingSheetRequest",o(vec![("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("destinationPath",s())])),
    ("ApiInvoiceCloneRequest",o(vec![("newInvoiceNo",s()),("options",r("InvoiceCloneOptions"))]).required(vec!["newInvoiceNo","options"])),
    ("ApiInvoiceCloneResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoice",r("ApiInvoiceDetailDto")),
        ("message",s())
    ]).required(vec!["success","id","invoice","message"])),
    ("ApiInvoiceCloneTypeRequest",o(vec![("targetType",s()),("options",r("InvoiceCloneOptions"))]).required(vec!["targetType","options"])),
    ("ApiInvoiceCloneTypeResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoice",r("ApiInvoiceDetailDto")),
        ("message",s())
    ]).required(vec!["success","id","invoice","message"])),
    ("ApiInvoiceDataMaintenancePreviewResponse",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceNo",s()),("type",s()),
        ("status",s()),("statusDisplayName",s()),("invoiceDate",s().format("date")),
        ("customerName",s()),("canPurge",b()),("guidance",s()),
        ("storagePolicy",s())
    ]).required(vec!["id","invoiceNo","type","status","statusDisplayName","invoiceDate","customerName","canPurge","guidance","storagePolicy"])),
    ("ApiInvoiceDetailDto",object_groups(vec![
        (&["id","exporterId","customerId"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["ownerUserId"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["departmentId","companyScope","invoiceNo","contractNo","letterOfCreditNo","letterOfCreditSourcePath","letterOfCreditContent","issuingBank","customsBrokerName","customsBrokerCode","spare1","spare2","spare3","spare4","spare5","spare6","spare7","spare8","spare9","spare10","customFieldsJson","paymentTerms","portOfLoading","portOfDestination","destinationCountry","shippingMarks","shippingMarksType","shippingMarksImage","tradeTerms","transportMode","currency","specialTerms","type","supervisionMode","customerNameEN","customerAddressEN","notifyPartyName","notifyPartyAddress","exporterNameEN","exporterNameCN","exporterAddressEN","exporterAddressCN","exporterCreditCode","exporterCustomsCode","bankName","bankAccount","swiftCode","status","rowVersion"], s()),
        (&["invoiceDate","shipmentDate"], s().format("date")),
        (&["totalCartons","totalQuantity","totalGrossWeight","totalNetWeight","totalVolume","totalAmount","totalPurchaseAmount","totalTaxRefundAmount","totalProfit"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        (&["notifyPartyMode"], r("NotifyPartyMode")),
        (&["exchangeRate"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        (&["items"], a(r("ApiInvoiceItemDto"))),
        (&["pendingHsFeedback"], a(r("HsCodeKnowledgeFeedbackInput"))),
        (&["docSealPath","customsSealPath"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).description("受控发票印章；null 继承出口商档案，空字符串不盖章。")),
        (&["amountAdjustments"], a(r("ApiInvoiceAmountAdjustment")).default_value(serde_json::json!([])).description("可选报表金额调整；原商品合计 totalAmount 不变。").max_items(32)),
        (&["useEntryPrintRows"], b().default_value(serde_json::json!(false)).description("明细表按货项内录入的打印行顺序输出；模板须指定录入说明列。")),
    ]).required(vec!["id","departmentId","companyScope","invoiceNo","contractNo","invoiceDate","letterOfCreditNo","letterOfCreditSourcePath","letterOfCreditContent","issuingBank","customsBrokerName","customsBrokerCode","spare1","spare2","spare3","spare4","spare5","spare6","spare7","spare8","spare9","spare10","customFieldsJson","paymentTerms","portOfLoading","portOfDestination","destinationCountry","shippingMarks","shippingMarksType","shippingMarksImage","tradeTerms","transportMode","shipmentDate","exporterId","customerId","totalCartons","totalQuantity","totalGrossWeight","totalNetWeight","totalVolume","totalAmount","totalPurchaseAmount","totalTaxRefundAmount","totalProfit","currency","specialTerms","type","supervisionMode","customerNameEN","customerAddressEN","notifyPartyMode","notifyPartyName","notifyPartyAddress","exporterNameEN","exporterNameCN","exporterAddressEN","exporterAddressCN","exporterCreditCode","exporterCustomsCode","bankName","bankAccount","swiftCode","status","rowVersion","items","pendingHsFeedback"])),
    ("ApiInvoiceDocumentEmailRequest",o(vec![
        ("items",a(r("ApiInvoiceDocumentPackageItemRequest"))),("includeMergedPdf",b()),("toAddress",s()),
        ("subject",s()),("body",s())
    ])),
    ("ApiInvoiceDocumentPackageItemRequest",o(vec![
        ("name",s()),("reportType",s()),("templatePath",s()),
        ("withSeal",b())
    ])),
    ("ApiInvoiceDocumentPackagePreviewItemResponse",o(vec![
        ("name",s()),("reportType",s()),("templatePath",s()),
        ("withSeal",b()),("html",s())
    ]).required(vec!["name","reportType","templatePath","withSeal","html"])),
    ("ApiInvoiceDocumentPackagePreviewRequest",o(vec![("items",a(r("ApiInvoiceDocumentPackageItemRequest")))])),
    ("ApiInvoiceDocumentPackagePreviewResponse",o(vec![("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("items",a(r("ApiInvoiceDocumentPackagePreviewItemResponse"))),("storagePolicy",s())]).required(vec!["invoiceId","items","storagePolicy"])),
    ("ApiInvoiceDocumentPackageRequest",o(vec![
        ("items",a(r("ApiInvoiceDocumentPackageItemRequest"))),("includeMergedPdf",b()),("createZip",b()),
        ("destinationPath",s())
    ])),
    ("ApiInvoiceDraftReportHtmlPreviewRequest",o(vec![
        ("reportType",s()),("templatePath",s()),("withSeal",b()),
        ("invoice",Schema::default().one_of(vec![ty(Primitive::Null),r("ApiInvoiceDetailDto")]))
    ])),
    ("ApiInvoiceItemDto",object_groups(vec![
        (&["id","invoiceId"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["poNumber","styleNo","styleName","fabricComposition","styleNameCN","brand","hsCode","origin","unitEN","unitCN","ctnUnitEN","ctnUnitCN","priceCalculationMode","spare1","spare2","spare3","spare4","spare5","spare6","spare7","spare8","spare9","spare10","customFieldsJson"], s()),
        (&["quantity","pcsPerCtn","cartons","length","width","height","volume","gwPerCtn","nwPerCtn","gwTotal","nwTotal","unitPrice","totalPrice","purchasePrice","purchaseTotal","taxRebateRate","taxRefundAmount"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        (&["printRows"], a(r("ApiInvoicePrintRow")).default_value(serde_json::json!([])).description("此货项的打印行，非空时必须且只能有一行 Item 承载本货项数量金额；Text 与 Blank 不参与合计。").max_items(64)),
    ]).required(vec!["id","invoiceId","poNumber","styleNo","styleName","fabricComposition","styleNameCN","brand","hsCode","origin","quantity","unitEN","unitCN","pcsPerCtn","cartons","ctnUnitEN","ctnUnitCN","length","width","height","volume","gwPerCtn","nwPerCtn","gwTotal","nwTotal","priceCalculationMode","unitPrice","totalPrice","purchasePrice","purchaseTotal","taxRebateRate","taxRefundAmount","spare1","spare2","spare3","spare4","spare5","spare6","spare7","spare8","spare9","spare10","customFieldsJson"])),
    ("ApiInvoiceListItemDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceNo",s()),("contractNo",s()),
        ("invoiceDate",s().format("date")),("customerName",s()),("exporterName",s()),
        ("destinationCountry",s()),("portOfLoading",s()),("portOfDestination",s()),
        ("currency",s()),("totalAmount",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("type",s()),
        ("status",s())
    ]).required(vec!["id","invoiceNo","contractNo","invoiceDate","customerName","exporterName","destinationCountry","portOfLoading","portOfDestination","currency","totalAmount","type","status"])),
    ("ApiInvoiceProfitAnalysisRequest",o(vec![("invoice",r("ApiInvoiceDetailDto"))]).required(vec!["invoice"])),
    ("ApiInvoiceProfitAnalysisResponse",object_groups(vec![
        (&["currency","salesTotalText","exchangeRateText","salesRmbText","purchaseCostText","taxRefundText","grossProfitText","marginText","storagePolicy"], s()),
        (&["salesTotal","salesRmb","purchaseCost","taxRefund","grossProfit","margin"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        (&["exchangeRate"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
    ]).required(vec!["currency","salesTotal","salesRmb","purchaseCost","taxRefund","grossProfit","margin","salesTotalText","exchangeRateText","salesRmbText","purchaseCostText","taxRefundText","grossProfitText","marginText","storagePolicy"])),
    ("ApiInvoicePurgeRequest",o(vec![("invoiceNoConfirmation",s()),("reason",s())])),
    ("ApiInvoicePurgeResponse",o(vec![
        ("success",b()),("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceNo",s()),
        ("previousStatus",s()),("message",s()),("storagePolicy",s())
    ]).required(vec!["success","invoiceId","invoiceNo","previousStatus","message","storagePolicy"])),
    ("ApiInvoiceReportZipRequest",o(vec![
        ("invoiceIds",a(Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))),("reportType",s()),("templatePath",s()),
        ("withSeal",b()),("destinationPath",s()),("templatePaths",Schema::default().kind(Types::Many(vec![Primitive::Array,Primitive::Null])).items(Box::new(s().min_length(1))).default_value(serde_json::json!(null)).description("为每张发票生成所有选中模板。未提供或为 null 时使用原 templatePath；总 PDF 数受 batchLimits 限制。").min_items(1).max_items(20).unique_items(true))
    ])),
    ("ApiInvoiceSaveResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("isUpdate",b()),
        ("invoice",r("ApiInvoiceDetailDto"))
    ]).required(vec!["success","id","isUpdate","invoice"])),
    ("ApiInvoiceStatusHistoryDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("fromStatus",s()),
        ("toStatus",s()),("note",s()),("changedByUserId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("changedByUsername",s()),("changedAt",s().format("date-time"))
    ]).required(vec!["id","invoiceId","fromStatus","toStatus","note","changedByUsername","changedAt"])),
    ("ApiInvoiceStatusTransitionRequest",o(vec![("targetStatus",s()),("rowVersion",s()),("note",s())])),
    ("ApiInvoiceTransferExportResponse",o(vec![
        ("success",b()),("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("packagePath",s()),
        ("storagePolicy",s()),("message",s())
    ]).required(vec!["success","invoiceId","packagePath","storagePolicy","message"])),
    ("ApiInvoiceTransferImportRequest",o(vec![("packagePath",s()),("conflictAction",s()),("newInvoiceNo",s())])),
    ("ApiInvoiceTransferImportResponse",o(vec![
        ("success",b()),("result",r("ApiInvoiceTransferImportResultDto")),("preview",r("ApiInvoiceTransferPreviewDto")),
        ("storagePolicy",s()),("message",s())
    ]).required(vec!["success","result","preview","storagePolicy","message"])),
    ("ApiInvoiceTransferImportResultDto",o(vec![
        ("success",b()),("message",s()),("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("finalInvoiceNo",s()),("actionTaken",s())
    ]).required(vec!["success","message","finalInvoiceNo","actionTaken"])),
    ("ApiInvoiceTransferPathRequest",o(vec![("packagePath",s())])),
    ("ApiInvoiceTransferPreviewDto",o(vec![
        ("invoiceNo",s()),("type",s()),("itemCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("customerExists",b()),("exporterExists",b()),("invoiceExists",b()),
        ("invoiceMatches",b()),("existingInvoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["invoiceNo","type","itemCount","customerExists","exporterExists","invoiceExists","invoiceMatches","existingInvoiceId"])),
    ("ApiInvoiceTransferPreviewResponse",o(vec![
        ("checksumValid",b()),("checksumMessage",s()),("preview",r("ApiInvoiceTransferPreviewDto")),
        ("storagePolicy",s())
    ]).required(vec!["checksumValid","checksumMessage","preview","storagePolicy"])),
    ("ApiInvoiceUnverifyRequest",o(vec![("rowVersion",s()),("note",s())])),
    ("ApiPagedResponseOfApiCrmCustomerDto",super::schema_shapes::paged_response("ApiCrmCustomerDto")),
    ("ApiPagedResponseOfApiCustomerDto",super::schema_shapes::paged_response("ApiCustomerDto")),
    ("ApiPagedResponseOfApiExporterDto",super::schema_shapes::paged_response("ApiExporterDto")),
    ("ApiPagedResponseOfApiInvoiceListItemDto",super::schema_shapes::paged_response("ApiInvoiceListItemDto")),
    ("ApiPagedResponseOfApiPayeeDto",super::schema_shapes::paged_response("ApiPayeeDto")),
    ("ApiPagedResponseOfApiPaymentDto",super::schema_shapes::paged_response("ApiPaymentDto")),
    ("ApiPagedResponseOfApiQueryInvoiceRowDto",super::schema_shapes::paged_response("ApiQueryInvoiceRowDto")),
    ("ApiPayeeDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("category",s()),("name",s()),
        ("bankName",s()),("rmbAccount",s()),("usdAccount",s()),
        ("contactPerson",s()),("phone",s()),("notes",s()),
        ("rowVersion",s().default_value(serde_json::json!("")))
    ]).required(vec!["id","category","name","bankName","rmbAccount","usdAccount","contactPerson","phone","notes","rowVersion"])),
    ("ApiPaymentDraftReportHtmlPreviewRequest",o(vec![("templatePath",s()),("payment",Schema::default().one_of(vec![ty(Primitive::Null),r("ApiPaymentDto")]))]).additional(Additional::Allowed(false))),
    ("ApiPaymentDto",object_groups(vec![
        (&["id","payeeId"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["ownerUserId"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["departmentId","companyScope","invoiceNo","voucherNo","quantityUnit","tradeMethod","taxRebateRate","spare1","spare2","spare3","spare4","spare5","spare6","spare7","spare8","spare9","spare10","department","project","paymentMethod","payeeName","payerName","bankName","accountNo","notes","goodsName","quantity","shipmentCountry","rowVersion"], s()),
        (&["shipmentDate","paymentDate","receiptDate"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),
        (&["usdAmount","cnyAmount","travelExpense","businessEntertainmentExpense","telephoneExpense","officeExpense","repairExpense","freightMiscExpense","inspectionExpense","otherExpense"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
    ]).required(vec!["id","departmentId","companyScope","invoiceNo","voucherNo","quantityUnit","tradeMethod","taxRebateRate","spare1","spare2","spare3","spare4","spare5","spare6","spare7","spare8","spare9","spare10","payeeId","department","project","usdAmount","cnyAmount","paymentMethod","payeeName","payerName","bankName","accountNo","notes","goodsName","quantity","shipmentCountry","travelExpense","businessEntertainmentExpense","telephoneExpense","officeExpense","repairExpense","freightMiscExpense","inspectionExpense","otherExpense","rowVersion"]).additional(Additional::Allowed(false))),
    ("ApiPaymentReportHtmlPreviewRequest",o(vec![
        ("templatePath",s()),("content",s()),("previewFormat",s().default_value(serde_json::json!("Svg")).enumeration(Enumeration::Values(vec![serde_json::json!("Svg"),serde_json::json!("PdfPage")]))),
        ("pageIndex",i().default_value(serde_json::json!(num("0"))).minimum(num("0")).maximum(num("499"))),("previewRevision",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).description("翻页时提交上一页修订号；单据或模板变化返回 409。").max_length(64))
    ]).additional(Additional::Allowed(false))),
    ("ApiPaymentReportHtmlPreviewResponse",o(vec![
        ("paymentId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("reportType",s()),("templatePath",s()),
        ("html",s()),("storagePolicy",s().default_value(serde_json::json!(""))),("pageIndex",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::Null])).minimum(num("0"))),
        ("pageCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::Null])).minimum(num("0"))),("previewRevision",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])))
    ]).required(vec!["paymentId","reportType","templatePath","html","storagePolicy"])),
    ("ApiPaymentReportPdfRequest",o(vec![("templatePath",s()),("destinationPath",s())]).additional(Additional::Allowed(false))),
    ("ApiPaymentSaveResponse",o(vec![("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("payment",r("ApiPaymentDto"))]).required(vec!["success","id","payment"])),
    ("ApiQueryInvoiceExportRequest",o(vec![
        ("destinationPath",s()),("startDate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),("endDateExclusive",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),
        ("customerId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("exporterId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("keyword",s()),
        ("contractNo",s()),("invoiceType",s()),("transportMode",s()),
        ("styleName",s()),("styleNo",s())
    ])),
    ("ApiQueryInvoiceFilterRequest",o(vec![
        ("startDate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),("endDateExclusive",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),("customerId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("exporterId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("keyword",s()),("contractNo",s()),
        ("invoiceType",s()),("transportMode",s()),("styleName",s()),
        ("styleNo",s())
    ])),
    ("ApiQueryInvoiceRowDto",object_groups(vec![
        (&["id"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["invoiceNo","invoiceDate","contractNo","customerName","exporterName","destinationCountry","tradeTerms","shipmentDate","transportMode","currency","type"], s()),
        (&["totalCartons","totalQuantity","totalAmount"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
    ]).required(vec!["id","invoiceNo","invoiceDate","contractNo","customerName","exporterName","destinationCountry","tradeTerms","shipmentDate","transportMode","totalCartons","totalQuantity","totalAmount","currency","type"])),
    ("DocumentFieldLabelSettings",o(vec![("invoice",ty(Primitive::Object).additional(Additional::Schema(Box::new(s())))),("item",ty(Primitive::Object).additional(Additional::Schema(Box::new(s())))),("payment",ty(Primitive::Object).additional(Additional::Schema(Box::new(s()))))]).required(vec!["invoice","item","payment"])),
    ("InvoiceCloneOptions",o(vec![
        ("copyHeader",b()),("copyItems",b()),("resetDates",b()),
        ("clearAmounts",b())
    ])),
    ("InvoiceReviewIssue",o(vec![("field",s()),("rowNumber",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("message",s())]).required(vec!["field","message"])),
    ("InvoiceReviewResult",o(vec![("ready",b()),("issues",a(r("InvoiceReviewIssue")))]).required(vec!["ready","issues"])),
    ("PaymentTemplateItem",o(vec![
        ("name",s()),("templatePath",s()),("isEnabled",b()),
        ("reportType",s())
    ]).required(vec!["name","templatePath","isEnabled","reportType"]).additional(Additional::Allowed(false))),
    ("ApiInvoicePrintRow",o(vec![("kind",s().enumeration(Enumeration::Values(vec![serde_json::json!("Item"),serde_json::json!("Text"),serde_json::json!("Blank")]))),("text",s().description("本行打印原文；空字符串保留空白，不回填品名。").max_length(1000))]).required(vec!["kind","text"]).additional(Additional::Allowed(false))),
    ("ApiInvoiceAmountAdjustment",o(vec![("description",s().description("报表上的调整说明，例如 RECEIVED DEPOSIT 或 SAMPLE CHARGE。").max_length(200)),("amount",s().pattern("^[+-]?[0-9]+(?:\\.[0-9]{1,2})?$").description("同发票币种的精确金额；负数扣减，正数增加。").max_length(32))]).required(vec!["description","amount"]).additional(Additional::Allowed(false))),
    ("ApiInvoiceReportBatchLimitsContract",o(vec![
        ("maxInvoices",i().default_value(serde_json::json!(num("200")))),("maxTemplates",i().default_value(serde_json::json!(num("20")))),("maxReports",i().default_value(serde_json::json!(num("200")))),
        ("maxArchiveBytes",i().default_value(serde_json::json!(num("67108864"))))
    ])),
    ("ApiInvoiceSealImageSaveRequest",o(vec![("imageDataUrl",s())])),
    ("ApiInvoiceSealImageSaveResponse",o(vec![
        ("imagePath",s()),("fileName",s()),("contentType",s()),
        ("sizeBytes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),("storagePolicy",s())
    ]).required(vec!["imagePath","fileName","contentType","sizeBytes","storagePolicy"])),
    ("ApiInvoiceEditingLimitsContract",o(vec![("maxItems",i().default_value(serde_json::json!(num("5000"))).constant(serde_json::json!(num("5000"))))]).required(vec!["maxItems"]).additional(Additional::Allowed(false))),
] }
