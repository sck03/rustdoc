use super::{
    error::{Result, conflict, invalid},
    records::{required, text},
    store::{self, Actor, Store},
};
pub use export_doc_domain::organization::search_key;
use export_doc_storage::{Connection, DataScope, GenericQuery, PersonnelQuery};
use serde_json::{Value, json};
use std::collections::{HashMap, hash_map::Entry};

pub fn validate_assignment(connection: &Connection, company: &str, department: &str) -> Result<()> {
    if company.is_empty() && department.is_empty() {
        return Ok(());
    }
    if company.is_empty() {
        return Err(invalid("选择部门前必须先选择所属公司。"));
    }
    if !connection
        .find_identity("companies", &store::normalize(company))?
        .is_some_and(|item| item["code"] == company && item["isActive"] == true)
    {
        return Err(invalid("选择的公司不存在或已停用。"));
    }
    if !department.is_empty()
        && !connection
            .find_identity("departments", &store::normalize(department))?
            .is_some_and(|item| {
                item["code"] == department
                    && item["companyCode"] == company
                    && item["isActive"] == true
            })
    {
        return Err(invalid("选择的部门不存在、已停用或不属于所选公司。"));
    }
    Ok(())
}

pub fn departments(store: &Store) -> Result<Vec<Value>> {
    let mut rows = store.catalog("departments")?;
    enrich_departments(store, &mut rows)?;
    Ok(rows)
}
pub(super) fn project(tx: &Connection, row: Value) -> Result<Value> {
    let mut department = crate::contracts::overlay(
        crate::contracts::initial(crate::contracts::schema("ApiOrganizationDepartmentDto")),
        &row,
    );
    department["managerName"] = json!("");
    department["managerEmployeeNumber"] = json!("");
    if let Some(id) = row["managerEmployeeId"].as_i64() {
        let person = tx
            .get("people", id)?
            .filter(|person| person["companyScope"] == row["companyCode"])
            .ok_or_else(|| super::error::unavailable("部门负责人引用无效。"))?;
        department["managerName"] = person["profile"]["fullName"].clone();
        department["managerEmployeeNumber"] = person["employeeNumber"].clone();
    }
    Ok(department)
}
pub(super) fn enrich_departments(store: &Store, rows: &mut [Value]) -> Result<()> {
    let tx = store.connection()?;
    for row in rows {
        *row = project(&tx, std::mem::take(row))?;
    }
    Ok(())
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
    let keyword = search_key(keyword);
    let (page, size, offset) = store::page_parameters(query);
    store.read(|tx| {
        let (total, mut people) = tx.query_personnel(&PersonnelQuery {
            company,
            department: "",
            status: "",
            keyword: &keyword,
            search_fields: &["employeeNumber", "profile.fullName"],
            scopes: &[],
            fields: &[
                ("id", "id"),
                ("fullName", "profile.fullName"),
                ("employeeNumber", "employeeNumber"),
                ("departmentId", "departmentId"),
            ],
            offset,
            limit: size as i64,
        })?;
        let mut departments = DepartmentNames::default();
        for person in &mut people {
            person["departmentName"] =
                json!(departments.get(tx, company, &text(person, "departmentId"))?);
        }
        Ok(crate::contracts::page(people, total as usize, page, size))
    })
}

#[derive(Default)]
pub(super) struct DepartmentNames(HashMap<(String, String), String>);
impl DepartmentNames {
    pub fn get(&mut self, tx: &Connection, company: &str, code: &str) -> Result<String> {
        match self.0.entry((company.to_owned(), code.to_owned())) {
            Entry::Occupied(entry) => Ok(entry.get().clone()),
            Entry::Vacant(entry) => {
                let name = tx
                    .find_identity("departments", &store::normalize(code))?
                    .filter(|department| department["companyCode"] == company)
                    .map(|department| text(&department, "name"))
                    .unwrap_or_else(|| code.to_owned());
                Ok(entry.insert(name).clone())
            }
        }
    }
}

fn exists(tx: &Connection, kind: &str, filters: &[(&str, String)]) -> Result<bool> {
    Ok(tx
        .query_generic(&GenericQuery {
            kind,
            scope: DataScope {
                all: true,
                ..Default::default()
            },
            filters,
            keyword: "",
            search_root: None,
            fields: &[("id", "id")],
            offset: 0,
            limit: 1,
        })?
        .0
        > 0)
}
fn has_active_members(tx: &Connection, company: &str, department: &str) -> Result<bool> {
    let mut filters = vec![
        ("companyScope", company.to_owned()),
        ("isActive", "true".into()),
    ];
    if !department.is_empty() {
        filters.push(("departmentId", department.to_owned()));
    }
    if exists(tx, "users", &filters)? {
        return Ok(true);
    }
    Ok(tx
        .query_personnel(&PersonnelQuery {
            company,
            department,
            status: "",
            keyword: "",
            search_fields: &[],
            scopes: &[],
            fields: &[("id", "id")],
            offset: 0,
            limit: 1,
        })?
        .0
        > 0)
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
            if exists(
                connection,
                "departments",
                &[("companyCode", code.clone()), ("isActive", "true".into())],
            )? || has_active_members(connection, &code, "")?
            {
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
    if !connection
        .find_identity("companies", &store::normalize(&company))?
        .is_some_and(|item| item["code"] == company && item["isActive"] == true)
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
    if !value["managerEmployeeId"].is_null() {
        let manager = super::records::positive(value, "managerEmployeeId", "部门负责人")?;
        let person = store::get(connection, "people", manager)?;
        if person["companyScope"] != company || person["status"] == "Departed" {
            return Err(invalid("负责人必须是本公司在职人员。"));
        }
    }
    if value["isActive"] == false
        && (!value["managerEmployeeId"].is_null()
            || has_active_members(connection, &company, &code)?)
    {
        return Err(conflict(
            "部门仍有负责人、启用账号或在职人员，请先交接、调岗后再停用部门。",
        ));
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
