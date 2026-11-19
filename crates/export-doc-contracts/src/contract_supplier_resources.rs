//! Independently recorded supplier pagination, exact price and response admission changes.
use serde_json::{Value, json};
pub(super) fn apply(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    for (name, key) in [
        ("ApiAttachmentUploadForm", "file"),
        ("ApiPersonnelImageForm", "file"),
        ("ApiExcelImportAnalysisReportDto", "itemTable"),
        ("ApiExcelImportPreviewResponse", "customer"),
        ("ApiExcelImportPreviewResponse", "exporter"),
        ("ApiExcelImportPreviewResponse", "analysisReport"),
        ("PersonnelRecord", "account"),
        ("PersonnelUpdateRequest", "registration"),
    ] {
        let property = &mut schemas[name]["properties"][key];
        property["anyOf"] = property.as_object_mut().unwrap().remove("oneOf").unwrap();
    }
    for name in [
        "ApiSupplierProductLinkDto",
        "ApiSupplierProductLinkSaveRequest",
    ] {
        let schema = &mut schemas[name];
        for key in [
            "id",
            "supplierCompanyId",
            "productId",
            "leadTimeDays",
            "versionNumber",
            "expectedVersion",
        ] {
            if let Some(field) = schema["properties"].get_mut(key) {
                *field = json!({"type":"integer","format":"int32"});
                if key == "leadTimeDays" {
                    field["minimum"] = json!(0);
                    field["maximum"] = json!(3650);
                }
                if key == "expectedVersion" {
                    field["default"] = json!(0);
                }
            }
        }
        for (key, max) in [
            ("supplierProductCode", 100),
            ("currency", 3),
            ("productCode", 200),
            ("productNameCN", 1000),
            ("productNameEN", 1000),
            ("status", 20),
        ] {
            if let Some(field) = schema["properties"].get_mut(key) {
                field["maxLength"] = json!(max);
            }
        }
        schema["properties"]["referencePrice"] = json!({"type":"string","format":"decimal","maxLength":60,"pattern":"^(?:0|[1-9]\\d*)(?:\\.\\d+)?$"});
        schema["additionalProperties"] = json!(false);
        schema["required"] = if name.ends_with("Dto") {
            json!([
                "id",
                "supplierCompanyId",
                "productId",
                "supplierProductCode",
                "referencePrice",
                "currency",
                "leadTimeDays",
                "productCode",
                "productNameCN",
                "productNameEN",
                "status",
                "versionNumber"
            ])
        } else {
            json!([
                "id",
                "supplierCompanyId",
                "productId",
                "supplierProductCode",
                "referencePrice",
                "currency",
                "leadTimeDays"
            ])
        };
    }
    for name in [
        "ApiSupplierAssessmentDto",
        "ApiSupplierAssessmentSaveRequest",
    ] {
        for (key, field) in schemas[name]["properties"].as_object_mut().unwrap() {
            if field["type"] == json!(["integer", "string"]) {
                field["type"] = json!("integer");
            }
            if field["type"] == json!(["number", "string"]) {
                field["type"] = json!("number");
            }
            if ["assessmentKind", "conclusion", "status"].contains(&key.as_str()) {
                field["maxLength"] = json!(20);
            }
            if ["assessedBy", "confirmedBy"].contains(&key.as_str()) {
                field["maxLength"] = json!(100);
            }
            if key == "notes" {
                field["maxLength"] = json!(1000);
            }
        }
    }
    let mut page = schemas["ApiPagedResponseOfApiSupplierProductLinkDto"].clone();
    page["properties"]["items"]["items"] =
        json!({"$ref":"#/components/schemas/ApiSupplierAssessmentDto"});
    schemas["ApiPagedResponseOfApiSupplierAssessmentDto"] = page;
    let totals = [
        "totalCount",
        "qualityTotal",
        "deliveryTotal",
        "serviceTotal",
        "priceTotal",
        "preferredCount",
        "qualifiedCount",
        "watchCount",
        "pausedCount",
    ];
    let mut properties = serde_json::Map::new();
    for key in totals {
        properties.insert(
            key.into(),
            json!({"type":"integer","minimum":0,"format":"int64"}),
        );
    }
    properties.insert("recent".into(),json!({"type":"array","items":{"$ref":"#/components/schemas/ApiSupplierAssessmentDto"},"maxItems":12}));
    let mut required: Vec<_> = totals.into_iter().map(|key| json!(key)).collect();
    required.push(json!("recent"));
    schemas["ApiSupplierAssessmentAnalyticsDto"] = json!({"type":"object","properties":properties,"required":required,"additionalProperties":false});
    let endpoint = &mut baseline["paths"]["/api/suppliers/{supplierId}/assessments"]["get"];
    let mut analytics = endpoint.clone();
    analytics["operationId"] = json!("GetSupplierAssessmentAnalytics");
    for operation in [&mut *endpoint, &mut analytics] {
        for (code, description) in [("400", "Bad Request"), ("404", "Not Found")] {
            operation["responses"][code] = json!({"description":description});
        }
    }
    endpoint["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/ApiPagedResponseOfApiSupplierAssessmentDto"});
    endpoint["parameters"].as_array_mut().unwrap().extend([
        json!({"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1,"format":"int32"}}),
        json!({"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":200,"format":"int32"}}),
    ]);
    analytics["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/ApiSupplierAssessmentAnalyticsDto"});
    baseline["paths"]["/api/suppliers/{supplierId}/assessment-analytics"] =
        json!({"get":analytics});
    for path in baseline["paths"].as_object_mut().unwrap().values_mut() {
        for operation in path.as_object_mut().unwrap().values_mut() {
            let limit = operation["responses"]
                .as_object()
                .unwrap()
                .iter()
                .filter(|(code, _)| code.starts_with('2'))
                .map(
                    |(_, response)| match response["content"]["application/json"]["schema"]["$ref"]
                        .as_str()
                        .unwrap_or("")
                        .rsplit('/')
                        .next()
                        .unwrap()
                    {
                        "ApiCommandResponse"
                        | "ApiSupplierProductLinkDto"
                        | "ApiSupplierAssessmentDto" => 65536,
                        "ApiSupplierAssessmentAnalyticsDto" => 1048576,
                        _ => 67108864,
                    },
                )
                .max()
                .unwrap_or(67108864);
            operation["x-exportdoc-json-response-limit-bytes"] = json!(limit);
        }
    }
}
