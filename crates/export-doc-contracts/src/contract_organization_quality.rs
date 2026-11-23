//! Reviewed organization labels, sibling ordering and never-submitted draft deletion.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    let schemas = &mut baseline["components"]["schemas"];
    for (name, fields) in [
        (
            "AccountPersonnelRecord",
            &["companyName", "companyScope", "departmentName"][..],
        ),
        ("PersonnelAssignment", &["companyName"][..]),
        ("OaMaterialPersonnelAssignment", &["companyName"][..]),
        ("OaMaterialOaPersonnelChange", &["departmentName"][..]),
        ("UnitApproverContext", &["departmentName"][..]),
        ("UnitApproverOption", &["departmentName"][..]),
        ("OaRequest", &["departmentName"][..]),
        ("OaPersonnelChangeProjection", &["departmentName"][..]),
        (
            "OaMaterialOaPersonnelChangeProjection",
            &["departmentName"][..],
        ),
    ] {
        for field in fields {
            schemas[name]["properties"][*field] = json!({"type":"string","readOnly":true});
        }
    }
    schemas["OaRequest"]["properties"]["canDeleteDraft"] =
        json!({"type":"boolean","readOnly":true});
    schemas["ApiOrganizationDepartmentDto"]["properties"]["sortOrder"] =
        json!({"type":"integer","minimum":0,"readOnly":true});
    schemas["OrganizationDepartmentOrder"] = json!({"type":"object","properties":{
        "companyCode":{"type":"string","minLength":1,"maxLength":50},
        "parentId":{"type":"integer","format":"int64","minimum":1},
        "items":{"type":"array","items":{"$ref":"#/components/schemas/OrganizationDepartmentOrderItem"},"minItems":1,"maxItems":2000}
    },"required":["companyCode","items"]});
    schemas["OrganizationDepartmentOrderItem"] = json!({"type":"object","properties":{
        "id":{"type":"integer","format":"int64","minimum":1},
        "expectedVersion":{"type":"integer","format":"int64","minimum":1}
    },"required":["id","expectedVersion"]});
    for (kind, name) in [
        ("personnel", "PersonnelChange"),
        ("leave", "Leave"),
        ("overtime", "Overtime"),
        ("expense", "Expense"),
        ("travel", "Travel"),
        ("purchase", "Purchase"),
        ("general", "General"),
    ] {
        let mut delete =
            baseline["paths"][format!("/api/office/{kind}-requests/{{id}}/cancel")]["post"].clone();
        delete["operationId"] = json!(format!("Delete{name}RequestDraft"));
        delete["responses"]
            .as_object_mut()
            .unwrap()
            .remove("200")
            .unwrap();
        delete["responses"]["204"] = json!({"description":"No Content"});
        delete["x-exportdoc-office"]["action"] = json!("delete-draft");
        delete["x-exportdoc-office"]["permission"] = json!("edit");
        delete["x-exportdoc-policy"]["access"]["resources"][0]["actions"] = json!(["edit"]);
        baseline["paths"][format!("/api/office/{kind}-requests/{{id}}")]["delete"] = delete;
    }
    let mut reorder = baseline["paths"]["/api/organization-directory"]["get"].clone();
    reorder["operationId"] = json!("ReorderOrganizationDepartments");
    reorder["responses"]
        .as_object_mut()
        .unwrap()
        .remove("200")
        .unwrap();
    reorder["responses"]["204"] = json!({"description":"No Content"});
    reorder["responses"]["429"] =
        baseline["paths"]["/api/office/general-requests/{id}/cancel"]["post"]["responses"]["429"]
            .clone();
    reorder["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/OrganizationDepartmentOrder"}}}});
    baseline["paths"]["/api/organization-directory/departments/order"] = json!({"post":reorder});
}
