//! Unit administrators maintain public appointments without access to private HR files.
use super::super::personnel_organization as organization;
use super::*;
use export_doc_storage::{DataScope, GenericQuery};

fn project(
    person: &Value,
    unit: &str,
    references: &[Value],
    today: chrono::NaiveDate,
) -> Result<Value> {
    let assignments = organization::assignments(person)?
        .into_iter()
        .enumerate()
        .filter(|(_, a)| a.company_code == unit)
        .map(|(i, a)| {
            let mut value = serde_json::to_value(&a)?;
            value["isPrimary"] = json!(i == 0);
            value["isEffective"] = json!(
                matches!(person["status"].as_str(), Some("Active" | "Probation"))
                    && a.active_on(today).map_err(unavailable)?
            );
            let reference = references
                .iter()
                .find(|r| r["id"] == a.id)
                .ok_or_else(|| unavailable("任职引用投影缺失。"))?;
            let reason = if i == 0 {
                "主任职须通过离职或主任职调整办理".into()
            } else {
                organization::appointment_removal_reason(reference)?
            };
            value["canRemove"] = json!(reason.is_empty());
            value["removalReason"] = json!(reason);
            Ok(value)
        })
        .collect::<Result<Vec<Value>>>()?;
    let account = person["account"]["id"]
        .as_i64()
        .or_else(|| person["accountId"].as_i64());
    Ok(
        json!({"personId":person["id"],"employeeNumber":person["employeeNumber"],"fullName":person.get("fullName").unwrap_or(&person["profile"]["fullName"]),"status":person["status"],"primaryCompanyCode":person["companyScope"],"accountId":account,"versionNumber":person["versionNumber"],"appointments":assignments}),
    )
}

pub(in crate::engine) fn members(
    store: &store::Store,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx,actor)?;
        let unit = query.iter().find(|(k,_)| *k == "companyCode").map(|(_,v)| v.as_str()).unwrap_or(&actor.company);
        demand(&actor,unit,"organization.appointments")?;
        let unit_row = company(tx,unit)?;
        let argument = |key| query.iter().find(|(k,_)| *k == key).map(|(_,v)| v.as_str()).unwrap_or("");
        let available = argument("includeAvailable") == "true";
        if available && !actor.admin { return Err(error(403,"跨单位人员分配由系统管理员办理。")); }
        let number = argument("pageNumber").parse::<i64>().unwrap_or(1).max(1);
        let size = argument("pageSize").parse::<i64>().unwrap_or(30).clamp(1,100);
        let (total,rows) = tx.query_generic(&GenericQuery {
            kind: "people", scope: DataScope { all: available, company_wide: true, company: unit, personnel_memberships: true, ..Default::default() },
            filters: &[], keyword: argument("keyword"), search_root: Some("employee"),
            fields: &[("id","id"),("employeeNumber","employeeNumber"),("fullName","profile.fullName"),("status","status"),("companyScope","companyScope"),("departmentId","departmentId"),("jobTitle","jobTitle"),("hireDate","hireDate"),("organization","organization"),("accountId","account.id"),("versionNumber","versionNumber")], offset: (number-1)*size, limit:size,
        })?;
        let ids = rows.iter().filter_map(|p| p["id"].as_i64()).collect::<Vec<_>>();
        let references = tx.appointment_references(&ids)?;
        let date = today(tx)?;
        let items = rows.iter().map(|p| {
            let usage = references.iter().find(|r| r["id"] == p["id"]).and_then(|r| r["appointments"].as_array()).ok_or_else(|| unavailable("任职引用投影缺失。"))?;
            project(p,unit,usage,date)
        }).collect::<Result<Vec<_>>>()?;
        let departments = store::matching(tx,"departments",&[("companyCode",unit.into())],&[])?.into_iter().map(|d| crate::contracts::dto(crate::contracts::schema("PersonnelDepartmentRecord"),d)).collect::<Vec<_>>();
        Ok(json!({"unitId":unit_row["id"].to_string(),"items":items,"totalCount":total,"pageNumber":number,"pageSize":size,"departments":departments}))
    })
}
