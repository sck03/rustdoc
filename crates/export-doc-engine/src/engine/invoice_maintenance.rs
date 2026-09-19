use super::{
    error::{Result, conflict, error, invalid},
    records::{id, text},
    store::{self, Actor, Store},
};
#[allow(unused_imports)]
use crate::{contracts, generated_api::*};
use export_doc_storage::{AuditWrite, Connection};
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    GET_INVOICE_DATA_MAINTENANCE_PREVIEW,
    PURGE_CANCELLED_INVOICE,
];
const POLICY: &str = "发票数据清理是独立的管理员维护操作：只允许清理已作废发票，必须再次核对发票号并填写原因；操作在同一数据库事务中写入 MaintenancePurge 审计记录并删除发票、明细、状态历史及关联单一窗口工作区记录，不读取付款/报销业务表，也不创建系统盘文件。";
const RETENTION_GUIDANCE: &str = "该发票包含业务归档资料，必须保留原单据和全部附件，不能物理清理。";

fn display_name(status: &str) -> String {
    match status {
        "Draft" => "草稿",
        "Verified" => "已核对",
        "Shipped" => "已出运",
        "Completed" => "已结汇",
        "Cancelled" => "已作废",
        other => return other.to_string(),
    }
    .into()
}

pub fn handle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    if !actor.admin {
        return Err(error(403, "只有管理员可以使用发票数据维护功能。"));
    }
    let invoice_id = id(parameters)?;
    if invoice_id <= 0 {
        return Err(invalid("发票 ID 必须大于 0。"));
    }
    match operation {
        GET_INVOICE_DATA_MAINTENANCE_PREVIEW => preview(store, invoice_id),
        PURGE_CANCELLED_INVOICE => purge(store, actor, invoice_id, body),
        _ => Err(invalid("发票数据维护操作无效。")),
    }
}

fn retained_attachments(connection: &Connection, invoice_id: i64) -> Result<bool> {
    Ok(store::all(connection, "attachments")?
        .iter()
        .any(|attachment| attachment["invoiceId"].as_i64() == Some(invoice_id)))
}

fn guidance(status: &str, retained: bool) -> String {
    if retained {
        return RETENTION_GUIDANCE.into();
    }
    if status == "Cancelled" {
        return "该发票已作废。仅在确有法规、测试数据或错误数据清理依据时，才可由管理员物理清理。"
            .into();
    }
    if status == "Draft" {
        return "该发票仍为草稿，请回到发票编辑页使用普通删除。".into();
    }
    "该发票属于正式业务状态，禁止物理删除；如确需清理，必须先按业务流程作废。".into()
}

fn preview(store: &Store, invoice_id: i64) -> Result<Value> {
    let connection = store.connection()?;
    let invoice = store::get(&*connection, "invoices", invoice_id)?;
    let status = text(&invoice, "status");
    let retained = retained_attachments(&*connection, invoice_id)?;
    let can_purge = status == "Cancelled" && !retained;
    Ok(json!({
        "id": invoice["id"],
        "invoiceNo": text(&invoice, "invoiceNo"),
        "type": text(&invoice, "type"),
        "status": status,
        "statusDisplayName": display_name(&status),
        "invoiceDate": invoice["invoiceDate"],
        "customerName": text(&invoice, "customerNameEN"),
        "canPurge": can_purge,
        "guidance": guidance(&status, retained),
        "storagePolicy": POLICY,
    }))
}

fn purge(store: &Store, actor: &Actor, invoice_id: i64, body: &Value) -> Result<Value> {
    let confirmation = text(body, "invoiceNoConfirmation");
    if confirmation.is_empty() {
        return Err(invalid("请输入完整发票号进行二次确认。"));
    }
    let reason = text(body, "reason");
    if reason.is_empty() {
        return Err(invalid("请填写数据清理原因。"));
    }
    if reason.chars().count() > 500 {
        return Err(invalid("数据清理原因不能超过 500 个字符。"));
    }
    store.transaction(|transaction| {
        let invoice = store::get(transaction, "invoices", invoice_id)?;
        let status = text(&invoice, "status");
        if status != "Cancelled" {
            return Err(conflict(
                if status == "Draft" {
                    "草稿发票应在发票编辑页使用普通删除，不允许通过管理员数据维护绕过正常流程。"
                } else {
                    "只有已作废发票可以通过管理员数据维护清理；正式状态发票必须先作废。"
                },
            ));
        }
        let invoice_no = text(&invoice, "invoiceNo");
        if invoice_no != confirmation {
            return Err(invalid("发票号确认不一致，未执行数据清理。"));
        }
        if retained_attachments(transaction, invoice_id)? {
            return Err(conflict(RETENTION_GUIDANCE));
        }
        delete_workspace(transaction, invoice_id)?;
        let previous = invoice.clone();
        transaction.append_audit_details(
            &AuditWrite {
                kind: "invoices",
                record_id: invoice_id,
                version: previous["versionNumber"].as_i64().unwrap_or(0),
                action: "MaintenancePurge",
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: &reason,
            },
            &json!({
                "previous": {
                    "id": previous["id"],
                    "invoiceNo": invoice_no,
                    "type": text(&previous, "type"),
                    "status": status,
                    "ownerUserId": previous["ownerUserId"],
                    "itemCount": previous["items"].as_array().map(|items| items.len()).unwrap_or(0)
                },
                "current": {"reason": reason, "policy": "Administrator cancelled-invoice purge"}
            }),
        )?;
        if !transaction.delete(
            "invoices",
            invoice_id,
            previous["versionNumber"].as_i64().unwrap_or(0),
        )? {
            return Err(conflict("数据清理失败：该发票已被其他用户修改或删除，请重新查询后再试。"));
        }
        Ok(json!({
            "success": true,
            "invoiceId": invoice_id,
            "invoiceNo": invoice_no,
            "previousStatus": status,
            "message": format!("已清理已作废发票“{invoice_no}”，维护原因和原始摘要已写入审计日志。"),
            "storagePolicy": POLICY,
        }))
    })
}

fn delete_workspace(transaction: &Connection, invoice_id: i64) -> Result<()> {
    for kind in ["sw-coo", "sw-acd", "sw-batches"] {
        for record in store::all(transaction, kind)?
            .into_iter()
            .filter(|record| record["sourceInvoiceId"].as_i64() == Some(invoice_id))
        {
            if !transaction.delete(
                kind,
                record["id"].as_i64().unwrap_or(0),
                record["versionNumber"].as_i64().unwrap_or(0),
            )? {
                return Err(conflict("关联单一窗口工作区记录已变化，请重新查询后再试。"));
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::paths::{RuntimePaths, nonce};
    use std::fs;

    struct Fixture {
        root: std::path::PathBuf,
        store: Store,
        actor: Actor,
        invoice_id: i64,
    }
    impl Fixture {
        fn new() -> Self {
            let workspace = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .parent()
                .unwrap()
                .parent()
                .unwrap()
                .to_path_buf();
            let root = workspace
                .join(".codex-runtime/maintenance-tests")
                .join(nonce().unwrap());
            fs::create_dir_all(&root).unwrap();
            let paths = RuntimePaths::server(&root, &root.join("Data")).unwrap();
            let store = Store::open(&paths).unwrap();
            let actor = Actor {
                id: 1,
                name: "管理员".into(),
                company: "DEFAULT".into(),
                department: "GENERAL".into(),
                admin: true,
                grants: vec![],
            };
            let mut user = contracts::initial(contracts::schema("ApiUserDto"));
            user["username"] = json!("admin");
            user["isAdmin"] = json!(true);
            user["status"] = json!("Active");
            store::save(
                &*store.connection().unwrap(),
                "users",
                0,
                user,
                None,
                &actor,
                "create",
            )
            .unwrap();
            let mut invoice = contracts::initial(contracts::schema("ApiInvoiceDetailDto"));
            invoice["invoiceNo"] = json!("INV-100");
            invoice["status"] = json!("Cancelled");
            invoice["invoiceDate"] = json!("2026-09-01");
            invoice["customerNameEN"] = json!("BUYER LTD.");
            let saved = store::save(
                &*store.connection().unwrap(),
                "invoices",
                0,
                invoice,
                None,
                &actor,
                "create",
            )
            .unwrap();
            Self {
                root,
                store,
                actor,
                invoice_id: saved["id"].as_i64().unwrap(),
            }
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
        }
    }

    #[test]
    fn preview_reports_cancelled_invoice_and_blocks_when_attachments_exist() {
        let fixture = Fixture::new();
        let value = preview(&fixture.store, fixture.invoice_id).unwrap();
        assert_eq!(value["status"], "Cancelled");
        assert_eq!(value["statusDisplayName"], "已作废");
        assert_eq!(value["canPurge"], true);
        assert!(value["guidance"].as_str().unwrap().contains("已作废"));
        fixture
            .store
            .transaction(|transaction| {
                let mut attachment =
                    contracts::initial(contracts::schema("BusinessAttachmentRecord"));
                attachment["invoiceId"] = json!(fixture.invoice_id);
                store::save(
                    transaction,
                    "attachments",
                    0,
                    attachment,
                    None,
                    &fixture.actor,
                    "upload",
                )?;
                Ok(())
            })
            .unwrap();
        let blocked = preview(&fixture.store, fixture.invoice_id).unwrap();
        assert_eq!(blocked["canPurge"], false);
        assert!(
            blocked["guidance"]
                .as_str()
                .unwrap()
                .contains("业务归档资料")
        );
    }

    #[test]
    fn purge_requires_confirmation_reason_and_cancelled_status() {
        let fixture = Fixture::new();
        assert!(
            purge(
                &fixture.store,
                &fixture.actor,
                fixture.invoice_id,
                &json!({})
            )
            .is_err()
        );
        assert!(
            purge(
                &fixture.store,
                &fixture.actor,
                fixture.invoice_id,
                &json!({"invoiceNoConfirmation": "INV-100"})
            )
            .is_err()
        );
        let wrong = purge(
            &fixture.store,
            &fixture.actor,
            fixture.invoice_id,
            &json!({"invoiceNoConfirmation": "INV-999", "reason": "测试数据清理"}),
        )
        .unwrap_err();
        assert_eq!(wrong.status, Some(400));
        fixture
            .store
            .transaction(|transaction| {
                let mut invoice = store::get(transaction, "invoices", fixture.invoice_id)?;
                invoice["status"] = json!("Verified");
                store::save(
                    transaction,
                    "invoices",
                    fixture.invoice_id,
                    invoice,
                    None,
                    &fixture.actor,
                    "edit",
                )?;
                Ok(())
            })
            .unwrap();
        let conflict = purge(
            &fixture.store,
            &fixture.actor,
            fixture.invoice_id,
            &json!({"invoiceNoConfirmation": "INV-100", "reason": "测试数据清理"}),
        )
        .unwrap_err();
        assert_eq!(conflict.status, Some(409));
    }

    #[test]
    fn purge_removes_invoice_and_records_audit() {
        let fixture = Fixture::new();
        let result = purge(
            &fixture.store,
            &fixture.actor,
            fixture.invoice_id,
            &json!({"invoiceNoConfirmation": "INV-100", "reason": "错误数据清理"}),
        )
        .unwrap();
        assert_eq!(result["success"], true);
        assert_eq!(result["previousStatus"], "Cancelled");
        assert!(fixture.store.get("invoices", fixture.invoice_id).is_err());
        let logs = store::history(
            &*fixture.store.connection().unwrap(),
            "invoices",
            fixture.invoice_id,
        )
        .unwrap();
        assert!(
            logs.iter()
                .any(|entry| entry["action"] == "MaintenancePurge")
        );
    }

    #[test]
    fn non_admin_is_rejected() {
        let fixture = Fixture::new();
        let staff = Actor {
            admin: false,
            ..fixture.actor.clone()
        };
        let denied = handle(
            &fixture.store,
            &staff,
            GET_INVOICE_DATA_MAINTENANCE_PREVIEW,
            &[("id".into(), "1".into())],
            &json!({}),
        )
        .unwrap_err();
        assert_eq!(denied.status, Some(403));
    }
}
