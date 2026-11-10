//! Native declarations. Change these typed definitions, then regenerate both clients.
use super::model::*;
use std::collections::BTreeMap;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<(&'static str,Schema)> { vec![
    ("AISettings",o(vec![
        ("apiEndpoint",s().description("支持兼容 OpenAI 接口的大模型 API 地址，例如 https://api.deepseek.com/v1/chat/completions 或 http://localhost:11434/v1/chat/completions (Ollama)")),("apiKey",s().description("用于调用大模型的 API Key。如果使用本地 Ollama，可以填入任意字符 (如 'ollama')")),("modelName",s().description("例如 deepseek-chat, qwen-max 或本地模型如 qwen2.5:7b")),
        ("systemPrompt",s())
    ]).required(vec!["apiEndpoint","apiKey","modelName","systemPrompt"])),
    ("ApiAgentConsignmentUnlockFieldsResponse",o(vec![
        ("success",b()),("changedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("document",r("ApiAgentConsignmentDocumentDto")),
        ("lockedFields",a(r("ApiSingleWindowLockedFieldDto"))),("message",s())
    ]).required(vec!["success","changedCount","document","lockedFields","message"])),
    ("ApiAttachmentSaveRequest",o(vec![("destinationPath",s())]).required(vec!["destinationPath"])),
    ("ApiAttachmentUploadForm",o(vec![
        ("file",Schema::default().one_of(vec![ty(Primitive::Null),r("IFormFile")])),("attachmentId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("uploadKey",s().format("uuid")),("title",s()),("categoryId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("poNumber",s()),("styleNo",s()),("note",s())
    ])),
    ("ApiAuditLogCleanupRequest",o(vec![("daysToKeep",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("maxCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("confirmed",b())])),
    ("ApiAuditLogCommandResponse",o(vec![
        ("success",b()),("message",s()),("affectedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("destinationPath",s()),("storagePolicy",s())
    ]).required(vec!["success","message","affectedCount","destinationPath","storagePolicy"])),
    ("ApiAuditLogDeleteRequest",o(vec![
        ("confirmed",b()),("invoiceKeyword",s()),("entityName",s()),
        ("action",s()),("userId",s()),("startTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        ("endTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("keyword",s()),("maxCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ])),
    ("ApiAuditLogDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("entityName",s()),("action",s()),
        ("entityId",s()),("oldValues",s()),("newValues",s()),
        ("userId",s()),("timestamp",s().format("date-time")),("oldValuesPreview",s()),
        ("newValuesPreview",s())
    ]).required(vec!["id","entityName","action","entityId","oldValues","newValues","userId","timestamp","oldValuesPreview","newValuesPreview"])),
    ("ApiAuditLogFilterRequest",o(vec![
        ("invoiceKeyword",s()),("entityName",s()),("action",s()),
        ("userId",s()),("startTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("endTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        ("keyword",s()),("maxCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ])),
    ("ApiAuditLogPathExportRequest",o(vec![
        ("destinationPath",s()),("invoiceKeyword",s()),("entityName",s()),
        ("action",s()),("userId",s()),("startTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        ("endTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("keyword",s()),("maxCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ])),
    ("ApiCommandResponse",o(vec![("success",b()),("message",s())]).required(vec!["success","message"])),
    ("ApiContainerDimensionsDto",o(vec![
        ("length",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("width",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("height",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("volume",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("maxWeight",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$"))
    ])),
    ("ApiContainerPackingAnalysisDto",object_groups(vec![
        (&["packedItems"], a(r("ApiPackedCargoItemDto"))),
        (&["totalPackages","packedPackages","unpackedPackages","totalPallets","packedPallets","containersNeededByVolume","containersNeededByWeight","estimatedContainerCount"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["totalVolume","totalWeight","packedVolume","packedWeight","volumeUtilizationPercent","weightUtilizationPercent","centerOfGravityX","centerOfGravityY","centerOfGravityLengthDeviationPercent","centerOfGravityWidthDeviationPercent"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        (&["isCenterOfGravityWithinTolerance"], b()),
    ]).required(vec!["packedItems","totalPackages","packedPackages","unpackedPackages","totalPallets","packedPallets","totalVolume","totalWeight","packedVolume","packedWeight","volumeUtilizationPercent","weightUtilizationPercent","containersNeededByVolume","containersNeededByWeight","estimatedContainerCount","centerOfGravityX","centerOfGravityY","centerOfGravityLengthDeviationPercent","centerOfGravityWidthDeviationPercent","isCenterOfGravityWithinTolerance"])),
    ("ApiContainerPackingAnalyzeRequest",o(vec![("container",r("ApiContainerDimensionsDto")),("cargoItems",a(r("ApiContainerPackingCargoInputDto"))),("rules",r("ApiContainerPackingRulesDto"))])),
    ("ApiContainerPackingAnalyzeResponse",o(vec![("analysis",r("ApiContainerPackingAnalysisDto")),("storagePolicy",s())]).required(vec!["analysis","storagePolicy"])),
    ("ApiContainerPackingCargoInputDto",o(vec![
        ("name",s()),("length",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("width",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("height",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("weight",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("quantity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("colorArgb",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("usePallet",b()),("unitsPerPallet",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("maxTopLoadWeight",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("preferredZone",s()),("loadSequence",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("priorityGroup",s())
    ])),
    ("ApiContainerPackingPdfRequest",o(vec![
        ("projectName",s()),("containerType",s()),("destinationPath",s()),
        ("container",r("ApiContainerDimensionsDto")),("analysis",r("ApiContainerPackingAnalysisDto"))
    ])),
    ("ApiContainerPackingPdfSaveResponse",o(vec![
        ("success",b()),("filePath",s()),("sizeBytes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("message",s())
    ]).required(vec!["success","filePath","sizeBytes","message"])),
    ("ApiContainerPackingProjectDto",o(vec![
        ("container",r("ApiContainerDimensionsDto")),("rules",r("ApiContainerPackingRulesDto")),("cargoItems",a(r("ApiContainerPackingCargoInputDto"))),
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("containerType",s()),("createdAt",s().format("date-time")),("updatedAt",s().format("date-time"))
    ]).required(vec!["container","rules","cargoItems","id","versionNumber","name","containerType","createdAt","updatedAt"])),
    ("ApiContainerPackingProjectListResponse",o(vec![("projects",a(r("ApiContainerPackingProjectSummaryDto"))),("storagePolicy",s())]).required(vec!["projects","storagePolicy"])),
    ("ApiContainerPackingProjectResponse",o(vec![("project",r("ApiContainerPackingProjectDto")),("storagePolicy",s())]).required(vec!["project","storagePolicy"])),
    ("ApiContainerPackingProjectSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("containerType",s()),("container",r("ApiContainerDimensionsDto")),("rules",r("ApiContainerPackingRulesDto")),
        ("cargoItems",a(r("ApiContainerPackingCargoInputDto")))
    ])),
    ("ApiContainerPackingProjectSaveResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("project",r("ApiContainerPackingProjectDto")),
        ("message",s()),("storagePolicy",s())
    ]).required(vec!["success","id","project","message","storagePolicy"])),
    ("ApiContainerPackingProjectSummaryDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("containerType",s()),("createdAt",s().format("date-time")),("updatedAt",s().format("date-time"))
    ]).required(vec!["id","versionNumber","name","containerType","createdAt","updatedAt"])),
    ("ApiContainerPackingRulesDto",o(vec![
        ("allowRotation",b()),("usePalletConstraints",b()),("defaultPalletLength",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("defaultPalletWidth",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("defaultPalletHeight",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("defaultPalletWeight",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("enforceCenterOfGravity",b()),("centerOfGravityTolerancePercent",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("minimumSupportAreaPercent",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("requireSameFootprintStacking",b())
    ])),
    ("ApiContainerTypeDto",o(vec![
        ("versionNumber",i().minimum(num("1")).format("int64")),
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("length",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("width",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("height",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("maxVolume",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("maxWeight",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("isSystemDefault",b())
    ]).required(vec!["id","versionNumber","name","length","width","height","maxVolume","maxWeight","isSystemDefault"])),
    ("ApiContainerTypeListResponse",o(vec![("containerTypes",a(r("ApiContainerTypeDto"))),("storagePolicy",s())]).required(vec!["containerTypes","storagePolicy"])),
    ("ApiContainerTypeSaveRequest",o(vec![
        ("expectedVersion",i().minimum(num("1")).format("int64")),
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("length",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("width",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("height",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("maxVolume",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("maxWeight",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$"))
    ])),
    ("ApiContainerTypeSaveResponse",o(vec![
        ("success",b()),("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("containerType",r("ApiContainerTypeDto")),
        ("message",s()),("storagePolicy",s())
    ]).required(vec!["success","id","containerType","message","storagePolicy"])),
    ("ApiCustomOptionListResponse",o(vec![
        ("optionType",s()),("predefinedOptions",a(s())),("customOptions",a(s())),
        ("options",a(s())),("allowCustomValues",b()),("storagePolicy",s())
    ]).required(vec!["optionType","predefinedOptions","customOptions","options","allowCustomValues","storagePolicy"])),
    ("ApiCustomOptionSaveRequest",o(vec![("value",s())])),
    ("ApiDashboardResponse",o(vec![
        ("monthlyExportAmount",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("monthlyProfit",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("monthlyTaxRefund",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("pendingCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("shippedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("totalActiveCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("periodLabel",s()),("previousMonthlyExportAmount",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("previousMonthlyProfit",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("previousMonthlyTaxRefund",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("monthlyInvoiceCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("draftCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("verifiedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("completedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("singleWindowStatusSummary",s()),
        ("recentInvoices",a(r("ApiDashboardRecentInvoiceDto"))),("todoItems",a(r("ApiDashboardTodoItemDto"))),("storagePolicy",s()),
        ("exportAmountCurrency",s().description("Currency of the scalar export amounts. Empty for no invoices or mixed currencies; scalar amounts are then zero. Use exportAmountsByCurrency.")),("exportAmountsByCurrency",a(r("ApiDashboardCurrencyAmount")).description("Original-currency totals for the union of both months. Compare only amounts from the same entry; no exchange-rate conversion."))
    ]).required(vec!["monthlyExportAmount","monthlyProfit","monthlyTaxRefund","pendingCount","shippedCount","totalActiveCount","periodLabel","previousMonthlyExportAmount","previousMonthlyProfit","previousMonthlyTaxRefund","monthlyInvoiceCount","draftCount","verifiedCount","completedCount","singleWindowStatusSummary","recentInvoices","todoItems","storagePolicy"])),
    ("ApiDashboardTodoItemDto",o(vec![
        ("title",s()),("description",s()),("actionType",s()),
        ("referenceId",s())
    ]).required(vec!["title","description","actionType","referenceId"])),
    ("ApiDownloadTicket",o(vec![("token",s()),("downloadUrl",s()),("expiresAtUtc",s().format("date-time"))]).required(vec!["token","downloadUrl","expiresAtUtc"])),
    ("ApiEffectivePermissionGrantDto",o(vec![
        ("resourceKey",s()),("action",s()),("dataScope",s()),
        ("source",s()),("sourceResourceKey",s())
    ]).required(vec!["resourceKey","action","dataScope","source","sourceResourceKey"])),
    ("ApiEmailDeliveryDto",o(vec![
        ("deliveryId",s()),("jobId",s()),("kind",s()),
        ("recipient",s()),("subject",s()),("attachmentCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("status",s()),("errorMessage",s()),("createdAt",s().format("date-time")),
        ("sentAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("updatedAt",s().format("date-time"))
    ]).required(vec!["deliveryId","jobId","kind","recipient","subject","attachmentCount","status","errorMessage","createdAt","updatedAt"])),
    ("ApiEmailSendRequest",o(vec![
        ("toAddress",s()),("subject",s()),("body",s()),
        ("attachmentPaths",a(s()))
    ])),
    ("ApiEmailSendResponse",o(vec![
        ("success",b()),("message",s()),("toAddress",s()),
        ("subject",s()),("attachmentCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("storagePolicy",s())
    ]).required(vec!["success","message","toAddress","subject","attachmentCount","storagePolicy"])),
    ("ApiEmailServerSuggestionRequest",o(vec![("emailAddress",s())])),
    ("ApiEmailServerSuggestionResponse",o(vec![
        ("success",b()),("message",s()),("emailAddress",s()),
        ("smtpHost",s()),("smtpPort",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("enableSsl",b()),
        ("storagePolicy",s())
    ]).required(vec!["success","message","emailAddress","smtpHost","smtpPort","enableSsl","storagePolicy"])),
    ("ApiEmailStatusResponse",o(vec![
        ("isConfigured",b()),("smtpHost",s()),("smtpPort",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("enableSsl",b()),("fromAddress",s()),("fromDisplayName",s()),
        ("storagePolicy",s())
    ]).required(vec!["isConfigured","smtpHost","smtpPort","enableSsl","fromAddress","fromDisplayName","storagePolicy"])),
    ("ApiEmailTestResponse",o(vec![
        ("success",b()),("message",s()),("fromAddress",s()),
        ("smtpHost",s()),("storagePolicy",s())
    ]).required(vec!["success","message","fromAddress","smtpHost","storagePolicy"])),
    ("ApiErrorResponse",o(vec![("message",s()),("code",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String]))),("correlationId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])))]).required(vec!["message"])),
    ("ApiExcelConvertBookingSheetRequest",o(vec![("sourcePath",s()),("destinationPath",s())])),
    ("ApiExcelImportAnalysisIssueDto",o(vec![
        ("severity",s()),("code",s()),("message",s()),
        ("fieldKey",s())
    ]).required(vec!["severity","code","message","fieldKey"])),
    ("ApiExcelImportFieldAnalysisDto",o(vec![
        ("fieldKey",s()),("displayName",s()),("value",s()),
        ("worksheetName",s()),("row",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("column",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("confidence",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("source",s())
    ]).required(vec!["fieldKey","displayName","value","worksheetName","row","column","confidence","source"])),
    ("ApiExcelImportItemColumnAnalysisDto",object_groups(vec![
        (&["poNumberCol","styleNoCol","styleNameCol","fabricCompositionCol","styleNameCNCol","brandCol","hsCodeCol","originCol","quantityCol","unitENCol","unitCNCol","cartonsCol","ctnUnitENCol","lengthCol","widthCol","heightCol","dimensionCol","volumeCol","gwPerCtnCol","gwTotalCol","nwPerCtnCol","nwTotalCol","unitPriceCol","totalPriceCol"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
    ]).required(vec!["poNumberCol","styleNoCol","styleNameCol","fabricCompositionCol","styleNameCNCol","brandCol","hsCodeCol","originCol","quantityCol","unitENCol","unitCNCol","cartonsCol","ctnUnitENCol","lengthCol","widthCol","heightCol","dimensionCol","volumeCol","gwPerCtnCol","gwTotalCol","nwPerCtnCol","nwTotalCol","unitPriceCol","totalPriceCol"])),
    ("ApiExcelImportItemTableAnalysisDto",Schema::default().properties(BTreeMap::from([
        ("worksheetName",s()),("headerRow",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("headerDepth",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("dataStartRow",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("confidence",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("columns",r("ApiExcelImportItemColumnAnalysisDto"))
    ])).kind(Types::Many(vec![Primitive::Null,Primitive::Object])).required(vec!["worksheetName","headerRow","headerDepth","dataStartRow","confidence","columns"])),
    ("ApiExcelImportPreviewRequest",o(vec![("filePath",s()),("schemeName",s().max_length(100))]).required(vec!["filePath"])),
    ("ApiExcelImportPreviewResponse",o(vec![
        ("sourcePath",s()),("success",b()),("invoice",Schema::default().one_of(vec![ty(Primitive::Null),r("ApiInvoiceDetailDto")])),
        ("customer",Schema::default().one_of(vec![ty(Primitive::Null),r("ApiImportedCustomerDto")])),("exporter",Schema::default().one_of(vec![ty(Primitive::Null),r("ApiImportedExporterDto")])),("analysisReport",Schema::default().one_of(vec![ty(Primitive::Null),r("ApiExcelImportAnalysisReportDto")])),
        ("errors",a(s())),("storagePolicy",s())
    ]).required(vec!["sourcePath","success","errors","storagePolicy"])),
    ("ApiExcelImportSheetAnalysisDto",o(vec![
        ("name",s()),("usedRowCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("usedColumnCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("fieldCandidateCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("hasItemTable",b()),("confidence",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$"))
    ]).required(vec!["name","usedRowCount","usedColumnCount","fieldCandidateCount","hasItemTable","confidence"])),
    ("ApiExcelOutputRequest",o(vec![("destinationPath",s())])),
    ("ApiExchangeRateAvailableCurrenciesResponse",o(vec![
        ("currencies",a(s())),("sourceUrl",s()),("fetchedAt",s().format("date-time")),
        ("storagePolicy",s()),("isStale",b().default_value(serde_json::json!(false))),("lastError",s().default_value(serde_json::json!("")))
    ]).required(vec!["currencies","sourceUrl","fetchedAt","storagePolicy"])),
    ("ApiExchangeRateDto",o(vec![
        ("currencyName",s()),("buyingRate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("cashBuyingRate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("sellingRate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("cashSellingRate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("middleRate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("publishTime",s())
    ]).required(vec!["currencyName","publishTime"])),
    ("ApiExchangeRateListResponse",o(vec![
        ("rates",a(r("ApiExchangeRateDto"))),("sourceUrl",s()),("selectedCurrencies",a(s())),
        ("cacheDurationMinutes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("fetchedAt",s().format("date-time")),("statusText",s()),
        ("storagePolicy",s()),("autoRefreshEnabled",b().default_value(serde_json::json!(false))),("sourceFormat",s().default_value(serde_json::json!("BankOfChinaHtml")).enumeration(Enumeration::Values(vec![serde_json::json!("BankOfChinaHtml"),serde_json::json!("Icbc"),serde_json::json!("Cmb"),serde_json::json!("Json"),serde_json::json!("Auto")])).description("BankOfChinaHtml reads BOC HTML; Icbc and Cmb read the official public quotation endpoints. Json requires baseCurrency CNY, unit 100 and rates using ApiExchangeRateDto.")),
        ("isStale",b().default_value(serde_json::json!(false))),("lastError",s().default_value(serde_json::json!(""))),("availableCurrencies",a(s()).default_value(serde_json::json!([]))),
        ("missingCurrencies",a(s()).default_value(serde_json::json!([]))),("lastAttemptAt",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).default_value(serde_json::json!(null)).format("date-time")),("nextRefreshAt",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).default_value(serde_json::json!(null)).format("date-time"))
    ]).required(vec!["rates","sourceUrl","selectedCurrencies","cacheDurationMinutes","fetchedAt","statusText","storagePolicy"])),
    ("ApiHealthResponse",object_groups(vec![
        (&["status","productVersion","informationalVersion","appRoot","dataRoot","databaseRoot","singleWindowRoot","templateRoot","ocrModelRoot","logRoot","databaseProvider","databaseProviderKey","sqliteDatabasePath","storagePolicy"], s()),
        (&["checkedAt"], s().format("date-time")),
        (&["runtimePaths"], a(r("ApiRuntimePathInfo"))),
        (&["runtimeDependencies"], a(r("ApiRuntimeDependencyInfo"))),
    ]).required(vec!["status","checkedAt","productVersion","informationalVersion","appRoot","dataRoot","databaseRoot","singleWindowRoot","templateRoot","ocrModelRoot","logRoot","databaseProvider","databaseProviderKey","sqliteDatabasePath","runtimePaths","runtimeDependencies","storagePolicy"])),
    ("ApiLetterOfCreditImportRequest",super::schema_shapes::file_path_request()),
    ("ApiLetterOfCreditImportResponse",o(vec![
        ("sourcePath",s()),("sourceDescription",s()),("extractedText",s()),
        ("storagePolicy",s())
    ]).required(vec!["sourcePath","sourceDescription","extractedText","storagePolicy"])),
    ("ApiLetterOfCreditReviewRequest",o(vec![("invoice",r("ApiInvoiceDetailDto"))]).required(vec!["invoice"])),
    ("ApiLetterOfCreditReviewResponse",o(vec![
        ("reportText",s()),("contextSummary",s()),("letterOfCreditContentTruncated",b()),
        ("storagePolicy",s())
    ]).required(vec!["reportText","contextSummary","letterOfCreditContentTruncated","storagePolicy"])),
    ("ApiLicenseRegisterRequest",o(vec![("licenseKey",s())]).required(vec!["licenseKey"])),
    ("ApiLicenseRegisterResponse",o(vec![("success",b()),("message",s()),("status",r("ApiLicenseStatusResponse"))]).required(vec!["success","message","status"])),
    ("ApiLicenseStatusResponse",o(vec![
        ("isRegistered",b()),("isTrialExpired",b()),("trialDays",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("daysRemaining",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("machineId",s()),("message",s()),
        ("expireDate",s().format("date")),("licenseStoragePath",s()),("storagePolicy",s())
    ]).required(vec!["isRegistered","isTrialExpired","trialDays","daysRemaining","machineId","message","expireDate","licenseStoragePath","storagePolicy"])),
    ("ApiLoginRequest",o(vec![("username",s()),("password",s())]).required(vec!["username","password"])),
    ("ApiLoginResponse",o(vec![
        ("tokenType",s()),("accessToken",s()),("expiresAt",s().format("date-time")),
        ("user",r("ApiUserDto"))
    ]).required(vec!["tokenType","accessToken","expiresAt","user"])),
    ("ApiLogoutResponse",o(vec![("success",b())]).required(vec!["success"])),
    ("ApiModuleAccessDto",o(vec![("moduleKey",s()),("accessLevel",s())]).required(vec!["moduleKey","accessLevel"])),
    ("ApiOcrLineDto",o(vec![
        ("text",s()),("x",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("y",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("width",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("height",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["text","x","y","width","height"])),
    ("ApiOcrRecognizeImageRequest",super::schema_shapes::file_path_request()),
    ("ApiOcrRecognizeImageResponse",o(vec![
        ("sourcePath",s()),("fullText",s()),("lines",a(r("ApiOcrLineDto"))),
        ("storagePolicy",s())
    ]).required(vec!["sourcePath","fullText","lines","storagePolicy"])),
    ("ApiPackedCargoItemDto",object_groups(vec![
        (&["x","y","width","height","baseHeight","occupiedHeight","topHeight"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("float").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?(?:[eE][+-]?\\d+)?$")),
        (&["colorArgb","unitsRepresented","stackCount","loadCount"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["displayText","detailText","name","priorityGroup","preferredZone"], s()),
        (&["isRotated","isPalletized"], b()),
        (&["totalWeight"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
    ]).required(vec!["x","y","width","height","baseHeight","occupiedHeight","topHeight","colorArgb","unitsRepresented","stackCount","loadCount","displayText","detailText","isRotated","isPalletized","name","totalWeight","priorityGroup","preferredZone"])),
    ("ApiPagedResponseOfApiAuditLogDto",super::schema_shapes::paged_response("ApiAuditLogDto")),
    ("ApiPagedResponseOfApiEmailDeliveryDto",super::schema_shapes::paged_response("ApiEmailDeliveryDto")),
    ("ApiPagedResponseOfApiPortDto",super::schema_shapes::paged_response("ApiPortDto")),
    ("ApiPagedResponseOfApiProductDto",super::schema_shapes::paged_response("ApiProductDto")),
    ("ApiPagedResponseOfBackgroundJobSnapshot",super::schema_shapes::paged_response("BackgroundJobSnapshot")),
    ("ApiPdfMergeRequest",o(vec![("sourceFiles",a(s())),("destinationPath",s())])),
    ("ApiPermissionActionDefinitionDto",o(vec![
        ("key",s()),("name",s()),("description",s()),
        ("sortOrder",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("presetLevel",s()),("unitAssignable",b().read_only(true))
    ]).required(vec!["key","name","description","sortOrder","presetLevel"])),
    ("ApiPermissionGrantDto",o(vec![("resourceKey",s()),("action",s()),("dataScope",s())]).required(vec!["resourceKey","action","dataScope"])),
    ("ApiPermissionResourceDefinitionDto",o(vec![
        ("key",s()),("name",s()),("group",s()),
        ("workspace",s()),("moduleKey",s()),("sortOrder",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("isTechnical",b()),("supportsDataScope",b()),("actions",a(r("ApiPermissionActionDefinitionDto"))),
        ("unitAssignable",b().read_only(true)),("scopeDescription",s().read_only(true)),("workflowResource",b().read_only(true))
    ]).required(vec!["key","name","group","workspace","moduleKey","sortOrder","isTechnical","supportsDataScope","actions"])),
    ("ApiPortDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("nameEN",s()),("nameCN",s()),
        ("country",s()),("code",s()),("rowVersion",s().default_value(serde_json::json!("")))
    ]).required(vec!["id","nameEN","nameCN","country","code","rowVersion"])),
    ("ApiPostgreSqlMaintenanceStatusResponse",object_groups(vec![
        (&["postgreSqlSelected","postgreSqlConfigured","toolsReady"], b()),
        (&["host","database","username","backupRoot","toolBinRoot","pgDumpPath","pgRestorePath","psqlPath","storagePolicy"], s()),
        (&["port"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
    ]).required(vec!["postgreSqlSelected","postgreSqlConfigured","host","port","database","username","backupRoot","toolBinRoot","pgDumpPath","pgRestorePath","psqlPath","toolsReady","storagePolicy"])),
    ("ApiProductDto",object_groups(vec![
        (&["id"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["productCode","nameEN","nameCN","description","hsCode","elements","supervisionConditions","inspectionCategory","material","brand","origin","unitEN","unitCN","packageUnitEN","packageUnitCN"], s()),
        (&["taxRebateRate","length","width","height","gwPerCtn","nwPerCtn","pcsPerCtn","defaultPrice"], Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        (&["createdAt","updatedAt"], s().format("date-time")),
        (&["rowVersion"], s().default_value(serde_json::json!(""))),
    ]).required(vec!["id","productCode","nameEN","nameCN","description","hsCode","elements","supervisionConditions","inspectionCategory","taxRebateRate","material","brand","origin","unitEN","unitCN","length","width","height","gwPerCtn","nwPerCtn","pcsPerCtn","packageUnitEN","packageUnitCN","defaultPrice","createdAt","updatedAt","rowVersion"])),
    ("ApiRuntimeDependencyInfo",o(vec![
        ("key",s()),("label",s()),("requirement",s()),
        ("status",s()),("ready",b()),("resolvedPath",s()),
        ("message",s())
    ]).required(vec!["key","label","requirement","status","ready","resolvedPath","message"])),
    ("ApiRuntimePathInfo",o(vec![
        ("key",s()),("label",s()),("path",s()),
        ("storageClass",s()),("accessMode",s()),("requirement",s()),
        ("exists",b()),("description",s())
    ]).required(vec!["key","label","path","storageClass","accessMode","requirement","exists","description"])),
    ("ApiSensitiveOperationAuthorizationRequest",o(vec![("action",s()),("adminPassword",s())]).required(vec!["action","adminPassword"])),
    ("ApiSensitiveOperationAuthorizationResponse",o(vec![("action",s()),("ticket",s()),("expiresAtUtc",s().format("date-time"))]).required(vec!["action","ticket","expiresAtUtc"])),
    ("ApiSettingsResponse",o(vec![("settings",r("AppSettings")),("secrets",r("ApiSettingsSecretsDto")),("storagePolicy",s())]).required(vec!["settings","secrets","storagePolicy"])),
    ("ApiSettingsSaveRequest",o(vec![("settings",r("AppSettings")),("updateSecrets",b())]).required(vec!["settings","updateSecrets"])),
    ("ApiSettingsSaveResponse",o(vec![
        ("success",b()),("requiresRestart",b()),("settings",r("AppSettings")),
        ("secrets",r("ApiSettingsSecretsDto")),("message",s())
    ]).required(vec!["success","requiresRestart","settings","secrets","message"])),
    ("ApiSettingsSecretsDto",o(vec![
        ("emailPasswordSet",b()),("webDavPasswordSet",b()),("postgreSqlPasswordSet",b()),
        ("aiApiKeySet",b())
    ]).required(vec!["emailPasswordSet","webDavPasswordSet","postgreSqlPasswordSet","aiApiKeySet"])),
    ("ApiSettingsValidationMessageDto",o(vec![
        ("level",s()),("propertyName",s()),("message",s()),
        ("isAutoFixable",b())
    ]).required(vec!["level","propertyName","message","isAutoFixable"])),
    ("ApiSettingsValidationRequest",o(vec![("settings",r("AppSettings")),("updateSecrets",b())]).required(vec!["settings","updateSecrets"])),
    ("ApiSettingsValidationResponse",o(vec![
        ("isValid",b()),("hasWarnings",b()),("canAutoFix",b()),
        ("messages",a(r("ApiSettingsValidationMessageDto"))),("normalizedSettings",r("AppSettings")),("storagePolicy",s())
    ]).required(vec!["isValid","hasWarnings","canAutoFix","messages","normalizedSettings","storagePolicy"])),
    ("ApiSharedDatabaseOwnershipSummaryResponse",o(vec![
        ("totalInvoices",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("unassignedInvoices",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("totalPayments",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("unassignedPayments",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("totalOtherBusinessData",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("unassignedOtherBusinessData",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("owners",a(r("ApiSharedDatabaseOwnerSummaryItemDto"))),("storagePolicy",s())
    ]).required(vec!["totalInvoices","unassignedInvoices","totalPayments","unassignedPayments","totalOtherBusinessData","unassignedOtherBusinessData","owners","storagePolicy"])),
    ("ApiSharedDatabaseOwnershipTransferRequest",o(vec![
        ("fromUserId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("toUserId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("includeInvoices",b()),
        ("includePayments",b()),("includeOtherBusinessData",b()),("onlyUnassigned",b()),
        ("departmentId",s()),("companyScope",s()),("confirmationText",s())
    ]).required(vec!["fromUserId","toUserId","includeInvoices","includePayments","includeOtherBusinessData","onlyUnassigned","departmentId","companyScope","confirmationText"])),
    ("ApiSharedDatabaseOwnershipTransferResponse",o(vec![
        ("success",b()),("message",s()),("updatedInvoices",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("updatedPayments",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("updatedOtherBusinessData",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("storagePolicy",s())
    ]).required(vec!["success","message","updatedInvoices","updatedPayments","updatedOtherBusinessData","storagePolicy"])),
    ("ApiSharedDatabaseOwnerSummaryItemDto",o(vec![
        ("userId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("username",s()),("fullName",s()),
        ("role",s()),("departmentId",s()),("companyScope",s()),
        ("isActive",b()),("invoiceCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("paymentCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("otherBusinessDataCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["userId","username","fullName","role","departmentId","companyScope","isActive","invoiceCount","paymentCount","otherBusinessDataCount"])),
    ("ApiShippingMarkImagePreviewRequest",o(vec![("imagePath",s())])),
    ("ApiShippingMarkImagePreviewResponse",o(vec![
        ("imagePath",s()),("fileName",s()),("contentType",s()),
        ("sizeBytes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),("dataUrl",s()),("storagePolicy",s())
    ]).required(vec!["imagePath","fileName","contentType","sizeBytes","dataUrl","storagePolicy"])),
    ("ApiShippingMarkImageSaveRequest",o(vec![("imageDataUrl",s())])),
    ("ApiShippingMarkImageSaveResponse",o(vec![
        ("imagePath",s()),("fileName",s()),("contentType",s()),
        ("sizeBytes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),("storagePolicy",s())
    ]).required(vec!["imagePath","fileName","contentType","sizeBytes","storagePolicy"])),
    ("ApiShutdownMaintenanceResponse",o(vec![
        ("success",b()),("message",s()),("deletedAuditLogs",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("deletedTextLogs",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("uploadedBackupFileName",s()),("cloudSyncErrorMessage",s()),
        ("cloudSyncFailed",b()),("backupRoot",s()),("logRoot",s()),
        ("storagePolicy",s())
    ]).required(vec!["success","message","deletedAuditLogs","deletedTextLogs","uploadedBackupFileName","cloudSyncErrorMessage","cloudSyncFailed","backupRoot","logRoot","storagePolicy"])),
    ("ApiSupportPackageRequest",o(vec![("includeLatestDatabaseBackup",b()),("includeSampleFiles",b()),("confirmationText",s())]).required(vec!["includeLatestDatabaseBackup","includeSampleFiles","confirmationText"])),
    ("ApiSupportPackageResponse",o(vec![
        ("success",b()),("message",s()),("fileName",s()),
        ("fullPath",s()),("sizeBytes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),("supportPackageRoot",s()),
        ("storagePolicy",s())
    ]).required(vec!["success","message","fileName","fullPath","sizeBytes","supportPackageRoot","storagePolicy"])),
    ("ApiSystemLogCleanupResponse",o(vec![
        ("success",b()),("message",s()),("deletedAuditLogs",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("deletedTextLogs",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("deletedTextLogsByAge",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("deletedTextLogsByCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("logRoot",s()),("storagePolicy",s())
    ]).required(vec!["success","message","deletedAuditLogs","deletedTextLogs","deletedTextLogsByAge","deletedTextLogsByCount","logRoot","storagePolicy"])),
    ("ApiUserAccountDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("username",s()),("fullName",s()),
        ("role",s()),("permissionTemplateId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("permissionTemplateCode",s()),
        ("permissionTemplateName",s()),("departmentId",s()),("companyScope",s()),
        ("isActive",b()),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("1"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("permissionGrants",Schema::default().kind(Types::Many(vec![Primitive::Array,Primitive::Null])).items(Box::new(r("ApiPermissionGrantDto"))).default_value(serde_json::json!(null)).description("null inherits the explicitly selected scheme; an array replaces it, including an empty deny-all array.").max_items(1000)),
        ("disabledModules",a(s()).default_value(serde_json::json!([])).description("Disabled modules are removed from both effective permissions and navigation.").max_items(100)),("employee",Schema::default().any_of(vec![r("AccountPersonnelRecord"),ty(Primitive::Null)]).default_value(serde_json::json!(null)).description("Current personnel link; maintained through personnel workflows, never account save input.").read_only(true)),("oaAssignedOnly",b().default_value(serde_json::json!(false)).description("Restrict office requests to the user's own requests and explicitly assigned approval/handling work, within the ordinary data scopes.")),
        ("permissionSchemeVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::Null])).default_value(serde_json::json!(null)).minimum(num("1")).format("int64"))
    ]).required(vec!["id","username","fullName","role","versionNumber","permissionTemplateCode","permissionTemplateName","departmentId","companyScope","isActive"])),
    ("ApiUserCapabilitiesDto",o(vec![
        ("canManageSettings",b()),("canManageUsers",b()),("canViewAllBusinessData",b()),
        ("canUseDocumentWorkspace",b()),("canUseSalesWorkspace",b()),("productEdition",s()),
        ("enabledModules",a(s())),("moduleAccess",a(r("ApiModuleAccessDto"))),("permissions",a(r("ApiPermissionGrantDto"))),
        ("oaAssignedOnly",b().default_value(serde_json::json!(false)).description("Restrict office requests to the user's own requests and explicitly assigned approval/handling work, within the ordinary data scopes.")),("availableFeatures",a(s())),("unitManagement",r("UnitManagement"))
    ]).required(vec!["canManageSettings","canManageUsers","canViewAllBusinessData","canUseDocumentWorkspace","canUseSalesWorkspace","productEdition","enabledModules","moduleAccess","permissions","availableFeatures"])),
    ("ApiUserDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("username",s()),("fullName",s()),
        ("role",s()),("departmentId",s()),("companyScope",s()),
        ("isActive",b()),("businessDate",s().format("date")),("businessTimeZone",s()),
        ("businessDateValidUntilUtc",s().format("date-time")),("capabilities",r("ApiUserCapabilitiesDto")),("permissionSource",r("PermissionSource")),
        ("employee",Schema::default().any_of(vec![r("AccountPersonnelRecord"),ty(Primitive::Null)]).default_value(serde_json::json!(null)).description("Current personnel link; maintained through personnel workflows, never account save input.").read_only(true)),("effectiveDepartmentIds",a(s()).description("Current department data scope: appointments plus departments managed by the linked employee and their active descendants. Does not grant operations.").read_only(true)),("primaryCompanyScope",s().read_only(true)),
        ("contextId",s().read_only(true)),("unitContexts",a(r("UnitContext")).read_only(true)),("permissionSchemeVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::Null])).default_value(serde_json::json!(null)).minimum(num("1")).format("int64"))
    ]).required(vec!["id","username","fullName","role","departmentId","companyScope","isActive","businessDate","businessTimeZone","businessDateValidUntilUtc","capabilities"])),
    ("ApiUserListResponse",o(vec![
        ("users",a(r("ApiUserAccountDto"))),("roles",a(s())),("permissionTemplates",a(r("ApiPermissionTemplateOptionDto"))),
        ("companies",a(r("ApiOrganizationCompanyDto"))),("departments",a(r("ApiOrganizationDepartmentDto")))
    ]).required(vec!["users","roles","permissionTemplates","companies","departments"])),
    ("ApiUserSaveRequest",o(vec![
        ("username",s()),("fullName",s()),("role",s()),
        ("permissionTemplateId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("departmentId",s()),("companyScope",s()),
        ("isActive",b()),("resetPassword",s()),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("permissionGrants",Schema::default().kind(Types::Many(vec![Primitive::Array,Primitive::Null])).items(Box::new(r("ApiPermissionGrantDto"))).default_value(serde_json::json!(null)).description("null inherits the explicitly selected scheme; an array replaces it, including an empty deny-all array.").max_items(1000)),("disabledModules",a(s()).default_value(serde_json::json!([])).description("Disabled modules are removed from both effective permissions and navigation.").max_items(100)),("oaAssignedOnly",b().default_value(serde_json::json!(false)).description("Restrict office requests to the user's own requests and explicitly assigned approval/handling work, within the ordinary data scopes.")),
        ("permissionSchemeVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::Null])).default_value(serde_json::json!(null)).minimum(num("1")).format("int64"))
    ]).required(vec!["username","fullName","role","permissionTemplateId","departmentId","companyScope","isActive","resetPassword"])),
    ("ApiUserSaveResponse",o(vec![("success",b()),("message",s()),("user",r("ApiUserAccountDto"))]).required(vec!["success","message","user"])),
    ("AppSettings",o(vec![
        ("revision",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),("system",r("SystemSettings")),("reportTemplateDefaults",r("ReportTemplateDefaults")),
        ("batchExport",r("BatchExportSettings")),("paymentTemplates",a(r("PaymentTemplateItem"))),("excelImport",r("ExcelImportSettings")),
        ("excelImportSchemes",a(r("ExcelImportSettings"))),("exchangeRate",r("ExchangeRateSettings")),("email",r("EmailConfig")),
        ("webDav",r("WebDavSettings")),("ai",r("AISettings")),("singleWindow",r("SingleWindowSettings"))
    ]).required(vec!["revision","system","reportTemplateDefaults","batchExport","paymentTemplates","excelImport","excelImportSchemes","exchangeRate","email","webDav","ai","singleWindow"])),
    ("BackgroundJobSnapshot",o(vec![
        ("jobId",s()),("kind",s()),("title",s()),
        ("status",s()),("progressPercent",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("statusText",s()),
        ("detailText",s()),("requestedBy",s()),("requestedByUserId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("createdAt",s().format("date-time")),("startedAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("completedAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        ("updatedAt",s().format("date-time")),("outputPath",s()),("errorMessage",s()),
        ("canCancel",b()),("canRetry",b()),("retryOperation",s()),
        ("retryRequestJson",s())
    ]).required(vec!["jobId","kind","title","status","statusText","detailText","requestedBy","requestedByUserId","createdAt","updatedAt","outputPath","errorMessage","canCancel","canRetry","retryOperation","retryRequestJson"])),
    ("BatchExportItem",o(vec![
        ("showSeal",b()),("name",s()),("templatePath",s()),
        ("isEnabled",b()),("reportType",s())
    ]).required(vec!["showSeal","name","templatePath","isEnabled","reportType"])),
    ("BatchExportSettings",o(vec![
        ("items",a(r("BatchExportItem"))),("outputFileNamePattern",s().description("支持占位符：{InvoiceNo} {Customer} {DocType} {Date}")),("outputFolderPattern",s().description("支持占位符：{InvoiceNo} {Customer} {Date}")),
        ("mergePdf",b()),("zipAfterExport",b())
    ]).required(vec!["items","outputFileNamePattern","outputFolderPattern","mergePdf","zipAfterExport"])),
    ("BusinessAttachmentCategoryCatalog",o(vec![("companyScope",s()),("items",a(r("BusinessAttachmentCategoryRecord"))),("canManage",b())]).required(vec!["companyScope","items","canManage"])),
    ("BusinessAttachmentCategoryCreate",o(vec![("companyScope",s()),("name",s())]).required(vec!["companyScope","name"])),
    ("BusinessAttachmentCategoryRecord",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("attachmentCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","name","versionNumber","attachmentCount"])),
    ("BusinessAttachmentCategoryUpdate",o(vec![("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s())]).required(vec!["expectedVersion","name"])),
    ("BusinessAttachmentDelete",o(vec![("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("note",s())]).required(vec!["expectedVersion","note"])),
    ("BusinessAttachmentDetails",o(vec![
        ("attachment",r("BusinessAttachmentRecord")),("revisions",a(r("BusinessAttachmentRevisionRecord"))),("events",a(r("BusinessAttachmentEventRecord"))),
        ("eventCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["attachment","revisions","events","eventCount"])),
    ("BusinessAttachmentEventRecord",o(vec![
        ("action",s()),("revision",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("actorName",s()),
        ("note",s()),("createdAt",s().format("date-time"))
    ]).required(vec!["action","actorName","note","createdAt"])),
    ("BusinessAttachmentMetadataUpdate",o(vec![
        ("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("title",s()),("categoryId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("poNumber",s()),("styleNo",s()),("note",s())
    ]).required(vec!["expectedVersion","title","categoryId","poNumber","styleNo","note"])),
    ("BusinessAttachmentPage",o(vec![
        ("page",r("PagedResultOfBusinessAttachmentRecord")),("canUpload",b()),("usedBytes",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("fileBytesLimit",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceBytesLimit",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int64").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["page","canUpload","fileBytesLimit","invoiceBytesLimit"])),
    ("BusinessAttachmentRecord",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("invoiceNo",s()),
        ("invoiceType",s()),("customerName",s()),("title",s()),
        ("categoryId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("categoryName",s()),("poNumber",s()),
        ("styleNo",s()),("latestRevision",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("currentRevision",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("isArchived",b()),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("updatedAt",s().format("date-time")),
        ("canEdit",b()),("canDelete",b())
    ]).required(vec!["id","invoiceId","invoiceNo","invoiceType","customerName","title","categoryId","categoryName","poNumber","styleNo","latestRevision","isArchived","versionNumber","updatedAt","canEdit","canDelete"])),
    ("BusinessAttachmentRevisionRecord",o(vec![
        ("revision",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("fileName",s()),("contentType",s()),
        ("length",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("sha256",s()),("uploadedBy",s()),
        ("note",s()),("createdAt",s().format("date-time"))
    ]).required(vec!["revision","fileName","contentType","length","sha256","uploadedBy","note","createdAt"])),
    ("BusinessAttachmentUpdate",o(vec![
        ("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("currentRevision",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("isArchived",b()),
        ("note",s())
    ]).required(vec!["expectedVersion","currentRevision","isArchived","note"])),
    ("DeleteRecordRequest",o(vec![("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("reason",s())]).required(vec!["expectedVersion","reason"])),
    ("EmailConfig",o(vec![
        ("smtpHost",s().description("邮件服务器地址，例如 smtp.qq.com")),("smtpPort",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("邮件服务器端口，通常为 587 (TLS) 或 465 (SSL)")),("userName",s().description("登录邮箱的用户名或完整邮箱地址")),
        ("password",s().description("邮箱密码或应用授权码 (推荐使用授权码)")),("enableSsl",b().description("是否启用安全连接 (SSL/TLS)")),("fromAddress",s().description("邮件显示的发送方地址")),
        ("fromDisplayName",s().description("邮件显示的发送方名称，例如 '单证部'")),("documentEmailSubjectTemplate",s().description("从发票页发送单据附件时的默认主题，支持占位符：{InvoiceNo} {Customer} {Date}")),("documentEmailBodyTemplate",s().description("从发票页发送单据附件时的默认正文，支持占位符：{InvoiceNo} {Customer} {Date}")),
        ("recipientAllowList",s().description("可选；每行填写一个完整邮箱或域名。填写后只允许向名单内收件人发送，域名规则同时包含其子域名。")),("recipientBlockList",s().description("可选；每行填写一个完整邮箱或域名。黑名单优先于白名单，域名规则同时包含其子域名。"))
    ]).required(vec!["smtpHost","smtpPort","userName","password","enableSsl","fromAddress","fromDisplayName","documentEmailSubjectTemplate","documentEmailBodyTemplate","recipientAllowList","recipientBlockList"])),
    ("EmploymentStatus",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("Probation"),serde_json::json!("Active"),serde_json::json!("Departed")]))),
    ("EmploymentType",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("FullTime"),serde_json::json!("PartTime"),serde_json::json!("Intern"),serde_json::json!("Contractor")]))),
    ("ExcelImportSettings",o(vec![("sheetName",s().min_length(1).max_length(31)),
        ("schemeName",s()),("exporterNameCNCell",s().description("出口商中文名称所在的Excel单元格位置，例如 A1")),("exporterNameCell",s().description("出口商英文名称所在的Excel单元格位置，例如 B3")),
        ("exporterAddressStartCell",s().description("出口商地址的起始单元格位置，通常包含多行，例如 B4")),("exporterAddressLineCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("出口商地址占用的行数")),("creditCodeCell",s().description("统一社会信用代码所在的Excel单元格位置，例如 O4")),
        ("customerNameCell",s().description("客户名称所在的Excel单元格位置，例如 B8")),("customerAddressStartCell",s().description("客户地址的起始单元格位置，通常包含多行，例如 B9")),("customerAddressLineCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("客户地址占用的行数")),
        ("notifyPartyNameCell",s().description("通知方名称所在的Excel单元格位置，例如 B13")),("notifyPartyAddressStartCell",s().description("通知方地址的起始单元格位置，通常包含多行，例如 B14")),("notifyPartyAddressLineCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("通知方地址占用的行数")),
        ("invoiceDateCell",s().description("发票日期所在的Excel单元格位置，例如 O3")),("contractNoCell",s().description("合同号所在的Excel单元格位置，例如 O5")),("issuingBankCell",s().description("开证行所在的Excel单元格位置，例如 O7")),
        ("currencyCell",s().description("币种所在的Excel单元格位置，例如 O8")),("invoiceNoCell",s().description("发票号所在的Excel单元格位置，例如 O9")),("supervisionModeCell",s().description("监管方式所在的Excel单元格位置，例如 O10")),
        ("letterOfCreditNoCell",s().description("信用证号所在的Excel单元格位置，例如 O6")),("paymentTermsCell",s().description("付款方式所在的Excel单元格位置，例如 O11")),("transportModeCell",s().description("运输方式所在的Excel单元格位置，例如 O12")),
        ("tradeTermsCell",s().description("贸易条款所在的Excel单元格位置，例如 O14")),("portOfLoadingCell",s().description("起运港所在的Excel单元格位置，例如 O15")),("portOfDestinationCell",s().description("目的港所在的Excel单元格位置，例如 O16")),
        ("destinationCountryCell",s().description("目的国所在的Excel单元格位置，例如 O17")),("shippingMarksCell",s().description("唛头所在的Excel单元格位置，例如 A20")),("itemsStartRow",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("商品明细数据开始的Excel行号，例如 20")),
        ("itemsEndRow",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("商品明细数据结束的Excel行号，0表示自动判断")),("poNumberCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("订单号所在的列号 (1表示A列, 2表示B列...)")),("styleNoCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("款号所在的列号")),
        ("styleNameCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("品名英文所在的列号")),("fabricCompositionCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("面料成分所在的列号")),("styleNameCNCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("品名中文所在的列号")),
        ("brandCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("品牌所在的列号")),("hsCodeCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("HS编码所在的列号")),("originCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("原产地所在的列号")),
        ("quantityCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("数量所在的列号")),("unitENCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("单位英文所在的列号")),("unitCNCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("单位中文所在的列号")),
        ("cartonsCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("箱数所在的列号")),("ctnUnitENCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("箱数单位英文所在的列号")),("lengthCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("长度所在的列号")),
        ("widthCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("宽度所在的列号")),("heightCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("高度所在的列号")),("volumeCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("体积所在的列号")),
        ("gwPerCtnCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("每箱毛重所在的列号")),("gwTotalCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("总毛重所在的列号")),("nwPerCtnCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("每箱净重所在的列号")),
        ("nwTotalCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("总净重所在的列号")),("unitPriceCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("单价所在的列号")),("totalPriceCol",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("总价所在的列号"))
    ]).required(vec!["sheetName","schemeName","exporterNameCNCell","exporterNameCell","exporterAddressStartCell","exporterAddressLineCount","creditCodeCell","customerNameCell","customerAddressStartCell","customerAddressLineCount","notifyPartyNameCell","notifyPartyAddressStartCell","notifyPartyAddressLineCount","invoiceDateCell","contractNoCell","issuingBankCell","currencyCell","invoiceNoCell","supervisionModeCell","letterOfCreditNoCell","paymentTermsCell","transportModeCell","tradeTermsCell","portOfLoadingCell","portOfDestinationCell","destinationCountryCell","shippingMarksCell","itemsStartRow","itemsEndRow","poNumberCol","styleNoCol","styleNameCol","fabricCompositionCol","styleNameCNCol","brandCol","hsCodeCol","originCol","quantityCol","unitENCol","unitCNCol","cartonsCol","ctnUnitENCol","lengthCol","widthCol","heightCol","volumeCol","gwPerCtnCol","gwTotalCol","nwPerCtnCol","nwTotalCol","unitPriceCol","totalPriceCol"])),
    ("ExchangeRateSettings",o(vec![
        ("url",s().description("获取汇率的网页地址 (默认中国银行)")),("cacheDurationMinutes",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$").description("汇率数据的缓存时间，在此时间内不会重复请求")),("selectedCurrencies",a(s())),
        ("allSupportedCurrencies",a(s())),("lastCurrencyListUpdateTime",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("autoRefreshEnabled",b().default_value(serde_json::json!(false))),
        ("sourceFormat",s().default_value(serde_json::json!("BankOfChinaHtml")).enumeration(Enumeration::Values(vec![serde_json::json!("BankOfChinaHtml"),serde_json::json!("Icbc"),serde_json::json!("Cmb"),serde_json::json!("Json"),serde_json::json!("Auto")])).description("BankOfChinaHtml reads BOC HTML; Icbc and Cmb read the official public quotation endpoints. Json requires baseCurrency CNY, unit 100 and rates using ApiExchangeRateDto."))
    ]).required(vec!["url","cacheDurationMinutes","selectedCurrencies","allSupportedCurrencies"])),
    ("IFormFile",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("binary")),
    ("IFormFileCollection",a(r("IFormFile"))),
    ("MeetingBookingCreateRequest",o(vec![
        ("requestKey",s().format("uuid")),("meetingRoomId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("title",s()),
        ("attendeeCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("startsAt",s().format("date-time")),("endsAt",s().format("date-time")),
        ("employeeId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("submitImmediately",b().default_value(serde_json::json!(true))),("departmentId",s().max_length(50)),
        ("appointmentId",s().max_length(100)),("projectId",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).pattern("^[1-9][0-9]*$"))
    ]).required(vec!["requestKey","meetingRoomId","title","attendeeCount","startsAt","endsAt"])),
    ("MeetingBookingRecord",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("meetingRoomId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("roomName",s()),
        ("location",s()),("requiresKey",b()),("ownerUserId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("applicantName",s()),("departmentId",s()),("title",s()),
        ("attendeeCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("startsAt",s().format("date-time")),("endsAt",s().format("date-time")),
        ("status",r("MeetingBookingStatus")),("issuedAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("returnedAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        ("createdAt",s().format("date-time")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("approvalPlan",r("OaApprovalPlan")),
        ("canReview",b()),("reviewTaskKeys",a(s())),("handlingKey",s()),
        ("handlingName",s()),("handlerNames",s()),("canHandle",b()),
        ("attachments",a(r("OaAttachment")).read_only(true)),("submissionId",i().format("int64").read_only(true)),("submissionNumber",i().format("int64").read_only(true)),
        ("approvalDocument",r("ResourceSubmission")),("companyScope",s().read_only(true)),("applicantAppointmentId",s().read_only(true)),
        ("applicantJobTitle",s().read_only(true)),("workflowActions",a(s().enumeration(Enumeration::Values(vec![serde_json::json!("AddBefore"),serde_json::json!("AddAfter"),serde_json::json!("AddParallel"),serde_json::json!("RemoveSigner"),serde_json::json!("ReturnToNode"),serde_json::json!("Suspend"),serde_json::json!("Resume"),serde_json::json!("Terminate"),serde_json::json!("RetryNode"),serde_json::json!("CancelSubflow"),serde_json::json!("RetrySubflow"),serde_json::json!("CompensateSubflow")]))).unique_items(true)),("workflowTasks",a(r("OaFulfillmentTask")).max_items(32)),
        ("fulfillmentState",s().enumeration(Enumeration::Values(vec![serde_json::json!("Pending"),serde_json::json!("Active"),serde_json::json!("Completed"),serde_json::json!("Blocked"),serde_json::json!("Suspended"),serde_json::json!("Cancelled")]))),("projectId",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).pattern("^[1-9][0-9]*$")),("projectName",s().read_only(true)),
        ("projectCode",s().read_only(true)),("employeeId",i().format("int64").description("Stable employee reference, independent of the applicant name.").read_only(true)),("employeeNumber",s().description("Employee number for disambiguation; relationships and authorization use the stable employee ID. Request records retain the number captured when created.").read_only(true)),
        ("materialAccess",r("OaMaterialAccess"))
    ]).required(vec!["id","meetingRoomId","ownerUserId","applicantName","departmentId","status","createdAt","versionNumber","companyScope"])),
    ("MeetingBookingStatus",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("Pending"),serde_json::json!("Approved"),serde_json::json!("InUse"),serde_json::json!("Completed"),serde_json::json!("Rejected"),serde_json::json!("Cancelled"),serde_json::json!("Draft"),serde_json::json!("Suspended"),serde_json::json!("Terminated")]))),
    ("MeetingBookingUpdateRequest",o(vec![
        ("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("title",s()),("attendeeCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("startsAt",s().format("date-time")),("endsAt",s().format("date-time")),("projectId",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).pattern("^[1-9][0-9]*$"))
    ]).required(vec!["expectedVersion","title","attendeeCount","startsAt","endsAt"])),
    ("MeetingBusySlot",o(vec![("startsAt",s().format("date-time")),("endsAt",s().format("date-time")),("status",r("MeetingBookingStatus"))]).required(vec!["startsAt","endsAt","status"])),
    ("MeetingRoomRecord",object_groups(vec![
        (&["id","capacity","maximumBookingHours","advanceBookingDays","versionNumber"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["name","location","equipment","handlingKey","handlingName","handlerNames"], s()),
        (&["requiresKey","isActive","inUse","canHandle"], b()),
    ]).required(vec!["id","name","location","equipment","capacity","maximumBookingHours","advanceBookingDays","requiresKey","isActive","inUse","versionNumber"])),
    ("MeetingRoomSaveRequest",o(vec![
        ("name",s()),("location",s()),("equipment",s()),
        ("capacity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("maximumBookingHours",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("advanceBookingDays",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("requiresKey",b()),("isActive",b()),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("handlingKey",s())
    ]).required(vec!["name","location","equipment","capacity","maximumBookingHours","advanceBookingDays","requiresKey","isActive","expectedVersion"])),
    ("NotifyPartyMode",Schema::default().enumeration(Enumeration::Values(vec![serde_json::json!("None"),serde_json::json!("SameAsConsignee"),serde_json::json!("Separate")]))),
    ("OfficeDecisionRequest",o(vec![
        ("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("note",s().default_value(serde_json::json!(""))),("taskKey",s()),
        ("commandKey",s().pattern("^[A-Za-z0-9_:-]+$").min_length(8).max_length(128)),("nodeKey",s()),("decision",s().default_value(serde_json::json!("Approve")).enumeration(Enumeration::Values(vec![serde_json::json!("Approve"),serde_json::json!("Reject"),serde_json::json!("Return")]))),
        ("verificationValues",ty(Primitive::Object).additional(Additional::Schema(Box::new(Schema::default().one_of(vec![s().max_length(1000),b()])))).default_value(serde_json::json!({})).max_properties(20)),("confirmedReviewPoints",a(i().minimum(num("0")).maximum(num("11"))).default_value(serde_json::json!([])).max_items(12).unique_items(true))
    ]).required(vec!["expectedVersion","commandKey"])),
    ("OfficeRequestEventRecord",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("action",s()),("quantity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("actorName",s()),("note",s()),("createdAt",s().format("date-time")),
        ("approvalGeneration",i().format("int64")),("definitionVersion",i().format("int64")),("definitionKey",s())
    ]).required(vec!["id","action","quantity","actorName","note","createdAt"])),
    ("OfficeReturnRequest",o(vec![
        ("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("quantity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("note",s().default_value(serde_json::json!(""))),
        ("commandKey",s().pattern("^[A-Za-z0-9_:-]+$").min_length(8).max_length(128)),("nodeKey",s()),("decision",s().default_value(serde_json::json!("Approve")).enumeration(Enumeration::Values(vec![serde_json::json!("Approve"),serde_json::json!("Reject"),serde_json::json!("Return")])))
    ]).required(vec!["expectedVersion","quantity","commandKey"])),
    ("OfficeStockMovementRecord",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("kind",s()),("quantityDelta",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("stockAfter",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("actorName",s()),("note",s()),
        ("createdAt",s().format("date-time"))
    ]).required(vec!["id","kind","quantityDelta","stockAfter","actorName","note","createdAt"])),
    ("OfficeStockRequest",o(vec![
        ("operationId",s().format("uuid")),("quantity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("note",s())
    ]).required(vec!["operationId","quantity","expectedVersion","note"])),
    ("OfficeSupplyRecord",object_groups(vec![
        (&["id","stockQuantity","reservedQuantity","minimumStock","versionNumber","availableQuantity"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["name","unit","location","description","handlingKey","handlingName","handlerNames"], s()),
        (&["isReturnable","isActive","lowStock","canHandle"], b()),
    ]).required(vec!["id","name","unit","location","description","isReturnable","isActive","stockQuantity","reservedQuantity","minimumStock","versionNumber","availableQuantity","lowStock"])),
    ("OfficeSupplyRequestRecord",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("officeSupplyId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("supplyName",s()),
        ("unit",s()),("isReturnable",b()),("ownerUserId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("applicantName",s()),("departmentId",s()),("purpose",s()),
        ("quantity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("returnedQuantity",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("returnDueDate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),
        ("status",r("SupplyRequestStatus")),("createdAt",s().format("date-time")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("approvalPlan",r("OaApprovalPlan")),("canReview",b()),("reviewTaskKeys",a(s())),
        ("handlingKey",s()),("handlingName",s()),("handlerNames",s()),
        ("canHandle",b()),("attachments",a(r("OaAttachment")).read_only(true)),("submissionId",i().format("int64").read_only(true)),
        ("submissionNumber",i().format("int64").read_only(true)),("approvalDocument",r("ResourceSubmission")),("companyScope",s().read_only(true)),
        ("applicantAppointmentId",s().read_only(true)),("applicantJobTitle",s().read_only(true)),("reservationState",s().enumeration(Enumeration::Values(vec![serde_json::json!("Unreserved"),serde_json::json!("Reserved"),serde_json::json!("Insufficient"),serde_json::json!("Consumed")])).read_only(true)),
        ("workflowActions",a(s().enumeration(Enumeration::Values(vec![serde_json::json!("AddBefore"),serde_json::json!("AddAfter"),serde_json::json!("AddParallel"),serde_json::json!("RemoveSigner"),serde_json::json!("ReturnToNode"),serde_json::json!("Suspend"),serde_json::json!("Resume"),serde_json::json!("Terminate"),serde_json::json!("RetryNode"),serde_json::json!("CancelSubflow"),serde_json::json!("RetrySubflow"),serde_json::json!("CompensateSubflow")]))).unique_items(true)),("workflowTasks",a(r("OaFulfillmentTask")).max_items(32)),("fulfillmentState",s().enumeration(Enumeration::Values(vec![serde_json::json!("Pending"),serde_json::json!("Active"),serde_json::json!("Completed"),serde_json::json!("Blocked"),serde_json::json!("Suspended"),serde_json::json!("Cancelled")]))),
        ("projectId",Schema::default().kind(Types::Many(vec![Primitive::String,Primitive::Null])).pattern("^[1-9][0-9]*$")),("projectName",s().read_only(true)),("projectCode",s().read_only(true)),
        ("employeeId",i().format("int64").description("Stable employee reference, independent of the applicant name.").read_only(true)),("employeeNumber",s().description("Employee number for disambiguation; relationships and authorization use the stable employee ID. Request records retain the number captured when created.").read_only(true)),("materialAccess",r("OaMaterialAccess"))
    ]).required(vec!["id","officeSupplyId","ownerUserId","applicantName","departmentId","status","createdAt","versionNumber","companyScope"])),
    ("OfficeSupplySaveRequest",o(vec![
        ("name",s()),("unit",s()),("location",s()),
        ("description",s()),("isReturnable",b()),("isActive",b()),
        ("minimumStock",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("handlingKey",s())
    ]).required(vec!["name","unit","location","description","isReturnable","isActive","minimumStock","expectedVersion"])),
    ("PagedResultOfBusinessAttachmentRecord",super::schema_shapes::paged_response("BusinessAttachmentRecord")),
    ("PagedResultOfMeetingBookingRecord",super::schema_shapes::paged_response("MeetingBookingRecord")),
    ("PagedResultOfMeetingRoomRecord",super::schema_shapes::paged_response("MeetingRoomRecord")),
    ("PagedResultOfOfficeRequestEventRecord",super::schema_shapes::paged_response("OfficeRequestEventRecord")),
] }
