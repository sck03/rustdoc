//! Independent, reviewed additions; all other authentication and role metadata stay frozen.
use serde_json::{Value, json};

pub(crate) fn apply(baseline: &mut Value) {
    baseline["components"]["schemas"]["SiteNotification"]["properties"]["actionRequired"] =
        json!({"type":"boolean","readOnly":true});
    baseline["components"]["schemas"]["SiteNotification"]["properties"]["requestStatus"] =
        json!({"type":"string","readOnly":true});
    baseline["components"]["schemas"]["ApiChangePasswordRequest"] = json!({
        "type":"object", "properties":{
            "currentPassword":{"type":"string","maxLength":128},
            "newPassword":{"type":"string","minLength":8,"maxLength":128}
        }, "required":["currentPassword","newPassword"]
    });
    let mut change = baseline["paths"]["/api/auth/logout"]["post"].clone();
    change["operationId"] = json!("ChangeOwnPassword");
    change["requestBody"] = json!({"required":true,"content":{"application/json":{
        "schema":{"$ref":"#/components/schemas/ApiChangePasswordRequest"}
    }},"x-exportdoc-max-bytes":4096});
    for (status, description) in [
        ("400", "Bad Request"),
        ("409", "Conflict"),
        ("413", "Payload Too Large"),
        ("429", "Too Many Requests"),
        ("503", "Service Unavailable"),
    ] {
        change["responses"][status] = json!({"description":description,"content":{"application/json":{
            "schema":{"$ref":"#/components/schemas/ApiErrorResponse"}
        }}});
    }
    baseline["paths"]["/api/auth/password"] = json!({"post":change});
    let finance = baseline["x-exportdoc-permissions"]["roles"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|role| role["code"] == "Finance")
        .unwrap();
    finance["description"] =
        json!("查看本公司费用报销并登记财务接收，维护本人付款和申请；审批决定另行授权。");
    let view = finance["grants"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|grant| grant["resourceKey"] == "office.expenses" && grant["action"] == "view")
        .unwrap();
    assert_eq!(view["dataScope"], "own");
    view["dataScope"] = json!("company");
}
