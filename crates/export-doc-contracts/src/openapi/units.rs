//! Unit contexts and scoped administration extend the single public contract.
use serde_json::{Value, json};
#[cfg(test)]
pub(super) mod tests;
#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "ApiOrganizationCompanyDto",
        &["id", "parentCompanyCode", "unitKind"],
    ),
    (
        "ApiOrganizationCompanySaveRequest",
        &["parentCompanyCode", "unitKind"],
    ),
    (
        "ApiUserDto",
        &[
            "primaryCompanyScope",
            "contextId",
            "unitContexts",
            "permissionSource",
        ],
    ),
    ("ApiUserCapabilitiesDto", &["unitManagement"]),
    (
        "ApiPermissionTemplateDto",
        &[
            "accountReferenceCount",
            "unitRoleReferenceCount",
            "ownerUnitId",
        ],
    ),
    (
        "PersonnelAssignment",
        &["companyCode", "startsOn", "endsOn"],
    ),
    ("PersonnelOptions", &["companies"]),
    ("OrganizationManagerRecord", &["appointments"]),
    ("ApiOrganizationDepartmentDto", &["id"]),
    ("PersonnelOrganization", &["primaryStartsOn"]),
    (
        "PersonnelEventRecord",
        &[
            "previousAppointments",
            "currentAppointments",
            "previousPrimaryAssignmentId",
            "primaryAssignmentId",
        ],
    ),
];
fn object(properties: Value, required: &[&str]) -> Value {
    json!({"type":"object","properties":properties,"required":required})
}
fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    s["OrganizationImpactUnit"] = object(
        json!({"id":int,"code":text,"name":text}),
        &["id", "code", "name"],
    );
    s["OrganizationGrantImpact"] = object(
        json!({"roleId":int,"roleName":text,"userId":int,"userName":text,
        "addedUnits":{"type":"array","items":reference("OrganizationImpactUnit")},"removedUnits":{"type":"array","items":reference("OrganizationImpactUnit")}}),
        &[
            "roleId",
            "roleName",
            "userId",
            "userName",
            "addedUnits",
            "removedUnits",
        ],
    );
    s["OrganizationChangeImpact"] = object(
        json!({"unitId":int,"versionNumber":int,"requiresHandover":{"type":"boolean"},"impacts":{"type":"array","items":reference("OrganizationGrantImpact")}}),
        &["unitId", "versionNumber", "requiresHandover", "impacts"],
    );
    s["PersonnelOrganization"]["properties"]["primaryStartsOn"] =
        json!({"type":"string","readOnly":true});
    for field in ["previousAppointments", "currentAppointments"] {
        s["PersonnelEventRecord"]["properties"][field] =
            json!({"type":"array","items":reference("PersonnelAssignment"),"readOnly":true});
    }
    for field in ["previousPrimaryAssignmentId", "primaryAssignmentId"] {
        s["PersonnelEventRecord"]["properties"][field] =
            json!({"type":["string","null"],"readOnly":true});
    }
    for name in ["ApiOrganizationCompanyDto", "ApiOrganizationDepartmentDto"] {
        s[name]["properties"]["id"] = json!({"type":"integer","format":"int64","readOnly":true});
        s[name]["required"]
            .as_array_mut()
            .unwrap()
            .push(json!("id"));
    }
    s["UnitPermissionTemplateSave"] = s["ApiPermissionTemplateSaveRequest"].clone();
    s["UnitPermissionTemplateSave"]["properties"]["unitId"] = int.clone();
    s["UnitPermissionTemplateSave"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("unitId"));
    s["UnitPermissionTemplates"] = object(
        json!({"unitId":int,"items":{"type":"array","items":reference("ApiPermissionTemplateDto")}}),
        &["unitId", "items"],
    );
    s["PersonnelOptions"]["properties"]["companies"] =
        json!({"type":"array","items":reference("ApiOrganizationCompanyDto")});
    for name in [
        "ApiOrganizationCompanyDto",
        "ApiOrganizationCompanySaveRequest",
    ] {
        s[name]["properties"]["parentCompanyCode"] =
            json!({"type":["string","null"],"maxLength":50,"default":null});
        s[name]["properties"]["unitKind"] =
            json!({"type":"string","enum":["Company","Group"],"default":"Company"});
    }
    s["UnitManagement"] = object(
        json!({"organization":{"type":"boolean"},"appointments":{"type":"boolean","default":false},"workflows":{"type":"boolean"},"roles":{"type":"boolean"}}),
        &["organization", "appointments", "workflows", "roles"],
    );
    s["UnitManagement"]["properties"]["analytics"] = json!({"type":"boolean","default":false});
    s["UnitWorkflowSummaryRow"] = object(
        json!({"unitId":int,"companyCode":text,"companyName":text,"requestCount":int,"activeTaskCount":int,"queuedTaskCount":int,"overdueTaskCount":int,"completedTaskCount":int,"averageProcessingSeconds":{"type":["number","null"],"minimum":0}}),
        &[
            "unitId",
            "companyCode",
            "companyName",
            "requestCount",
            "activeTaskCount",
            "queuedTaskCount",
            "overdueTaskCount",
            "completedTaskCount",
            "averageProcessingSeconds",
        ],
    );
    s["UnitWorkflowSummary"] = object(
        json!({"items":{"type":"array","items":reference("UnitWorkflowSummaryRow")},"totalCount":int,"pageNumber":int,"pageSize":int,"asOf":{"type":"string","format":"date-time"},"completedSince":{"type":"string","format":"date-time"}}),
        &[
            "items",
            "totalCount",
            "pageNumber",
            "pageSize",
            "asOf",
            "completedSince",
        ],
    );
    s["UnitContext"] = object(
        json!({"id":text,"companyCode":text,"companyName":text,"name":text,"departmentId":text,"mode":{"type":"string","enum":["Primary","Business","Collaboration","Administration"]}}),
        &[
            "id",
            "companyCode",
            "companyName",
            "name",
            "departmentId",
            "mode",
        ],
    );
    s["ApiUserDto"]["properties"]["primaryCompanyScope"] = json!({"type":"string","readOnly":true});
    s["ApiUserDto"]["properties"]["contextId"] = json!({"type":"string","readOnly":true});
    s["ApiUserDto"]["properties"]["unitContexts"] =
        json!({"type":"array","items":reference("UnitContext"),"readOnly":true});
    s["ApiUserCapabilitiesDto"]["properties"]["unitManagement"] = reference("UnitManagement");
    s["UnitContextSelection"] = object(
        json!({"contextId":{"type":"string","maxLength":150}}),
        &["contextId"],
    );
    let grants =
        json!({"type":["array","null"],"items":reference("ApiPermissionGrantDto"),"maxItems":1000});
    let properties = json!({"id":int,"userId":int,"companyCode":{"type":"string","maxLength":50},"name":{"type":"string","maxLength":120},
        "mode":{"type":"string","enum":["Business","Collaboration","Administration"]},"appointmentId":{"type":"string","maxLength":64},
        "permissionGrants":grants,"permissionTemplateId":{"type":["integer","null"],"format":"int64"},"disabledModules":{"type":"array","items":text,"maxItems":100},"oaAssignedOnly":{"type":"boolean"},
        "management":reference("UnitManagement"),"includeDescendants":{"type":"boolean"},"startsAt":{"type":"string","format":"date-time"},"endsAt":{"type":"string","format":"date-time"},
        "isActive":{"type":"boolean"},"expectedVersion":int});
    let required = [
        "userId",
        "companyCode",
        "name",
        "mode",
        "appointmentId",
        "permissionGrants",
        "disabledModules",
        "oaAssignedOnly",
        "management",
        "includeDescendants",
        "startsAt",
        "endsAt",
        "isActive",
        "expectedVersion",
    ];
    s["UnitAccessSave"] = object(properties.clone(), &required);
    let mut properties = properties;
    properties["versionNumber"] = int.clone();
    properties["userName"] = text.clone();
    properties["departmentId"] = text.clone();
    s["UnitAccessRecord"] = object(
        properties,
        &[
            "id",
            "userId",
            "companyCode",
            "name",
            "mode",
            "appointmentId",
            "departmentId",
            "permissionGrants",
            "disabledModules",
            "oaAssignedOnly",
            "management",
            "includeDescendants",
            "startsAt",
            "endsAt",
            "isActive",
            "versionNumber",
            "userName",
        ],
    );
    s["UnitUserOption"] = object(
        json!({"id":int,"name":text,"companyCode":text}),
        &["id", "name", "companyCode"],
    );
    s["UnitAppointmentOption"] = object(
        json!({"id":text,"departmentId":text,"departmentName":text,"jobTitle":text}),
        &["id", "departmentId", "departmentName", "jobTitle"],
    );
    s["UnitUserOption"]["properties"]["appointments"] =
        json!({"type":"array","items":reference("UnitAppointmentOption")});
    s["UnitUserOption"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("appointments"));
    s["UnitAccessDirectory"] = object(
        json!({"items":{"type":"array","items":reference("UnitAccessRecord")},"users":{"type":"array","items":reference("UnitUserOption")},"companies":{"type":"array","items":reference("ApiOrganizationCompanyDto")}}),
        &["items", "users", "companies"],
    );
    s["UnitAccessDirectory"]["properties"]["resources"] =
        json!({"type":"array","items":reference("ApiPermissionResourceDefinitionDto")});
    s["UnitAccessDirectory"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("resources"));
    s["UnitAccessDirectory"]["properties"]["grantCeiling"] =
        json!({"type":["array","null"],"items":reference("ApiPermissionGrantDto")});
    s["UnitAccessDirectory"]["properties"]["permissionTemplates"] =
        json!({"type":"array","items":reference("ApiPermissionTemplateDto")});
    s["UnitAccessDirectory"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("permissionTemplates"));
    s["UnitApproverOption"] = object(
        json!({"id":int,"fullName":text,"username":text,"departmentId":text}),
        &["id", "fullName", "username", "departmentId"],
    );
    s["UnitApproverContext"] = object(
        json!({"id":text,"name":text,"departmentId":text}),
        &["id", "name", "departmentId"],
    );
    s["UnitApproverOption"]["properties"]["contexts"] =
        json!({"type":"array","items":reference("UnitApproverContext")});
    s["UnitApproverOption"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("contexts"));
    s["UnitApproverOptions"] = object(
        json!({"users":{"type":"array","items":reference("UnitApproverOption")},"handlers":{"type":"array","items":reference("UnitApproverOption")}}),
        &["users", "handlers"],
    );
    s["UnitMemberAppointment"] = s["PersonnelAssignment"].clone();
    for name in ["PersonnelAssignment", "UnitMemberAppointment"] {
        s[name]["properties"]["canRemove"] = json!({"type":"boolean","readOnly":true});
        s[name]["properties"]["removalReason"] = json!({"type":"string","readOnly":true});
    }
    s["UnitMemberAppointment"]["properties"]["isEffective"] =
        json!({"type":"boolean","readOnly":true});
    for (name, property) in [
        ("companyCode", text.clone()),
        ("startsOn", text.clone()),
        ("endsOn", text.clone()),
        ("isPrimary", json!({"type":"boolean"})),
    ] {
        s["UnitMemberAppointment"]["properties"][name] = property;
        s["UnitMemberAppointment"]["required"]
            .as_array_mut()
            .unwrap()
            .push(json!(name));
    }
    let appointments =
        json!({"type":"array","items":reference("UnitMemberAppointment"),"maxItems":21});
    s["UnitMember"] = object(
        json!({"personId":int,"employeeNumber":text,"fullName":text,"status":text,"primaryCompanyCode":text,"accountId":{"type":["integer","null"],"format":"int64"},"versionNumber":int,"appointments":appointments}),
        &[
            "personId",
            "employeeNumber",
            "fullName",
            "status",
            "primaryCompanyCode",
            "accountId",
            "versionNumber",
            "appointments",
        ],
    );
    s["UnitMemberDirectory"] = object(
        json!({"unitId":{"type":"string","pattern":"^[1-9][0-9]{0,18}$"},"items":{"type":"array","items":reference("UnitMember")},"totalCount":int,"pageNumber":int,"pageSize":int,"departments":{"type":"array","items":reference("PersonnelDepartmentRecord")}}),
        &[
            "unitId",
            "items",
            "totalCount",
            "pageNumber",
            "pageSize",
            "departments",
        ],
    );
    s["UnitWorkflowTask"] = object(
        json!({"requestId":int,"requestKind":text,"companyCode":text,"versionNumber":int,"taskKey":text,"nodeKey":text,"assigneeId":int,"assigneeName":text,"contextId":text,"active":{"type":"boolean"},"dueAt":text}),
        &[
            "requestId",
            "requestKind",
            "companyCode",
            "versionNumber",
            "taskKey",
            "nodeKey",
            "assigneeId",
            "assigneeName",
            "contextId",
            "active",
            "dueAt",
        ],
    );
    s["UnitWorkflowTasks"] = object(
        json!({"items":{"type":"array","items":reference("UnitWorkflowTask")},"totalCount":int,"pageNumber":int,"pageSize":int}),
        &["items", "totalCount", "pageNumber", "pageSize"],
    );
    s["UnitWorkflowTask"]["properties"]["identityAvailable"] = json!({"type":"boolean"});
    s["UnitWorkflowTask"]["properties"]["authorizationEndsAt"] = text.clone();
    s["UnitTaskReassignment"] = object(
        json!({"requestId":int,"requestKind":{"type":"string","enum":["oa-leave","oa-overtime","oa-expense","oa-travel","oa-purchase","oa-general","oa-personnel","bookings","supply-requests"]},"expectedVersion":int,"taskKey":text,"fromUserId":int,"toUserId":int,"note":{"type":"string","maxLength":500}}),
        &[
            "requestId",
            "requestKind",
            "expectedVersion",
            "taskKey",
            "fromUserId",
            "toUserId",
            "note",
        ],
    );
    s["UnitTaskChange"] = object(
        json!({"requestId":int,"versionNumber":int}),
        &["requestId", "versionNumber"],
    );
    s["OaRoutingUnit"] = object(
        json!({"id":int,"code":text,"name":text}),
        &["id", "code", "name"],
    );
    s["OaRoutingUnits"] = object(
        json!({"items":{"type":"array","items":reference("OaRoutingUnit")}}),
        &["items"],
    );
    s["OaSimulationRequest"] = object(
        json!({"definition":reference("OaApprovalRule"),"employeeId":int,"appointmentId":text,"amount":text,"currency":text,"duration":text,"category":text}),
        &[
            "definition",
            "employeeId",
            "appointmentId",
            "amount",
            "currency",
            "duration",
            "category",
        ],
    );
    s["OaSimulationResult"] = object(
        json!({"approvalPlan":reference("OaApprovalPlan"),"notes":{"type":"array","items":text}}),
        &["approvalPlan", "notes"],
    );
    for name in ["UnitTaskReassignment", "OaApprovalReassign"] {
        s[name]["properties"]["toContextId"] = json!({"type":"string","maxLength":150});
    }
    for name in ["PersonnelAssignment"] {
        s[name]["properties"]["companyCode"] = json!({"type":"string","maxLength":50,"default":""});
        s[name]["properties"]["startsOn"] = json!({"type":"string","default":""});
        s[name]["properties"]["endsOn"] = json!({"type":"string","default":""});
    }
    // Authentication metadata and errors stay identical to session renewal.
    doc["paths"]["/api/office/people/options"]["get"]["parameters"]
        .as_array_mut()
        .unwrap()
        .push(json!({"name":"companyCode","in":"query","schema":{"type":"string","maxLength":50}}));
    let mut switch = doc["paths"]["/api/auth/renew"]["post"].clone();
    assert_eq!(switch["operationId"], "RenewSession");
    switch["operationId"] = json!("SwitchUnitContext");
    switch["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference("UnitContextSelection")}}});
    doc["paths"]["/api/auth/unit-context"] = json!({"post":switch});
    for (path, method, operation, request, response, resource) in [
        (
            "unit-schemes",
            "get",
            "GetUnitPermissionTemplates",
            None,
            "UnitPermissionTemplates",
            "organization.roles",
        ),
        (
            "unit-schemes",
            "post",
            "SaveUnitPermissionTemplate",
            Some("UnitPermissionTemplateSave"),
            "ApiPermissionTemplateDto",
            "organization.roles",
        ),
        (
            "unit-access",
            "get",
            "GetUnitAccessDirectory",
            None,
            "UnitAccessDirectory",
            "organization.roles",
        ),
        (
            "unit-access",
            "post",
            "SaveUnitAccess",
            Some("UnitAccessSave"),
            "UnitAccessRecord",
            "organization.roles",
        ),
        (
            "unit-approvers",
            "get",
            "GetUnitApproverOptions",
            None,
            "UnitApproverOptions",
            "organization.workflows",
        ),
        (
            "unit-members",
            "get",
            "GetUnitMembers",
            None,
            "UnitMemberDirectory",
            "organization.appointments",
        ),
        (
            "unit-tasks",
            "get",
            "GetUnitWorkflowTasks",
            None,
            "UnitWorkflowTasks",
            "organization.workflows",
        ),
        (
            "unit-tasks/reassign",
            "post",
            "ReassignUnitWorkflowTask",
            Some("UnitTaskReassignment"),
            "UnitTaskChange",
            "organization.workflows",
        ),
    ] {
        let mut endpoint = doc["paths"]["/api/office/supply-requests"]["post"].clone();
        endpoint["operationId"] = json!(operation);
        endpoint
            .as_object_mut()
            .unwrap()
            .remove("x-exportdoc-office");
        endpoint["x-exportdoc-policy"]["requirements"] =
            json!([{"resourceKey":resource,"action":"manage"}]);
        endpoint["responses"]["200"]["content"]["application/json"]["schema"] = reference(response);
        endpoint.as_object_mut().unwrap().remove("requestBody");
        endpoint["parameters"] =
            json!([{"name":"companyCode","in":"query","schema":{"type":"string","maxLength":50}}]);
        if operation == "GetUnitMembers" {
            endpoint["parameters"].as_array_mut().unwrap().extend([
            json!({"name":"keyword","in":"query","schema":{"type":"string","maxLength":100}}),json!({"name":"includeAvailable","in":"query","schema":{"type":"boolean"}}),json!({"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}}),json!({"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}})
        ]);
        }
        if let Some(request) = request {
            endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference(request)}}});
        }
        if operation == "GetUnitWorkflowTasks" {
            endpoint["parameters"] = json!([
                {"name":"assigneeId","in":"query","schema":{"type":"integer","format":"int64","minimum":1}},
                {"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},
                {"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}
            ]);
        }
        doc["paths"][format!("/api/organization/{path}")][method] = endpoint;
    }
    for (path, method, operation, request, response) in [
        (
            "approval-simulation",
            "post",
            "SimulateOaDefinition",
            Some("OaSimulationRequest"),
            "OaSimulationResult",
        ),
        (
            "routing-units",
            "get",
            "GetOaRoutingUnits",
            None,
            "OaRoutingUnits",
        ),
    ] {
        let mut endpoint = doc["paths"]["/api/organization/unit-tasks"]["get"].clone();
        endpoint["operationId"] = json!(operation);
        endpoint["parameters"] = json!([]);
        endpoint["responses"]["200"]["content"]["application/json"]["schema"] = reference(response);
        if let Some(request) = request {
            endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference(request)}}});
        }
        doc["paths"][format!("/api/organization/{path}")][method] = endpoint;
    }
    let mut summary = doc["paths"]["/api/organization/unit-tasks"]["get"].clone();
    summary["operationId"] = json!("GetUnitWorkflowSummary");
    summary["x-exportdoc-policy"]["requirements"] =
        json!([{"resourceKey":"organization.analytics","action":"manage"}]);
    summary["parameters"] = json!([{"name":"unitId","in":"query","schema":{"type":"integer","format":"int64","minimum":1}},{"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},{"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}]);
    summary["responses"]["200"]["content"]["application/json"]["schema"] =
        reference("UnitWorkflowSummary");
    doc["paths"]["/api/organization/workflow-summary"] = json!({"get":summary});
    let mut impact = doc["paths"]["/api/organization/unit-access"]["post"].clone();
    impact["operationId"] = json!("PreviewOrganizationCompanyChange");
    impact["x-exportdoc-policy"]["requirements"] =
        json!([{"resourceKey":"organization.directory","action":"manage"}]);
    impact["parameters"] = json!([{"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int64","minimum":1}}]);
    impact["requestBody"]["content"]["application/json"]["schema"] =
        reference("ApiOrganizationCompanySaveRequest");
    impact["responses"]["200"]["content"]["application/json"]["schema"] =
        reference("OrganizationChangeImpact");
    doc["paths"]["/api/organization-directory/companies/{id}/impact"] = json!({"post":impact});
    let catalog = &mut doc["x-exportdoc-permissions"];
    for (key, name, order) in [
        ("organization.directory", "单位组织管理", 810),
        ("organization.analytics", "集团审批统计", 840),
        ("organization.appointments", "单位任职管理", 815),
        ("organization.roles", "单位授权管理", 820),
        ("organization.workflows", "单位流程管理", 830),
    ] {
        catalog["resources"].as_array_mut().unwrap().push(json!({"key":key,"name":name,"group":"组织管理","workspace":"office","moduleKey":key,"sortOrder":order,"isTechnical":true,"supportsDataScope":false,"actions":[{"key":"manage","name":name,"description":"由有明确范围和上限的单位管理授权派生。","sortOrder":0,"navigationAccessLevel":"manage"}]}));
        catalog["modules"].as_array_mut().unwrap().push(json!({"key":key,"name":name,"group":"组织管理","workspace":"office","sortOrder":order,"isTechnical":true}));
        catalog["editions"]["Full"]
            .as_array_mut()
            .unwrap()
            .push(json!(key));
    }
    // Organization identity is a stable ID, including when companies reuse department codes.
    // Retire the code-addressed routes rather than retaining an ambiguous compatibility path.
    for entity in ["companies", "departments"] {
        let paths = doc["paths"].as_object_mut().unwrap();
        let old = format!("/api/organization-directory/{entity}/{{code}}");
        let mut methods = paths
            .remove(&old)
            .expect("Organization update/delete routes");
        for operation in methods.as_object_mut().unwrap().values_mut() {
            for parameter in operation["parameters"].as_array_mut().unwrap() {
                if parameter["in"] == "path" && parameter["name"] == "code" {
                    *parameter = json!({"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int64","minimum":1}});
                }
            }
        }
        paths.insert(
            format!("/api/organization-directory/{entity}/{{id}}"),
            methods,
        );
    }
    doc["x-exportdoc-permissions"]["resources"].as_array_mut().unwrap().iter_mut().find(|r| r["key"] == "organization.directory").unwrap()["actions"].as_array_mut().unwrap().push(json!({"key":"view","name":"查看单位公开人员","description":"只用于当前管理单位的公开人员候选。","sortOrder":1,"navigationAccessLevel":"none"}));
    for methods in doc["paths"].as_object_mut().unwrap().values_mut() {
        for operation in methods.as_object_mut().unwrap().values_mut() {
            let id = operation["operationId"].as_str().unwrap_or("");
            let public_directory = id == "ListOrganizationManagers";
            let resource = if [
                "GetOrganizationDirectory",
                "ListOrganizationManagers",
                "CreateOrganizationCompany",
                "UpdateOrganizationCompany",
                "DeleteOrganizationCompany",
                "CreateOrganizationDepartment",
                "UpdateOrganizationDepartment",
                "DeleteOrganizationDepartment",
            ]
            .contains(&id)
            {
                Some("organization.directory")
            } else if [
                "GetOaApprovalSettings",
                "SaveOaApprovalSettings",
                "PublishOaDefinition",
                "DisableOaDefinition",
                "GetOaApproverOptions",
            ]
            .contains(&id)
            {
                Some("organization.workflows")
            } else {
                None
            };
            if let Some(resource) = resource {
                operation["x-exportdoc-policy"]["requirements"] =
                    json!([{"resourceKey":resource,"action":"manage"}]);
                if public_directory {
                    operation["x-exportdoc-policy"]["requirements"] =
                        json!([{"resourceKey":resource,"action":"view"}]);
                }
                if operation["x-exportdoc-office"].is_object() {
                    operation["x-exportdoc-office"]["resource"] = json!(resource);
                }
            }
        }
    }
}
