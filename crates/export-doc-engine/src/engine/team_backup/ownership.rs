//! 共享库归属摘要与改派：更新发票、付款报销及其它业务资料的 ownerUserId、
//! departmentId、companyScope 归属字段。关联子记录继续随所属业务聚合访问，
//! 不移动附件、不生成导出目录。改派在单事务内完成并写审计。
use super::{
    NativeService, OWNERSHIP_STORAGE_POLICY, auth,
    error::{Result, conflict, error, invalid},
    store::{Actor, Store},
};
use crate::contracts;
use export_doc_storage::{Connection, RecordWrite};
use serde_json::{Value, json};

const INVOICE_KIND: &str = "invoices";
const PAYMENT_KIND: &str = "payments";
const OTHER_KINDS: &[&str] = &[
    "customers",
    "exporters",
    "payees",
    "crm-customers",
    "crm-follow-ups",
    "suppliers",
    "opportunities",
    "email-templates",
    "report-templates",
    "container-projects",
];
const CONFIRM_TEXT: &str = "TRANSFER OWNERSHIP";
const MAX_SCOPE_CHARS: usize = 50;

struct Request {
    from_user_id: Option<i64>,
    to_user_id: i64,
    include_invoices: bool,
    include_payments: bool,
    include_other: bool,
    only_unassigned: bool,
    department_id: String,
    company_scope: String,
}
impl Request {
    fn parse(body: &Value) -> Result<Self> {
        let to_user_id = body["toUserId"]
            .as_i64()
            .filter(|value| *value > 0)
            .ok_or_else(|| invalid("请选择新的归属用户。"))?;
        let from_user_id = body["fromUserId"].as_i64().filter(|value| *value > 0);
        let include_invoices = body["includeInvoices"].as_bool().unwrap_or(false);
        let include_payments = body["includePayments"].as_bool().unwrap_or(false);
        let include_other = body["includeOtherBusinessData"].as_bool().unwrap_or(false);
        let only_unassigned = body["onlyUnassigned"].as_bool().unwrap_or(false);
        if !include_invoices && !include_payments && !include_other {
            return Err(invalid("请至少选择一种需要改派的业务数据。"));
        }
        if !only_unassigned && from_user_id.is_none() {
            return Err(invalid("来源用户无效。"));
        }
        if !only_unassigned && from_user_id == Some(to_user_id) {
            return Err(invalid("来源用户和目标用户不能相同。"));
        }
        let department_id = scope_value(body, "departmentId")?;
        let company_scope = scope_value(body, "companyScope")?;
        Ok(Self {
            from_user_id,
            to_user_id,
            include_invoices,
            include_payments,
            include_other,
            only_unassigned,
            department_id,
            company_scope,
        })
    }
    fn selected_kinds(&self) -> Vec<&'static str> {
        let mut kinds = Vec::new();
        if self.include_invoices {
            kinds.push(INVOICE_KIND);
        }
        if self.include_payments {
            kinds.push(PAYMENT_KIND);
        }
        if self.include_other {
            kinds.extend(OTHER_KINDS);
        }
        kinds
    }
}

fn scope_value(body: &Value, key: &str) -> Result<String> {
    let value = super::records_text(body, key);
    if value.chars().count() > MAX_SCOPE_CHARS {
        return Err(invalid(format!(
            "{}不能超过 {MAX_SCOPE_CHARS} 个字符。",
            if key == "departmentId" {
                "部门"
            } else {
                "公司范围"
            }
        )));
    }
    Ok(value)
}

fn user_label(connection: &Connection, id: i64) -> Result<Value> {
    connection
        .get("users", id)?
        .ok_or_else(|| error(404, "指定的用户不存在。"))
}

fn owner_of(record: &Value) -> Option<i64> {
    record["ownerUserId"].as_i64()
}

fn counts(store: &Store, kinds: &[&str]) -> Result<(i64, i64, Vec<(i64, i64)>)> {
    let connection = store.connection()?;
    let mut total = 0i64;
    let mut unassigned = 0i64;
    let mut per_owner: Vec<(i64, i64)> = Vec::new();
    for kind in kinds {
        for record in connection.all(kind)? {
            total += 1;
            match owner_of(&record) {
                Some(owner) => match per_owner.iter_mut().find(|(key, _)| *key == owner) {
                    Some((_, count)) => *count += 1,
                    None => per_owner.push((owner, 1)),
                },
                None => unassigned += 1,
            }
        }
    }
    Ok((total, unassigned, per_owner))
}

pub(super) fn summary(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let users = service.store.all("users")?;
    let (invoice_total, invoice_unassigned, invoice_owners) =
        counts(&service.store, &[INVOICE_KIND])?;
    let (payment_total, payment_unassigned, payment_owners) =
        counts(&service.store, &[PAYMENT_KIND])?;
    let (other_total, other_unassigned, other_owners) = counts(&service.store, OTHER_KINDS)?;
    let owners = users
        .iter()
        .map(|user| {
            let user_id = user["id"].as_i64().unwrap_or_default();
            let count = |owners: &[(i64, i64)]| {
                owners
                    .iter()
                    .find(|(key, _)| *key == user_id)
                    .map(|(_, count)| *count)
                    .unwrap_or_default()
            };
            let mut item =
                contracts::initial(contracts::schema("ApiSharedDatabaseOwnerSummaryItemDto"));
            item["userId"] = json!(user_id);
            item["username"] = json!(super::records_text(user, "username"));
            item["fullName"] = json!(super::records_text(user, "fullName"));
            item["role"] = json!(super::records_text(user, "role"));
            item["departmentId"] = json!(super::records_text(user, "departmentId"));
            item["companyScope"] = json!(super::records_text(user, "companyScope"));
            item["isActive"] = json!(user["isActive"].as_bool().unwrap_or(true));
            item["invoiceCount"] = json!(count(&invoice_owners));
            item["paymentCount"] = json!(count(&payment_owners));
            item["otherBusinessDataCount"] = json!(count(&other_owners));
            item
        })
        .collect::<Vec<_>>();
    let mut response = contracts::initial(contracts::schema(
        "ApiSharedDatabaseOwnershipSummaryResponse",
    ));
    response["totalInvoices"] = json!(invoice_total);
    response["unassignedInvoices"] = json!(invoice_unassigned);
    response["totalPayments"] = json!(payment_total);
    response["unassignedPayments"] = json!(payment_unassigned);
    response["totalOtherBusinessData"] = json!(other_total);
    response["unassignedOtherBusinessData"] = json!(other_unassigned);
    response["owners"] = json!(owners);
    response["storagePolicy"] = json!(OWNERSHIP_STORAGE_POLICY);
    Ok(response)
}

pub(super) fn transfer(service: &NativeService, actor: &Actor, body: &Value) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    if super::records_text(body, "confirmationText") != CONFIRM_TEXT {
        return Err(invalid(format!(
            "归属改派前需要输入确认文本 {CONFIRM_TEXT}。"
        )));
    }
    let request = Request::parse(body)?;
    let target = {
        let connection = service.store.connection()?;
        let target = user_label(&connection, request.to_user_id)?;
        if !target["isActive"].as_bool().unwrap_or(true) {
            return Err(invalid("目标用户已停用，不能改派归属。"));
        }
        if let Some(from) = request.from_user_id {
            let source = user_label(&connection, from)?;
            if !source["isActive"].as_bool().unwrap_or(true) {
                return Err(invalid("来源用户已停用，不能改派归属。"));
            }
        }
        target
    };
    let department_id = if request.department_id.is_empty() {
        super::records_text(&target, "departmentId")
    } else {
        request.department_id.clone()
    };
    let company_scope = if request.company_scope.is_empty() {
        super::records_text(&target, "companyScope")
    } else {
        request.company_scope.clone()
    };
    let kinds = request.selected_kinds();
    let to_user_id = request.to_user_id;
    let from_user_id = request.from_user_id;
    let only_unassigned = request.only_unassigned;
    let mut updated = vec![0i64; kinds.len()];
    service.store.transaction(|connection| {
        for (index, kind) in kinds.iter().enumerate() {
            for record in connection.all(kind)? {
                let owner = owner_of(&record);
                let matches = if only_unassigned {
                    owner.is_none()
                } else {
                    owner == from_user_id
                };
                if !matches {
                    continue;
                }
                apply_ownership(
                    connection,
                    kind,
                    record,
                    to_user_id,
                    &department_id,
                    &company_scope,
                )?;
                updated[index] += 1;
            }
        }
        Ok(())
    })?;
    let invoice_count = updated[0];
    let payment_count = updated[1];
    let other_count = updated[2..].iter().sum::<i64>();
    super::audit(
        &service.store,
        actor,
        "transfer-ownership",
        &format!(
            "已将业务资料归属改派给用户 {to_user_id}：发票 {invoice_count} 条，付款报销 {payment_count} 条，其它业务资料 {other_count} 条"
        ),
    )?;
    let mut response = contracts::initial(contracts::schema(
        "ApiSharedDatabaseOwnershipTransferResponse",
    ));
    response["success"] = json!(true);
    response["message"] = json!(format!(
        "归属改派完成：发票 {invoice_count} 条，付款报销 {payment_count} 条，其他业务资料 {other_count} 条。"
    ));
    response["updatedInvoices"] = json!(invoice_count);
    response["updatedPayments"] = json!(payment_count);
    response["updatedOtherBusinessData"] = json!(other_count);
    response["storagePolicy"] = json!(OWNERSHIP_STORAGE_POLICY);
    Ok(response)
}

fn identity_for(kind: &str, body: &Value) -> Option<String> {
    if kind == INVOICE_KIND {
        Some(
            serde_json::to_string(&[&body["companyScope"], &body["invoiceNo"], &body["type"]])
                .ok()?,
        )
    } else {
        let resource = super::super::catalog::resource(kind)?;
        if resource.identity.is_empty() {
            None
        } else {
            Some(super::super::store::normalize(
                &super::super::records::text(body, resource.identity),
            ))
        }
    }
}

/// 复用与正常保存一致的版本号与身份键更新路径，仅改归属三字段。
fn apply_ownership(
    connection: &Connection,
    kind: &str,
    mut record: Value,
    to_user_id: i64,
    department_id: &str,
    company_scope: &str,
) -> Result<()> {
    let id = record["id"]
        .as_i64()
        .ok_or_else(|| invalid("记录缺少编号，不能改派归属。"))?;
    let previous_version = record["versionNumber"].as_i64().unwrap_or_default();
    record["ownerUserId"] = json!(to_user_id);
    record["departmentId"] = json!(department_id);
    record["companyScope"] = json!(company_scope);
    record["versionNumber"] = json!(previous_version + 1);
    record["rowVersion"] = json!(format!("native:{}", previous_version + 1));
    record["updatedAt"] = json!(super::super::store::timestamp());
    let identity = identity_for(kind, &record);
    let written = connection.update(
        id,
        previous_version,
        &RecordWrite {
            kind,
            identity: identity.as_deref(),
            body: &record,
        },
    )?;
    if !written {
        return Err(conflict("记录版本已变化，归属改派已取消，请重试。"));
    }
    connection.set_body(id, &record)?;
    Ok(())
}
