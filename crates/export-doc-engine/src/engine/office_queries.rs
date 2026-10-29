use super::{
    auth,
    error::{Result, error, invalid},
    records::{self, text},
    store::{self, Actor, Store},
};
use chrono::{DateTime, Duration, Utc};
use export_doc_storage::Connection;
use serde_json::{Value, json};

pub fn list(store: &Store, actor: &Actor, kind: &str, query: &[(&str, String)]) -> Result<Value> {
    let get = |name: &str| {
        query
            .iter()
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let flag = |name: &str| match get(name) {
        "" | "false" => Ok(false),
        "true" => Ok(true),
        _ => Err(invalid("筛选开关须为 true 或 false。")),
    };
    let include_inactive = flag("includeInactive")?;
    let low_stock = flag("lowStockOnly")?;
    let mine = flag("mineOnly")?;
    let handling = flag("handlingOnly")?;
    if handling && !matches!(kind, "supply-requests" | "bookings") {
        return Err(invalid("只有资源申请支持待我办理筛选。"));
    }
    let keyword = store::normalize(get("keyword"));
    if keyword.chars().count() > 120 {
        return Err(invalid("搜索关键词不得超过 120 字。"));
    }
    let status = get("status");
    let allowed = if kind == "bookings" {
        &[
            "Pending",
            "Approved",
            "InUse",
            "Completed",
            "Rejected",
            "Cancelled",
        ][..]
    } else {
        &[
            "Pending",
            "Approved",
            "Issued",
            "Returned",
            "Rejected",
            "Cancelled",
        ][..]
    };
    if !status.is_empty() && !allowed.contains(&status) {
        return Err(invalid("未知的行政记录状态。"));
    }
    let instant = |name: &str| {
        if get(name).is_empty() {
            Ok(None)
        } else {
            DateTime::parse_from_rfc3339(get(name))
                .map(Some)
                .map_err(|_| invalid("筛选日期必须包含时区。"))
        }
    };
    let from = instant("from")?;
    let to = instant("to")?;
    if from.zip(to).is_some_and(|(from, to)| from >= to) {
        return Err(invalid("筛选结束时间须晚于开始时间。"));
    }
    let mut identifiers = vec![];
    for (key, field) in [
        ("requestId", "id"),
        (
            "resourceId",
            if kind == "bookings" {
                "meetingRoomId"
            } else {
                "officeSupplyId"
            },
        ),
        ("employeeId", "employeeId"),
        ("applicantUserId", "ownerUserId"),
    ] {
        if !get(key).is_empty() {
            let value = get(key)
                .parse::<i64>()
                .ok()
                .filter(|id| *id > 0)
                .ok_or_else(|| invalid("筛选编号必须大于零。"))?;
            identifiers.push((field, value));
        }
    }
    let resource = if matches!(kind, "rooms" | "bookings") {
        "office.rooms"
    } else {
        "office.supplies"
    };
    store.read(|tx| {
        let directory = matches!(kind, "rooms" | "supplies");
        let keys = super::handling::keys(tx, actor, resource)?;
        let mut keyword_keys = vec![];
        if directory && !keyword.is_empty() {
            for service in super::handling::directory(tx, actor, resource)?["items"]
                .as_array()
                .into_iter()
                .flatten()
            {
                if store::normalize(&text(service, "handlerNames")).contains(&keyword) {
                    keyword_keys.push(text(service, "key"));
                }
            }
        }
        let (page, size, offset) = store::page_parameters(query);
        let from = (!directory)
            .then(|| from.map(|date| date.to_rfc3339()))
            .flatten();
        let to = (!directory)
            .then(|| to.map(|date| date.to_rfc3339()))
            .flatten();
        let principals = super::oa::approval::queue_approvers(tx, actor, resource)?;
        let (total, rows) = tx.query_office(&export_doc_storage::OfficeQuery {
            viewer: actor.id,
            assigned_only: actor.oa_assigned_only,
            principals: &principals,
            kind,
            company: &actor.company,
            view: auth::data_scope(actor, resource, "view"),
            issue: auth::data_scope(actor, resource, "issue"),
            return_scope: auth::data_scope(actor, resource, "return"),
            handling_keys: &keys,
            keyword_handling_keys: &keyword_keys,
            keyword: if directory { &keyword } else { "" },
            status: if directory { "" } else { status },
            identifiers: if directory { &[] } else { &identifiers },
            owner: (!directory && mine).then_some(actor.id),
            include_inactive,
            low_stock,
            handling_only: handling,
            can_issue: auth::authorize(actor, resource, "issue").is_ok(),
            can_return: auth::authorize(actor, resource, "return").is_ok(),
            from: from.as_deref(),
            to: to.as_deref(),
            offset,
            limit: size as i64,
        })?;
        let mut items = Vec::with_capacity(rows.len());
        for row in rows {
            let mut row = super::handling::project_resource(tx, actor, resource, row)?;
            if kind == "rooms" {
                row["inUse"] = json!(
                    tx.query_generic(&export_doc_storage::GenericQuery {
                        kind: "bookings",
                        scope: export_doc_storage::DataScope {
                            company: &actor.company,
                            company_wide: true,
                            ..Default::default()
                        },
                        filters: &[
                            ("meetingRoomId", row["id"].to_string()),
                            ("status", "InUse".into())
                        ],
                        keyword: "",
                        search_root: None,
                        fields: &[("id", "id")],
                        offset: 0,
                        limit: 1,
                    })?
                    .0 > 0
                );
            }
            items.push(row);
        }
        Ok(crate::contracts::page(items, total as usize, page, size))
    })
}

pub fn availability(
    store: &Store,
    actor: &Actor,
    id: i64,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, "office.rooms", "view")?;
    let instant = |key: &str| {
        query
            .iter()
            .find(|(name, _)| *name == key)
            .and_then(|(_, value)| DateTime::parse_from_rfc3339(value).ok())
            .ok_or_else(|| invalid("预约查询需要带时区的起止时间。"))
    };
    let from = instant("from")?;
    let to = instant("to")?;
    if to <= from || to - from > Duration::days(7) + Duration::hours(1) {
        return Err(invalid("可用时段查询须在一周以内。"));
    }
    store.read(|tx|{
        let room=records::parent(tx,actor,"rooms",id)?;
        if room["companyScope"]!=actor.company{return Err(error(403,"不能查看其他公司的会议室。"));}
        let now=Utc::now();let mut slots=vec![];
        for booking in store::matching(tx,"bookings", &[("meetingRoomId",id.to_string()),("companyScope",actor.company.clone())], &[("startsAt","startsAt"),("endsAt","endsAt"),("status","status")])?.iter().filter(|booking|["Pending","Approved","InUse"].contains(&text(booking,"status").as_str())) {
            let start=DateTime::parse_from_rfc3339(&text(booking,"startsAt")).map_err(|_|super::error::unavailable("预约开始时间损坏。"))?;
            let end=DateTime::parse_from_rfc3339(&text(booking,"endsAt")).map_err(|_|super::error::unavailable("预约结束时间损坏。"))?;
            if start<to&&(end>from||booking["status"]=="InUse") {
                slots.push((start,json!({"startsAt":start.to_rfc3339(),"endsAt":if booking["status"]=="InUse"&&end<now{to.to_rfc3339()}else{end.to_rfc3339()},"status":booking["status"]})));
            }
        }
        slots.sort_by_key(|(start,_)|*start);
        Ok(json!(slots.into_iter().map(|(_,slot)|slot).collect::<Vec<_>>()))
    })
}
pub fn clearance(tx: &Connection, person: &Value) -> Result<Value> {
    let id = person["id"]
        .as_i64()
        .ok_or_else(|| invalid("人员编号无效。"))?;
    let account = person["account"]["id"].as_i64();
    clearance_for(tx, Some(id), account, &person["companyScope"])
}
pub fn account_clearance(tx: &Connection, account: &Value) -> Result<Value> {
    let id = account["id"]
        .as_i64()
        .ok_or_else(|| invalid("账号编号无效。"))?;
    let employee =
        super::personnel_accounts::linked_person(tx, id)?.and_then(|person| person["id"].as_i64());
    clearance_for(tx, employee, Some(id), &account["companyScope"])
}
fn clearance_for(
    tx: &Connection,
    employee: Option<i64>,
    account: Option<i64>,
    company: &Value,
) -> Result<Value> {
    let related = |kind| -> Result<Vec<Value>> {
        let mut rows = std::collections::BTreeMap::new();
        for (field, id) in [("employeeId", employee), ("ownerUserId", account)] {
            if let Some(id) = id {
                for row in store::matching(
                    tx,
                    kind,
                    &[
                        ("companyScope", company.as_str().unwrap_or("").into()),
                        (field, id.to_string()),
                    ],
                    &[],
                )? {
                    rows.insert(records::positive(&row, "id", "行政记录")?, row);
                }
            }
        }
        Ok(rows.into_values().collect())
    };
    let mut meetings = vec![];
    let mut supplies = vec![];
    for booking in related("bookings")?
        .iter()
        .filter(|r| ["Pending", "Approved", "InUse"].contains(&text(r, "status").as_str()))
    {
        meetings.push(json!({"kind":"rooms","requestId":booking["id"],"resourceName":booking["roomName"],"status":booking["status"],"outstandingQuantity":if booking["status"]=="InUse"{1}else{0}}));
    }
    for request in related("supply-requests")?.iter() {
        let remaining = request["quantity"].as_i64().unwrap_or(0)
            - request["returnedQuantity"].as_i64().unwrap_or(0);
        let pending = ["Pending", "Approved"].contains(&text(request, "status").as_str());
        if pending
            || (request["status"] == "Issued" && request["isReturnable"] == true && remaining > 0)
        {
            supplies.push(json!({"kind":"supplies","requestId":request["id"],"resourceName":request["supplyName"],"status":request["status"],"outstandingQuantity":if pending{0}else{remaining}}));
        }
    }
    let departments = match employee {
        Some(employee) => store::matching(
            tx,
            "departments",
            &[
                ("companyCode", company.as_str().unwrap_or("").into()),
                ("managerEmployeeId", employee.to_string()),
            ],
            &[("code", "code"), ("name", "name")],
        )?,
        None => vec![],
    }
    .iter()
    .map(|r| json!({"code":r["code"],"name":r["name"]}))
    .collect::<Vec<_>>();
    let meeting_count = meetings.len();
    let supply_count = supplies.len();
    let mut approval_count =
        super::oa::references(tx, company.as_str().unwrap_or(""), employee, account, true)?;
    let mut handling_services = vec![];
    if let Some(user) = account {
        handling_services = super::handling::services(tx, company.as_str().unwrap_or(""))?
            .iter()
            .filter(|s| {
                s["handlerUserIds"]
                    .as_array()
                    .is_some_and(|ids| ids.contains(&json!(user)))
            })
            .map(|s| text(s, "name"))
            .collect();
        approval_count += super::oa::approval_references::assigned(
            tx,
            company.as_str().unwrap_or(""),
            user,
            true,
        )?;
    }
    let clear = meeting_count == 0
        && supply_count == 0
        && approval_count == 0
        && handling_services.is_empty();
    let reporting_count = if let Some(employee) = employee {
        super::personnel_organization::references(
            tx,
            company.as_str().unwrap_or(""),
            employee,
            true,
        )?
    } else {
        0
    };
    let executive = match employee {
        Some(id) => !store::matching(
            tx,
            "companies",
            &[("executiveEmployeeId", id.to_string())],
            &[("id", "id")],
        )?
        .is_empty(),
        None => false,
    };
    meetings.sort_by_key(|r| r["requestId"].as_i64());
    supplies.sort_by_key(|r| r["requestId"].as_i64());
    Ok(
        json!({"meetingCount":meeting_count,"supplyCount":supply_count,"approvalCount":approval_count,"handlingCount":handling_services.len(),"handlingServices":handling_services,"items":meetings.into_iter().take(20).chain(supplies.into_iter().take(20)).collect::<Vec<_>>(),"canDepart":clear&&departments.is_empty()&&reporting_count==0&&!executive,"isClear":clear,"managedDepartments":departments,"reportingCount":reporting_count,"isCompanyExecutive":executive}),
    )
}
