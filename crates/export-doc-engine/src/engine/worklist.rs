use super::{
    auth,
    error::{Result, invalid, unavailable},
    records::text,
    store::{self, Actor, Store},
};
use crate::clock::{BusinessClock, BusinessTime};
use chrono::{DateTime, Duration, NaiveDate};
use export_doc_storage::Connection;
use serde_json::{Value, json};

struct Group {
    key: &'static str,
    name: &'static str,
    rows: Vec<Value>,
}

fn item(
    record: &Value,
    title: String,
    description: String,
    parent: Value,
    date: Value,
    instant: Value,
) -> Value {
    json!({"recordId":record["id"], "parentId":parent, "title":title, "description":description, "dueDate":date, "dueAt":instant, "isOverdue":false})
}
fn allowed(actor: &Actor, resource: &str, actions: &[&str]) -> bool {
    actions
        .iter()
        .all(|action| auth::authorize(actor, resource, action).is_ok())
}
fn rows(
    tx: &Connection,
    actor: &Actor,
    kind: &str,
    resource: &str,
    actions: &[&str],
    office: bool,
) -> Result<Vec<Value>> {
    Ok(store::all(tx, kind)?
        .into_iter()
        .filter(|record| {
            (!office || record["companyScope"] == actor.company)
                && actions
                    .iter()
                    .all(|action| auth::visible(actor, resource, action, record))
        })
        .collect())
}
fn groups(tx: &Connection, actor: &Actor) -> Result<Vec<Group>> {
    let local = tx.provider() == "SQLite";
    let mine = |record: &Value| local || record["ownerUserId"] == actor.id;
    let mut groups = vec![];
    if allowed(actor, "document.invoices", &["view", "operate"]) {
        let rows = rows(
            tx,
            actor,
            "invoices",
            "document.invoices",
            &["view", "operate"],
            false,
        )?
        .iter()
        .filter(|record| record["status"] == "Draft" && mine(record))
        .map(|record| {
            item(
                record,
                text(record, "invoiceNo"),
                format!(
                    "{} · {}",
                    text(record, "type"),
                    text(record, "customerNameEN")
                ),
                Value::Null,
                Value::Null,
                Value::Null,
            )
        })
        .collect();
        groups.push(Group {
            key: "invoice-review",
            name: "单据待核对",
            rows,
        });
    }
    if allowed(actor, "sales.follow-ups", &["view", "complete"])
        && allowed(actor, "sales.customers", &["view"])
    {
        let mut items = vec![];
        for record in rows(
            tx,
            actor,
            "crm-follow-ups",
            "sales.follow-ups",
            &["view", "complete"],
            false,
        )?
        .iter()
        .filter(|record| record["isCompleted"] != true && mine(record))
        {
            let customer = store::get(
                tx,
                "crm-customers",
                record["crmCustomerId"]
                    .as_i64()
                    .ok_or_else(|| unavailable("跟进记录缺少客户编号。"))?,
            )?;
            if !auth::visible(actor, "sales.customers", "view", &customer) {
                continue;
            }
            let next = text(record, "nextAction");
            items.push(item(
                record,
                text(&customer, "name"),
                if next.is_empty() {
                    text(record, "summary")
                } else {
                    next
                },
                customer["id"].clone(),
                Value::Null,
                record["nextFollowUpAt"].clone(),
            ));
        }
        groups.push(Group {
            key: "customer-follow-up",
            name: "客户跟进",
            rows: items,
        });
    }
    if allowed(actor, "office.rooms", &["view"]) {
        let meetings = rows(tx, actor, "bookings", "office.rooms", &["view"], true)?;
        if !local && allowed(actor, "office.rooms", &["approve"]) {
            let rows = meetings
                .iter()
                .filter(|r| {
                    r["status"] == "Pending"
                        && r["ownerUserId"] != actor.id
                        && auth::visible(actor, "office.rooms", "approve", r)
                })
                .map(|r| {
                    item(
                        r,
                        text(r, "title"),
                        format!("{} · 待审批", text(r, "applicantName")),
                        Value::Null,
                        Value::Null,
                        r["startsAt"].clone(),
                    )
                })
                .collect();
            groups.push(Group {
                key: "meeting-approval",
                name: "会议室待审批",
                rows,
            });
        }
        for (key, name, status, due, description) in [
            (
                "meeting-collection",
                if local {
                    "会议室待交接"
                } else {
                    "我的会议室待使用"
                },
                "Approved",
                "startsAt",
                "待领取钥匙",
            ),
            (
                "meeting-return",
                if local {
                    "钥匙待归还"
                } else {
                    "我的钥匙待归还"
                },
                "InUse",
                "endsAt",
                "待归还钥匙",
            ),
        ] {
            let rows = meetings
                .iter()
                .filter(|r| mine(r) && r["status"] == status)
                .map(|r| {
                    item(
                        r,
                        text(r, "title"),
                        format!("{} · {description}", text(r, "applicantName")),
                        Value::Null,
                        Value::Null,
                        r[due].clone(),
                    )
                })
                .collect();
            groups.push(Group { key, name, rows });
        }
    }
    if allowed(actor, "office.supplies", &["view"]) {
        let requests = rows(
            tx,
            actor,
            "supply-requests",
            "office.supplies",
            &["view"],
            true,
        )?;
        if !local && allowed(actor, "office.supplies", &["approve"]) {
            let rows = requests
                .iter()
                .filter(|r| {
                    r["status"] == "Pending"
                        && r["ownerUserId"] != actor.id
                        && auth::visible(actor, "office.supplies", "approve", r)
                })
                .map(|r| {
                    item(
                        r,
                        format!("{} · 物品领用", text(r, "applicantName")),
                        text(r, "purpose"),
                        Value::Null,
                        Value::Null,
                        Value::Null,
                    )
                })
                .collect();
            groups.push(Group {
                key: "supply-approval",
                name: "物品待审批",
                rows,
            });
        }
        let mut collection = vec![];
        let mut returns = vec![];
        for r in requests.iter().filter(|r| mine(r)) {
            if !["Approved", "Issued"].contains(&text(r, "status").as_str()) {
                continue;
            }
            let supply = store::get(
                tx,
                "supplies",
                r["officeSupplyId"]
                    .as_i64()
                    .ok_or_else(|| unavailable("领用记录缺少物品编号。"))?,
            )?;
            let description = format!("{} · {}", text(r, "applicantName"), text(r, "purpose"));
            if r["status"] == "Approved" {
                collection.push(item(
                    r,
                    text(&supply, "name"),
                    description,
                    Value::Null,
                    Value::Null,
                    Value::Null,
                ));
            } else if supply["isReturnable"] == true
                && r["returnedQuantity"].as_i64().unwrap_or(0) < r["quantity"].as_i64().unwrap_or(0)
            {
                returns.push(item(
                    r,
                    text(&supply, "name"),
                    description,
                    Value::Null,
                    r["returnDueDate"].clone(),
                    Value::Null,
                ));
            }
        }
        groups.push(Group {
            key: "supply-collection",
            name: if local {
                "物品待发放"
            } else {
                "我的物品待领用"
            },
            rows: collection,
        });
        groups.push(Group {
            key: "supply-return",
            name: if local {
                "借用物品待归还"
            } else {
                "我的借用物品待归还"
            },
            rows: returns,
        });
    }
    if allowed(actor, "office.people", &["view", "view-details"]) {
        let people = rows(
            tx,
            actor,
            "people",
            "office.people",
            &["view", "view-details"],
            true,
        )?;
        for (key, name, field) in [
            ("probation-end", "试用期到期", "probationEndsOn"),
            ("contract-end", "劳动合同到期", "contractEndsOn"),
        ] {
            let rows = people
                .iter()
                .filter(|r| {
                    r["status"] != "Departed"
                        && (key != "probation-end" || r["status"] == "Probation")
                        && r[field].is_string()
                })
                .map(|r| {
                    item(
                        r,
                        text(&r["profile"], "fullName"),
                        format!("{} · {name}", text(r, "employeeNumber")),
                        Value::Null,
                        r[field].clone(),
                        Value::Null,
                    )
                })
                .collect();
            groups.push(Group { key, name, rows });
        }
    }
    Ok(groups)
}
fn deadline(item: &Value, now: &BusinessTime) -> Result<Option<(bool, bool, String)>> {
    if let Some(date) = item["dueDate"].as_str() {
        let date = NaiveDate::parse_from_str(date, "%Y-%m-%d")
            .map_err(|_| unavailable("待办业务日期损坏。"))?;
        return Ok(Some((
            date < now.today,
            date >= now.today && date <= now.today + Duration::days(30),
            date.to_string(),
        )));
    }
    if let Some(instant) = item["dueAt"].as_str() {
        let instant =
            DateTime::parse_from_rfc3339(instant).map_err(|_| unavailable("待办业务时间损坏。"))?;
        return Ok(Some((
            instant < now.utc_now,
            instant >= now.utc_now && instant <= now.utc_now + Duration::days(30),
            instant.to_utc().to_rfc3339(),
        )));
    }
    Ok(None)
}
pub fn query(
    store: &Store,
    actor: &Actor,
    clock: &BusinessClock,
    query: &[(&str, String)],
) -> Result<Value> {
    let parameter = |key: &str| {
        query
            .iter()
            .find(|(k, _)| *k == key)
            .map(|(_, v)| v.as_str())
            .unwrap_or("")
    };
    let number = |key: &str, default: usize, max: usize| -> Result<usize> {
        let text = parameter(key);
        let value = if text.is_empty() {
            default
        } else {
            text.parse().map_err(|_| invalid("待办分页无效。"))?
        };
        if !(1..=max).contains(&value) {
            return Err(invalid("待办分页超出范围。"));
        }
        Ok(value)
    };
    let page = number("pageNumber", 1, 1_000_000)?;
    let size = number("pageSize", 20, 100)?;
    let due = match parameter("due") {
        "" => "All",
        value => value,
    };
    if !["All", "Overdue", "Upcoming", "Undated"].contains(&due) {
        return Err(invalid("未知待办到期筛选。"));
    }
    let selected = parameter("source");
    let now = clock.now().map_err(unavailable)?;
    store.transaction(|tx|{
        let groups=groups(tx,actor)?;
        if !selected.is_empty()&&!groups.iter().any(|g|g.key==selected){return Err(invalid("该待办来源不存在或当前账号无权查看。"));}
        let mut sources=vec![];let mut all=vec![];
        for group in groups {
            let mut rows=vec![];
            for mut item in group.rows {
                crate::operation::check()?;
                let deadline=deadline(&item,&now)?;
                let included=match due {"Overdue"=>deadline.as_ref().is_some_and(|d|d.0),"Upcoming"=>deadline.as_ref().is_some_and(|d|d.1),"Undated"=>deadline.is_none(),_=>true};
                if !included{continue;}
                item["source"]=json!(group.key);item["isOverdue"]=json!(deadline.as_ref().is_some_and(|d|d.0));
                rows.push((deadline.map(|d|d.2).unwrap_or_else(||"~".into()),item));
            }
            rows.sort_by(|a,b|a.0.cmp(&b.0).then_with(||a.1["recordId"].as_i64().cmp(&b.1["recordId"].as_i64())));
            sources.push(json!({"key":group.key,"name":group.name,"count":rows.len()}));
            if selected.is_empty()||selected==group.key {all.extend(rows.into_iter().map(|(_,item)|item));}
        }
        let count=all.len();
        Ok(json!({"page":crate::contracts::page(all.into_iter().skip((page-1)*size).take(size).collect(),count,page,size),"sources":sources,"businessDate":now.today.to_string(),"asOf":now.utc_now.to_rfc3339()}))
    })
}
