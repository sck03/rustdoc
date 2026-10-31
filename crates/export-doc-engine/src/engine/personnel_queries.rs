//! Personnel directory and detail projections never expose a stored permission snapshot.
use super::{
    auth,
    error::{Result, invalid, unavailable},
    office,
    records::text,
    store::{self, Actor, Store},
};
use crate::engine::store::Connection;
use crate::{clock::BusinessClock, contracts};
use chrono::{Duration, NaiveDate};
use export_doc_storage::{DataScope, GenericQuery, PersonnelQuery};
use serde_json::{Value, json};

pub fn employee(tx: &Connection, actor: &Actor, record: &Value) -> Result<Value> {
    directory_entry(tx, actor, record, &mut Default::default())
}
fn directory_entry(
    tx: &Connection,
    actor: &Actor,
    record: &Value,
    departments: &mut super::organization::DepartmentNames,
) -> Result<Value> {
    let mut employee = record["employee"].clone();
    employee["departmentName"] = json!(departments.get(
        tx,
        &text(record, "companyScope"),
        &text(record, "departmentId")
    )?);
    employee["canViewDetails"] = json!(auth::visible(
        actor,
        "office.people",
        "view-details",
        record
    ));
    Ok(employee)
}

pub fn detail(tx: &Connection, actor: &Actor, mut record: Value) -> Result<Value> {
    super::personnel_privacy::hydrate(tx, &mut record)?;
    record["organization"] = super::personnel_organization::project(tx, &record)?;
    record["account"] = super::personnel_accounts::current_account(tx, &record)?;
    if record.get("attachments").is_none() {
        record["attachments"] = json!([]);
    }
    record["employee"] = employee(tx, actor, &record)?;
    let editable = actor.admin || record["status"] != "Departed";
    let required_actions = super::oa::personnel_changes::required_actions();
    let restriction = office::deletion_restriction(
        tx,
        record["id"]
            .as_i64()
            .ok_or_else(|| unavailable("人员编号损坏。"))?,
    )?;
    let correct = restriction.is_none();
    for (field, action, allowed) in [
        ("canEdit", "edit", editable),
        ("canTransition", "transition", true),
        ("canLinkAccount", "assign", editable && actor.admin),
        ("canDelete", "delete", true),
        ("canCorrectRegistration", "edit", editable && correct),
    ] {
        record[field] = json!(allowed && auth::visible(actor, "office.people", action, &record));
    }
    record["deleteRestriction"] = json!(restriction.unwrap_or_default());
    record["approvalRequiredActions"] = required_actions;
    Ok(record)
}

pub fn read(store: &Store, actor: &Actor, id: i64) -> Result<Value> {
    store.read(|tx| {
        let record = super::personnel::person(tx, actor, id, "view-details")?;
        detail(tx, actor, record)
    })
}

pub fn options(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    auth::authorize(actor, "office.people", "view")?;
    store.read(|tx| {
    let mut company = if let Some((_, id)) = query.iter().find(|(key, _)| *key == "employeeId") {
        let id = id.parse::<i64>().ok().filter(|id| *id > 0)
            .ok_or_else(|| invalid("人员编号无效。"))?;
        text(&super::personnel::person(tx, actor, id, "view-details")?, "companyScope")
    } else {
        actor.company.clone()
    };
    if let Some((_, target)) = query.iter().find(|(key, _)| *key == "companyCode") {
        if !actor.admin && target != &actor.company { return Err(super::error::error(403,"请选择当前单位的人事选项。")); }
        company = target.clone();
    }
    let companies = if actor.admin { store::all(tx, "companies")? } else { store::matching(tx,"companies",&[("code",company.clone())],&[])? };
    let companies: Vec<_> = companies.into_iter().filter(|c| c["unitKind"] != "Group" && c["isActive"] == true).map(|c| contracts::dto(contracts::schema("ApiOrganizationCompanyDto"), c)).collect();
    let departments: Vec<_> = store::all(tx, "departments")?
        .into_iter()
        .filter(|department| department["companyCode"] == company)
        .map(|department| {
            contracts::project(contracts::schema("PersonnelDepartmentRecord"), department)
        })
        .collect();
    let actions = super::oa::personnel_changes::required_actions();
    Ok(
        json!({"companies":companies,"departments":departments,"positions":tx.find_identity("companies", &store::normalize(&company))?.and_then(|c| c.get("positions").cloned()).unwrap_or(json!([])),"canCreate":auth::authorize(actor,"office.people","create").is_ok(),"approvalRequiredActions":actions}),
    )
    })
}

const DIRECTORY_FIELDS: &[(&str, &str)] = &[
    ("jobTitle", "jobTitle"),
    ("hireDate", "hireDate"),
    ("organization", "organization"),
    ("employee", "employee"),
    ("companyScope", "companyScope"),
    ("departmentId", "departmentId"),
    ("ownerUserId", "ownerUserId"),
    ("status", "status"),
];
const ATTENTION_FIELDS: &[(&str, &str)] = &[
    ("privateProfileAvailable", "privateProfileAvailable"),
    ("probationEndsOn", "probationEndsOn"),
    ("contractEndsOn", "contractEndsOn"),
    ("identityLongTerm", "profile.identityLongTerm"),
    ("identityValidUntil", "profile.identityValidUntil"),
];

pub fn directory(
    store: &Store,
    actor: &Actor,
    clock: &BusinessClock,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, "office.people", "view")?;
    let argument = |name| {
        query
            .iter()
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let status = argument("status");
    if !["", "Probation", "Active", "Departed"].contains(&status) {
        return Err(invalid("任职状态无效。"));
    }
    let attention = match argument("attentionOnly") {
        "" | "false" => false,
        "true" => true,
        _ => return Err(invalid("到期筛选须为 true 或 false。")),
    };
    let date = clock.now().map_err(unavailable)?.today.to_string();
    let mut public_scope = auth::data_scope(actor, "office.people", "view");
    public_scope.personnel_date = Some(&date);
    let mut scopes = vec![public_scope];
    if attention || status == "Departed" {
        auth::authorize(actor, "office.people", "view-details")?;
        scopes.push(auth::data_scope(actor, "office.people", "view-details"));
    }
    let keyword = super::organization::search_key(argument("keyword"));
    if keyword.chars().count() > 100 {
        return Err(invalid("搜索词不能超过 100 字。"));
    }
    let through = if attention {
        Some(
            clock
                .now()
                .map_err(unavailable)?
                .today
                .checked_add_signed(Duration::days(30))
                .ok_or_else(|| unavailable("业务日期超出范围。"))?,
        )
    } else {
        None
    };
    let (page, size, start) = store::page_parameters(query);
    let mut fields = DIRECTORY_FIELDS.to_vec();
    if attention {
        fields.extend_from_slice(ATTENTION_FIELDS);
    }
    store.read(|tx| {
        let mut listing = PersonnelQuery {
            as_of: &date,
            company: &actor.company,
            department: argument("departmentId"),
            status,
            keyword: &keyword,
            search_fields: &[
                "employee.fullName",
                "employee.employeeNumber",
                "employee.jobTitle",
                "employee.workEmail",
                "employee.workPhone",
                "employee.workLocation",
            ],
            scopes: &scopes,
            fields: &fields,
            offset: if attention { 0 } else { start },
            limit: if attention { 200 } else { size as i64 },
        };
        let mut items = Vec::new();
        let mut count = 0;
        let mut departments = super::organization::DepartmentNames::default();
        loop {
            crate::operation::check()?;
            let (total, rows) = tx.query_personnel(&listing)?;
            listing.offset += rows.len() as i64;
            let empty = rows.is_empty();
            for record in rows {
                // Validate dates even outside the requested page; malformed private
                // metadata must not become a successful, misleading attention count.
                if let Some(through) = through {
                    if record["privateProfileAvailable"] != true {
                        return Err(unavailable("人员私密档案缺失，无法核对到期事项。"));
                    }
                    if !needs_attention(&record, through)? {
                        continue;
                    }
                }
                if !attention || (count >= start && items.len() < size) {
                    let mut entry = directory_entry(tx, actor, &record, &mut departments)?;
                    if record["status"] != "Departed" {
                        let selected = super::personnel_organization::departments_in(
                            tx,
                            &record,
                            &actor.company,
                        )?;
                        let department = if argument("departmentId").is_empty() {
                            selected
                                .first()
                                .map(String::as_str)
                                .ok_or_else(|| unavailable("人员目录任职投影失效。"))?
                        } else {
                            argument("departmentId")
                        };
                        let assignment = super::personnel_organization::appointment_in(
                            tx,
                            &record,
                            &actor.company,
                            department,
                        )?;
                        entry["departmentId"] = json!(department);
                        entry["departmentName"] =
                            json!(departments.get(tx, &actor.company, department)?);
                        entry["jobTitle"] = json!(assignment.job_title);
                    }
                    items.push(entry);
                }
                count += 1;
            }
            if !attention {
                count = total;
                break;
            }
            if empty || listing.offset >= total {
                break;
            }
        }
        Ok(contracts::page(items, count as usize, page, size))
    })
}

fn needs_attention(record: &Value, through: NaiveDate) -> Result<bool> {
    if record["status"] == "Departed" {
        return Ok(false);
    }
    for (enabled, field) in [
        (record["status"] == "Probation", "probationEndsOn"),
        (true, "contractEndsOn"),
        (record["identityLongTerm"] != true, "identityValidUntil"),
    ] {
        let value = text(record, field);
        if !enabled || value.is_empty() {
            continue;
        }
        let date = NaiveDate::parse_from_str(&value, "%Y-%m-%d")
            .map_err(|_| unavailable("人员到期日期无效。"))?;
        if date <= through {
            return Ok(true);
        }
    }
    Ok(false)
}

pub fn history(store: &Store, actor: &Actor, id: i64, query: &[(&str, String)]) -> Result<Value> {
    let (page, size, offset) = store::page_parameters(query);
    store.read(|tx| {
        super::personnel::person(tx, actor, id, "view-details")?;
        let (total, events) = tx.query_generic(&GenericQuery {
            kind: "personnel-events",
            scope: DataScope {
                all: true,
                ..Default::default()
            },
            filters: &[("employeeId", id.to_string())],
            keyword: "",
            search_root: None,
            fields: &[],
            offset,
            limit: size as i64,
        })?;
        Ok(contracts::page(events, total as usize, page, size))
    })
}
