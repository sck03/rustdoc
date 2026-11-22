//! The same deletion, handover and session contract runs on SQLite and PostgreSQL.
use export_doc_engine::{
    engine::NativeService, generated_api::*, invoice::InvoiceDraft, paths::nonce,
};
use serde_json::json;
#[path = "approval_fixture.rs"]
mod workflow;

pub fn exercise(service: &NativeService, admin: &str) {
    let current = workflow::request(service, admin, GET_CURRENT_USER, None);
    let accounts = workflow::request(service, admin, LIST_USERS, None);
    let sole_admin = accounts["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|account| account["id"] == current["id"])
        .unwrap();
    let mut inactive = sole_admin.clone();
    inactive["expectedVersion"] = sole_admin["versionNumber"].clone();
    inactive["isActive"] = json!(false);
    assert_eq!(
        workflow::call(
            service,
            admin,
            UPDATE_USER_ACCOUNT,
            current["id"].as_i64().unwrap(),
            Some(inactive)
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    assert_eq!(workflow::call(service, admin, SUSPEND_ACCOUNT_FOR_SECURITY, 0, Some(json!({
        "accountId":current["id"].as_i64().unwrap().to_string(),"expectedVersion":sole_admin["versionNumber"],
        "commandKey":nonce().unwrap(),"note":"保留最后一个管理员的安全边界验证"
    }))).unwrap_err().status, Some(409));
    let suffix = nonce().unwrap();
    let username = format!("handover-{suffix}");
    let account = workflow::request(
        service,
        admin,
        CREATE_USER_ACCOUNT,
        Some(json!({
            "username":username,"fullName":"归属交接验证","role":"Admin","isActive":true,
            "companyScope":"DEFAULT","departmentId":"GENERAL","resetPassword":"Handover-Test-2026"
        })),
    )["user"]
        .clone();
    let id = account["id"].as_i64().unwrap();
    let login = || {
        workflow::request(
            service,
            "",
            LOGIN,
            Some(json!({
                "username":username,"password":"Handover-Test-2026"
            })),
        )["accessToken"]
            .as_str()
            .unwrap()
            .to_owned()
    };
    let owner = login();
    let logout_session = login();
    let delete = || {
        workflow::query(
            service,
            admin,
            DELETE_USER_ACCOUNT,
            &[("id", id.to_string())],
            &[("expectedVersion", account["versionNumber"].to_string())],
            None,
        )
    };
    for (operation, body, count_field, transfer_field) in [
        (
            CREATE_INVOICE,
            json!(
                InvoiceDraft::demo("2026-11-22", &format!("HANDOVER-{suffix}"))
                    .build()
                    .unwrap()
            ),
            "invoiceCount",
            "includeInvoices",
        ),
        (
            CREATE_PAYMENT,
            json!({"voucherNo":format!("HANDOVER-{suffix}"),"payeeName":"测试收款人","paymentDate":"2026-11-22","cnyAmount":"123.45"}),
            "paymentCount",
            "includePayments",
        ),
        (
            CREATE_CUSTOMER,
            json!({"displayName":format!("归属客户-{suffix}"),"customerNameEN":format!("Handover customer {suffix}")}),
            "otherBusinessDataCount",
            "includeOtherBusinessData",
        ),
    ] {
        workflow::request(service, &owner, operation, Some(body));
        assert_eq!(
            delete().unwrap_err().status,
            Some(409),
            "{} must prevent account deletion",
            operation.id
        );
        let mut inactive = account.clone();
        inactive["expectedVersion"] = account["versionNumber"].clone();
        inactive["isActive"] = json!(false);
        assert_eq!(
            workflow::call(service, admin, UPDATE_USER_ACCOUNT, id, Some(inactive))
                .unwrap_err()
                .status,
            Some(409)
        );
        let overview =
            workflow::request(service, admin, GET_SHARED_DATABASE_OWNERSHIP_SUMMARY, None);
        let row = overview["owners"]
            .as_array()
            .unwrap()
            .iter()
            .find(|row| row["userId"] == id)
            .unwrap();
        assert_eq!(
            row[count_field], 1,
            "blocked deletion preserves visible ownership"
        );
        let mut transfer = json!({"fromUserId":id,"toUserId":current["id"],
            "includeInvoices":false,"includePayments":false,"includeOtherBusinessData":false,
            "onlyUnassigned":false,"departmentId":"","companyScope":"","confirmationText":"TRANSFER OWNERSHIP"});
        transfer[transfer_field] = json!(true);
        workflow::request(
            service,
            admin,
            TRANSFER_SHARED_DATABASE_OWNERSHIP,
            Some(transfer),
        );
    }
    assert_eq!(delete().unwrap()["success"], true);
    assert_eq!(
        workflow::call(service, &logout_session, LOGOUT, 0, None)
            .unwrap_err()
            .status,
        Some(401),
        "logout must classify a deleted account as an expired identity"
    );
    for operation in [GET_CURRENT_USER, RENEW_SESSION, LOGOUT] {
        assert_eq!(
            workflow::call(service, &owner, operation, 0, None)
                .unwrap_err()
                .status,
            Some(401),
            "{}",
            operation.id
        );
    }
    assert_eq!(
        workflow::request(service, admin, GET_CURRENT_USER, None)["id"],
        current["id"]
    );
    assert_eq!(
        workflow::call(service, admin, GET_INVOICE, i64::MAX, None)
            .unwrap_err()
            .status,
        Some(404),
        "ordinary missing resources keep their 404 classification"
    );
}
