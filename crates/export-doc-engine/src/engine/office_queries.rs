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
    store.transaction(|tx| {
        let directory = matches!(kind, "rooms" | "supplies");
        let mut rows: Vec<_> = store::all(tx, kind)?
            .into_iter()
            .filter(|row| {
                row["companyScope"] == actor.company && auth::visible(actor, resource, "view", row)
            })
            .collect();
        if directory {
            rows.retain(|row| {
                (include_inactive || row["isActive"] == true)
                    && (!low_stock || row["lowStock"] == true)
                    && (keyword.is_empty()
                        || ["name", "location"]
                            .iter()
                            .any(|field| store::normalize(&text(row, field)).contains(&keyword)))
            });
            if kind == "rooms" {
                let bookings = store::all(tx, "bookings")?;
                for row in &mut rows {
                    row["inUse"] = json!(
                        bookings
                            .iter()
                            .any(|booking| booking["meetingRoomId"] == row["id"]
                                && booking["status"] == "InUse")
                    );
                }
            }
            rows.sort_by_key(|row| {
                (
                    row["isActive"] != true,
                    text(row, "name"),
                    row["id"].as_i64(),
                )
            });
        } else {
            rows.retain(|row| {
                (!mine || row["ownerUserId"] == actor.id)
                    && (status.is_empty() || row["status"] == status)
                    && identifiers.iter().all(|(field, id)| row[*field] == *id)
            });
            let mut filtered = vec![];
            for mut row in rows {
                let (start, end) = if kind == "bookings" {
                    ("startsAt", "endsAt")
                } else {
                    ("createdAt", "createdAt")
                };
                let read_time = |key: &str| {
                    DateTime::parse_from_rfc3339(&text(&row, key))
                        .map_err(|_| super::error::unavailable("行政记录时间损坏。"))
                };
                let end = from.map(|_| read_time(end)).transpose()?;
                let start = to.map(|_| read_time(start)).transpose()?;
                if from.zip(end).is_some_and(|(from, end)| end <= from)
                    || to.zip(start).is_some_and(|(to, start)| start >= to)
                {
                    continue;
                }
                let (parent_kind, parent_id, fields) = if kind == "bookings" {
                    (
                        "rooms",
                        "meetingRoomId",
                        &[
                            ("roomName", "name"),
                            ("location", "location"),
                            ("requiresKey", "requiresKey"),
                        ][..],
                    )
                } else {
                    (
                        "supplies",
                        "officeSupplyId",
                        &[
                            ("supplyName", "name"),
                            ("unit", "unit"),
                            ("isReturnable", "isReturnable"),
                        ][..],
                    )
                };
                let parent = store::get(
                    tx,
                    parent_kind,
                    row[parent_id]
                        .as_i64()
                        .ok_or_else(|| super::error::unavailable("行政记录关联编号损坏。"))?,
                )?;
                for (target, source) in fields {
                    row[*target] = parent[*source].clone();
                }
                filtered.push(row);
            }
            rows = filtered;
            rows.sort_by_key(|row| std::cmp::Reverse((text(row, "createdAt"), row["id"].as_i64())));
        }
        let paging: Vec<_> = query
            .iter()
            .filter(|(key, _)| matches!(*key, "pageNumber" | "pageSize"))
            .cloned()
            .collect();
        Ok(store::paged(rows, &paging))
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
    store.transaction(|tx|{
        let room=records::parent(tx,actor,"rooms",id)?;
        if room["companyScope"]!=actor.company{return Err(error(403,"不能查看其他公司的会议室。"));}
        let now=Utc::now();let mut slots=vec![];
        for booking in store::all(tx,"bookings")?.iter().filter(|booking|booking["meetingRoomId"]==id&&booking["companyScope"]==actor.company&&["Pending","Approved","InUse"].contains(&text(booking,"status").as_str())) {
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
    let employee = store::all(tx, "people")?
        .iter()
        .find(|person| person["account"]["id"] == id)
        .and_then(|person| person["id"].as_i64());
    clearance_for(tx, employee, Some(id), &account["companyScope"])
}
fn clearance_for(
    tx: &Connection,
    employee: Option<i64>,
    account: Option<i64>,
    company: &Value,
) -> Result<Value> {
    let belongs = |record: &&Value| {
        record["companyScope"] == *company
            && (employee.is_some_and(|id| record["employeeId"] == id)
                || account.is_some_and(|user| record["ownerUserId"] == user))
    };
    let mut meetings = vec![];
    let mut supplies = vec![];
    for booking in store::all(tx, "bookings")?
        .iter()
        .filter(belongs)
        .filter(|r| ["Pending", "Approved", "InUse"].contains(&text(r, "status").as_str()))
    {
        meetings.push(json!({"kind":"rooms","requestId":booking["id"],"resourceName":booking["roomName"],"status":booking["status"],"outstandingQuantity":if booking["status"]=="InUse"{1}else{0}}));
    }
    for request in store::all(tx, "supply-requests")?.iter().filter(belongs) {
        let remaining = request["quantity"].as_i64().unwrap_or(0)
            - request["returnedQuantity"].as_i64().unwrap_or(0);
        let pending = ["Pending", "Approved"].contains(&text(request, "status").as_str());
        if pending
            || (request["status"] == "Issued" && request["isReturnable"] == true && remaining > 0)
        {
            supplies.push(json!({"kind":"supplies","requestId":request["id"],"resourceName":request["supplyName"],"status":request["status"],"outstandingQuantity":if pending{0}else{remaining}}));
        }
    }
    let departments = store::all(tx, "departments")?
        .iter()
        .filter(|r| {
            r["companyCode"] == *company && employee.is_some_and(|id| r["managerEmployeeId"] == id)
        })
        .map(|r| json!({"code":r["code"],"name":r["name"]}))
        .collect::<Vec<_>>();
    let meeting_count = meetings.len();
    let supply_count = supplies.len();
    let clear = meeting_count == 0 && supply_count == 0;
    meetings.sort_by_key(|r| r["requestId"].as_i64());
    supplies.sort_by_key(|r| r["requestId"].as_i64());
    Ok(
        json!({"meetingCount":meeting_count,"supplyCount":supply_count,"items":meetings.into_iter().take(20).chain(supplies.into_iter().take(20)).collect::<Vec<_>>(),"canDepart":clear&&departments.is_empty(),"isClear":clear,"managedDepartments":departments}),
    )
}
