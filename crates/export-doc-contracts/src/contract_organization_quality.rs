//! Reviewed organization labels, sibling ordering and never-submitted draft deletion.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    // Preserve the eight existing profiles and independently state the two approval presets.
    let roles = baseline["x-exportdoc-permissions"]["roles"]
        .as_array_mut()
        .unwrap();
    assert_eq!(roles.len(), 8);
    for (code, name, description, scope) in [
        (
            "DepartmentApprover",
            "部门审批人员",
            "查看和审批本部门 OA 事项，自助申请仅限本人；须关联有效任职并明确指定为流程责任人。",
            "department",
        ),
        (
            "CompanyApprover",
            "公司审批人员",
            "查看和审批本公司 OA 事项，自助申请仅限本人；不默认授予人事档案、资源办理或系统管理权限。",
            "company",
        ),
    ] {
        let mut grants = vec![
            json!({"resourceKey":"office.people","action":"view","dataScope":"company"}),
            json!({"resourceKey":"system.about","action":"view","dataScope":"own"}),
            json!({"resourceKey":"office.announcements","action":"view","dataScope":"company"}),
            json!({"resourceKey":"office.notifications","action":"view","dataScope":"company"}),
        ];
        let resources = [
            "office.personnel-changes",
            "office.leave",
            "office.overtime",
            "office.expenses",
            "office.travel",
            "office.purchase",
            "office.general",
            "office.rooms",
            "office.supplies",
        ];
        for resource in resources {
            for action in ["view", "approve"] {
                grants.push(json!({"resourceKey":resource,"action":action,"dataScope":scope}));
            }
        }
        for resource in resources.into_iter().skip(1) {
            for action in ["create", "edit", "cancel"] {
                grants.push(json!({"resourceKey":resource,"action":action,"dataScope":"own"}));
            }
        }
        roles.push(json!({"code":code,"name":name,"description":description,"grants":grants}));
    }
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
