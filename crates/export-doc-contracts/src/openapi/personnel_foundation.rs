//! Typed organization facts use string IDs and the same generated access policy.
use serde_json::{Value, json};
fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
fn object(properties: Value, required: &[&str]) -> Value {
    json!({"type":"object","additionalProperties":false,"properties":properties,"required":required})
}
pub(super) fn extend(doc: &mut Value) {
    let schemas = &mut doc["components"]["schemas"];
    let id = json!({"type":"string","pattern":"^[1-9][0-9]{0,18}$"});
    let optional_id =
        json!({"type":["string","null"],"pattern":"^[1-9][0-9]{0,18}$","default":null});
    let text = json!({"type":"string","maxLength":120});
    let integer = json!({"type":"integer","format":"int64","minimum":0});
    schemas["IdentityEffectivePeriod"] = object(
        json!({"startsOn":{"type":"string","format":"date"},"endsOn":{"type":["string","null"],"format":"date","default":null}}),
        &["startsOn"],
    );
    schemas["IdentityAssignment"] = object(
        json!({"id":optional_id,"appointmentId":{"type":"string","minLength":1,"maxLength":64},"projectId":optional_id,"role":{"type":"string","enum":["","Member","Leader"],"default":""},"period":reference("IdentityEffectivePeriod")}),
        &["appointmentId", "period"],
    );
    schemas["IdentityDepartmentOption"] = object(
        json!({"id":id,"code":text,"name":text}),
        &["id", "code", "name"],
    );
    schemas["IdentityAppointmentOption"] = object(
        json!({"id":{"type":"string"},"departmentId":id,"departmentCode":text,"jobTitle":text,"positionId":optional_id,"isPrimary":{"type":"boolean"}}),
        &[
            "id",
            "departmentId",
            "departmentCode",
            "jobTitle",
            "positionId",
            "isPrimary",
        ],
    );
    schemas["IdentityPersonOption"] = object(
        json!({"personId":id,"name":text,"employeeNumber":text,"appointments":{"type":"array","items":reference("IdentityAppointmentOption")}}),
        &["personId", "name", "employeeNumber", "appointments"],
    );
    schemas["IdentityCandidatePage"] = object(
        json!({"items":{"type":"array","items":reference("IdentityPersonOption")},"totalCount":integer,"pageNumber":integer,"pageSize":integer,"departments":{"type":"array","items":reference("IdentityDepartmentOption")},"departmentCount":integer}),
        &[
            "items",
            "totalCount",
            "pageNumber",
            "pageSize",
            "departments",
            "departmentCount",
        ],
    );
    let mut position = object(
        json!({"id":optional_id,"unitId":id,"departmentId":optional_id,"code":text,"name":text,"isActive":{"type":"boolean"},"expectedVersion":integer,"note":{"type":"string","minLength":1,"maxLength":500}}),
        &[
            "unitId",
            "code",
            "name",
            "isActive",
            "expectedVersion",
            "note",
        ],
    );
    let mut employment = object(
        json!({"id":optional_id,"unitId":id,"personId":id,"employeeNumber":text,"stage":{"type":"string","enum":["Probation","Regular","Departed"]},"period":reference("IdentityEffectivePeriod"),"expectedVersion":integer,"note":{"type":"string","minLength":1,"maxLength":500}}),
        &[
            "unitId",
            "personId",
            "employeeNumber",
            "stage",
            "period",
            "expectedVersion",
            "note",
        ],
    );
    let mut responsibility = object(
        json!({"id":optional_id,"unitId":id,"departmentId":optional_id,"code":text,"name":text,"workflowKinds":{"type":"array","items":{"type":"string","enum":["leave","overtime","expense","travel","purchase","general","room","supply","personnel"]},"minItems":1,"maxItems":9,"uniqueItems":true},"assignments":{"type":"array","items":reference("IdentityAssignment"),"maxItems":200},"isActive":{"type":"boolean"},"expectedVersion":integer,"note":{"type":"string","minLength":1,"maxLength":500}}),
        &[
            "unitId",
            "code",
            "name",
            "workflowKinds",
            "assignments",
            "isActive",
            "expectedVersion",
            "note",
        ],
    );
    let mut project = object(
        json!({"id":optional_id,"unitId":id,"code":text,"name":text,"ownerAppointmentId":{"type":"string","minLength":1,"maxLength":64},"members":{"type":"array","items":reference("IdentityAssignment"),"maxItems":200},"isActive":{"type":"boolean"},"expectedVersion":integer,"note":{"type":"string","minLength":1,"maxLength":500}}),
        &[
            "unitId",
            "code",
            "name",
            "ownerAppointmentId",
            "members",
            "isActive",
            "expectedVersion",
            "note",
        ],
    );
    for (name, model) in [
        ("Position", &mut position),
        ("Employment", &mut employment),
        ("Responsibility", &mut responsibility),
        ("Project", &mut project),
    ] {
        if name != "Employment" {
            schemas[format!("Identity{name}Save")] = model.clone();
        }
        let properties = model["properties"].as_object_mut().unwrap();
        properties.remove("expectedVersion");
        properties.remove("note");
        properties.insert("id".into(), id.clone());
        properties.insert("versionNumber".into(), integer.clone());
        if name == "Employment" {
            properties.insert("identityId".into(), id.clone());
        }
        if name == "Position" {
            properties.insert("appointmentCount".into(), integer.clone());
        }
        model["required"] = json!(properties.keys().cloned().collect::<Vec<_>>());
        schemas[format!("Identity{name}")] = model.clone();
        schemas[format!("Identity{name}Page")] = object(
            json!({"items":{"type":"array","items":reference(&format!("Identity{name}"))},"totalCount":integer,"pageNumber":integer,"pageSize":integer}),
            &["items", "totalCount", "pageNumber", "pageSize"],
        );
    }
    for (kind, name, capability) in [
        ("positions", "Position", "organization.appointments"),
        ("employments", "Employment", "office.people"),
        (
            "responsibilities",
            "Responsibility",
            "organization.appointments",
        ),
        ("projects", "Project", "organization.appointments"),
    ] {
        for (method, action, response) in [
            ("get", "list", format!("Identity{name}Page")),
            ("post", "save", format!("Identity{name}")),
        ] {
            if name == "Employment" && action == "save" {
                continue;
            }
            let mut endpoint = doc["paths"]["/api/organization/unit-tasks"]["get"].clone();
            endpoint["operationId"] = json!(if action == "list" {
                format!(
                    "List{}",
                    if name == "Responsibility" {
                        "Responsibilities".into()
                    } else {
                        format!("{name}s")
                    }
                )
            } else {
                format!("Save{name}")
            });
            endpoint["x-exportdoc-foundation"] = json!({"kind":kind,"action":action});
            endpoint["x-exportdoc-policy"]["requirements"] = if capability == "office.people" {
                if action == "list" {
                    json!([{"resourceKey":"office.people","action":"view-details"}])
                } else {
                    json!([{"resourceKey":"office.people","action":"view-details"},{"resourceKey":"organization.appointments","action":"manage"}])
                }
            } else if action == "list" {
                json!([{"resourceKey":"organization.directory","action":"view"}])
            } else {
                json!([{"resourceKey":capability,"action":"manage"}])
            };
            endpoint["parameters"] = if action == "list" {
                json!([{"name":"unitId","in":"query","schema":id},{"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},{"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}])
            } else {
                json!([])
            };
            if action == "save" {
                endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference(&format!("Identity{name}Save"))}}});
            }
            endpoint["responses"]["200"]["content"]["application/json"]["schema"] =
                reference(&response);
            doc["paths"][format!("/api/organization/{kind}")][method] = endpoint;
        }
    }
    let mut endpoint = doc["paths"]["/api/organization/positions"]["get"].clone();
    endpoint["operationId"] = json!("GetIdentityCandidates");
    endpoint["x-exportdoc-foundation"] = json!({"kind":"candidates","action":"list"});
    endpoint["parameters"]
        .as_array_mut()
        .unwrap()
        .push(json!({"name":"keyword","in":"query","schema":{"type":"string","maxLength":100}}));
    endpoint["responses"]["200"]["content"]["application/json"]["schema"] =
        reference("IdentityCandidatePage");
    doc["paths"]["/api/organization/identity-candidates"]["get"] = endpoint;
}
