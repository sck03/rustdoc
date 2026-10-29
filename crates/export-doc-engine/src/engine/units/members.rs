//! Unit administrators maintain public appointments without access to private HR files.
use super::super::{personnel, personnel_organization as organization, records};
use super::*;
use export_doc_storage::{DataScope, GenericQuery};

fn project(tx: &Connection, person: &Value, unit: &str) -> Result<Value> {
    let assignments = organization::assignments(person)?
        .into_iter()
        .enumerate()
        .filter(|(_, a)| a.company_code == unit)
        .map(|(i, a)| {
            let mut value = serde_json::to_value(a)?;
            value["isPrimary"] = json!(i == 0);
            Ok(value)
        })
        .collect::<Result<Vec<Value>>>()?;
    let account = person["account"]["id"]
        .as_i64()
        .or_else(|| person["accountId"].as_i64());
    let _ = tx;
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
        company(tx,unit)?;
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
        let items = rows.iter().map(|p| project(tx,p,unit)).collect::<Result<Vec<_>>>()?;
        let departments = store::matching(tx,"departments",&[("companyCode",unit.into())],&[])?.into_iter().map(|d| crate::contracts::dto(crate::contracts::schema("PersonnelDepartmentRecord"),d)).collect::<Vec<_>>();
        Ok(json!({"items":items,"totalCount":total,"pageNumber":number,"pageSize":size,"departments":departments}))
    })
}

pub(in crate::engine) fn save_member(
    store: &store::Store,
    actor: &Actor,
    input: Value,
) -> Result<Value> {
    store.transaction_as(actor, |tx, actor| {
        let body = crate::contracts::dto(crate::contracts::schema("UnitMemberSave"), input);
        let unit = text(&body, "companyCode");
        demand(actor, &unit, "organization.appointments")?;
        if company(tx, &unit)?["unitKind"] == "Group" {
            return Err(invalid("集团目录不能建立业务任职。"));
        }
        records::required(&body, "note", "任职变更说明", 500)?;
        let id = records::positive(&body, "personId", "人员")?;
        let mut person = store::get(tx, "people", id)?;
        store::check_version(&person, store::expected(&body))?;
        if !matches!(person["status"].as_str(), Some("Active" | "Probation")) {
            return Err(invalid("离职人员须先办理复职。"));
        }
        if !actor.admin
            && !organization::assignments(&person)?
                .iter()
                .any(|a| a.company_code == unit)
        {
            return Err(error(403, "人员不在当前单位的任职管理范围内。"));
        }
        let appointments = body["appointments"]
            .as_array()
            .ok_or_else(|| invalid("任职格式无效。"))?;
        let mut org = organization::organization(&person)?;
        org.additional_assignments
            .retain(|a| a.company_code != unit);
        let primary_here = person["companyScope"] == unit;
        let mut primary_count = 0;
        for value in appointments {
            let assignment: export_doc_domain::personnel_organization::Assignment =
                serde_json::from_value(value.clone()).map_err(|_| invalid("任职格式无效。"))?;
            if assignment.company_code != unit {
                return Err(error(403, "不能改写其他单位的任职。"));
            }
            if value["isPrimary"] == true {
                primary_count += 1;
                if !primary_here {
                    return Err(error(403, "主任职归属调整须由人事办理。"));
                }
                if !assignment.ends_on.is_empty() {
                    return Err(invalid("主任职终止请办理离职或主任职调整。"));
                }
                person["departmentId"] = json!(assignment.department_id);
                person["jobTitle"] = json!(assignment.job_title);
                org.manager_employee_id = assignment.manager_employee_id;
                org.functional_manager_employee_ids = assignment.functional_manager_employee_ids;
                org.manager_assignment_id = assignment.manager_assignment_id;
                org.functional_manager_assignment_ids =
                    assignment.functional_manager_assignment_ids;
            } else {
                org.additional_assignments.push(assignment);
            }
        }
        if primary_count != usize::from(primary_here) {
            return Err(invalid("必须保留本单位唯一的主任职。"));
        }
        let submitted = serde_json::to_value(org)?;
        organization::validate(tx, actor, &mut person, &submitted, "transition")?;
        for key in ["departmentId", "jobTitle"] {
            person["employee"][key] = person[key].clone();
        }
        person["lastEffectiveDate"] = json!(today(tx)?.to_string());
        let saved = personnel::persist(
            tx,
            actor,
            id,
            person,
            "TransferPersonnel",
            &text(&body, "note"),
        )?;
        project(tx, &saved, &unit)
    })
}
