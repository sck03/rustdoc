//! Contacts, supply relationships and assessments are authorized through their parent directory.
use super::{
    auth,
    catalog::{self, Resource},
    error::{Result, conflict, error, invalid},
    records::{self, text},
    store::{self, Actor, Store},
};
use crate::{clock::BusinessClock, contracts, generated_api::*};
use export_doc_domain::{crm, sales, supplier};
use export_doc_storage::{AuditWrite, Connection};
use rust_decimal::Decimal;
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    QUERY_CRM_CONTACTS,
    CREATE_CRM_CONTACT,
    UPDATE_CRM_CONTACT,
    DELETE_CRM_CONTACT,
    SET_PRIMARY_CRM_CONTACT,
    QUERY_SUPPLIER_CONTACTS,
    CREATE_SUPPLIER_CONTACT,
    UPDATE_SUPPLIER_CONTACT,
    DELETE_SUPPLIER_CONTACT,
    SET_PRIMARY_SUPPLIER_CONTACT,
    QUERY_SUPPLIER_PRODUCT_LINKS,
    CREATE_SUPPLIER_PRODUCT_LINK,
    UPDATE_SUPPLIER_PRODUCT_LINK,
    DELETE_SUPPLIER_PRODUCT_LINK,
    DEACTIVATE_SUPPLIER_PRODUCT_LINK,
    RESTORE_SUPPLIER_PRODUCT_LINK,
    LIST_SUPPLIER_ASSESSMENTS,
    CREATE_SUPPLIER_ASSESSMENT,
    UPDATE_SUPPLIER_ASSESSMENT,
    DELETE_SUPPLIER_ASSESSMENT,
    CONFIRM_SUPPLIER_ASSESSMENT,
];
fn resource(operation: Operation) -> &'static Resource {
    records::find(operation).unwrap_or_else(|| {
        catalog::resource(match operation {
            SET_PRIMARY_CRM_CONTACT => "crm-contacts",
            SET_PRIMARY_SUPPLIER_CONTACT => "supplier-contacts",
            CONFIRM_SUPPLIER_ASSESSMENT => "supplier-assessments",
            _ => "supplier-products",
        })
        .unwrap()
    })
}
fn relation(resource: &Resource) -> (&'static str, &'static str, &'static str) {
    if resource.key == "crm-contacts" {
        ("crm-customers", "customerId", "crmCustomerId")
    } else {
        ("suppliers", "supplierId", "supplierCompanyId")
    }
}
fn project(tx: &Connection, resource: &Resource, mut value: Value) -> Result<Value> {
    if resource.key == "supplier-products" {
        let product = store::get(tx, "products", value["productId"].as_i64().unwrap_or(0))?;
        for (source, target) in [
            ("productCode", "productCode"),
            ("nameCN", "productNameCN"),
            ("nameEN", "productNameEN"),
        ] {
            value[target] = json!(text(&product, source));
        }
    }
    Ok(contracts::project(
        contracts::schema(resource.schema),
        value,
    ))
}
fn validate(
    tx: &Connection,
    resource: &Resource,
    actor: &Actor,
    previous: &Value,
    value: &mut Value,
    today: &str,
) -> Result<()> {
    if resource.key.ends_with("contacts") {
        for (key, label, limit) in [
            ("name", "姓名", 100),
            ("title", "职位", 100),
            ("email", "邮箱", 200),
            ("phone", "电话", 100),
            ("instantMessaging", "即时通讯", 200),
        ] {
            value[key] = json!(
                crm::text(
                    value[key].as_str().unwrap_or(""),
                    label,
                    limit,
                    key == "name"
                )
                .map_err(invalid)?
            );
        }
        value["isPrimary"] = json!(previous["isPrimary"] == true);
    } else if resource.key == "supplier-products" {
        auth::authorize(actor, "common.product-reference", "view")?;
        let product_id = records::positive(value, "productId", "产品")?;
        store::get(tx, "products", product_id)?;
        let price: Decimal = serde_json::from_value(value["referencePrice"].clone())
            .map_err(|_| invalid("参考价格式无效。"))?;
        if price < Decimal::ZERO {
            return Err(invalid("参考价不能小于零。"));
        }
        if value["leadTimeDays"]
            .as_i64()
            .is_none_or(|days| !(0..=3650).contains(&days))
        {
            return Err(invalid("交期天数必须在 0 至 3650 之间。"));
        }
        value["currency"] = json!(sales::currency(&text(value, "currency")).map_err(invalid)?);
        value["supplierProductCode"] = json!(
            crm::text(
                &text(value, "supplierProductCode"),
                "供应商产品编号",
                100,
                false
            )
            .map_err(invalid)?
        );
        if store::all(tx, resource.key)?.iter().any(|row| {
            row["id"] != value["id"]
                && row["supplierCompanyId"] == value["supplierCompanyId"]
                && row["productId"] == product_id
        }) {
            return Err(conflict("该供应商已经关联此产品。"));
        }
        value["status"] = if previous["id"].as_i64().unwrap_or(0) > 0 {
            previous["status"].clone()
        } else {
            json!("供货中")
        };
    } else {
        if previous["status"] == "Confirmed" {
            return Err(conflict("已确认评价不能修改，请新建复评记录。"));
        }
        supplier::assessment(value, today).map_err(invalid)?;
        value["status"] = json!("Draft");
        value["assessedBy"] = json!(actor.name);
    }
    Ok(())
}
pub fn handle(
    store: &Store,
    actor: &Actor,
    clock: &BusinessClock,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let resource = resource(operation);
    let (parent_kind, parameter, field) = relation(resource);
    let parent_id = parameters
        .iter()
        .find(|(key, _)| *key == parameter)
        .and_then(|(_, value)| value.parse::<i64>().ok())
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("请先选择客户或供应商。"))?;
    let action = if operation == resource.list {
        "view"
    } else if operation == resource.create {
        "create"
    } else if operation == resource.update {
        "edit"
    } else if resource.delete == Some(operation) {
        "delete"
    } else {
        match operation {
            SET_PRIMARY_CRM_CONTACT | SET_PRIMARY_SUPPLIER_CONTACT => "set-primary",
            CONFIRM_SUPPLIER_ASSESSMENT => "approve",
            RESTORE_SUPPLIER_PRODUCT_LINK => "restore",
            _ => "deactivate",
        }
    };
    let permission_action = auth::operation_action(operation, resource.permission, action)?;
    store.transaction(|tx| {
        let parent = store::get(tx, parent_kind, parent_id)?;
        if !auth::visible(actor, resource.permission, permission_action, &parent) {
            return Err(error(403, "所属客户或供应商不在当前账号的操作范围内。"));
        }
        if operation == resource.list {
            let mut rows: Vec<_> = store::all(tx, resource.key)?
                .into_iter()
                .filter(|row| row[field] == parent_id)
                .collect();
            if resource.key == "supplier-assessments"
                && !auth::visible(actor, resource.permission, "approve", &parent)
            {
                rows.retain(|row| row["status"] == "Confirmed" || row["ownerUserId"] == actor.id);
            }
            rows.sort_by(|a, b| {
                (b["isPrimary"] == true)
                    .cmp(&(a["isPrimary"] == true))
                    .then_with(|| {
                        b["assessmentDate"]
                            .as_str()
                            .cmp(&a["assessmentDate"].as_str())
                    })
                    .then_with(|| b["id"].as_i64().cmp(&a["id"].as_i64()))
            });
            let rows = rows
                .into_iter()
                .map(|row| project(tx, resource, row))
                .collect::<Result<Vec<_>>>()?;
            return Ok(if operation == LIST_SUPPLIER_ASSESSMENTS {
                json!(rows)
            } else {
                store::page_only(rows, query)
            });
        }
        let id = if operation == resource.create {
            0
        } else {
            records::id(parameters)?
        };
        let previous = if id > 0 {
            let row = store::get(tx, resource.key, id)?;
            if row[field] != parent_id {
                return Err(error(404, "记录不属于所选客户或供应商。"));
            }
            let expected = query
                .iter()
                .find(|(key, _)| *key == "expectedVersion")
                .and_then(|(_, value)| value.parse().ok())
                .unwrap_or_else(|| store::expected(body));
            store::check_version(&row, expected)?;
            row
        } else {
            contracts::initial(contracts::schema(resource.schema))
        };
        if resource.delete == Some(operation) {
            if resource.key == "supplier-assessments" && previous["status"] == "Confirmed" {
                return Err(conflict("已确认评价不能删除。"));
            }
            if resource.key == "crm-contacts" {
                for mut follow_up in store::all(tx, "crm-follow-ups")?
                    .into_iter()
                    .filter(|row| row["crmContactId"] == id)
                {
                    follow_up["crmContactId"] = Value::Null;
                    store::save(
                        tx,
                        "crm-follow-ups",
                        follow_up["id"].as_i64().unwrap_or(0),
                        follow_up,
                        None,
                        actor,
                        "contact-deleted",
                    )?;
                }
            }
            if !tx.delete(
                resource.key,
                id,
                previous["versionNumber"].as_i64().unwrap_or(0),
            )? {
                return Err(conflict("记录版本已变化。"));
            }
            tx.append_audit_details(
                &AuditWrite {
                    kind: resource.key,
                    record_id: id,
                    version: previous["versionNumber"].as_i64().unwrap_or(0),
                    action,
                    actor_id: actor.id,
                    occurred_at: &store::timestamp(),
                    note: "",
                },
                &super::audit_values::changes(Some(&previous), None),
            )?;
            return Ok(json!({"success":true,"message":"记录已删除。"}));
        }
        let mut value = previous.clone();
        value[field] = json!(parent_id);
        if operation == resource.create || operation == resource.update {
            for key in contracts::properties(contracts::request(operation.id))
                .into_iter()
                .flat_map(|properties| properties.keys())
            {
                if !["id", "expectedVersion", field].contains(&key.as_str()) {
                    if let Some(item) = body.get(key) {
                        value[key] = item.clone();
                    }
                }
            }
            validate(
                tx,
                resource,
                actor,
                &previous,
                &mut value,
                &clock
                    .now()
                    .map_err(super::error::unavailable)?
                    .today
                    .to_string(),
            )?;
        } else if action == "set-primary" {
            if value["isPrimary"] == true {
                return project(tx, resource, value);
            }
            for mut sibling in store::all(tx, resource.key)?.into_iter().filter(|row| {
                row[field] == parent_id && row["id"] != id && row["isPrimary"] == true
            }) {
                sibling["isPrimary"] = json!(false);
                store::save(
                    tx,
                    resource.key,
                    sibling["id"].as_i64().unwrap_or(0),
                    sibling,
                    None,
                    actor,
                    action,
                )?;
            }
            value["isPrimary"] = json!(true);
        } else if action == "approve" {
            if value["status"] != "Draft" {
                return Err(conflict("此评价已经确认。"));
            }
            value["status"] = json!("Confirmed");
            value["confirmedBy"] = json!(actor.name);
            value["confirmedAt"] = json!(store::timestamp());
        } else {
            let restore = action == "restore";
            if (restore && !["暂停", "停用"].contains(&text(&value, "status").as_str()))
                || (!restore && value["status"] == "停用")
            {
                return Err(conflict("当前供货关系状态不允许此项操作。"));
            }
            value["status"] = json!(if restore { "供货中" } else { "停用" });
        }
        project(
            tx,
            resource,
            store::save(tx, resource.key, id, value, None, actor, action)?,
        )
    })
}
