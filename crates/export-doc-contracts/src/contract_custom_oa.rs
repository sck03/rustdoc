//! Reviewed pre-production break: scoped settings writes and versioned custom forms.
use serde_json::{Value, json};
pub(super) fn apply(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    schemas
        .as_object_mut()
        .unwrap()
        .remove("OaApprovalSettingsSave")
        .unwrap();
    schemas["OaSettingsPatch"] = json!({"type":"object","properties":{
        "section":{"type":"string","enum":["rules","calendar","delegations","handlingServices","sheetTemplates"]},
        "expectedRevision":{"type":"integer","minimum":0},"definitionKey":{"type":"string"},"remove":{"type":"boolean"},
        "rule":{"$ref":"#/components/schemas/OaApprovalRule"},"calendar":{"$ref":"#/components/schemas/OaBusinessCalendar"},
        "delegations":{"type":"array","items":{"$ref":"#/components/schemas/OaApprovalDelegation"},"maxItems":50},
        "handlingServices":{"type":"array","items":{"$ref":"#/components/schemas/OfficeHandlingService"},"maxItems":100},
        "sheetTemplates":{"type":"array","items":{"$ref":"#/components/schemas/OaSheetTemplate"},"maxItems":18}},"required":["section","expectedRevision"]});
    schemas["OaFormColumn"] = json!({"type":"object","properties":{
        "key":{"type":"string","pattern":"^[A-Za-z0-9_]{1,40}$"},"label":{"type":"string","minLength":1,"maxLength":100},
        "kind":{"type":"string","enum":["Text","Multiline","Decimal","Money","Date","Select","Person","Department","Table"]},
        "required":{"type":"boolean"},"options":{"type":"array","items":{"type":"string","maxLength":100},"maxItems":50,"uniqueItems":true},
        "currency":{"type":"string","maxLength":3}},"required":["key","label","kind"]});
    schemas["OaFormField"] = schemas["OaFormColumn"].clone();
    schemas["OaFormField"]["properties"]["columns"] =
        json!({"type":"array","items":{"$ref":"#/components/schemas/OaFormColumn"},"maxItems":10});
    schemas["OaFormField"]["properties"]["visibleWhen"] =
        json!({"anyOf":[{"$ref":"#/components/schemas/OaFormVisibility"},{"type":"null"}]});
    schemas["OaFormVisibility"] = json!({"type":"object","properties":{"field":{"type":"string","maxLength":40},"equals":{"type":"string","maxLength":100}},"required":["field","equals"]});
    schemas["OaFormDefinition"] = json!({"type":"object","properties":{
        "category":{"type":"string","minLength":1,"maxLength":50},"attachmentRequired":{"type":"boolean"},
        "departmentIds":{"type":"array","items":{"type":"string","maxLength":50},"maxItems":100,"uniqueItems":true},
        "fields":{"type":"array","items":{"$ref":"#/components/schemas/OaFormField"},"minItems":1,"maxItems":40}},"required":["category","fields"]});
    schemas["OaFormRow"] = json!({"type":"object","additionalProperties":{"type":"string","maxLength":2000},"maxProperties":10});
    schemas["OaFormValues"] = json!({"type":"object","additionalProperties":{"oneOf":[{"type":"string","maxLength":2000},{"type":"array","items":{"$ref":"#/components/schemas/OaFormRow"},"maxItems":100}]},"maxProperties":40});
    schemas["OaApplication"] = json!({"type":"object","properties":{"key":{"type":"string"},"name":{"type":"string"},"version":{"type":"integer","format":"int64"},"form":{"$ref":"#/components/schemas/OaFormDefinition"}},"required":["key","name","version","form"]});
    schemas["OaApplicationCatalog"] = json!({"type":"object","properties":{"items":{"type":"array","items":{"$ref":"#/components/schemas/OaApplication"}}},"required":["items"]});
    schemas["OaFormReference"] = json!({"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}},"required":["id","name"]});
    schemas["OaFormReferencePage"] = json!({"type":"object","properties":{"items":{"type":"array","items":{"$ref":"#/components/schemas/OaFormReference"}},"totalCount":{"type":"integer","format":"int64"},"pageNumber":{"type":"integer","format":"int64"},"pageSize":{"type":"integer","format":"int64"}},"required":["items","totalCount","pageNumber","pageSize"]});
    schemas["OaApprovalRule"]["properties"]["form"] =
        json!({"anyOf":[{"$ref":"#/components/schemas/OaFormDefinition"},{"type":"null"}]});
    schemas["OaApprovalSettings"]["properties"]["rules"]["minItems"] = json!(13);
    schemas["OaApprovalSettings"]["properties"]["rules"]["maxItems"] = json!(113);
    schemas["OaApprovalSettings"]["properties"]["revisions"] =
        json!({"type":"object","additionalProperties":{"type":"integer","format":"int64"}});
    schemas["OaApprovalCondition"]["properties"]["field"]["enum"]
        .as_array_mut()
        .unwrap()
        .extend([json!("FormText"), json!("FormNumber")]);
    schemas["OaApprovalCondition"]["properties"]["fieldKey"] =
        json!({"type":"string","maxLength":40});
    for name in ["OaDefinitionAction", "OaPublishAction", "OaScheduleAction"] {
        schemas[name]["properties"]["expectedRevision"] = json!({"type":"integer","minimum":0});
        schemas[name]["required"]
            .as_array_mut()
            .unwrap()
            .insert(1, json!("expectedRevision"));
    }
    for name in ["OaRequest", "OaRequestSave", "OaSubmittedRequest"] {
        schemas[name]["properties"].as_object_mut().unwrap().extend(json!({
            "definitionKey":{"type":"string","maxLength":80},"definitionVersion":{"type":"integer","format":"int64"},
            "formDefinition":{"$ref":"#/components/schemas/OaFormDefinition","readOnly":true},"applicationName":{"type":"string","readOnly":true},
            "formValues":{"$ref":"#/components/schemas/OaFormValues"},"formDisplayValues":{"$ref":"#/components/schemas/OaFormValues","readOnly":true}}).as_object().unwrap().clone());
    }
    for name in ["OaSimulationRequest", "OaSimulationSample"] {
        schemas[name]["properties"]["formValues"] =
            json!({"$ref":"#/components/schemas/OaFormValues"});
    }
    baseline["paths"]["/api/office/approval-settings"]["put"]["requestBody"]["content"]["application/json"]
        ["schema"] = json!({"$ref":"#/components/schemas/OaSettingsPatch"});
    baseline["paths"]["/api/office/general-requests"]["get"]["parameters"]
        .as_array_mut()
        .unwrap()
        .push(
            json!({"name":"definitionKey","in":"query","schema":{"type":"string","maxLength":80}}),
        );
    let mut catalog = baseline["paths"]["/api/office/general-requests"]["get"].clone();
    catalog.as_object_mut().unwrap().remove("parameters");
    catalog["operationId"] = json!("ListOaApplications");
    catalog["x-exportdoc-office"]["action"] = json!("application-catalog");
    catalog["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/OaApplicationCatalog"});
    baseline["paths"]["/api/office/applications"] = json!({"get":catalog});
    catalog["operationId"] = json!("ListOaFormReferences");
    catalog["x-exportdoc-office"]["action"] = json!("form-references");
    catalog["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/OaFormReferencePage"});
    catalog["parameters"] = json!([
        {"name":"kind","in":"query","required":true,"schema":{"type":"string","enum":["Person","Department"]}},
        {"name":"keyword","in":"query","schema":{"type":"string","maxLength":100}},
        {"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},
        {"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}
    ]);
    baseline["paths"]["/api/office/applications/references"] = json!({"get":catalog});
}
