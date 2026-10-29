use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    let schemas = &mut doc["components"]["schemas"];
    let manager = json!({"type":["integer","null"],"format":"int64","minimum":1});
    let managers = json!({"type":"array","items":{"type":"integer","format":"int64","minimum":1},"maxItems":10,"uniqueItems":true});
    schemas["OrganizationLeader"] = json!({"type":"object","properties":{
        "employeeId":{"type":"integer","format":"int64","minimum":1},
        "appointmentId":{"type":"string","maxLength":64},
        "workflowKinds":{"type":"array","minItems":1,"maxItems":9,"uniqueItems":true,"items":{"type":"string","enum":["leave","overtime","expense","travel","purchase","general","personnel","room","supply"]}}
    },"required":["employeeId","workflowKinds"]});
    for name in [
        "ApiOrganizationCompanyDto",
        "ApiOrganizationCompanySaveRequest",
        "ApiOrganizationDepartmentDto",
        "ApiOrganizationDepartmentSaveRequest",
    ] {
        schemas[name]["properties"]["additionalLeaders"] = json!({"type":"array","maxItems":10,"items":{"$ref":"#/components/schemas/OrganizationLeader"}});
        let field = if name.contains("Company") {
            "executiveAppointmentId"
        } else {
            "managerAppointmentId"
        };
        schemas[name]["properties"][field] = json!({"type":["string","null"],"maxLength":64});
    }
    for name in ["ApiOrganizationCompanyDto", "ApiOrganizationDepartmentDto"] {
        schemas[name]["properties"]["additionalLeaderNames"] =
            json!({"type":"object","additionalProperties":{"type":"string"},"readOnly":true});
    }
    schemas["PersonnelAssignment"] = json!({"type":"object","properties":{
        "departmentId":{"type":"string","maxLength":50},"jobTitle":{"type":"string","maxLength":120},
        "managerEmployeeId":manager,"functionalManagerEmployeeIds":managers
    },"required":["departmentId","jobTitle","functionalManagerEmployeeIds"]});
    schemas["PersonnelOrganization"] = json!({"type":"object","properties":{
        "primaryAssignmentId":{"type":"string","readOnly":true},
        "managerEmployeeId":manager,"functionalManagerEmployeeIds":managers,
        "additionalAssignments":{"type":"array","items":{"$ref":"#/components/schemas/PersonnelAssignment"},"maxItems":20}
    },"required":["functionalManagerEmployeeIds","additionalAssignments"]});
    for name in ["PersonnelOrganization", "PersonnelAssignment"] {
        schemas[name]["properties"]["managerAssignmentId"] =
            json!({"type":["string","null"],"maxLength":64});
        schemas[name]["properties"]["functionalManagerAssignmentIds"] =
            json!({"type":"array","items":{"type":"string","maxLength":64},"maxItems":10});
        schemas[name]["properties"]["managerName"] = json!({"type":"string","readOnly":true});
        schemas[name]["properties"]["functionalManagerNames"] =
            json!({"type":"array","items":{"type":"string"},"readOnly":true});
    }
    schemas["PersonnelAssignment"]["properties"]["id"] = json!({"type":"string","maxLength":64,"description":"Stable appointment identifier; omitted only for a new appointment."});
    schemas["OrganizationManagerAppointment"] = json!({"type":"object","properties":{"id":{"type":"string"},"departmentId":{"type":"string"},"departmentName":{"type":"string"},"jobTitle":{"type":"string"}},"required":["id","departmentId","departmentName","jobTitle"]});
    schemas["OrganizationManagerRecord"]["properties"]["appointments"] = json!({"type":"array","items":{"$ref":"#/components/schemas/OrganizationManagerAppointment"}});
    for name in [
        "PersonnelRecord",
        "PersonnelCreateRequest",
        "PersonnelRegistrationCorrection",
        "PersonnelTransitionRequest",
        "OaPersonnelChange",
        "AccountPersonnelRecord",
    ] {
        schemas[name]["properties"]["organization"] =
            json!({"$ref":"#/components/schemas/PersonnelOrganization"});
    }
    schemas["PersonnelAssignment"]["properties"]["departmentName"] =
        json!({"type":"string","readOnly":true});
    schemas["ApiUserDto"]["properties"]["effectiveDepartmentIds"] = json!({"type":"array","items":{"type":"string"},"readOnly":true,
        "description":"Current department data scope: appointments plus departments managed by the linked employee and their active descendants. Does not grant operations."});
    schemas["AccountPersonnelRecord"]["properties"]["departmentId"] = json!({"type":"string"});
    for name in ["PersonnelRecord", "OaRequest"] {
        schemas[name]["properties"]["companyScope"] = json!({"type":"string","readOnly":true});
    }
    for name in [
        "ApiOrganizationCompanyDto",
        "ApiOrganizationCompanySaveRequest",
    ] {
        schemas[name]["properties"]["executiveEmployeeId"] = manager.clone();
        schemas[name]["properties"]["positions"] = json!({"type":"array","maxItems":200,"items":{"$ref":"#/components/schemas/OrganizationPosition"}});
    }
    schemas["ApiOrganizationCompanyDto"]["properties"]["executiveName"] =
        json!({"type":"string","readOnly":true});
    schemas["OrganizationPosition"] = json!({"type":"object","properties":{"name":{"type":"string","minLength":1,"maxLength":120},"isActive":{"type":"boolean"}},"required":["name","isActive"]});
    schemas["PersonnelOptions"]["properties"]["positions"] =
        json!({"type":"array","items":{"$ref":"#/components/schemas/OrganizationPosition"}});
    for name in [
        "ApiUserAccountDto",
        "ApiUserSaveRequest",
        "ApiPermissionTemplateDto",
        "ApiPermissionTemplateOptionDto",
        "ApiPermissionTemplateSaveRequest",
        "ApiUserCapabilitiesDto",
    ] {
        schemas[name]["properties"]["oaAssignedOnly"] = json!({"type":"boolean","default":false,
            "description":"Restrict office requests to the user's own requests and explicitly assigned approval/handling work, within the ordinary data scopes."});
    }
    schemas["ApiUserCapabilitiesDto"]["properties"]
        .as_object_mut()
        .unwrap()
        .remove("usesOfficeRegister");
    schemas["ApiUserCapabilitiesDto"]["required"]
        .as_array_mut()
        .unwrap()
        .retain(|field| field != "usesOfficeRegister");
    schemas["OaRequestSave"]["properties"]["departmentId"] = json!({"type":"string","maxLength":50,
        "description":"Choose an active primary or additional assignment when creating a request; defaults to the primary department."});
    schemas["PersonnelClearance"]["properties"]["reportingCount"] =
        json!({"type":"integer","minimum":0});
    schemas["PersonnelClearance"]["properties"]["isCompanyExecutive"] = json!({"type":"boolean"});
    for name in [
        "businessDataCount",
        "invoiceCount",
        "paymentCount",
        "otherBusinessDataCount",
    ] {
        schemas["PersonnelClearance"]["properties"][name] =
            json!({"type":"integer","format":"int64","minimum":0});
    }
    let mut managers = doc["paths"]["/api/organization-directory/managers"]["get"].clone();
    assert!(
        managers.is_object(),
        "Missing organization manager options route"
    );
    managers["operationId"] = json!("ListPersonnelReportingOptions");
    managers["x-exportdoc-policy"]["requirements"] =
        json!([{"resourceKey":"office.people","action":"view"}]);
    doc["paths"]["/api/office/people/reporting-options"] = json!({"get":managers});
}
