use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};
use std::sync::{Arc, Barrier};

pub fn operation(kind: &str, action: &str) -> Operation {
    *ALL_OPERATIONS
        .iter()
        .find(|op| {
            let metadata = &contracts::contract()["operations"][op.id]["office"];
            metadata["kind"] == kind && metadata["action"] == action
        })
        .unwrap()
}
pub fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let parameters = if id > 0 {
        vec![("id", id.to_string())]
    } else {
        vec![]
    };
    let result: Value =
        serde_json::from_slice(&service.dispatch(op, &parameters, &[], body, token)?).unwrap();
    export_doc_contracts::validation::response(op.id, &result).unwrap();
    Ok(result)
}
fn act(service: &NativeService, token: &str, kind: &str, action: &str, row: &Value) -> Value {
    call(
        service,
        token,
        operation(kind, action),
        row["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":row["versionNumber"],"note":"流程验收说明"})),
    )
    .unwrap()
}

fn finance_user(
    service: &NativeService,
    admin: &str,
    role: &str,
    company: &str,
    department: &str,
) -> String {
    let username = format!("finance-{}", nonce().unwrap());
    call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":username,"fullName":"财务验收","role":role,"companyScope":company,"departmentId":department,"isActive":true,"resetPassword":"Finance-Review-2026"}))).unwrap();
    call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":username,"password":"Finance-Review-2026"})),
    )
    .unwrap()["accessToken"]
        .as_str()
        .unwrap()
        .into()
}
fn finance_list(
    service: &NativeService,
    token: &str,
    status: &str,
    finance: bool,
) -> Result<Value, ApiError> {
    let bytes = service.dispatch(
        operation("expense", "list"),
        &[],
        &[
            ("mineOnly", "false".into()),
            ("financeOnly", finance.to_string()),
            ("status", status.into()),
        ],
        None,
        token,
    )?;
    Ok(serde_json::from_slice(&bytes).unwrap())
}
fn receive_expense(service: &NativeService, admin: &str, finance: &str, row: &Value) -> Value {
    let id = row["id"].as_i64().unwrap();
    assert_eq!(
        finance_list(service, finance, "Approved", true).unwrap()["items"][0]["id"],
        id
    );
    assert_eq!(
        finance_list(service, finance, "", false).unwrap()["totalCount"],
        0
    );
    assert_eq!(
        finance_list(service, finance, "Pending", true)
            .unwrap_err()
            .status,
        Some(400)
    );
    assert_eq!(
        call(service, finance, operation("expense", "get"), id, None).unwrap()["id"],
        id
    );
    let body =
        json!({"expectedVersion":row["versionNumber"],"note":"凭证已核对，接收编号 FIN-001"});
    assert_eq!(
        call(
            service,
            finance,
            operation("expense", "approve"),
            id,
            Some(body.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let manager = finance_user(service, admin, "OfficeManager", "DEFAULT", "GENERAL");
    assert_eq!(
        call(
            service,
            &manager,
            operation("expense", "complete"),
            id,
            Some(body.clone())
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let company = format!("FIN-{}", &nonce().unwrap()[..8]);
    call(
        service,
        admin,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"隔离财务公司","isActive":true})),
    )
    .unwrap();
    call(
        service,
        admin,
        CREATE_ORGANIZATION_DEPARTMENT,
        0,
        Some(json!({"code":company,"companyCode":company,"name":"财务部","isActive":true})),
    )
    .unwrap();
    let other = finance_user(service, admin, "Finance", &company, &company);
    assert_eq!(
        finance_list(service, &other, "Approved", true).unwrap()["totalCount"],
        0
    );
    assert_eq!(
        call(service, &other, operation("expense", "get"), id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let attachment = row["attachments"][0]["id"].to_string();
    assert!(
        service
            .download_file(
                operation("expense", "download"),
                &[("id", id.to_string()), ("attachmentId", attachment)],
                finance
            )
            .is_ok()
    );
    let received = call(
        service,
        finance,
        operation("expense", "complete"),
        id,
        Some(body.clone()),
    )
    .unwrap();
    assert_eq!(
        call(
            service,
            finance,
            operation("expense", "complete"),
            id,
            Some(body)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        finance_list(service, finance, "Approved", true).unwrap()["totalCount"],
        0
    );
    assert_eq!(
        finance_list(service, finance, "HandedOff", true).unwrap()["items"][0]["id"],
        id
    );
    let history = call(service, finance, operation("expense", "history"), id, None).unwrap();
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["action"] == "complete"
                && e["actorName"] == "财务验收"
                && e["note"] == "凭证已核对，接收编号 FIN-001")
    );
    received
}

pub fn exercise(service: &Arc<NativeService>, admin: &str, applicant: &str, employee: Option<i64>) {
    let finance = finance_user(service, admin, "Finance", "DEFAULT", "GENERAL");
    let date = (chrono::Utc::now() - chrono::Duration::days(1))
        .date_naive()
        .to_string();
    let ends = chrono::Utc::now() - chrono::Duration::hours(2);
    let starts = ends - chrono::Duration::hours(2);
    for kind in [
        "leave", "overtime", "expense", "travel", "purchase", "general",
    ] {
        let details = match kind {
            "leave" => {
                json!({"leave":{"category":"Annual","startsOn":date,"endsOn":date,"startPeriod":"AM","endPeriod":"PM"}})
            }
            "overtime" => {
                json!({"overtime":{"startsAt":starts.to_rfc3339_opts(chrono::SecondsFormat::Secs,true),"endsAt":ends.to_rfc3339_opts(chrono::SecondsFormat::Secs,true),"location":"办公室"}})
            }
            "expense" => {
                json!({"currency":"CNY","lines":[{"category":"Travel","spentOn":date,"description":"交通票据","amount":"0.10"},{"category":"Meals","spentOn":date,"description":"工作餐","amount":"0.20"}]})
            }
            "travel" => json!({"travel":{"destination":"上海","startsOn":date,"endsOn":date}}),
            "purchase" => {
                json!({"currency":"CNY","purchaseLines":[{"name":"办公纸","quantity":"2","unit":"包","unitPrice":"18.25"}]})
            }
            _ => json!({"category":"IT"}),
        };
        let mut body = json!({"requestKey":nonce().unwrap(),"title":format!("{kind}流程验收"),"reason":"测试内部审批与附件闭环","employeeId":employee});
        body.as_object_mut()
            .unwrap()
            .extend(details.as_object().unwrap().clone());
        let mut row = call(
            service,
            applicant,
            operation(kind, "create"),
            0,
            Some(body.clone()),
        )
        .unwrap();
        let id = row["id"].as_i64().unwrap();
        if kind == "expense" {
            assert_eq!(
                call(service, &finance, operation(kind, "get"), id, None)
                    .unwrap_err()
                    .status,
                Some(403)
            );
        }
        assert!(row.get("identity").is_none());
        assert!(row.get("submissionDigest").is_none());
        assert_eq!(
            call(
                service,
                applicant,
                operation(kind, "create"),
                0,
                Some(body.clone())
            )
            .unwrap()["id"],
            id
        );
        let mut different = body.clone();
        different["title"] = json!("不同申请");
        assert_eq!(
            call(
                service,
                applicant,
                operation(kind, "create"),
                0,
                Some(different)
            )
            .unwrap_err()
            .status,
            Some(409)
        );
        if kind == "expense" {
            assert_eq!(row["totalAmount"], "0.30");
            assert_eq!(
                call(
                    service,
                    applicant,
                    operation(kind, "submit"),
                    id,
                    Some(json!({"expectedVersion":row["versionNumber"],"note":"提交"}))
                )
                .unwrap_err()
                .status,
                Some(400)
            );
            let bytes = b"%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n";
            row = service
                .upload(
                    operation(kind, "upload"),
                    &[("id", id.to_string())],
                    json!({"expectedVersion":row["versionNumber"]}),
                    "receipt.pdf",
                    bytes,
                    applicant,
                )
                .unwrap();
            let attachment = row["attachments"][0]["id"].as_i64().unwrap();
            let file = service
                .download_file(
                    operation(kind, "download"),
                    &[
                        ("id", id.to_string()),
                        ("attachmentId", attachment.to_string()),
                    ],
                    applicant,
                )
                .unwrap();
            assert_eq!(file.content, bytes);
            assert_eq!(
                service
                    .upload(
                        operation(kind, "upload"),
                        &[("id", id.to_string())],
                        json!({"expectedVersion":row["versionNumber"]}),
                        "receipt.png",
                        bytes,
                        applicant
                    )
                    .unwrap_err()
                    .status,
                Some(400)
            );
        }
        row = act(service, applicant, kind, "submit", &row);
        assert_eq!(row["status"], "Pending");
        if kind == "expense" {
            assert_eq!(
                call(service, &finance, operation(kind, "get"), id, None)
                    .unwrap_err()
                    .status,
                Some(403)
            );
        }
        if employee.is_none() {
            assert_eq!(
                call(
                    service,
                    applicant,
                    operation(kind, "approve"),
                    id,
                    Some(json!({"expectedVersion":row["versionNumber"],"note":"本人审批"}))
                )
                .unwrap_err()
                .status,
                Some(403)
            );
        }
        if kind == "expense" {
            assert_eq!(
                service
                    .upload(
                        operation(kind, "upload"),
                        &[("id", id.to_string())],
                        json!({"expectedVersion":row["versionNumber"]}),
                        "frozen.pdf",
                        b"%PDF-1.7\n%%EOF",
                        applicant
                    )
                    .unwrap_err()
                    .status,
                Some(409)
            );
        }
        row = act(service, admin, kind, "reject", &row);
        body["expectedVersion"] = row["versionNumber"].clone();
        body["title"] = json!(format!("{kind}补正申请"));
        row = call(
            service,
            applicant,
            operation(kind, "update"),
            id,
            Some(body),
        )
        .unwrap();
        row = act(service, applicant, kind, "submit", &row);
        row = act(service, applicant, kind, "withdraw", &row);
        assert_eq!(row["status"], "Draft");
        row = act(service, applicant, kind, "submit", &row);
        let barrier = Barrier::new(2);
        let responses = std::thread::scope(|scope| {
            let attempts: Vec<_> = (0..2)
                .map(|_| {
                    scope.spawn(|| {
                        barrier.wait();
                        call(
                            service,
                            admin,
                            operation(kind, "approve"),
                            id,
                            Some(json!({"expectedVersion":row["versionNumber"],"note":"并发审批"})),
                        )
                    })
                })
                .collect();
            attempts
                .into_iter()
                .map(|attempt| attempt.join().unwrap())
                .collect::<Vec<_>>()
        });
        assert_eq!(responses.iter().filter(|r| r.is_ok()).count(), 1);
        assert_eq!(
            responses
                .iter()
                .find_map(|r| r.as_ref().err())
                .unwrap()
                .status,
            Some(409)
        );
        row = responses.into_iter().find_map(Result::ok).unwrap();
        row = if kind == "expense" {
            receive_expense(service, admin, &finance, &row)
        } else {
            act(service, admin, kind, "complete", &row)
        };
        assert_eq!(
            row["status"],
            if kind == "expense" {
                "HandedOff"
            } else {
                "Completed"
            }
        );
        let events = call(service, applicant, operation(kind, "history"), id, None).unwrap();
        assert_eq!(
            events["items"]
                .as_array()
                .unwrap()
                .iter()
                .filter(|e| e["action"] == "approve")
                .count(),
            1
        );
        assert_eq!(
            call(
                service,
                admin,
                operation(kind, "complete"),
                id,
                Some(json!({"expectedVersion":row["versionNumber"],"note":"重复完成"}))
            )
            .unwrap_err()
            .status,
            Some(409)
        );
    }
}
