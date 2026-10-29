//! Explicit published-node fixtures shared by workflow regression contracts.
#![allow(dead_code)]
use export_doc_engine::{contracts, engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};
pub fn node(key: &str, deps: &[&str], source: &str, ids: Value) -> Value {
    json!({"key":key,"name":key,"dependsOn":deps,"source":source,"approverUserIds":ids,"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":48})
}
pub fn rule(kind: &str, variant: &str, stages: Value) -> Value {
    let key = format!(
        "{kind}{}",
        if variant.is_empty() {
            String::new()
        } else {
            format!(":{variant}")
        }
    );
    json!({"key":key,"kind":kind,"variant":variant,"name":key,"stages":stages,"notifyUserIds":[]})
}
pub fn request(service: &NativeService, token: &str, op: Operation, body: Option<Value>) -> Value {
    serde_json::from_slice(&service.dispatch(op, &[], &[], body, token).unwrap()).unwrap()
}
pub fn publish_all(service: &NativeService, token: &str, mut settings: Value) -> Value {
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let mut saved = request(service, token, SAVE_OA_APPROVAL_SETTINGS, Some(settings));
    let keys: Vec<_> = saved["rules"]
        .as_array()
        .unwrap()
        .iter()
        .map(|r| r["key"].clone())
        .collect();
    for key in keys {
        saved = request(
            service,
            token,
            PUBLISH_OA_DEFINITION,
            Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":key})),
        );
    }
    saved
}
pub fn configure(service: &NativeService, admin: &str, reviewer: i64) -> Value {
    let mut settings = request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    for rule in settings["rules"].as_array_mut().unwrap() {
        rule["stages"] = json!([node("review", &[], "Named", json!([reviewer]))]);
    }
    publish_all(service, admin, settings)
}
pub fn reviewer(service: &NativeService, admin: &str) -> (i64, String) {
    let current = request(service, admin, GET_CURRENT_USER, None);
    let username = format!("workflow-review-{}", nonce().unwrap());
    let user = request(
        service,
        admin,
        CREATE_USER_ACCOUNT,
        Some(
            json!({"username":username,"fullName":"独立审批验收","role":"Admin","companyScope":current["companyScope"],"departmentId":current["departmentId"],"isActive":true,"resetPassword":"Workflow-Review-2026"}),
        ),
    );
    let login = request(
        service,
        "",
        LOGIN,
        Some(json!({"username":username,"password":"Workflow-Review-2026"})),
    );
    (
        user["user"]["id"].as_i64().unwrap(),
        login["accessToken"].as_str().unwrap().into(),
    )
}
/// Test clients select a server-authorized task just as the detail form does.
pub fn decision_body(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    body: Option<Value>,
) -> Option<Value> {
    let mut body = body?;
    let resource_list = match op {
        APPROVE_MEETING_BOOKING | REJECT_MEETING_BOOKING => Some(LIST_MEETING_BOOKINGS),
        APPROVE_OFFICE_SUPPLY_REQUEST | REJECT_OFFICE_SUPPLY_REQUEST => {
            Some(LIST_OFFICE_SUPPLY_REQUESTS)
        }
        _ => None,
    };
    if let Some(list) = resource_list {
        if id > 0 && !body["taskKey"].as_str().is_some_and(|s| !s.is_empty()) {
            if let Ok(bytes) =
                service.dispatch(list, &[], &[("requestId", id.to_string())], None, token)
            {
                let page: Value = serde_json::from_slice(&bytes).unwrap();
                body["taskKey"] = page["items"][0]["reviewTaskKeys"][0].clone();
            }
        }
        return Some(body);
    }
    let meta = &contracts::contract()["operations"][op.id]["office"];
    let review = matches!(
        meta["action"].as_str(),
        Some("approve" | "reject" | "return" | "approval-reassign")
    );
    if id <= 0 || !review || body["taskKey"].as_str().is_some_and(|s| !s.is_empty()) {
        return Some(body);
    }
    let get = ALL_OPERATIONS.iter().find(|o| {
        let m = &contracts::contract()["operations"][o.id]["office"];
        m["kind"] == meta["kind"] && m["action"] == "get"
    });
    if let Some(get) = get {
        if let Ok(bytes) = service.dispatch(*get, &[("id", id.to_string())], &[], None, token) {
            let row: Value = serde_json::from_slice(&bytes).unwrap();
            body["taskKey"] = if meta["action"] == "approval-reassign" {
                row["approvalPlan"]["steps"]
                    .as_array()
                    .and_then(|ss| {
                        ss.iter().find(|s| {
                            s["status"] == "Waiting" && s["approverUserId"] == body["fromUserId"]
                        })
                    })
                    .map(|s| s["taskKey"].clone())
                    .unwrap_or(json!(""))
            } else {
                row["reviewTaskKeys"][0].clone()
            };
        }
    }
    Some(body)
}
