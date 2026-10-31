use serde_json::{Value, json};
fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
pub(super) fn extend(doc: &mut Value) {
    let id = json!({"type":"string","pattern":"^[1-9][0-9]{0,18}$"});
    let properties = json!({"principalKind":{"type":"string","enum":["Account","Unit"]},"principalId":id,"resourceKey":{"type":"string"},"action":{"type":"string"},"mode":{"type":"string","enum":["Deny","Unrestricted","Units","Departments","Projects","Participation"]},"members":{"type":"array","items":id,"maxItems":200,"uniqueItems":true},"reason":{"type":"string","minLength":1,"maxLength":500},"expectedVersion":{"type":"integer","format":"int64","minimum":0}});
    doc["components"]["schemas"]["AccessRestrictionSave"] = json!({"type":"object","additionalProperties":false,"properties":properties,"required":["principalKind","principalId","resourceKey","action","mode","members","reason","expectedVersion"]});
    let mut record = properties;
    record.as_object_mut().unwrap().remove("expectedVersion");
    record["id"] = id.clone();
    record["versionNumber"] = json!({"type":"integer","format":"int64"});
    record["members"] =
        json!({"type":"array","items":{"type":"object","properties":{"id":id},"required":["id"]}});
    doc["components"]["schemas"]["AccessRestrictionRecord"] = json!({"type":"object","properties":record,"required":["id","resourceKey","action","mode","members","reason","versionNumber"]});
    doc["components"]["schemas"]["AccessRestrictionList"] = json!({"type":"object","properties":{"items":{"type":"array","items":reference("AccessRestrictionRecord")}},"required":["items"]});
    for (method, operation, action, response) in [
        (
            "get",
            "GetAccessRestrictions",
            "list",
            "AccessRestrictionList",
        ),
        (
            "post",
            "SaveAccessRestriction",
            "save",
            "AccessRestrictionRecord",
        ),
    ] {
        let mut endpoint = doc["paths"]["/api/organization/unit-tasks"]["get"].clone();
        endpoint["operationId"] = json!(operation);
        endpoint["x-exportdoc-foundation"] = json!({"kind":"access-restrictions","action":action});
        endpoint["x-exportdoc-policy"]["requirements"] =
            json!([{"resourceKey":"organization.roles","action":"manage"}]);
        endpoint["parameters"] = if action == "list" {
            json!([{"name":"principalKind","in":"query","required":true,"schema":{"type":"string","enum":["Account","Unit"]}},{"name":"principalId","in":"query","required":true,"schema":id}])
        } else {
            json!([])
        };
        if action == "save" {
            endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":reference("AccessRestrictionSave")}}});
        }
        endpoint["responses"]["200"]["content"]["application/json"]["schema"] = reference(response);
        doc["paths"]["/api/organization/access-restrictions"][method] = endpoint;
    }
}
