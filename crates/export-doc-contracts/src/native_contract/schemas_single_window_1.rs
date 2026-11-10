//! Native declarations. Change these typed definitions, then regenerate both clients.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<(&'static str,Schema)> { vec![
    ("ApiCustomsCooAttachmentDto",object_groups(vec![
        (&["id","documentId","sortOrder"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["certNo","certType","aplRegNo","ciqRegNo","fileType","fileName","filePath","mediaType","description","docType"], s()),
        (&["isDelay","fileExistsAtBuild"], b()),
    ]).required(vec!["id","documentId","certNo","certType","aplRegNo","ciqRegNo","fileType","fileName","filePath","mediaType","description","docType","isDelay","fileExistsAtBuild","sortOrder"])),
    ("ApiCustomsCooEditorOptionsResponse",object_groups(vec![
        (&["applyTypeOptions","certStatusOptions","certTypeOptions","producerSecretOptions","exhibitFlagOptions","thirdPartyInvoiceOptions","predictFlagOptions","promiseOptions","currencyOptions","cooTradeModeOptions","goodsItemFlagOptions","packTypeOptions","goodsTaxRateOptions","packUnitOptions"], a(r("ApiCustomsCooOptionDto"))),
        (&["originCriteriaOptionSets","originCriteriaSubOptionSets"], a(r("ApiCustomsCooOriginCriteriaOptionSetDto"))),
        (&["storagePolicy"], s()),
    ]).required(vec!["applyTypeOptions","certStatusOptions","certTypeOptions","producerSecretOptions","exhibitFlagOptions","thirdPartyInvoiceOptions","predictFlagOptions","promiseOptions","currencyOptions","cooTradeModeOptions","goodsItemFlagOptions","packTypeOptions","goodsTaxRateOptions","packUnitOptions","originCriteriaOptionSets","originCriteriaSubOptionSets","storagePolicy"])),
    ("ApiCustomsCooItemDto",object_groups(vec![
        (&["id","documentId","sourceItemId","gNo"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["sourceStyleNo","goodsItemFlag","hsCode","goodsName","goodsNameE","packQty","packUnit","goodsQty","goodsQtyRef","goodsUnitE","goodsUnit","goodsUnitRef","secdGoodsQtyRef","secdGoodsUnitRef","grossWt","netWt","wtUnit","invPrice","invValue","fobValue","iCompPrpr","goodsDesc","oriCriteria","oriCriteriaRef","goodsOriginCountry","goodsOriginCountryEn","producer","producerTel","producerFax","producerEmail","ciqRegNo","prdcEtpsName","prdcEtpsConcEr","prdcEtpsTel","producerSertFlag","oriCriteriaSub","invNo","packType","goodsTaxRate"], s()),
    ]).required(vec!["id","documentId","sourceItemId","sourceStyleNo","goodsItemFlag","gNo","hsCode","goodsName","goodsNameE","packQty","packUnit","goodsQty","goodsQtyRef","goodsUnitE","goodsUnit","goodsUnitRef","secdGoodsQtyRef","secdGoodsUnitRef","grossWt","netWt","wtUnit","invPrice","invValue","fobValue","iCompPrpr","goodsDesc","oriCriteria","oriCriteriaRef","goodsOriginCountry","goodsOriginCountryEn","producer","producerTel","producerFax","producerEmail","ciqRegNo","prdcEtpsName","prdcEtpsConcEr","prdcEtpsTel","producerSertFlag","oriCriteriaSub","invNo","packType","goodsTaxRate"])),
    ("ApiCustomsCooNonpartyCorpDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("documentId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("sortNo",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("entName",s()),("entAddr",s()),("entCountryCode",s()),
        ("entCountryName",s())
    ]).required(vec!["id","documentId","sortNo","entName","entAddr","entCountryCode","entCountryName"])),
    ("ApiCustomsCooOptionDto",o(vec![("value",s()),("label",s())]).required(vec!["value","label"])),
    ("ApiCustomsCooOriginCriteriaOptionSetDto",o(vec![("certType",s()),("originCriteria",s()),("options",a(r("ApiCustomsCooOptionDto")))]).required(vec!["certType","originCriteria","options"])),
    ("ApiCustomsCooProducerProfileDto",object_groups(vec![
        (&["versionNumber"], i().minimum(num("1")).format("int64")),
        (&["id"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["ciqRegNo","prdcEtpsName","prdcEtpsConcEr","prdcEtpsTel","producer","producerTel","producerFax","producerEmail","producerSertFlag","lastInvoiceNo","lastContractNo","lastSourceStyleNo"], s()),
        (&["createdAt","updatedAt","lastUsedAt"], s().format("date-time")),
    ]).required(vec!["id","versionNumber","ciqRegNo","prdcEtpsName","prdcEtpsConcEr","prdcEtpsTel","producer","producerTel","producerFax","producerEmail","producerSertFlag","lastInvoiceNo","lastContractNo","lastSourceStyleNo","createdAt","updatedAt","lastUsedAt"])),
    ("ApiCustomsCooProducerProfileInputDto",object_groups(vec![
        (&["ciqRegNo","prdcEtpsName","prdcEtpsConcEr","prdcEtpsTel","producer","producerTel","producerFax","producerEmail","producerSertFlag","lastInvoiceNo","lastContractNo","lastSourceStyleNo"], s()),
    ])),
    ("ApiCustomsCooProducerProfileListResponse",o(vec![("items",a(r("ApiCustomsCooProducerProfileDto"))),("totalCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("storagePolicy",s())]).required(vec!["items","totalCount","storagePolicy"])),
    ("ApiCustomsCooProducerProfileResponse",o(vec![("profile",r("ApiCustomsCooProducerProfileDto")),("storagePolicy",s())]).required(vec!["profile","storagePolicy"])),
    ("ApiCustomsCooProducerProfileSaveRequest",o(vec![("profile",r("ApiCustomsCooProducerProfileInputDto")),("expectedVersion",i().minimum(num("1")).format("int64"))]).required(vec!["profile"])),
    ("ApiCustomsCooProducerProfileSaveResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("profile",r("ApiCustomsCooProducerProfileDto")),
        ("message",s()),("storagePolicy",s())
    ]).required(vec!["success","id","profile","message","storagePolicy"])),
    ("ApiCustomsCooUnlockFieldsResponse",o(vec![
        ("success",b()),("changedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("document",r("ApiCustomsCooDocumentDto")),
        ("lockedFields",a(r("ApiSingleWindowLockedFieldDto"))),("message",s())
    ]).required(vec!["success","changedCount","document","lockedFields","message"])),
    ("ApiSingleWindowClientDispatchRequest",o(vec![("batchId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["batchId"])),
    ("ApiSingleWindowClientDirectories",o(vec![("outBox",s()),("sentBox",s()),("inBox",s()),("failBox",s())]).required(vec!["outBox","sentBox","inBox","failBox"])),
    ("ApiSingleWindowClientProfileDto",o(vec![
        ("versionNumber",i().minimum(num("1")).format("int64")),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("stationKey",s()),("profileKey",s()),
        ("profileName",s()),("companyScope",s()),("cardIdentifier",s()),
        ("stationAssignmentCode",s()),("customsCooDirectories",r("ApiSingleWindowClientDirectories")),("agentConsignmentDirectories",r("ApiSingleWindowClientDirectories")),
        ("canSubmitCustomsCoo",b()),("canSubmitAgentConsignment",b()),("isEnabled",b()),
        ("isActive",b()),("updatedAt",s().format("date-time"))
    ]).required(vec!["versionNumber","id","stationKey","profileKey","profileName","companyScope","cardIdentifier","stationAssignmentCode","customsCooDirectories","agentConsignmentDirectories","canSubmitCustomsCoo","canSubmitAgentConsignment","isEnabled","isActive","updatedAt"])),
    ("ApiSingleWindowClientProfileSaveRequest",o(vec![
        ("expectedVersion",i().minimum(num("0")).format("int64")),("profileKey",s()),("profileName",s()),("companyScope",s()),
        ("cardIdentifier",s()),("customsCooDirectories",r("ApiSingleWindowClientDirectories")),("agentConsignmentDirectories",r("ApiSingleWindowClientDirectories")),
        ("canSubmitCustomsCoo",b()),("canSubmitAgentConsignment",b())
    ]).required(vec!["expectedVersion","profileKey","profileName","companyScope","cardIdentifier","customsCooDirectories","agentConsignmentDirectories","canSubmitCustomsCoo","canSubmitAgentConsignment"])),
    ("ApiSingleWindowClientProfilesResponse",o(vec![
        ("profiles",a(r("ApiSingleWindowClientProfileDto"))),("activeProfileKey",s()),("storagePolicy",s()),
        ("message",s()),("savedProfileKey",s())
    ]).required(vec!["profiles","activeProfileKey","storagePolicy","message"])),
    ("ApiSingleWindowHandoffPackageResponse",o(vec![
        ("success",b()),("packagePath",s()),("manifest",r("SingleWindowPackageManifest")),
        ("trackingBatchId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("storagePolicy",s()),("message",s())
    ]).required(vec!["success","packagePath","manifest","storagePolicy","message"])),
    ("ApiSingleWindowImportedPackageResponse",o(vec![
        ("success",b()),("packagePath",s()),("workingDirectory",s()),
        ("workingDirectoryKept",b()),("manifest",r("SingleWindowPackageManifest")),("parsedReceipts",a(r("SingleWindowReceiptParseResult"))),
        ("trackingBatchId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("trackingStatus",s()),("persistedReceiptCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("storagePolicy",s()),("message",s())
    ]).required(vec!["success","packagePath","workingDirectory","workingDirectoryKept","manifest","parsedReceipts","trackingStatus","persistedReceiptCount","storagePolicy","message"])),
    ("ApiSingleWindowImportPackageRequest",o(vec![("packagePath",s()),("workingDirectory",s().default_value(serde_json::json!(""))),("keepWorkingDirectory",b().default_value(serde_json::json!(false)))]).required(vec!["packagePath"])),
    ("ApiSingleWindowIssuingAuthorityCatalogResponse",o(vec![("options",a(r("ApiSingleWindowIssuingAuthorityOptionDto"))),("storagePolicy",s())]).required(vec!["options","storagePolicy"])),
    ("ApiSingleWindowIssuingAuthorityOptionDto",o(vec![("code",s()),("label",s()),("applicationAddress",s())]).required(vec!["code","label","applicationAddress"])),
    ("ApiSingleWindowLockedFieldDto",o(vec![
        ("key",s()),("displayName",s()),("currentValue",s()),
        ("suggestedValue",s())
    ]).required(vec!["key","displayName","currentValue","suggestedValue"])),
    ("ApiSingleWindowLockedFieldsResponse",o(vec![("count",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("fields",a(r("ApiSingleWindowLockedFieldDto")))]).required(vec!["count","fields"])),
    ("ApiSingleWindowReceiptCollectionRequest",o(vec![("batchId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["batchId"])),
    ("ApiSingleWindowReceiptPackageExportRequest",o(vec![
        ("businessType",s()),("batchReference",s()),("invoiceNo",s()),
        ("receiptFiles",a(s())),("packagePath",s())
    ]).required(vec!["businessType","batchReference","invoiceNo","receiptFiles","packagePath"])),
    ("ApiSingleWindowReferenceCatalogExcelColumnMappingDto",o(vec![
        ("fieldKey",s()),("label",s()),("columnNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("required",b())
    ]).required(vec!["fieldKey","label","columnNumber","required"])),
    ("ApiSingleWindowReferenceCatalogExcelImportPreviewResponse",o(vec![
        ("success",b()),("catalogKey",s()),("sheetName",s()),
        ("sheetNames",a(s())),("headerRowNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("dataStartRowNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("columnMappings",a(r("ApiSingleWindowReferenceCatalogExcelColumnMappingDto"))),("catalog",r("SingleWindowReferenceCatalogModel")),("rowCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("message",s()),("storagePolicy",s())
    ]).required(vec!["success","catalogKey","sheetName","sheetNames","headerRowNumber","dataStartRowNumber","columnMappings","catalog","rowCount","message","storagePolicy"])),
    ("ApiSingleWindowReferenceCatalogResponse",o(vec![("catalog",r("SingleWindowReferenceCatalogModel")),("revision",i().minimum(num("0")).format("int64")),("storagePolicy",s())]).required(vec!["catalog","revision","storagePolicy"])),
    ("ApiSingleWindowReferenceCatalogSaveRequest",o(vec![("catalog",r("SingleWindowReferenceCatalogModel")),("expectedRevision",i().minimum(num("0")).format("int64"))]).required(vec!["catalog","expectedRevision"])),
    ("ApiSingleWindowReferenceCatalogSaveResponse",o(vec![
        ("revision",i().minimum(num("1")).format("int64")),
        ("success",b()),("catalog",r("SingleWindowReferenceCatalogModel")),("message",s()),
        ("storagePolicy",s())
    ]).required(vec!["success","catalog","revision","message","storagePolicy"])),
    ("ApiSingleWindowRepairGroupsRequest",o(vec![("groupKeys",a(s()))]).required(vec!["groupKeys"])),
    ("ApiSingleWindowRepairGroupsResponse",o(vec![
        ("success",b()),("repairedGroupCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("review",r("SingleWindowExportReview")),
        ("message",s())
    ]).required(vec!["success","repairedGroupCount","review","message"])),
    ("ApiSingleWindowSubmitPackageRequest",o(vec![("expectedDraftRevision",i().minimum(num("1"))),("packagePath",s().default_value(serde_json::json!(""))),("stationAssignmentCode",s().default_value(serde_json::json!("")))]).required(vec!["expectedDraftRevision"])),
    ("ApiSingleWindowUnlockFieldsRequest",o(vec![("fieldKeys",a(s()))])),
    ("CustomsCooDefaultProfile",o(vec![
        ("applName",s()),("applicant",s()),("applTel",s()),
        ("orgCode",s()),("fetchPlace",s()),("aplAdd",s())
    ]).required(vec!["applName","applicant","applTel","orgCode","fetchPlace","aplAdd"])),
    ("SingleWindowBusinessType",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("CustomsCoo"),serde_json::json!("AgentConsignment")]))),
    ("SingleWindowClientDispatchResult",o(vec![
        ("batchId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("batchReference",s()),("targetDirectory",s()),
        ("profileName",s()),("payloadFileCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("attachmentFileCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["batchId","batchReference","targetDirectory","profileName","payloadFileCount","attachmentFileCount"])),
    ("SingleWindowEditorNavigationTarget",o(vec![("groupKey",s()),("propertyKey",s()),("goodsLineNo",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["groupKey","propertyKey","goodsLineNo"])),
    ("SingleWindowExportIssue",o(vec![
        ("groupKey",s()),("groupDisplayName",s()),("message",s()),
        ("severity",r("SingleWindowExportIssueSeverity")),("canAutoRepair",b()),("navigationTarget",r("SingleWindowEditorNavigationTarget"))
    ]).required(vec!["groupKey","groupDisplayName","message","severity","canAutoRepair","navigationTarget"])),
    ("SingleWindowExportIssueGroup",o(vec![
        ("groupKey",s()),("groupDisplayName",s()),("canAutoRepair",b()),
        ("issues",a(r("SingleWindowExportIssue"))),("errorCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("warningCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("infoCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["groupKey","groupDisplayName","canAutoRepair","issues","errorCount","warningCount","infoCount"])),
    ("SingleWindowExportIssueSeverity",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("Info"),serde_json::json!("Warning"),serde_json::json!("Error")]))),
    ("SingleWindowExportReview",o(vec![
        ("businessType",r("SingleWindowBusinessType")),("sourceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceNo",s()),
        ("contractNo",s()),("draftRevision",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("manualLockedFieldCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("sourceDiffCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("sourceDiffSummary",s()),("groups",a(r("SingleWindowExportIssueGroup"))),
        ("totalErrorCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("totalWarningCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("hasIssues",b())
    ]).required(vec!["businessType","sourceId","invoiceNo","contractNo","draftRevision","manualLockedFieldCount","sourceDiffCount","sourceDiffSummary","groups","totalErrorCount","totalWarningCount","hasIssues"])),
    ("SingleWindowOperationCenterDetail",object_groups(vec![
        (&["batchId","submissionVersion","draftRevision","payloadFileCount","attachmentFileCount","warningCount","clientDispatchAttemptCount"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["batchReference","businessType","invoiceNo","contractNo","companyScope","status","referenceNo","clientProfileName","assignedCardIdentifier","clientDispatchOperationId"], s()),
        (&["createdAt","updatedAt"], s().format("date-time")),
        (&["lastReceiptAt","lastClientDispatchAt","clientDispatchLeaseUntil"], Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        (&["packageRecords"], a(r("SingleWindowOperationCenterPackageRecord"))),
        (&["receiptRecords"], a(r("SingleWindowOperationCenterReceiptRecord"))),
    ]).required(vec!["batchId","batchReference","submissionVersion","draftRevision","businessType","invoiceNo","contractNo","companyScope","status","referenceNo","clientProfileName","assignedCardIdentifier","payloadFileCount","attachmentFileCount","warningCount","createdAt","updatedAt","clientDispatchOperationId","clientDispatchAttemptCount","packageRecords","receiptRecords"])),
    ("SingleWindowOperationCenterPackageRecord",o(vec![
        ("packageType",s()),("direction",s()),("payloadFileCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("attachmentFileCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("warningCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("createdAt",s().format("date-time"))
    ]).required(vec!["packageType","direction","payloadFileCount","attachmentFileCount","warningCount","createdAt"])),
    ("SingleWindowOperationCenterPageResult",o(vec![
        ("rows",a(r("SingleWindowOperationCenterRow"))),("totalCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("pageNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("pageSize",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("totalPages",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["rows","totalCount","pageNumber","pageSize","totalPages"])),
    ("SingleWindowOperationCenterReceiptRecord",o(vec![
        ("receiptKind",s()),("referenceNo",s()),("receiptCode",s()),
        ("receiptMessage",s()),("businessStatus",s()),("sourceFileName",s()),
        ("importedAt",s().format("date-time")),("occurredAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time"))
    ]).required(vec!["receiptKind","referenceNo","receiptCode","receiptMessage","businessStatus","sourceFileName","importedAt"])),
    ("SingleWindowOperationCenterRow",object_groups(vec![
        (&["batchId","submissionVersion","draftRevision","receiptCount"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["batchReference","businessType","invoiceNo","contractNo","companyScope","status","referenceNo","lastReceiptCode","lastReceiptMessage","clientProfileName","assignedCardIdentifier"], s()),
        (&["createdAt","updatedAt"], s().format("date-time")),
    ]).required(vec!["batchId","batchReference","submissionVersion","draftRevision","businessType","invoiceNo","contractNo","companyScope","status","referenceNo","lastReceiptCode","lastReceiptMessage","clientProfileName","assignedCardIdentifier","createdAt","updatedAt","receiptCount"])),
    ("SingleWindowPackageFile",o(vec![
        ("relativePath",s()),("mediaType",s()),("description",s()),
        ("sizeBytes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),("sha256",s())
    ]).required(vec!["relativePath","mediaType","description","sizeBytes","sha256"])),
    ("SingleWindowPackageManifest",object_groups(vec![
        (&["schemaVersion","packageId","batchReference","sourceDocumentType","sourceBaselineHash","invoiceNo","contractNo","companyScope","snapshotSha256","sourcePackageDigest","receiptReferenceNo","contentDigest","stationKey","cardIdentifier","clientProfileKey","clientProfileName","assignmentNonce","authenticationAlgorithm","authenticationTag","createdOnMachine"], s()),
        (&["packageType"], r("SingleWindowPackageType")),
        (&["businessType"], r("SingleWindowBusinessType")),
        (&["sourceId","sourceDocumentId","submissionVersion","draftRevision"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["createdAt"], s().format("date-time")),
        (&["payloadFiles","attachmentFiles"], a(r("SingleWindowPackageFile"))),
        (&["warnings"], a(s())),
    ]).required(vec!["schemaVersion","packageId","packageType","businessType","batchReference","sourceId","sourceDocumentId","sourceDocumentType","submissionVersion","draftRevision","sourceBaselineHash","invoiceNo","contractNo","companyScope","snapshotSha256","sourcePackageDigest","receiptReferenceNo","contentDigest","stationKey","cardIdentifier","clientProfileKey","clientProfileName","assignmentNonce","authenticationAlgorithm","authenticationTag","createdAt","createdOnMachine","payloadFiles","attachmentFiles","warnings"])),
    ("SingleWindowPackageType",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("SubmitPackage"),serde_json::json!("ReceiptPackage")]))),
    ("SingleWindowReceiptBusinessStatus",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("Unknown"),serde_json::json!("Received"),serde_json::json!("Accepted"),serde_json::json!("Rejected"),serde_json::json!("PendingReview"),serde_json::json!("Approved"),serde_json::json!("Failed")]))),
    ("SingleWindowReceiptCollectionResult",o(vec![
        ("batchId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("batchReference",s()),("receiptDirectories",a(s())),
        ("receiptFiles",a(s()))
    ]).required(vec!["batchId","batchReference","receiptDirectories","receiptFiles"])),
    ("SingleWindowReceiptKind",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("Unknown"),serde_json::json!("CustomsCooBusinessReceipt"),serde_json::json!("CustomsCooTechnicalReceipt"),serde_json::json!("CustomsCooAttachmentReceipt"),serde_json::json!("AgentConsignmentImportResponse"),serde_json::json!("AgentConsignmentAcd002")]))),
    ("SingleWindowReceiptParseResult",o(vec![
        ("businessType",r("SingleWindowBusinessType")),("receiptKind",r("SingleWindowReceiptKind")),("referenceNo",s()),
        ("receiptCode",s()),("receiptMessage",s()),("businessStatus",r("SingleWindowReceiptBusinessStatus")),
        ("occurredAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("sourceFileName",s())
    ]).required(vec!["businessType","receiptKind","referenceNo","receiptCode","receiptMessage","businessStatus","sourceFileName"])),
    ("SingleWindowReferenceAcdCountryEntry",o(vec![
        ("code",s()),("chineseName",s()),("englishName",s()),
        ("aliases",a(s()))
    ]).required(vec!["code","chineseName","englishName","aliases"])),
    ("SingleWindowReferenceAcdTradeModeEntry",o(vec![
        ("code",s()),("name",s()),("description",s()),
        ("aliases",a(s()))
    ]).required(vec!["code","name","description","aliases"])),
    ("SingleWindowReferenceCatalogModel",o(vec![
        ("countries",a(r("SingleWindowReferenceCountryEntry"))),("acdCountries",a(r("SingleWindowReferenceAcdCountryEntry"))),("currencies",a(r("SingleWindowReferenceCurrencyEntry"))),
        ("acdTradeModes",a(r("SingleWindowReferenceAcdTradeModeEntry"))),("transportModes",a(r("SingleWindowReferenceTransportModeEntry"))),("ports",a(r("SingleWindowReferencePortEntry")))
    ]).required(vec!["countries","acdCountries","currencies","acdTradeModes","transportModes","ports"])),
    ("SingleWindowReferenceCountryEntry",o(vec![
        ("code",s()),("englishName",s()),("chineseName",s()),
        ("aliases",a(s()))
    ]).required(vec!["code","englishName","chineseName","aliases"])),
    ("SingleWindowReferenceCurrencyEntry",o(vec![
        ("code",s()),("acdCode",s()),("alphaCode",s()),
        ("aliases",a(s()))
    ]).required(vec!["code","acdCode","alphaCode","aliases"])),
    ("SingleWindowReferencePortEntry",o(vec![("value",s()),("aliases",a(s()))]).required(vec!["value","aliases"])),
    ("SingleWindowReferenceTransportModeEntry",o(vec![("value",s()),("aliases",a(s()))]).required(vec!["value","aliases"])),
    ("SingleWindowSettings",o(vec![("customsCooDefaults",r("CustomsCooDefaultProfile"))]).required(vec!["customsCooDefaults"])),
] }
