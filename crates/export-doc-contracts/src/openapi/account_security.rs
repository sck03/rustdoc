use serde_json::{Value, json};
pub(super) fn extend(doc: &mut Value) {
    let id = json!({"type":"string","pattern":"^[1-9][0-9]{0,18}$"});
    doc["components"]["schemas"]["AccountSecuritySuspension"] = json!({"type":"object","additionalProperties":false,"properties":{"accountId":id,"expectedVersion":{"type":"integer","format":"int64","minimum":1},"note":{"type":"string","minLength":1,"maxLength":500},"commandKey":{"type":"string","minLength":8,"maxLength":128}},"required":["accountId","expectedVersion","note","commandKey"]});
    doc["components"]["schemas"]["AccountSecurityResult"] = json!({"type":"object","properties":{"accountId":id,"versionNumber":{"type":"integer","format":"int64"},"isActive":{"type":"boolean"},"handoverRequired":{"type":"boolean"}},"required":["accountId","versionNumber","isActive","handoverRequired"]});
    let mut endpoint = doc["paths"]["/api/organization/unit-tasks"]["get"].clone();
    endpoint["operationId"] = json!("SuspendAccountForSecurity");
    endpoint["x-exportdoc-foundation"] = json!({"kind":"account-security","action":"suspend"});
    endpoint["x-exportdoc-policy"]["requirements"] =
        json!([{"resourceKey":"system.users","action":"manage"}]);
    endpoint["parameters"] = json!([]);
    endpoint["requestBody"] = json!({"required":true,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/AccountSecuritySuspension"}}}});
    endpoint["responses"]["200"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/AccountSecurityResult"});
    doc["paths"]["/api/organization/account-security"]["post"] = endpoint;
}
