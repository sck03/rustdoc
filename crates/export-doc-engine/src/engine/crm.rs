//! Follow-up workflows share canonical completion, relationship and scope rules.
use super::{
    auth,
    error::{Result, conflict, error, invalid, unavailable},
    records::{id, text},
    store::{self, Actor, Store},
};
use crate::{clock::BusinessClock, contracts, generated_api::*};
use chrono::{DateTime, FixedOffset, Utc};
use export_doc_domain::crm;
use export_doc_storage::{AuditWrite, Connection, RecordWrite};
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    QUERY_CRM_FOLLOW_UPS,
    CREATE_CRM_FOLLOW_UP,
    UPDATE_CRM_FOLLOW_UP,
    COMPLETE_CRM_FOLLOW_UP,
    RESTORE_CRM_FOLLOW_UP,
    TRANSFER_CRM_FOLLOW_UP,
    DELETE_CRM_FOLLOW_UP,
    UPDATE_CRM_CUSTOMER_BATCH_STATUS,
];
const KIND: &str = "crm-follow-ups";
const PERMISSION: &str = "sales.follow-ups";

fn checked(tx: &Connection, actor: &Actor, record_id: i64, action: &str) -> Result<Value> {
    let value = store::get(tx, KIND, record_id)?;
    if !auth::visible(actor, PERMISSION, action, &value) {
        return Err(error(403, "跟进记录不在当前账号的操作范围内。"));
    }
    Ok(value)
}
fn related(
    tx: &Connection,
    actor: &Actor,
    customer_id: i64,
    contact_id: Option<i64>,
    action: &str,
) -> Result<(Value, String)> {
    let customer = store::get(tx, "crm-customers", customer_id)?;
    if !auth::visible(actor, PERMISSION, action, &customer) {
        return Err(error(403, "客户不在当前账号的跟进范围内。"));
    }
    let contact_name = if let Some(contact_id) = contact_id.filter(|id| *id > 0) {
        let contact = store::get(tx, "crm-contacts", contact_id)?;
        if contact["crmCustomerId"] != customer_id {
            return Err(invalid("联系人不属于所选客户。"));
        }
        text(&contact, "name")
    } else {
        String::new()
    };
    Ok((customer, contact_name))
}
pub fn project(tx: &Connection, mut value: Value) -> Result<Value> {
    let customer = store::get(
        tx,
        "crm-customers",
        value["crmCustomerId"]
            .as_i64()
            .ok_or_else(|| unavailable("跟进缺少客户编号。"))?,
    )?;
    value["customerName"] = customer["name"].clone();
    value["contactName"] = if let Some(id) = value["crmContactId"].as_i64().filter(|id| *id > 0) {
        tx.get("crm-contacts", id)?
            .map(|contact| contact["name"].clone())
            .unwrap_or(json!(""))
    } else {
        json!("")
    };
    Ok(contracts::project(
        contracts::schema("ApiCrmFollowUpDto"),
        value,
    ))
}
fn instant(value: &str) -> Result<DateTime<FixedOffset>> {
    DateTime::parse_from_rfc3339(value)
        .map_err(|_| invalid("跟进时间必须包含有效日期、时间和时区。"))
}
fn order(value: &Value, key: &str) -> Result<Option<DateTime<FixedOffset>>> {
    value[key]
        .as_str()
        .filter(|value| !value.is_empty())
        .map(instant)
        .transpose()
}
pub fn visible(tx: &Connection, actor: &Actor) -> Result<Vec<Value>> {
    let customers = store::all(tx, "crm-customers")?;
    store::all(tx, KIND)?
        .into_iter()
        .filter(|row| {
            auth::visible(actor, PERMISSION, "view", row)
                && customers.iter().any(|customer| {
                    customer["id"] == row["crmCustomerId"]
                        && auth::visible(actor, "sales.customers", "view", customer)
                })
        })
        .map(|row| project(tx, row))
        .collect()
}
pub fn sorted(rows: Vec<Value>) -> Result<Vec<Value>> {
    let mut rows = rows
        .into_iter()
        .map(|row| {
            Ok((
                row["isCompleted"] == true,
                order(&row, "nextFollowUpAt")?,
                order(&row, "followedUpAt")?,
                row,
            ))
        })
        .collect::<Result<Vec<_>>>()?;
    rows.sort_by(|a, b| {
        a.0.cmp(&b.0)
            .then_with(|| a.1.is_none().cmp(&b.1.is_none()))
            .then_with(|| a.1.cmp(&b.1))
            .then_with(|| b.2.cmp(&a.2))
            .then_with(|| b.3["id"].as_i64().cmp(&a.3["id"].as_i64()))
    });
    Ok(rows.into_iter().map(|row| row.3).collect())
}
fn query(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    let read = |key: &str| {
        query
            .iter()
            .find(|(name, _)| *name == key)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let filter_id = |key: &str| -> Result<Option<i64>> {
        if read(key).is_empty() {
            return Ok(None);
        }
        read(key)
            .parse::<i64>()
            .ok()
            .filter(|value| *value > 0)
            .map(Some)
            .ok_or_else(|| invalid("跟进或客户编号必须大于零。"))
    };
    let customer_id = filter_id("crmCustomerId")?;
    let follow_up_id = filter_id("followUpId")?;
    let tx = store.connection()?;
    let rows = sorted(visible(&tx, actor)?)?
        .into_iter()
        .filter(|row| {
            (read("includeCompleted") == "true" || row["isCompleted"] != true)
                && customer_id.is_none_or(|id| row["crmCustomerId"] == id)
                && follow_up_id.is_none_or(|id| row["id"] == id)
        })
        .collect::<Vec<_>>();
    let size = read("pageSize")
        .parse::<usize>()
        .unwrap_or(20)
        .clamp(1, 100);
    let page = read("pageNumber").parse::<usize>().unwrap_or(1).max(1);
    let total = rows.len();
    Ok(contracts::page(
        rows.into_iter()
            .skip(page.saturating_sub(1).saturating_mul(size))
            .take(size)
            .collect(),
        total,
        page,
        size,
    ))
}
fn save(
    store: &Store,
    actor: &Actor,
    record_id: i64,
    body: &Value,
    clock: &BusinessClock,
) -> Result<Value> {
    let request: ApiCrmFollowUpSaveRequest = serde_json::from_value(body.clone())
        .map_err(|cause| invalid(format!("跟进资料无效：{cause}")))?;
    if record_id == 0 && request.id > 0 {
        return Err(invalid("新增跟进不能包含已有编号。"));
    }
    let action = if record_id > 0 { "edit" } else { "create" };
    store.transaction(|tx| {
        let mut row = if record_id > 0 {
            let value = checked(tx, actor, record_id, action)?;
            store::check_version(&value, request.expected_version.unwrap_or(0))?;
            if value["crmCustomerId"] != request.crm_customer_id {
                return Err(invalid("已有跟进不能通过编辑更换客户，请使用转移跟进。"));
            }
            value
        } else {
            contracts::initial(contracts::schema("ApiCrmFollowUpDto"))
        };
        let contact_id = request.crm_contact_id.filter(|id| *id > 0);
        related(tx, actor, request.crm_customer_id, contact_id, action)?;
        row["crmCustomerId"] = json!(request.crm_customer_id);
        row["crmContactId"] = json!(contact_id);
        row["type"] = json!(
            crm::choice(
                request.r#type.as_deref().unwrap_or(""),
                crm::FOLLOW_UP_TYPES,
                "其他",
                "跟进方式"
            )
            .map_err(invalid)?
        );
        row["summary"] = json!(
            crm::text(
                request.summary.as_deref().unwrap_or(""),
                "跟进摘要",
                500,
                true
            )
            .map_err(invalid)?
        );
        row["nextAction"] = json!(
            crm::text(
                request.next_action.as_deref().unwrap_or(""),
                "下一步动作",
                300,
                false
            )
            .map_err(invalid)?
        );
        if let Some(value) = &request.followed_up_at {
            row["followedUpAt"] = json!(instant(value)?.with_timezone(&Utc).to_rfc3339());
        } else if record_id == 0 {
            row["followedUpAt"] = json!(clock.now().map_err(unavailable)?.utc_now.to_rfc3339());
        }
        row["nextFollowUpAt"] = json!(
            request
                .next_follow_up_at
                .as_ref()
                .map(|value| instant(value).map(|time| time.with_timezone(&Utc).to_rfc3339()))
                .transpose()?
        );
        project(
            tx,
            store::save(tx, KIND, record_id, row, None, actor, action)?,
        )
    })
}
fn mutate(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    record_id: i64,
    body: &Value,
    query: &[(&str, String)],
) -> Result<Value> {
    let action = match operation {
        COMPLETE_CRM_FOLLOW_UP => "complete",
        RESTORE_CRM_FOLLOW_UP => "restore",
        TRANSFER_CRM_FOLLOW_UP => "assign",
        _ => "delete",
    };
    let permission_action = auth::operation_action(operation, PERMISSION, action)?;
    store.transaction(|tx| {
        let mut row = checked(tx, actor, record_id, permission_action)?;
        let version = if operation == DELETE_CRM_FOLLOW_UP {
            query
                .iter()
                .find(|(key, _)| *key == "expectedVersion")
                .and_then(|(_, value)| value.parse().ok())
                .unwrap_or(0)
        } else {
            store::expected(body)
        };
        store::check_version(&row, version)?;
        if operation == DELETE_CRM_FOLLOW_UP {
            if !tx.delete(KIND, record_id, version)? {
                return Err(conflict("跟进版本已变化。"));
            }
            tx.append_audit_details(
                &AuditWrite {
                    kind: KIND,
                    record_id,
                    version,
                    action,
                    actor_id: actor.id,
                    occurred_at: &store::timestamp(),
                    note: "",
                },
                &super::audit_values::changes(Some(&row), None),
            )?;
            return Ok(json!({"success":true,"message":"跟进记录已删除。"}));
        }
        let owner = if operation == TRANSFER_CRM_FOLLOW_UP {
            let request: ApiCrmFollowUpTransferRequest =
                serde_json::from_value(body.clone()).map_err(|cause| invalid(cause.to_string()))?;
            let contact = request.crm_contact_id.filter(|id| *id > 0);
            if row["crmCustomerId"] == request.crm_customer_id
                && row["crmContactId"] == json!(contact)
            {
                return Err(conflict("跟进记录已属于所选客户和联系人。"));
            }
            let (customer, _) = related(tx, actor, request.crm_customer_id, contact, action)?;
            row["crmCustomerId"] = json!(request.crm_customer_id);
            row["crmContactId"] = json!(contact);
            Some(customer)
        } else {
            let completed = operation == COMPLETE_CRM_FOLLOW_UP;
            if row["isCompleted"] == completed {
                return Err(conflict(if completed {
                    "跟进已经完成。"
                } else {
                    "跟进尚未完成。"
                }));
            }
            row["isCompleted"] = json!(completed);
            None
        };
        let mut saved = store::save(tx, KIND, record_id, row, None, actor, action)?;
        if let Some(owner) = owner {
            for key in ["ownerUserId", "departmentId", "companyScope"] {
                saved[key] = owner[key].clone();
            }
            if !tx.update(
                record_id,
                saved["versionNumber"].as_i64().unwrap_or(0),
                &RecordWrite {
                    kind: KIND,
                    identity: None,
                    body: &saved,
                },
            )? {
                return Err(conflict("跟进归属已变化，请重新读取。"));
            }
        }
        project(tx, saved)
    })
}
pub fn handle(
    store: &Store,
    actor: &Actor,
    clock: &BusinessClock,
    operation: Operation,
    parameters: &[(&str, String)],
    query_values: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    match operation {
        QUERY_CRM_FOLLOW_UPS => query(store, actor, query_values),
        CREATE_CRM_FOLLOW_UP => save(store, actor, 0, body, clock),
        UPDATE_CRM_FOLLOW_UP => save(store, actor, id(parameters)?, body, clock),
        UPDATE_CRM_CUSTOMER_BATCH_STATUS => batch_status(store, actor, body),
        _ => mutate(store, actor, operation, id(parameters)?, body, query_values),
    }
}

pub fn batch_status(store: &Store, actor: &Actor, body: &Value) -> Result<Value> {
    auth::authorize(actor, "sales.customers", "deactivate")?;
    let normalized = crm::choice(
        &text(body, "status"),
        crm::CUSTOMER_STATUSES,
        "跟进中",
        "客户状态",
    )
    .map_err(invalid)?;
    let mut ids = vec![];
    for value in body["ids"].as_array().into_iter().flatten() {
        let id = value.as_i64().unwrap_or(0);
        if id > 0 && !ids.contains(&id) {
            ids.push(id);
        }
    }
    if ids.is_empty() {
        return Err(invalid("请选择 CRM 客户。"));
    }
    if ids.len() > 500 {
        return Err(invalid(
            "单次最多修改 500 家 CRM 客户，请分批提交；超出部分不会被静默忽略。",
        ));
    }
    let affected = store.transaction(|transaction| {
        let mut count = 0;
        for mut row in store::all(transaction, "crm-customers")?
            .into_iter()
            .filter(|row| ids.contains(&row["id"].as_i64().unwrap_or(0)))
        {
            if !auth::visible(actor, "sales.customers", "deactivate", &row) {
                continue;
            }
            if text(&row, "status") == normalized {
                count += 1;
                continue;
            }
            row["status"] = json!(normalized);
            store::save(
                transaction,
                "crm-customers",
                row["id"].as_i64().unwrap_or(0),
                row,
                None,
                actor,
                "deactivate",
            )?;
            count += 1;
        }
        Ok(count)
    })?;
    Ok(json!({"affectedCount": affected, "status": normalized}))
}
