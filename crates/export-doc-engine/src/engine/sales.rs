//! Opportunity use cases own quotation permissions, versions and business history.
use super::{
    auth,
    error::{Result, conflict, error, invalid},
    records::{id, text},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*};
use export_doc_domain::sales;
use export_doc_storage::Connection;
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    QUERY_SALES_OPPORTUNITIES,
    GET_SALES_OPPORTUNITY,
    CREATE_SALES_OPPORTUNITY,
    UPDATE_SALES_OPPORTUNITY,
    TRANSITION_SALES_OPPORTUNITY,
    ARCHIVE_SALES_OPPORTUNITY,
    LIST_SALES_OPPORTUNITY_HISTORY,
];
const KIND: &str = "opportunities";
const PERMISSION: &str = "sales.opportunities";

fn checked(
    tx: &Connection,
    actor: &Actor,
    record_id: i64,
    action: &str,
    archived: bool,
) -> Result<Value> {
    let value = store::get(tx, KIND, record_id)?;
    if !auth::visible(actor, PERMISSION, action, &value) {
        return Err(error(403, "当前账号无权操作此商机。"));
    }
    if !archived && value["isArchived"] == true {
        return Err(error(404, "商机已归档，请从版本历史追溯。"));
    }
    Ok(value)
}

fn customer(
    tx: &Connection,
    actor: &Actor,
    customer_id: i64,
    permission: &str,
    action: &str,
) -> Result<Value> {
    let value = store::get(tx, "crm-customers", customer_id)?;
    if !auth::visible(actor, permission, action, &value) {
        return Err(error(403, "当前账号无权访问关联客户。"));
    }
    Ok(value)
}

pub(super) fn details(
    tx: &Connection,
    actor: &Actor,
    mut value: Value,
    action: &str,
) -> Result<Value> {
    let party = customer(
        tx,
        actor,
        value["crmCustomerId"].as_i64().unwrap_or(0),
        if action == "view" {
            "sales.customers"
        } else {
            PERMISSION
        },
        action,
    )?;
    value["customerName"] = party["name"].clone();
    value["productCode"] = json!("");
    value["productName"] = json!("");
    if let Some(product_id) = value["productId"].as_i64().filter(|id| *id > 0) {
        auth::authorize(actor, "common.product-reference", "view")?;
        let product = store::get(tx, "products", product_id)?;
        value["productCode"] = json!(text(&product, "productCode"));
        let name = text(&product, "nameCN");
        value["productName"] = json!(if name.is_empty() {
            text(&product, "nameEN")
        } else {
            name
        });
    }
    value["allowedNextStages"] = json!(sales::next_stages(value["stage"].as_str().unwrap_or("")));
    Ok(contracts::project(
        contracts::schema("ApiSalesOpportunityDto"),
        value,
    ))
}

pub fn visible(tx: &Connection, actor: &Actor) -> Result<Vec<Value>> {
    let customers = store::all(tx, "crm-customers")?;
    store::all(tx, KIND)?
        .into_iter()
        .filter(|value| {
            value["isArchived"] != true && auth::visible(actor, PERMISSION, "view", value)
        })
        .filter(|value| {
            customers.iter().any(|party| {
                party["id"] == value["crmCustomerId"]
                    && auth::visible(actor, "sales.customers", "view", party)
            })
        })
        .map(Ok)
        .collect()
}

fn list(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    auth::authorize(actor, "common.product-reference", "view")?;
    let read = |key: &str| {
        query
            .iter()
            .find(|(name, _)| *name == key)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let stage = read("stage").trim();
    if !stage.is_empty() && !sales::STAGES.contains(&stage) {
        return Err(invalid("商机阶段无效。"));
    }
    let keyword = store::normalize(read("keyword"));
    let tx = store.connection()?;
    let mut rows = visible(&tx, actor)?;
    rows.sort_by(|left, right| {
        sales::closed(&text(left, "stage"))
            .cmp(&sales::closed(&text(right, "stage")))
            .then_with(|| right["updatedAt"].as_str().cmp(&left["updatedAt"].as_str()))
            .then_with(|| right["id"].as_i64().cmp(&left["id"].as_i64()))
    });
    let mut rows: Vec<_> = rows
        .into_iter()
        .map(|row| details(&tx, actor, row, "view"))
        .collect::<Result<_>>()?;
    rows.retain(|value| {
        (stage.is_empty() || value["stage"] == stage)
            && (keyword.is_empty()
                || [
                    "title",
                    "quotationNo",
                    "nextAction",
                    "customerName",
                    "productCode",
                    "productName",
                ]
                .iter()
                .any(|field| store::normalize(&text(value, field)).contains(&keyword)))
            && (read("crmCustomerId").is_empty()
                || value["crmCustomerId"]
                    .as_i64()
                    .is_some_and(|id| id.to_string() == read("crmCustomerId")))
    });
    let size = read("pageSize")
        .parse::<usize>()
        .unwrap_or(20)
        .clamp(10, 100);
    let page = read("pageNumber").parse::<usize>().unwrap_or(1).max(1);
    let count = rows.len();
    Ok(contracts::page(
        rows.into_iter()
            .skip(page.saturating_sub(1).saturating_mul(size))
            .take(size)
            .collect(),
        count,
        page,
        size,
    ))
}

fn append(
    tx: &Connection,
    actor: &Actor,
    value: &Value,
    change_type: &str,
    note: &str,
) -> Result<()> {
    let mut snapshot = contracts::project(
        contracts::schema("ApiSalesOpportunityHistoryDto"),
        value.clone(),
    );
    snapshot["salesOpportunityId"] = value["id"].clone();
    snapshot["changeType"] = json!(change_type);
    snapshot["changeNote"] = json!(note);
    snapshot["changedBy"] = json!(actor.name);
    snapshot["createdAt"] = value["updatedAt"].clone();
    store::save(
        tx,
        "opportunity-events",
        0,
        json!({"opportunityId":value["id"], "snapshot":snapshot}),
        None,
        actor,
        change_type,
    )?;
    Ok(())
}

fn history(store: &Store, actor: &Actor, record_id: i64) -> Result<Value> {
    let tx = store.connection()?;
    checked(&tx, actor, record_id, "view", true)?;
    let mut rows: Vec<_> = store::all(&tx, "opportunity-events")?
        .into_iter()
        .filter(|row| row["opportunityId"] == record_id)
        .map(|row| {
            let mut snapshot = row["snapshot"].clone();
            snapshot["id"] = row["id"].clone();
            snapshot
        })
        .collect();
    rows.sort_by_key(|row| std::cmp::Reverse((row["versionNumber"].as_i64(), row["id"].as_i64())));
    Ok(json!(rows))
}

fn save(store: &Store, actor: &Actor, record_id: i64, body: &Value) -> Result<Value> {
    let mut request: ApiSalesOpportunitySaveRequest = serde_json::from_value(body.clone())
        .map_err(|cause| invalid(format!("商机资料无效：{cause}")))?;
    if record_id == 0 && request.id > 0 {
        return Err(invalid("新增商机不能包含已有 ID。"));
    }
    request.id = record_id;
    sales::normalize(&mut request).map_err(invalid)?;
    let action = if record_id == 0 { "create" } else { "edit" };
    store.transaction(|tx| {
        let previous = if record_id > 0 {
            let previous = checked(tx, actor, record_id, action, false)?;
            store::check_version(&previous, request.expected_version.unwrap_or(0))?;
            if sales::closed(&text(&previous, "stage")) {
                return Err(conflict(
                    "已成交或已失单商机不可直接编辑，请先按阶段流转规则重新打开。",
                ));
            }
            previous
        } else {
            contracts::initial(contracts::schema("ApiSalesOpportunityDto"))
        };
        customer(tx, actor, request.crm_customer_id, PERMISSION, action)?;
        let mut value = contracts::overlay(previous.clone(), &serde_json::to_value(&request)?);
        // Optional values cleared in the editor must also clear the saved record.
        value["productId"] = json!(request.product_id);
        value["expectedCloseDate"] = json!(request.expected_close_date);
        value["stage"] = if record_id == 0 {
            json!("线索")
        } else {
            previous["stage"].clone()
        };
        value["isArchived"] = json!(false);
        let quote_changed = sales::QUOTE_FIELDS
            .iter()
            .any(|field| previous[*field] != value[*field]);
        if record_id == 0 && sales::has_quote(&request) {
            auth::authorize(actor, "sales.quotes", "create")?;
        } else if record_id > 0
            && quote_changed
            && !auth::visible(actor, "sales.quotes", "edit", &previous)
        {
            return Err(error(403, "当前账号不能修改该商机的报价信息。"));
        }
        let projected = details(tx, actor, value.clone(), action)?;
        for key in [
            "customerName",
            "productCode",
            "productName",
            "allowedNextStages",
        ] {
            value[key] = projected[key].clone();
        }
        let note = request.change_note.as_deref().unwrap_or("");
        let changed = ["crmCustomerId", "productId", "title", "nextAction", "notes"]
            .iter()
            .any(|field| previous[*field] != value[*field]);
        let change_type = if record_id == 0 {
            "创建"
        } else if quote_changed {
            "报价更新"
        } else if !note.is_empty() {
            "进展备注"
        } else if changed {
            "资料更新"
        } else {
            ""
        };
        value.as_object_mut().unwrap().remove("changeNote");
        let identity = text(&value, "quotationNo");
        let saved = store::save(tx, KIND, record_id, value, Some(identity), actor, action)?;
        if !change_type.is_empty() {
            append(tx, actor, &saved, change_type, note)?;
        }
        details(tx, actor, saved, action)
    })
}

fn action(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    record_id: i64,
    body: &Value,
) -> Result<Value> {
    let action = if operation == ARCHIVE_SALES_OPPORTUNITY {
        "archive"
    } else {
        "transition"
    };
    store.transaction(|tx| {
        let mut value = checked(tx, actor, record_id, action, false)?;
        store::check_version(&value, store::expected(body))?;
        let (change_type, note) = if operation == ARCHIVE_SALES_OPPORTUNITY {
            value["isArchived"] = json!(true);
            ("归档", "商机已归档，历史版本保留。".to_owned())
        } else {
            let from = text(&value, "stage");
            let to = text(body, "nextStage");
            if from == to {
                return Err(conflict("商机已经处于目标阶段。"));
            }
            if !sales::next_stages(&from).contains(&to.as_str()) {
                return Err(invalid(
                    "请按销售流程逐步推进或退回，不能直接跳转到该阶段。",
                ));
            }
            let note = sales::clean(&text(body, "changeNote"));
            if note.chars().count() > 1000 {
                return Err(invalid("阶段流转说明不能超过 1000 个字符。"));
            }
            details(tx, actor, value.clone(), action)?;
            value["stage"] = json!(to);
            (
                "阶段变更",
                if note.is_empty() {
                    format!("阶段从“{from}”流转到“{to}”。")
                } else {
                    note
                },
            )
        };
        let identity = text(&value, "quotationNo");
        let saved = store::save(tx, KIND, record_id, value, Some(identity), actor, action)?;
        append(tx, actor, &saved, change_type, &note)?;
        if operation == ARCHIVE_SALES_OPPORTUNITY {
            Ok(json!({"success":true,"message":"商机已归档，历史版本仍可查询。"}))
        } else {
            details(tx, actor, saved, action)
        }
    })
}

pub fn handle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    match operation {
        QUERY_SALES_OPPORTUNITIES => list(store, actor, query),
        GET_SALES_OPPORTUNITY => {
            auth::authorize(actor, "common.product-reference", "view")?;
            let tx = store.connection()?;
            details(
                &tx,
                actor,
                checked(&tx, actor, id(parameters)?, "view", false)?,
                "view",
            )
        }
        LIST_SALES_OPPORTUNITY_HISTORY => history(store, actor, id(parameters)?),
        CREATE_SALES_OPPORTUNITY => save(store, actor, 0, body),
        UPDATE_SALES_OPPORTUNITY => save(store, actor, id(parameters)?, body),
        _ => action(store, actor, operation, id(parameters)?, body),
    }
}
