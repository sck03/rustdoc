//! Native declarations. Change these typed definitions, then regenerate both clients.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<(&'static str,Schema)> { vec![
    ("ApiCrmContactDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("title",s()),("email",s()),("phone",s()),
        ("instantMessaging",s()),("isPrimary",b()),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","crmCustomerId","name","title","email","phone","instantMessaging","isPrimary","versionNumber"])),
    ("ApiCrmContactSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("title",s()),("email",s()),("phone",s()),
        ("instantMessaging",s()),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","crmCustomerId","name","title","email","phone","instantMessaging"])),
    ("ApiCrmDashboardDto",o(vec![
        ("customerCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("contactCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("pendingFollowUpCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("overdueFollowUpCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("dueNextSevenDaysCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("upcomingFollowUps",a(r("ApiCrmFollowUpDto"))),
        ("opportunityStages",a(r("ApiSalesOpportunityStageSummaryDto"))),("opportunityCurrencies",a(r("ApiSalesOpportunityCurrencySummaryDto"))),("upcomingOpportunityClosings",a(r("ApiSalesOpportunityDto")))
    ]).required(vec!["customerCount","contactCount","pendingFollowUpCount","overdueFollowUpCount","dueNextSevenDaysCount","upcomingFollowUps","opportunityStages","opportunityCurrencies","upcomingOpportunityClosings"])),
    ("ApiCrmEmailVariableDraftDto",o(vec![
        ("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmContactId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("toAddress",s()),
        ("variables",ty(Primitive::Object).additional(Additional::Schema(Box::new(s()))))
    ]).required(vec!["crmCustomerId","toAddress","variables"])),
    ("ApiCrmFollowUpDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("customerName",s()),
        ("crmContactId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("contactName",s()),("type",s()),
        ("summary",s()),("nextAction",s()),("followedUpAt",s().format("date-time")),
        ("nextFollowUpAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("isCompleted",b()),("createdAt",s().format("date-time")),
        ("updatedAt",s().format("date-time")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","crmCustomerId","customerName","contactName","type","summary","nextAction","followedUpAt","isCompleted","createdAt","updatedAt","versionNumber"])),
    ("ApiCrmFollowUpSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmContactId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("type",s().default_value(serde_json::json!(""))),("summary",s().default_value(serde_json::json!(""))),("nextAction",s().default_value(serde_json::json!(""))),
        ("followedUpAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("nextFollowUpAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","crmCustomerId"])),
    ("ApiCrmFollowUpTransferRequest",o(vec![("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmContactId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["crmCustomerId","crmContactId","expectedVersion"])),
    ("ApiCrmLifecycleRequest",super::schema_shapes::lifecycle_request()),
    ("ApiPagedResponseOfApiCrmContactDto",super::schema_shapes::paged_response("ApiCrmContactDto")),
    ("ApiPagedResponseOfApiCrmFollowUpDto",super::schema_shapes::paged_response("ApiCrmFollowUpDto")),
    ("ApiPagedResponseOfApiSalesOpportunityDto",super::schema_shapes::paged_response("ApiSalesOpportunityDto")),
    ("ApiPagedResponseOfApiSupplierContactDto",super::schema_shapes::paged_response("ApiSupplierContactDto")),
    ("ApiPagedResponseOfApiSupplierAssessmentDto",super::schema_shapes::paged_response("ApiSupplierAssessmentDto")),
    ("ApiSupplierAssessmentAnalyticsDto",supplier_assessment_analytics()),
    ("ApiPagedResponseOfApiSupplierDto",super::schema_shapes::paged_response("ApiSupplierDto")),
    ("ApiPagedResponseOfApiSupplierProductLinkDto",super::schema_shapes::paged_response("ApiSupplierProductLinkDto")),
    ("ApiPagedResponseOfApiSupplierProductOptionDto",super::schema_shapes::paged_response("ApiSupplierProductOptionDto")),
    ("ApiSalesOpportunityCurrencySummaryDto",o(vec![
        ("currency",s()),("count",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("estimatedAmount",s().format("decimal").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("weightedAmount",s().format("decimal").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$"))
    ]).required(vec!["currency","count","estimatedAmount","weightedAmount"])),
    ("ApiSalesOpportunityDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("customerName",s()),
        ("productId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("productCode",s()),("productName",s()),
        ("title",s()),("stage",s()),("isArchived",b()),("quotationNo",s()),
        ("estimatedAmount",s().format("decimal").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("currency",s()),("probabilityPercent",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("expectedCloseDate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),("nextAction",s()),("notes",s()),
        ("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("allowedNextStages",a(s()))
    ]).required(vec!["id","crmCustomerId","customerName","productCode","productName","title","stage","isArchived","quotationNo","estimatedAmount","currency","probabilityPercent","nextAction","notes","versionNumber","allowedNextStages"])),
    ("ApiSalesOpportunityHistoryPage",o(vec![
        ("items",a(r("ApiSalesOpportunityHistoryDto")).max_items(200)),
        ("nextCursor",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer])).format("int64"))
    ]).required(vec!["items","nextCursor"])),
    ("ApiSalesOpportunityHistoryDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("salesOpportunityId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("changeType",s()),("stage",s()),("quotationNo",s()),
        ("estimatedAmount",s().format("decimal").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("currency",s()),("probabilityPercent",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("expectedCloseDate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),("changeNote",s()),("changedBy",s()),
        ("createdAt",s().format("date-time"))
    ]).required(vec!["id","salesOpportunityId","versionNumber","changeType","stage","quotationNo","estimatedAmount","currency","probabilityPercent","changeNote","changedBy","createdAt"])),
    ("ApiSalesOpportunityLifecycleRequest",super::schema_shapes::lifecycle_request()),
    ("ApiSalesOpportunitySaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("crmCustomerId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("productId",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("title",s().default_value(serde_json::json!(""))),("quotationNo",s().default_value(serde_json::json!(""))),("estimatedAmount",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).default_value(serde_json::json!(num("0"))).format("decimal").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("currency",s().default_value(serde_json::json!("USD"))),("probabilityPercent",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("expectedCloseDate",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date")),
        ("nextAction",s().default_value(serde_json::json!(""))),("notes",s().default_value(serde_json::json!(""))),("changeNote",s().default_value(serde_json::json!(""))),
        ("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","crmCustomerId"])),
    ("ApiSalesOpportunityStageSummaryDto",o(vec![("stage",s()),("count",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["stage","count"])),
    ("ApiSalesOpportunityTransitionRequest",o(vec![("nextStage",s()),("changeNote",s()),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["nextStage","changeNote","expectedVersion"])),
    ("ApiSupplierAssessmentDto",o(vec![
        ("id",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("supplierCompanyId",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("assessmentDate",s().format("date")),
        ("assessmentKind",s().max_length(20)),("qualityScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("deliveryScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("serviceScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("priceScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("averageScore",Schema::default().kind(Types::One(Primitive::Number)).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("conclusion",s().max_length(20)),("notes",s().max_length(1000)),("assessedBy",s().max_length(100)),
        ("status",s().max_length(20)),("confirmedBy",s().max_length(100)),("confirmedAt",Schema::default().kind(Types::Many(vec![Primitive::Null,Primitive::String])).format("date-time")),
        ("createdAt",s().format("date-time")),("updatedAt",s().format("date-time")),("versionNumber",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","supplierCompanyId","assessmentDate","assessmentKind","qualityScore","deliveryScore","serviceScore","priceScore","averageScore","conclusion","notes","assessedBy","status","confirmedBy","createdAt","updatedAt","versionNumber"])),
    ("ApiSupplierAssessmentOverviewDto",o(vec![("totalCount",i()),("pageNumber",i()),("pageSize",i()),
        ("totalSuppliers",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("assessedSuppliers",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("unassessedSuppliers",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("preferredCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("qualifiedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("watchCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("pausedCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("averageQualityScore",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("averageDeliveryScore",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("averageServiceScore",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("averagePriceScore",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),("items",a(r("ApiSupplierAssessmentOverviewItemDto")))
    ]).required(vec!["totalCount","pageNumber","pageSize","totalSuppliers","assessedSuppliers","unassessedSuppliers","preferredCount","qualifiedCount","watchCount","pausedCount","averageQualityScore","averageDeliveryScore","averageServiceScore","averagePriceScore","items"])),
    ("ApiSupplierAssessmentOverviewItemDto",o(vec![
        ("supplierCompanyId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("supplierName",s()),("supplierStatus",s()),
        ("category",s()),("assessmentCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("latestAssessmentDate",s().format("date")),
        ("latestAssessmentKind",s()),("qualityScore",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("deliveryScore",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("serviceScore",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("priceScore",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("averageScore",Schema::default().kind(Types::Many(vec![Primitive::Number,Primitive::String])).format("double").pattern("^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$")),
        ("conclusion",s())
    ]).required(vec!["supplierCompanyId","supplierName","supplierStatus","category","assessmentCount","latestAssessmentDate","latestAssessmentKind","qualityScore","deliveryScore","serviceScore","priceScore","averageScore","conclusion"])),
    ("ApiSupplierAssessmentSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("supplierCompanyId",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("assessmentDate",s().format("date")),
        ("assessmentKind",s().max_length(20)),("qualityScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("deliveryScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("serviceScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("priceScore",Schema::default().kind(Types::One(Primitive::Integer)).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("conclusion",s().max_length(20)),
        ("notes",s().max_length(1000)),("expectedVersion",Schema::default().kind(Types::One(Primitive::Integer)).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","supplierCompanyId","assessmentDate","assessmentKind","qualityScore","deliveryScore","serviceScore","priceScore","conclusion","notes"])),
    ("ApiSupplierContactDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("supplierCompanyId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("title",s()),("email",s()),("phone",s()),
        ("instantMessaging",s()),("isPrimary",b()),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","supplierCompanyId","name","title","email","phone","instantMessaging","isPrimary","versionNumber"])),
    ("ApiSupplierContactSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("supplierCompanyId",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),
        ("title",s()),("email",s()),("phone",s()),
        ("instantMessaging",s()),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","supplierCompanyId","name","title","email","phone","instantMessaging"])),
    ("ApiSupplierDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("countryRegion",s()),
        ("category",s()),("website",s()),("status",s()),
        ("mainProducts",s()),("notes",s()),("versionNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","name","countryRegion","category","website","status","mainProducts","notes","versionNumber"])),
    ("ApiSupplierImportPreviewDto",o(vec![
        ("totalRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("validRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("duplicateRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("rows",a(r("ApiSupplierImportRowDto"))),("previewId",s())
    ]).required(vec!["totalRows","validRows","duplicateRows","rows","previewId"])),
    ("ApiSupplierImportRequest",o(vec![("previewId",s())]).required(vec!["previewId"])),
    ("ApiSupplierImportResultDto",o(vec![("createdSuppliers",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("createdContacts",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("skippedRows",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["createdSuppliers","createdContacts","skippedRows"])),
    ("ApiSupplierImportRowDto",object_groups(vec![
        (&["rowNumber"], Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        (&["name","countryRegion","category","website","status","mainProducts","notes","contactName","contactTitle","contactEmail","contactPhone","error"], s()),
        (&["isDuplicate"], b()),
    ]).required(vec!["rowNumber","name","countryRegion","category","website","status","mainProducts","notes","contactName","contactTitle","contactEmail","contactPhone","isDuplicate","error"])),
    ("ApiSupplierLifecycleRequest",super::schema_shapes::lifecycle_request()),
    ("ApiSupplierProductLinkDto",supplier_product_link(false)),
    ("ApiSupplierProductLinkSaveRequest",supplier_product_link(true)),
    ("ApiSupplierProductOptionDto",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("productCode",s()),("nameCN",s()),
        ("nameEN",s())
    ]).required(vec!["id","productCode","nameCN","nameEN"])),
    ("ApiSupplierSaveRequest",o(vec![
        ("id",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("name",s()),("countryRegion",s()),
        ("category",s()),("website",s()),("mainProducts",s()),
        ("notes",s()),("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).default_value(serde_json::json!(num("0"))).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))
    ]).required(vec!["id","name","countryRegion","category","website","mainProducts","notes"])),
] }

fn supplier_product_link(save: bool) -> Schema {
    let mut fields = vec![
        ("id", i().format("int32")),
        ("supplierCompanyId", i().format("int32")),
        ("productId", i().format("int32")),
        ("supplierProductCode", s().max_length(100)),
        (
            "referencePrice",
            s().format("decimal")
                .max_length(60)
                .pattern("^[+-]?[0-9]+(?:\\.[0-9]+)?$"),
        ),
        ("currency", s().max_length(3)),
        (
            "leadTimeDays",
            i().minimum(num("0")).maximum(num("3650")).format("int32"),
        ),
    ];
    if save {
        fields.push((
            "expectedVersion",
            i().format("int32").default_value(serde_json::json!(0)),
        ));
    } else {
        fields.extend([
            ("productCode", s().max_length(200)),
            ("productNameCN", s().max_length(1000)),
            ("productNameEN", s().max_length(1000)),
            ("status", s().max_length(20)),
            ("versionNumber", i().format("int32")),
        ]);
    }
    let required = fields
        .iter()
        .map(|(key, _)| *key)
        .filter(|key| *key != "expectedVersion")
        .collect();
    o(fields)
        .required(required)
        .additional(Additional::Allowed(false))
}

fn supplier_assessment_analytics() -> Schema {
    let mut fields: Vec<_> = [
        "totalCount",
        "qualityTotal",
        "deliveryTotal",
        "serviceTotal",
        "priceTotal",
        "preferredCount",
        "qualifiedCount",
        "watchCount",
        "pausedCount",
    ]
    .into_iter()
    .map(|key| (key, i().minimum(num("0")).format("int64")))
    .collect();
    fields.push(("recent", a(r("ApiSupplierAssessmentDto")).max_items(12)));
    let required = fields.iter().map(|(key, _)| *key).collect();
    o(fields)
        .required(required)
        .additional(Additional::Allowed(false))
}
