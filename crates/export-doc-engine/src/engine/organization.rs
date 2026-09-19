use super::{
    error::{Result, conflict, invalid},
    records::{required, text},
    store::{self, Actor, Store},
};
pub use export_doc_domain::organization::search_key;
use export_doc_storage::Connection;
use serde_json::Value;

pub fn validate_assignment(connection: &Connection, company: &str, department: &str) -> Result<()> {
    if company.is_empty() && department.is_empty() {
        return Ok(());
    }
    if company.is_empty() {
        return Err(invalid("选择部门前必须先选择所属公司。"));
    }
    if !store::all(connection, "companies")?
        .iter()
        .any(|item| item["code"] == company && item["isActive"] == true)
    {
        return Err(invalid("选择的公司不存在或已停用。"));
    }
    if !department.is_empty()
        && !store::all(connection, "departments")?.iter().any(|item| {
            item["code"] == department && item["companyCode"] == company && item["isActive"] == true
        })
    {
        return Err(invalid("选择的部门不存在、已停用或不属于所选公司。"));
    }
    Ok(())
}

pub fn departments(store: &Store) -> Result<Vec<Value>> {
    store.transaction(|tx| {
        let people = store::all(tx, "people")?;
        store::all(tx, "departments")?
            .into_iter()
            .map(|record| {
                let mut department = crate::contracts::overlay(
                    crate::contracts::initial(crate::contracts::schema(
                        "ApiOrganizationDepartmentDto",
                    )),
                    &record,
                );
                if let Some(id) = record["managerEmployeeId"].as_i64() {
                    let person = people
                        .iter()
                        .find(|person| {
                            person["id"] == id && person["companyScope"] == record["companyCode"]
                        })
                        .ok_or_else(|| super::error::unavailable("部门负责人引用无效。"))?;
                    department["managerName"] = person["profile"]["fullName"].clone();
                }
                Ok(department)
            })
            .collect()
    })
}

pub fn managers(store: &Store, query: &[(&str, String)]) -> Result<Value> {
    let company = query
        .iter()
        .find(|(key, _)| *key == "companyCode")
        .map(|(_, value)| value.trim())
        .filter(|value| !value.is_empty() && value.chars().count() <= 50)
        .ok_or_else(|| invalid("请选择所属公司。"))?;
    let keyword = query
        .iter()
        .find(|(key, _)| *key == "keyword")
        .map(|(_, value)| value.as_str())
        .unwrap_or("");
    if keyword.chars().count() > 100 {
        return Err(invalid("搜索词不能超过 100 字。"));
    }
    let keyword = export_doc_domain::organization::search_key(keyword);
    store.transaction(|tx| {
        let departments = store::all(tx, "departments")?;
        let mut people: Vec<_> = store::all(tx, "people")?.into_iter()
            .filter(|person| person["companyScope"] == company && person["status"] != "Departed")
            .filter(|person| export_doc_domain::organization::search_key(&format!("{} {}", text(person, "employeeNumber"), text(person, "profile.fullName"))).contains(&keyword))
            .map(|person| {
                let department = departments.iter().find(|department| department["code"] == person["departmentId"]);
                serde_json::json!({"id":person["id"],"fullName":person["profile"]["fullName"],"employeeNumber":person["employeeNumber"],"departmentName":department.map(|department| department["name"].clone()).unwrap_or_else(|| person["departmentId"].clone())})
            }).collect();
        people.sort_by(|a,b| store::normalize(&text(a,"employeeNumber")).cmp(&store::normalize(&text(b,"employeeNumber"))).then_with(|| a["id"].as_i64().cmp(&b["id"].as_i64())));
        let paging: Vec<_> = query.iter().filter(|(key, _)| *key != "keyword").cloned().collect();
        Ok(store::paged(people, &paging))
    })
}

pub fn validate(
    connection: &Connection,
    actor: &Actor,
    kind: &str,
    id: i64,
    previous: &Value,
    value: &mut Value,
) -> Result<()> {
    if !actor.admin {
        return Err(super::error::error(403, "只有管理员可以维护组织目录。"));
    }
    required(value, "code", "目录代码", 50)?;
    required(value, "name", "名称", 120)?;
    let code = text(value, "code");
    if id > 0 && previous["code"] != code {
        return Err(invalid("已有目录代码不能修改。"));
    }
    if kind == "companies" {
        if value["isActive"] == false {
            let active_departments = store::all(connection, "departments")?
                .iter()
                .any(|item| item["companyCode"] == code && item["isActive"] == true);
            let active_users = store::all(connection, "users")?
                .iter()
                .any(|item| item["companyScope"] == code && item["isActive"] == true);
            let active_people = store::all(connection, "people")?
                .iter()
                .any(|item| item["companyScope"] == code && item["status"] != "Departed");
            if active_departments || active_users || active_people {
                return Err(conflict(
                    "公司仍有启用部门、账号或在职人员，请先处理后再停用公司。",
                ));
            }
        }
        return Ok(());
    }
    let company = text(value, "companyCode");
    if id > 0 && previous["companyCode"] != company {
        return Err(invalid("已有部门不能修改所属公司。"));
    }
    if !store::all(connection, "companies")?
        .iter()
        .any(|item| item["code"] == company && item["isActive"] == true)
    {
        return Err(invalid("请选择启用的公司。"));
    }
    let departments = store::all(connection, "departments")?;
    let parent = text(value, "parentCode");
    if !parent.is_empty() {
        let ancestor = departments
            .iter()
            .find(|department| department["code"] == parent)
            .ok_or_else(|| invalid("上级部门不存在。"))?;
        if ancestor["companyCode"] != company
            || (value["isActive"] == true && ancestor["isActive"] != true)
        {
            return Err(invalid("上级部门必须属于同一公司且处于启用状态。"));
        }
    }
    if value["isActive"] == false
        && departments
            .iter()
            .any(|item| item["parentCode"] == code && item["isActive"] == true)
    {
        return Err(conflict("存在启用的下级部门，不能停用上级。"));
    }
    if let Some(manager) = value["managerEmployeeId"].as_i64().filter(|id| *id > 0) {
        let person = store::get(connection, "people", manager)?;
        if person["companyScope"] != company || person["status"] == "Departed" {
            return Err(invalid("负责人必须是本公司在职人员。"));
        }
    }
    let mut tree = departments
        .into_iter()
        .filter(|department| department["code"] != code)
        .map(serde_json::from_value::<export_doc_domain::organization::DepartmentLink>)
        .collect::<std::result::Result<Vec<_>, _>>()
        .map_err(|cause| super::error::unavailable(format!("组织目录数据无效：{cause}")))?;
    tree.push(
        serde_json::from_value(value.clone())
            .map_err(|cause| invalid(format!("部门资料无效：{cause}")))?,
    );
    export_doc_domain::organization::ancestry(&tree).map_err(invalid)?;
    Ok(())
}
