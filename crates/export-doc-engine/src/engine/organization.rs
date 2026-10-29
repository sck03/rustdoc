use super::{
    error::{Result, conflict, invalid},
    records::{required, text},
    store::{self, Actor, Store},
};
use crate::engine::store::Connection;
pub use export_doc_domain::organization::search_key;
use export_doc_storage::{OrganizationReference, PersonnelQuery};
use serde_json::{Value, json};
use std::collections::{HashMap, hash_map::Entry};

pub(super) fn leaders(
    value: &Value,
) -> Result<Vec<export_doc_domain::organization::OrganizationLeader>> {
    serde_json::from_value(value.get("additionalLeaders").cloned().unwrap_or(json!([])))
        .map_err(|_| super::error::unavailable("组织分管负责人资料损坏。"))
}
pub(super) fn leads(value: &Value, employee: i64, primary: &str) -> Result<bool> {
    Ok(value[primary] == employee || leaders(value)?.iter().any(|l| l.employee_id == employee))
}
fn validate_leaders(
    tx: &Connection,
    actor: &Actor,
    value: &mut Value,
    company: &str,
    primary: &str,
) -> Result<()> {
    let mut leaders: Vec<export_doc_domain::organization::OrganizationLeader> =
        serde_json::from_value(value.get("additionalLeaders").cloned().unwrap_or(json!([])))
            .map_err(|_| invalid("分管负责人格式无效。"))?;
    export_doc_domain::organization::validate_leaders(&leaders, value[primary].as_i64())
        .map_err(invalid)?;
    let reference_key = if primary == "executiveEmployeeId" {
        "executiveAppointmentId"
    } else {
        "managerAppointmentId"
    };
    value[reference_key] = json!(
        value[primary]
            .as_i64()
            .map(|id| super::personnel_reporting::reference(
                tx,
                company,
                id,
                value[reference_key].as_str()
            ))
            .transpose()?
    );
    for leader in &mut leaders {
        let person = super::personnel_organization::active_person(tx, company, leader.employee_id)?;
        leader.appointment_id = super::personnel_reporting::reference(
            tx,
            company,
            leader.employee_id,
            Some(&leader.appointment_id),
        )?;
        if primary == "executiveEmployeeId" {
            let user =
                super::records::positive(&person["account"], "id", "公司分管负责人关联账号")?;
            let mut scope = actor.clone();
            scope.company = company.into();
            for kind in &leader.workflow_kinds {
                super::oa::approval::reviewer(tx, &scope, user, &json!({"kind":kind}))?;
            }
        }
    }
    if value["isActive"] == false && !leaders.is_empty() {
        return Err(conflict("停用组织前须移交分管负责人。"));
    }
    value["additionalLeaders"] = serde_json::to_value(leaders)?;
    Ok(())
}

pub fn check_references(connection: &Connection, kind: &str, record: &Value) -> Result<()> {
    let code = &record["code"];
    if (kind == "companies" && !record["executiveEmployeeId"].is_null())
        || (kind == "departments" && !record["managerEmployeeId"].is_null())
        || !leaders(record)?.is_empty()
    {
        return Err(conflict("组织仍有主负责人或分管负责人，不能删除。"));
    }
    let reference = if kind == "companies" {
        OrganizationReference::Company
    } else {
        OrganizationReference::Department
    };
    let mut kinds: Vec<_> = super::catalog::RESOURCES
        .iter()
        .map(|resource| resource.key)
        .collect();
    kinds.push(super::units::KIND);
    let code = code
        .as_str()
        .filter(|code| !code.is_empty())
        .ok_or_else(|| super::error::unavailable("组织目录代码损坏。"))?;
    if connection.organization_referenced(
        reference,
        code,
        &text(record, "companyCode"),
        record["id"].as_i64().unwrap_or(0),
        &kinds,
    )? {
        return Err(conflict("目录存在下级、账号或业务引用，不能删除。"));
    }
    Ok(())
}

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
            .find_identity(
                "departments",
                &store::department_identity(company, department)?,
            )?
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
pub fn directory(store: &Store, actor: &Actor) -> Result<Value> {
    store.read(|tx| {
        let actor = super::auth::refresh_actor_in(tx, actor)?;
        super::auth::authorize(&actor, "organization.directory", "manage")?;
        let mut companies = super::units::companies(tx, &actor)?;
        for company in &mut companies {
            company["executiveName"] = match company["executiveEmployeeId"].as_i64() {
                Some(id) => json!(text(&store::get(tx, "people", id)?, "profile.fullName")),
                None => json!(""),
            };
            company["additionalLeaderNames"] = leader_names(tx, company)?;
        }
        let mut departments = if actor.admin {
            store::all(tx, "departments")?
        } else {
            store::matching(
                tx,
                "departments",
                &[("companyCode", actor.company.clone())],
                &[],
            )?
        };
        for department in &mut departments {
            *department = project(tx, std::mem::take(department))?;
        }
        Ok(json!({"companies":companies,"departments":departments}))
    })
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
            .ok_or_else(|| super::error::unavailable("部门负责人引用无效。"))?;
        department["managerName"] = person["profile"]["fullName"].clone();
        department["managerEmployeeNumber"] = person["employeeNumber"].clone();
    }
    department["additionalLeaderNames"] = leader_names(tx, &row)?;
    Ok(department)
}

pub(super) fn leader_names(tx: &Connection, row: &Value) -> Result<Value> {
    let mut names = serde_json::Map::new();
    for leader in leaders(row)? {
        let person = store::get(tx, "people", leader.employee_id)?;
        names.insert(
            leader.employee_id.to_string(),
            json!(format!(
                "{} · {}",
                text(&person, "profile.fullName"),
                text(&person, "employeeNumber")
            )),
        );
    }
    Ok(Value::Object(names))
}

pub(super) fn leadership_referenced(tx: &Connection, company: &str, employee: i64) -> Result<bool> {
    if let Some(row) = tx.find_identity("companies", &store::normalize(company))? {
        if leads(&row, employee, "executiveEmployeeId")? {
            return Ok(true);
        }
    }
    for row in store::matching(
        tx,
        "departments",
        &[("companyCode", company.into())],
        &[
            ("managerEmployeeId", "managerEmployeeId"),
            ("additionalLeaders", "additionalLeaders"),
        ],
    )? {
        if leads(&row, employee, "managerEmployeeId")? {
            return Ok(true);
        }
    }
    Ok(false)
}
/// An organizational duty keeps the exact appointment chosen when it was assigned.
pub(super) fn validate_personnel_duties(tx: &Connection, proposed: &Value) -> Result<()> {
    let employee = proposed["id"].as_i64().unwrap_or(0);
    if employee == 0 {
        return Ok(());
    }
    for (kind, primary, appointment) in [
        ("companies", "executiveEmployeeId", "executiveAppointmentId"),
        ("departments", "managerEmployeeId", "managerAppointmentId"),
    ] {
        for row in store::matching(
            tx,
            kind,
            &[],
            &[
                ("code", "code"),
                ("companyCode", "companyCode"),
                (primary, primary),
                (appointment, appointment),
                ("additionalLeaders", "additionalLeaders"),
            ],
        )? {
            let company = text(
                &row,
                if kind == "companies" {
                    "code"
                } else {
                    "companyCode"
                },
            );
            let mut references = Vec::new();
            if row[primary] == employee {
                references.push(text(&row, appointment));
            }
            references.extend(
                leaders(&row)?
                    .into_iter()
                    .filter(|l| l.employee_id == employee)
                    .map(|l| l.appointment_id),
            );
            for reference in references {
                super::personnel_organization::appointment_by_id(
                    tx, proposed, &company, &reference,
                )
                .map_err(|cause| {
                    if cause.status == Some(400) {
                        conflict(format!(
                            "组织负责人任职仍被引用，请先交接职责：{}",
                            cause.message
                        ))
                    } else {
                        cause
                    }
                })?;
            }
        }
    }
    Ok(())
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
            as_of: &super::units::today(tx)?.to_string(),
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
                ("companyScope", "companyScope"),
                ("status", "status"),
                ("hireDate", "hireDate"),
                ("jobTitle", "jobTitle"),
                ("organization", "organization"),
            ],
            offset,
            limit: size as i64,
        })?;
        let mut departments = DepartmentNames::default();
        for person in &mut people {
            let mut options = vec![];
            for a in super::personnel_organization::assignments(person)? {
                if a.company_code == company && a.active_on(super::units::today(tx)?).map_err(invalid)? {
                    options.push(json!({"id":a.id,"departmentId":a.department_id,"departmentName":departments.get(tx,company,&a.department_id)?,"jobTitle":a.job_title}));
                }
            }
            person["appointments"] = json!(options);
            let assignments = super::personnel_organization::departments_in(tx, person, company)?;
            person["departmentId"] = json!(assignments.first().ok_or_else(|| super::error::unavailable("负责人任职投影失效。"))?);
            person["departmentName"] =
                json!(departments.get(tx, company, &text(person, "departmentId"))?);
            for field in ["companyScope", "status", "hireDate", "jobTitle", "organization"] { person.as_object_mut().unwrap().remove(field); }
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
                    .find_identity("departments", &store::department_identity(company, code)?)?
                    .filter(|department| department["companyCode"] == company)
                    .map(|department| text(&department, "name"))
                    .unwrap_or_else(|| code.to_owned());
                Ok(entry.insert(name).clone())
            }
        }
    }
}

fn exists(tx: &Connection, kind: &str, filters: &[(&str, String)]) -> Result<bool> {
    Ok(tx.record_exists(kind, filters)?)
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
    Ok(tx.personnel_exists(&PersonnelQuery {
        as_of: &super::units::today(tx)?.to_string(),
        company,
        department,
        status: "",
        keyword: "",
        search_fields: &[],
        scopes: &[],
        fields: &[("id", "id")],
        offset: 0,
        limit: 1,
    })?)
}

pub fn validate(
    connection: &Connection,
    actor: &Actor,
    kind: &str,
    id: i64,
    previous: &Value,
    value: &mut Value,
) -> Result<()> {
    required(value, "code", "目录代码", 50)?;
    required(value, "name", "名称", 120)?;
    let code = text(value, "code");
    if id > 0 && previous["code"] != code {
        return Err(invalid("已有目录代码不能修改。"));
    }
    if kind == "companies" {
        super::units::validate_company(connection, actor, id, previous, value)?;
        value["companyScope"] = json!(code);
        validate_leaders(connection, actor, value, &code, "executiveEmployeeId")?;
        if let Some(positions) = value.get("positions") {
            let positions = positions
                .as_array()
                .filter(|p| p.len() <= 200)
                .ok_or_else(|| invalid("公司职位目录最多二百项。"))?;
            let mut seen = std::collections::HashSet::new();
            for position in positions {
                required(position, "name", "职位名称", 120)?;
                if !position["isActive"].is_boolean()
                    || !seen.insert(search_key(&text(position, "name")))
                {
                    return Err(invalid("职位名称不能重复，启用状态须有效。"));
                }
            }
        }
        if let Some(id) = value["executiveEmployeeId"].as_i64() {
            let person = super::personnel_organization::active_person(connection, &code, id)?;
            let user =
                super::records::positive(&person["account"], "id", "公司领导审批负责人关联账号")?;
            let reviewer = super::units::approval_actor(connection, user, actor.edition, &code)?;
            if !super::auth::is_company_approver(&reviewer) {
                return Err(invalid(
                    "公司领导审批负责人须关联本公司启用账号并具备审批能力；实际业务须有对应查看和审批权限。",
                ));
            }
        }
        if value["isActive"] == false {
            if !value["executiveEmployeeId"].is_null() {
                return Err(conflict("停用公司前须移交或清除公司领导审批负责人。"));
            }
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
    super::units::demand(actor, &company, "organization.directory")?;
    value["companyScope"] = json!(company);
    validate_leaders(connection, actor, value, &company, "managerEmployeeId")?;
    if id > 0 && previous["companyCode"] != company {
        return Err(invalid("已有部门不能修改所属公司。"));
    }
    if !connection
        .find_identity("companies", &store::normalize(&company))?
        .is_some_and(|item| item["code"] == company && item["isActive"] == true)
    {
        return Err(invalid("请选择启用的公司。"));
    }
    let departments = store::matching(
        connection,
        "departments",
        &[("companyCode", company.clone())],
        &[],
    )?;
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
        super::personnel_organization::active_person(connection, &company, manager)?;
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
