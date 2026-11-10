//! Explicit reviewed changes to the frozen served contract; never copy live output.
use serde_json::{Value, json};
pub(super) fn reviewed_versioning(baseline: &mut Value) {
    reviewed_email_pagination(baseline);
    baseline["paths"]["/api/worklist"]["get"]["responses"]["400"] =
        json!({"description":"Bad Request"});
    baseline["components"]["schemas"]["ApiRuntimeJobMetrics"]["properties"]["retentionFailure"] =
        json!({"type":["null","string"]});
    baseline["components"]["schemas"]["ApiRuntimeJobMetrics"]["required"]
        .as_array_mut()
        .unwrap()
        .insert(0, json!("retentionFailure"));
    baseline["paths"]["/api/worklist"]["get"]["parameters"]
        .as_array_mut()
        .unwrap()
        .insert(
            0,
            json!({"name":"cursor","in":"query","schema":{"type":"string","maxLength":1024}}),
        );
    baseline["components"]["schemas"]["WorklistPage"]["properties"]["nextCursor"] =
        json!({"type":["null","string"]});
    baseline["components"]["schemas"]["WorklistPage"]["required"]
        .as_array_mut()
        .unwrap()
        .insert(0, json!("nextCursor"));
    reviewed_hs_examples(baseline);
    reviewed_client_directories(baseline);
    for name in ["ApiCustomsCooProducerProfileDto", "ApiContainerTypeDto"] {
        let schema = &mut baseline["components"]["schemas"][name];
        schema["properties"]["versionNumber"] =
            json!({"type":"integer","minimum":1,"format":"int64"});
        schema["required"]
            .as_array_mut()
            .unwrap()
            .insert(1, json!("versionNumber"));
    }
    for name in [
        "ApiCustomsCooProducerProfileSaveRequest",
        "ApiContainerTypeSaveRequest",
    ] {
        baseline["components"]["schemas"][name]["properties"]["expectedVersion"] =
            json!({"type":"integer","minimum":1,"format":"int64"});
    }
    for (name, field, minimum, index) in [
        ("ApiSingleWindowReferenceCatalogResponse", "revision", 0, 1),
        (
            "ApiSingleWindowReferenceCatalogSaveRequest",
            "expectedRevision",
            0,
            1,
        ),
        (
            "ApiSingleWindowReferenceCatalogSaveResponse",
            "revision",
            1,
            2,
        ),
    ] {
        let schema = &mut baseline["components"]["schemas"][name];
        schema["properties"][field] = json!({"type":"integer","minimum":minimum,"format":"int64"});
        schema["required"]
            .as_array_mut()
            .unwrap()
            .insert(index, json!(field));
    }
    for path in [
        "/api/single-window/coo/producer-profiles/{id}",
        "/api/tools/container-packing/projects/{id}",
        "/api/tools/container-packing/container-types/{id}",
    ] {
        baseline["paths"][path]["delete"]["parameters"].as_array_mut().unwrap().push(json!({"name":"expectedVersion","in":"query","required":true,"schema":{"type":"integer","minimum":1,"format":"int64"}}));
    }
    let conflict = baseline["paths"]["/api/tools/container-packing/container-types/{id}"]["delete"]
        ["responses"]["409"]
        .clone();
    baseline["paths"]["/api/tools/container-packing/projects/{id}"]["delete"]["responses"]["409"] =
        conflict;
    for (path, method) in [
        ("/api/single-window/reference-catalog", "delete"),
        ("/api/single-window/reference-catalog/import-json", "post"),
    ] {
        baseline["paths"][path][method]["parameters"] = json!([{"name":"expectedRevision","in":"query","required":true,"schema":{"type":"integer","minimum":0,"format":"int64"}}]);
    }
    let parameters =
        baseline["paths"]["/api/single-window/coo/producer-profiles"]["get"]["parameters"]
            .as_array_mut()
            .unwrap();
    for name in ["pageNumber", "pageSize"] {
        parameters.push(json!({"name":name,"in":"query","schema":{"type":["integer","string"],"format":"int32","pattern":"^-?(?:0|[1-9]\\d*)$"}}));
    }
}

fn reviewed_email_pagination(baseline: &mut Value) {
    let bad_request =
        baseline["paths"]["/api/email-templates/{id}/draft"]["put"]["responses"]["400"].clone();
    let old_history = baseline["paths"]["/api/email-templates/{id}/versions"]["get"].clone();
    let mut detail = old_history.clone();
    detail["operationId"] = json!("GetEmailTemplateVersion");
    detail["parameters"] = json!([
        {"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int32"}},
        {"name":"versionNumber","in":"path","required":true,"schema":{"type":"integer","minimum":1,"format":"int32"}}
    ]);
    detail["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/ApiEmailTemplateVersionDto"});
    detail["responses"]["400"] = bad_request.clone();
    baseline["paths"]["/api/email-templates/{id}/versions/{versionNumber}"] = json!({"get":detail});
    for (path, schema) in [
        (
            "/api/email-templates",
            "ApiPagedResponseOfApiEmailTemplateDto",
        ),
        (
            "/api/email-templates/{id}/versions",
            "ApiPagedResponseOfApiEmailTemplateVersionSummaryDto",
        ),
    ] {
        let endpoint = &mut baseline["paths"][path]["get"];
        endpoint["responses"]["400"] = bad_request.clone();
        endpoint["responses"]["200"]["content"]["application/json"]["schema"] =
            json!({"$ref":format!("#/components/schemas/{schema}")});
        let params = endpoint["parameters"].as_array_mut().unwrap();
        for name in ["pageNumber", "pageSize"] {
            params.push(json!({"name":name,"in":"query","schema":{"type":["integer","string"],"format":"int32","pattern":"^-?(?:0|[1-9]\\d*)$"}}));
        }
        if path == "/api/email-templates" {
            // Native catalogue composes keyword/page parameters before mail filters.
            let category = params.remove(1);
            let archived = params.remove(1);
            params.extend([
                category,
                archived,
                json!({"name":"scope","in":"query","schema":{"type":"string"}}),
            ]);
        }
    }
    let schemas = &mut baseline["components"]["schemas"];
    let mut summary = schemas["ApiEmailTemplateVersionDto"].clone();
    for key in ["bodyHtml", "subject", "category"] {
        summary["properties"].as_object_mut().unwrap().remove(key);
        summary["required"]
            .as_array_mut()
            .unwrap()
            .retain(|v| v != key);
    }
    for key in ["id", "emailTemplateId", "versionNumber"] {
        summary["properties"][key] = json!({"type":"integer","format":"int32"});
    }
    schemas["ApiEmailTemplateVersionSummaryDto"] = summary;
    for (name, item) in [
        (
            "ApiPagedResponseOfApiEmailTemplateDto",
            "ApiEmailTemplateDto",
        ),
        (
            "ApiPagedResponseOfApiEmailTemplateVersionSummaryDto",
            "ApiEmailTemplateVersionSummaryDto",
        ),
    ] {
        let mut page = schemas["ApiPagedResponseOfApiUserReportTemplateVersionDto"].clone();
        page["properties"]["items"]["items"] =
            json!({"$ref":format!("#/components/schemas/{item}")});
        schemas[name] = page;
    }
}

fn reviewed_hs_examples(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    schemas["HsCodeDeclarationExample"]["properties"]["versionNumber"] =
        json!({"type":"integer","minimum":1,"format":"int64"});
    schemas["HsCodeDeclarationExample"]["required"]
        .as_array_mut()
        .unwrap()
        .insert(0, json!("versionNumber"));
    schemas["HsCodeExampleInput"]["properties"]["expectedVersion"] =
        json!({"type":"integer","minimum":0,"format":"int64"});
    schemas["HsCodeKnowledgeExampleDeleteBatchInput"] = json!({"type":"object","properties":{"items":{"type":"array","minItems":1,"maxItems":5000,"items":{"type":"object","properties":{"id":{"type":"integer","minimum":1,"format":"int64"},"expectedVersion":{"type":"integer","minimum":1,"format":"int64"}},"required":["id","expectedVersion"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false});
    for (path, method, codes) in [
        (
            "/api/master-data/hs-knowledge/examples",
            "post",
            vec!["404", "409", "503"],
        ),
        (
            "/api/master-data/hs-knowledge/examples/{id}",
            "delete",
            vec!["400", "409", "503"],
        ),
        (
            "/api/master-data/hs-knowledge/examples/delete-batch",
            "post",
            vec!["400", "404", "409", "503"],
        ),
    ] {
        for code in codes {
            baseline["paths"][path][method]["responses"][code] = json!({"description":match code {"400"=>"Bad Request","404"=>"Not Found","409"=>"Conflict",_=>"Service Unavailable"}});
        }
    }
    baseline["paths"]["/api/master-data/hs-knowledge/examples/{id}"]["delete"]["parameters"] = json!([
        {"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int32","pattern":"^-?(?:0|[1-9]\\d*)$"}},
        {"name":"expectedVersion","in":"query","required":true,"schema":{"type":"integer","minimum":1,"format":"int64"}}
    ]);
}

fn reviewed_client_directories(baseline: &mut Value) {
    for code in ["503", "504"] {
        baseline["paths"]["/api/single-window/client-profiles"]["put"]["responses"][code] =
            baseline["paths"]["/api/single-window/client/dispatch"]["post"]["responses"][code]
                .clone();
    }
    let schemas = &mut baseline["components"]["schemas"];
    schemas["ApiSingleWindowClientDirectories"] = json!({"type":"object","properties":{
        "outBox":{"type":"string"},"sentBox":{"type":"string"},"inBox":{"type":"string"},"failBox":{"type":"string"}},
        "required":["outBox","sentBox","inBox","failBox"]});
    for name in [
        "ApiSingleWindowClientProfileDto",
        "ApiSingleWindowClientProfileSaveRequest",
    ] {
        let schema = &mut schemas[name];
        for (old, new) in [
            ("customsCooClientRootPath", "customsCooDirectories"),
            (
                "agentConsignmentClientRootPath",
                "agentConsignmentDirectories",
            ),
        ] {
            schema["properties"]
                .as_object_mut()
                .unwrap()
                .remove(old)
                .unwrap();
            schema["properties"][new] =
                json!({"$ref":"#/components/schemas/ApiSingleWindowClientDirectories"});
            for field in schema["required"].as_array_mut().unwrap() {
                if field == old {
                    *field = json!(new);
                }
            }
        }
    }
    let schema = &mut schemas["SingleWindowReceiptCollectionResult"];
    schema["properties"]
        .as_object_mut()
        .unwrap()
        .remove("receiptRootPath")
        .unwrap();
    schema["properties"]["receiptDirectories"] = json!({"type":"array","items":{"type":"string"}});
    for field in schema["required"].as_array_mut().unwrap() {
        if field == "receiptRootPath" {
            *field = json!("receiptDirectories");
        }
    }
}

pub(super) fn reviewed_independent_declarations(baseline: &mut Value) {
    for (name, before) in [
        ("ApiAgentConsignmentDocumentDto", "sourceInvoiceId"),
        ("ApiCustomsCooDocumentDto", "sourceInvoiceId"),
        ("SingleWindowPackageManifest", "sourceInvoiceId"),
        ("SingleWindowExportReview", "invoiceId"),
    ] {
        let schema = &mut baseline["components"]["schemas"][name];
        let property = schema["properties"]
            .as_object_mut()
            .unwrap()
            .remove(before)
            .unwrap();
        schema["properties"]["sourceId"] = property;
        for field in schema["required"].as_array_mut().unwrap() {
            if field == before {
                *field = json!("sourceId");
            }
        }
    }
    let paths = baseline["paths"].as_object_mut().unwrap();
    let changed: Vec<_> = paths
        .keys()
        .filter(|p| p.starts_with("/api/single-window/") && p.contains("{invoiceId}"))
        .cloned()
        .collect();
    for path in changed {
        let mut operations = paths.remove(&path).unwrap();
        for operation in operations.as_object_mut().unwrap().values_mut() {
            for parameter in operation["parameters"].as_array_mut().unwrap() {
                if parameter["name"] == "invoiceId" {
                    parameter["name"] = json!("sourceId");
                    if !path.contains("export-review/{businessType}") {
                        parameter["schema"] =
                            json!({"type":"integer","minimum":1,"format":"int64"});
                    }
                }
            }
        }
        paths.insert(path.replace("{invoiceId}", "{sourceId}"), operations);
    }
    let mut template = baseline["paths"]["/api/single-window/coo/editor-options"]["get"].clone();
    for (path, method, id, response, errors, action) in [
        (
            "/api/single-window/sources",
            "get",
            "ListSingleWindowSources",
            "ApiSingleWindowSourceListResponse",
            vec!["401", "403"],
            "view",
        ),
        (
            "/api/single-window/sources",
            "post",
            "CreateSingleWindowSource",
            "ApiSingleWindowSourceDto",
            vec!["400", "401", "403", "409"],
            "operate",
        ),
        (
            "/api/single-window/sources/excel/preview",
            "post",
            "PreviewSingleWindowExcelImport",
            "ApiExcelImportPreviewResponse",
            vec!["400", "401", "403", "413"],
            "operate",
        ),
    ] {
        template["operationId"] = json!(id);
        template["x-exportdoc-policy"]["access"]["resources"][0]["actions"] = json!([action]);
        let mut operation = template.clone();
        operation["responses"] = json!({"200":{"description":"OK","content":{"application/json":{"schema":{"$ref":format!("#/components/schemas/{response}")}}}}});
        for code in errors {
            operation["responses"][code] = json!({"description":match code { "400"=>"Bad Request", "401"=>"Unauthorized", "403"=>"Forbidden", "409"=>"Conflict", _=>"Payload Too Large" }});
        }
        if method == "get" {
            operation["parameters"] = baseline["paths"]["/api/single-window/coo/producer-profiles"]
                ["get"]["parameters"]
                .clone();
        } else {
            let excel = id == "PreviewSingleWindowExcelImport";
            operation["requestBody"] = json!({"required":true,"content":{if excel {"application/octet-stream"} else {"application/json"}:{"schema":{"$ref":format!("#/components/schemas/{}",if excel {"IFormFile"} else {"ApiSingleWindowSourceCreateRequest"})}}}});
            if excel {
                operation["parameters"] =
                    json!([{"name":"fileName","in":"query","schema":{"type":"string"}}]);
            }
        }
        if baseline["paths"].get(path).is_none() {
            baseline["paths"][path] = json!({});
        }
        baseline["paths"][path][method] = operation;
    }
    let schemas = &mut baseline["components"]["schemas"];
    schemas["ApiSingleWindowSourceCreateRequest"] = json!({"type":"object","properties":{
        "referenceNo":{"type":"string","minLength":1,"maxLength":100},"invoice":{"$ref":"#/components/schemas/ApiInvoiceDetailDto"}},"required":["referenceNo"]});
    schemas["ApiSingleWindowSourceDto"] = json!({"type":"object","properties":{
        "id":{"type":"integer","format":"int64"},"invoiceNo":{"type":"string"},"sourceKind":{"type":"string"},"createdAt":{"type":"string","format":"date-time"}},"required":["id","invoiceNo","sourceKind","createdAt"]});
    schemas["ApiSingleWindowSourceListResponse"] = json!({"type":"object","properties":{
        "items":{"type":"array","items":{"$ref":"#/components/schemas/ApiSingleWindowSourceDto"}},"totalCount":{"type":"integer","format":"int64"}},"required":["items","totalCount"]});
}
