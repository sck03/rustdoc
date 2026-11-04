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
#[track_caller]
pub fn request(service: &NativeService, token: &str, op: Operation, body: Option<Value>) -> Value {
    call(service, token, op, 0, body).unwrap_or_else(|error| panic!("{}: {error:?}", op.id))
}
#[track_caller]
pub fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    body: Option<Value>,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let parameters = if id > 0 {
        vec![("id", id.to_string())]
    } else {
        vec![]
    };
    let body = body.map(|value| {
        if op == PUBLISH_OA_DEFINITION {
            publication_body(service, token, value)
        } else {
            value
        }
    });
    query(service, token, op, &parameters, &[], body)
}
pub fn query(
    service: &NativeService,
    token: &str,
    op: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let body = body.map(|value| {
        let mut defaults = contracts::object(op.id, true);
        if op == SIMULATE_OA_DEFINITION && value.get("reporting").is_none() {
            defaults.as_object_mut().unwrap().remove("reporting");
        }
        contracts::overlay(defaults, &value)
    });
    dispatch(service, token, op, parameters, query, body)
}
pub fn dispatch(
    service: &NativeService,
    token: &str,
    op: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let bytes = service.dispatch(op, parameters, query, body, token)?;
    let value = serde_json::from_slice(&bytes).unwrap();
    export_doc_contracts::validation::response(op.id, &value).unwrap();
    Ok(value)
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
    // A published sample needs an applicant independent of the signing person.
    // The fixture establishes that real public identity through the normal API.
    let department = active_department(service, admin);
    request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("SAMPLE-{}",nonce().unwrap()),"departmentId":department,"jobTitle":"流程仿真申请人","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":"独立发布样例"}}),
        ),
    );
    let mut settings = request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    for rule in settings["rules"].as_array_mut().unwrap() {
        rule["stages"] = json!([node("review", &[], "Named", json!([reviewer]))]);
    }
    publish_all(service, admin, settings)
}
/// Exercise a formal change through its published workflow, never through a test bypass.
pub fn personnel_change(
    service: &NativeService,
    admin: &str,
    action: &str,
    id: i64,
    body: Option<Value>,
) -> Result<Value, export_doc_engine::api::ApiError> {
    let body = body.unwrap();
    let mut change = body.clone();
    change["action"] = json!(action);
    change["expectedPersonnelVersion"] = body["expectedVersion"].clone();
    if let Some(value) = body.get("appointmentId") {
        change["primaryAppointmentId"] = value.clone();
    }
    for key in ["expectedVersion", "note", "appointmentId"] {
        change.as_object_mut().unwrap().remove(key);
    }
    let draft = call(
        service,
        admin,
        CREATE_PERSONNEL_CHANGE_REQUEST,
        0,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeId":id,"title":"人员变动合同验收","reason":body["note"],"personnelChange":change}),
        ),
    )?;
    let (reviewer_id, reviewer) = reviewer(service, admin);
    let mut settings = request(service, admin, GET_OA_APPROVAL_SETTINGS, None);
    let definition = format!("personnel:{action}");
    let rule = settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == definition)
        .unwrap();
    rule["stages"] = json!([node(
        "independent-review",
        &[],
        "Named",
        json!([reviewer_id])
    )]);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let saved = request(service, admin, SAVE_OA_APPROVAL_SETTINGS, Some(settings));
    request(
        service,
        admin,
        PUBLISH_OA_DEFINITION,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":definition})),
    );
    let request_id = draft["id"].as_i64().unwrap();
    let command = |token: &str, op: Operation, row: &Value| {
        call(
            service,
            token,
            op,
            request_id,
            decision_body(
                service,
                token,
                op,
                request_id,
                Some(json!({"expectedVersion":row["versionNumber"],"note":"独立业务身份签署"})),
            ),
        )
    };
    let pending = command(admin, SUBMIT_PERSONNEL_CHANGE_REQUEST, &draft)?;
    match command(&reviewer, APPROVE_PERSONNEL_CHANGE_REQUEST, &pending) {
        Ok(_) => call(service, admin, GET_PERSONNEL, id, None),
        Err(error) => {
            let withdrawn = command(admin, WITHDRAW_PERSONNEL_CHANGE_REQUEST, &pending).unwrap();
            command(admin, CANCEL_PERSONNEL_CHANGE_REQUEST, &withdrawn).unwrap();
            Err(error)
        }
    }
}
pub fn publish_scheme(service: &NativeService, admin: &str, mut scheme: Value) -> Value {
    let result = request(
        service,
        admin,
        PUBLISH_PERMISSION_SCHEME,
        Some(
            json!({"schemeId":scheme["id"].to_string(),"expectedVersion":scheme["versionNumber"],"note":"明确发布匿名验收方案","commandKey":nonce().unwrap()}),
        ),
    );
    scheme["versionNumber"] = result["versionNumber"].clone();
    scheme["publishedVersion"] = result["version"].clone();
    scheme
}
pub fn apply_scheme(
    service: &NativeService,
    admin: &str,
    scheme: &Value,
    kind: &str,
    target: &Value,
) -> Result<Value, export_doc_engine::api::ApiError> {
    call(
        service,
        admin,
        APPLY_PERMISSION_SCHEME_VERSION,
        0,
        Some(
            json!({"schemeId":scheme["id"].to_string(),"version":scheme["publishedVersion"],"targets":[{"kind":kind,"id":target["id"].to_string(),"expectedVersion":target["versionNumber"]}],"note":"明确应用已发布版本","commandKey":nonce().unwrap()}),
        ),
    )
}
pub fn reviewer(service: &NativeService, admin: &str) -> (i64, String) {
    let current = request(service, admin, GET_CURRENT_USER, None);
    let username = format!("workflow-review-{}", nonce().unwrap());
    let department = active_department(service, admin);
    let mut grants = export_doc_domain::permissions::catalog()
        .resources
        .iter()
        .filter(|r| r.workflow_resource)
        .flat_map(|r| {
            r.actions
                .iter()
                .filter(|a| matches!(a.key.as_str(), "view" | "approve"))
                .map(|a| json!({"resourceKey":r.key,"action":a.key,"dataScope":"company"}))
        })
        .collect::<Vec<_>>();
    for grant in export_doc_domain::permissions::role_grants("OfficeManager").unwrap() {
        if export_doc_domain::permissions::catalog()
            .resources
            .iter()
            .any(|resource| resource.key == grant.resource_key && resource.is_technical)
        {
            continue;
        }
        if !grants
            .iter()
            .any(|g| g["resourceKey"] == grant.resource_key && g["action"] == grant.action)
        {
            grants.push(serde_json::to_value(grant).unwrap());
        }
    }
    if !grants
        .iter()
        .any(|g| g["resourceKey"] == "office.notifications" && g["action"] == "view")
    {
        grants
            .push(json!({"resourceKey":"office.notifications","action":"view","dataScope":"own"}));
    }
    if !grants
        .iter()
        .any(|g| g["resourceKey"] == "office.people" && g["action"] == "view-details")
    {
        grants.push(
            json!({"resourceKey":"office.people","action":"view-details","dataScope":"company"}),
        );
    }
    let user = request(
        service,
        admin,
        CREATE_USER_ACCOUNT,
        Some(
            json!({"username":username,"fullName":"独立审批验收","role":"OfficeManager","companyScope":current["companyScope"],"departmentId":department,"permissionGrants":grants,"isActive":true,"resetPassword":"Workflow-Review-2026"}),
        ),
    );
    let person = request(
        service,
        admin,
        CREATE_PERSONNEL,
        Some(
            json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("REVIEW-{}",&nonce().unwrap()[..8]),"departmentId":department,"jobTitle":"审批验收人","hireDate":"2026-01-01","employmentType":"FullTime","onProbation":false,"profile":{"fullName":"独立审批验收"}}),
        ),
    );
    call(service,admin,LINK_PERSONNEL_ACCOUNT,person["employee"]["id"].as_i64().unwrap(),Some(json!({"expectedVersion":person["versionNumber"],"userId":user["user"]["id"],"expectedAccountVersion":user["user"]["versionNumber"]}))).unwrap();
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
fn active_department(service: &NativeService, admin: &str) -> Value {
    let current = request(service, admin, GET_CURRENT_USER, None);
    let directory = request(service, admin, GET_ORGANIZATION_DIRECTORY, None);
    directory["departments"].as_array().and_then(|rows|rows.iter().find(|row|row["companyCode"]==current["companyScope"]&&row["isActive"]==true)).map(|row|row["code"].clone()).unwrap_or_else(||request(service,admin,CREATE_ORGANIZATION_DEPARTMENT,Some(json!({"code":format!("REVIEW-{}",&nonce().unwrap()[..8]),"name":"审批验收部门","companyCode":current["companyScope"],"isActive":true})))["code"].clone())
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
    if op == PUBLISH_OA_DEFINITION {
        return Some(publication_body(service, token, body));
    }
    let metadata = &contracts::contract()["operations"][op.id]["office"];
    if matches!(
        metadata["action"].as_str(),
        Some(
            "submit"
                | "approve"
                | "reject"
                | "return"
                | "withdraw"
                | "cancel"
                | "void"
                | "complete"
                | "approval-reassign"
                | "resource-submit"
                | "workflow-command"
                | "personnel-execute"
                | "personnel-cancel"
        )
    ) || matches!(
        op,
        APPROVE_MEETING_BOOKING
            | REJECT_MEETING_BOOKING
            | APPROVE_OFFICE_SUPPLY_REQUEST
            | REJECT_OFFICE_SUPPLY_REQUEST
            | CANCEL_MEETING_BOOKING
            | ISSUE_MEETING_ROOM_KEY
            | RETURN_MEETING_ROOM_KEY
            | CANCEL_OFFICE_SUPPLY_REQUEST
            | ISSUE_OFFICE_SUPPLY
            | RETURN_OFFICE_SUPPLY
    ) {
        if body["commandKey"].is_null() {
            body["commandKey"] = json!(nonce().unwrap());
        }
    }
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
/// Old scenario fixtures now submit an explicit, successfully simulated sample.
/// Negative publication tests call dispatch directly so no invalid request is repaired.
pub fn publication_body(service: &NativeService, token: &str, mut body: Value) -> Value {
    if body["commandKey"].as_str().is_none_or(|key| key.is_empty()) {
        body["commandKey"] = json!(nonce().unwrap());
    }
    if body["samples"]
        .as_array()
        .is_some_and(|samples| !samples.is_empty())
    {
        return body;
    }
    let fetch = |op, query: &[(&str, String)]| -> Result<Value, export_doc_engine::api::ApiError> {
        Ok(serde_json::from_slice(&service.dispatch(op, &[], query, None, token)?).unwrap())
    };
    let current = fetch(GET_CURRENT_USER, &[]).unwrap();
    let settings = match fetch(GET_OA_APPROVAL_SETTINGS, &[]) {
        Ok(value) => value,
        Err(error) if error.status == Some(403) => return body,
        Err(error) => panic!("Publication fixture settings: {error}"),
    };
    let rule = settings["rules"]
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["key"] == body["definitionKey"])
        .unwrap();
    let people = fetch(
        LIST_ORGANIZATION_MANAGERS,
        &[
            ("companyCode", text_value(&current, "companyScope")),
            ("pageSize", "100".into()),
        ],
    )
    .unwrap();
    let mut failures = vec![];
    for (person, appointment) in
        people["items"]
            .as_array()
            .into_iter()
            .flatten()
            .flat_map(|person| {
                person["appointments"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .map(move |appointment| (person, appointment))
            })
    {
        let sample = json!({"employeeId":person["id"],"appointmentId":appointment["id"],"amount":"0","currency":"CNY","duration":"1","category":if rule["kind"]=="leave"{"Annual"}else{"Other"},"isReturnable":true});
        let mut simulation = sample.clone();
        simulation["definition"] = rule.clone();
        match service.dispatch(SIMULATE_OA_DEFINITION, &[], &[], Some(simulation), token) {
            Ok(_) => {
                body["samples"] = json!([sample]);
                return body;
            }
            Err(error) if matches!(error.status, Some(400 | 403 | 409)) => {
                failures.push(error.message);
            }
            Err(error) => panic!("Publication sample failed: {error}"),
        }
    }
    panic!(
        "No valid publication sample for {} in {}: {:?}",
        body["definitionKey"], current["companyScope"], failures
    );
}
fn text_value(value: &Value, key: &str) -> String {
    value[key].as_str().unwrap_or("").into()
}
