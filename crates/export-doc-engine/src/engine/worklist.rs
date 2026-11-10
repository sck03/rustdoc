use super::{
    auth,
    error::{Result, invalid, unavailable},
    records::text,
    store::{Actor, Store},
};
use crate::clock::{BusinessClock, BusinessTime};
use crate::engine::store::Connection;
use chrono::{DateTime, Duration, NaiveDate};
use serde_json::{Value, json};
#[path = "worklist_oa.rs"]
mod oa;
#[path = "worklist_page.rs"]
mod page;
#[path = "worklist_reads.rs"]
mod reads;
use page::{Group, Selection};
use reads::visit;

fn item(
    record: &Value,
    title: String,
    description: String,
    parent: Value,
    date: Value,
    instant: Value,
) -> Value {
    json!({"recordId":record["id"],"parentId":parent,"title":title,"description":description,"dueDate":date,"dueAt":instant,"isOverdue":false})
}
fn allowed(actor: &Actor, resource: &str, actions: &[&str]) -> bool {
    actions
        .iter()
        .all(|action| auth::authorize(actor, resource, action).is_ok())
}
fn groups(tx: &Connection, actor: &Actor, selection: &Selection<'_>) -> Result<Vec<Group>> {
    let mut groups = vec![];
    if allowed(actor, "document.invoices", &["view", "operate"]) {
        let mut group = Group::new("invoice-review", "单据待核对");
        if selection.needs(&[group.key], &groups) {
            visit(
                tx,
                actor,
                "invoices",
                "document.invoices",
                &["view", "operate"],
                false,
                |r| {
                    group.push(
                        item(
                            &r,
                            text(&r, "invoiceNo"),
                            format!("{} · {}", text(&r, "type"), text(&r, "customerNameEN")),
                            Value::Null,
                            Value::Null,
                            Value::Null,
                        ),
                        selection,
                    )
                },
            )?;
        }
        groups.push(group);
    }
    if allowed(actor, "sales.follow-ups", &["view", "complete"])
        && allowed(actor, "sales.customers", &["view"])
    {
        let mut group = Group::new("customer-follow-up", "客户跟进");
        if selection.needs(&[group.key], &groups) {
            visit(
                tx,
                actor,
                "crm-follow-ups",
                "sales.follow-ups",
                &["view", "complete"],
                false,
                |r| {
                    let next = text(&r, "nextAction");
                    group.push(
                        item(
                            &r,
                            text(&r, "customerName"),
                            if next.is_empty() {
                                text(&r, "summary")
                            } else {
                                next
                            },
                            r["crmCustomerId"].clone(),
                            Value::Null,
                            r["nextFollowUpAt"].clone(),
                        ),
                        selection,
                    )
                },
            )?;
        }
        groups.push(group);
    }
    if allowed(actor, "office.rooms", &["view"]) {
        let approve = allowed(actor, "office.rooms", &["approve"]);
        let mut approval = Group::new("meeting-approval", "会议室待审批");
        let mut collection = Group::new("meeting-collection", "会议室待使用／交接");
        let mut returns = Group::new("meeting-return", "会议室待结束／归还");
        if selection.needs(&[approval.key, collection.key, returns.key], &groups) {
            visit(
                tx,
                actor,
                "bookings",
                "office.rooms",
                &["view"],
                true,
                |r| {
                    let own = r["ownerUserId"] == actor.id;
                    match text(&r, "status").as_str() {
                        "Pending"
                            if approve
                                && !own
                                && super::oa::approval::authority(tx, actor, &r)?.is_some() =>
                        {
                            approval.push(
                                item(
                                    &r,
                                    text(&r, "title"),
                                    format!("{} · 待审批", text(&r, "applicantName")),
                                    Value::Null,
                                    Value::Null,
                                    r["startsAt"].clone(),
                                ),
                                selection,
                            )?
                        }
                        "Approved" | "InUse" => {
                            let collecting = r["status"] == "Approved";
                            let action = if collecting { "issue" } else { "return" };
                            if own
                                || super::handling::resource_access(
                                    tx,
                                    actor,
                                    "office.rooms",
                                    action,
                                    &r,
                                    false,
                                )?
                            {
                                let (group, due, label) = if collecting {
                                    (&mut collection, "startsAt", "待使用／交接")
                                } else {
                                    (&mut returns, "endsAt", "待结束使用／归还钥匙")
                                };
                                group.push(
                                    item(
                                        &r,
                                        text(&r, "title"),
                                        format!("{} · {label}", text(&r, "applicantName")),
                                        Value::Null,
                                        Value::Null,
                                        r[due].clone(),
                                    ),
                                    selection,
                                )?;
                            }
                        }
                        _ => {}
                    }
                    Ok(())
                },
            )?;
        }
        if approve {
            groups.push(approval);
        }
        groups.extend([collection, returns]);
    }
    if allowed(actor, "office.supplies", &["view"]) {
        let approve = allowed(actor, "office.supplies", &["approve"]);
        let mut approval = Group::new("supply-approval", "物品待审批");
        let mut collection = Group::new("supply-collection", "物品待领用／发放");
        let mut returns = Group::new("supply-return", "借用物品待归还");
        if selection.needs(&[approval.key, collection.key, returns.key], &groups) {
            visit(
                tx,
                actor,
                "supply-requests",
                "office.supplies",
                &["view"],
                true,
                |r| {
                    let own = r["ownerUserId"] == actor.id;
                    match text(&r, "status").as_str() {
                        "Pending"
                            if approve
                                && !own
                                && super::oa::approval::authority(tx, actor, &r)?.is_some() =>
                        {
                            approval.push(
                                item(
                                    &r,
                                    format!("{} · 物品领用", text(&r, "applicantName")),
                                    text(&r, "purpose"),
                                    Value::Null,
                                    Value::Null,
                                    Value::Null,
                                ),
                                selection,
                            )?
                        }
                        "Approved" | "Issued" => {
                            let collecting = r["status"] == "Approved";
                            let action = if collecting { "issue" } else { "return" };
                            if own
                                || super::handling::resource_access(
                                    tx,
                                    actor,
                                    "office.supplies",
                                    action,
                                    &r,
                                    false,
                                )?
                            {
                                let description = format!(
                                    "{} · {}",
                                    text(&r, "applicantName"),
                                    text(&r, "purpose")
                                );
                                if collecting {
                                    collection.push(
                                        item(
                                            &r,
                                            text(&r, "supplyName"),
                                            description,
                                            Value::Null,
                                            Value::Null,
                                            Value::Null,
                                        ),
                                        selection,
                                    )?;
                                } else if r["isReturnable"] == true
                                    && r["returnedQuantity"].as_i64().unwrap_or(0)
                                        < r["quantity"].as_i64().unwrap_or(0)
                                {
                                    returns.push(
                                        item(
                                            &r,
                                            text(&r, "supplyName"),
                                            description,
                                            Value::Null,
                                            r["returnDueDate"].clone(),
                                            Value::Null,
                                        ),
                                        selection,
                                    )?;
                                }
                            }
                        }
                        _ => {}
                    }
                    Ok(())
                },
            )?;
        }
        if approve {
            groups.push(approval);
        }
        groups.extend([collection, returns]);
    }
    if allowed(actor, "office.people", &["view", "view-details"]) {
        let mut probation = Group::new("probation-end", "试用期到期");
        let mut contract = Group::new("contract-end", "劳动合同到期");
        if selection.needs(&[probation.key, contract.key], &groups) {
            visit(
                tx,
                actor,
                "people",
                "office.people",
                &["view", "view-details"],
                true,
                |r| {
                    for (group, field, eligible) in [
                        (
                            &mut probation,
                            "probationEndsOn",
                            r["status"] == "Probation",
                        ),
                        (&mut contract, "contractEndsOn", true),
                    ] {
                        if eligible && r[field].is_string() {
                            group.push(
                                item(
                                    &r,
                                    text(&r, "fullName"),
                                    format!("{} · {}", text(&r, "employeeNumber"), group.name),
                                    Value::Null,
                                    r[field].clone(),
                                    Value::Null,
                                ),
                                selection,
                            )?;
                        }
                    }
                    Ok(())
                },
            )?;
        }
        groups.extend([probation, contract]);
    }
    oa::append_groups(tx, actor, selection, &mut groups)?;
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
    let parameter = |key: &str| crate::engine::request_values::find(query, key).unwrap_or("");
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
    let include_summary = match parameter("includeSummary") {
        "" | "true" => true,
        "false" => false,
        _ => return Err(invalid("待办汇总参数无效。")),
    };
    let after = page::Cursor::parse(parameter("cursor"), due, selected, page, size)?;
    let now = clock.now().map_err(unavailable)?;
    store.read_for(actor,crate::generated_api::GET_WORKLIST,query,|tx,actor| {
        let selection=Selection { now:&now,due,source:selected,keep:size+1,after:after.as_ref(),include_summary };
        let groups=groups(tx,actor,&selection)?;
        if !selected.is_empty() && !groups.iter().any(|group|group.key==selected) { return Err(invalid("该待办来源不存在或当前账号无权查看。")); }
        let mut sources=vec![];
        let mut rows=vec![];
        let mut count=0;
        if after.as_ref().is_some_and(|cursor| !groups.iter().any(|group| group.key == cursor.source)) {
            return Err(invalid("游标来源已不可见，请从首页重新读取。"));
        }
        let mut reached=after.is_none();
        for group in groups {
            let mut source=json!({"key":group.key,"name":group.name});
            if include_summary { source["count"]=json!(group.count); }
            sources.push(source);
            if !selected.is_empty() && selected!=group.key { continue; }
            count+=group.count;
            if after.as_ref().is_some_and(|cursor| cursor.source == group.key) { reached=true; }
            if !reached { continue; }
            if rows.len()<=size {
                rows.extend(group.into_rows().take(size+1-rows.len()));
            }
        }
        let has_next=rows.len()>size;
        rows.truncate(size);
        let next_cursor=if has_next {
            let last=rows.last().ok_or_else(|| unavailable("待办分页结果无效。"))?;
            Some(serde_json::to_string(&page::Cursor {
                source:text(last,"source"),deadline:deadline(last,&now)?.map(|d| d.2).unwrap_or_else(|| "~".into()),
                id:last["recordId"].as_i64().ok_or_else(|| unavailable("待办编号无效。"))?,
                due:due.into(),filter:selected.into(),page:page+1,size,
            })?)
        } else { None };
        let mut result=crate::contracts::page(rows,count,page,size);
        result["pageNumber"]=json!(page);
        result["hasNextPage"]=json!(has_next);
        if !include_summary {
            let page=result.as_object_mut().ok_or_else(|| unavailable("待办分页结果无效。"))?;
            page.remove("totalCount"); page.remove("totalPages");
        }
        Ok(json!({"page":result,"sources":sources,"businessDate":now.today.to_string(),"asOf":now.utc_now.to_rfc3339(),"nextCursor":next_cursor}))
    })
}
