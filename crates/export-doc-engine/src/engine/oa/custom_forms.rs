//! Custom applications reuse general-request persistence, authorization and runtime.
use super::*;
use export_doc_domain::oa_form::{FormDefinition, FormValues};

pub(super) fn references(
    tx: &Connection,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    use export_doc_storage::{DataScope, GenericQuery, PersonnelQuery};
    let get = |key| crate::engine::request_values::find(query, key).unwrap_or("");
    let keyword = get("keyword");
    if keyword.chars().count() > 100 {
        return Err(invalid("搜索词不能超过 100 字。"));
    }
    let (page, size) = paging(query)?;
    let (total, mut items) = match get("kind") {
        "Person" => tx.query_personnel(&PersonnelQuery {
            as_of: &super::super::units::today(tx)?.to_string(),
            company: &actor.company,
            department: "",
            status: "",
            keyword,
            search_fields: &["employeeNumber", "profile.fullName"],
            scopes: &[],
            fields: &[("id", "id"), ("name", "profile.fullName")],
            offset: (page - 1) * size,
            limit: size,
        })?,
        "Department" => tx.query_generic(&GenericQuery {
            kind: "departments",
            scope: DataScope {
                company_wide: true,
                company: &actor.company,
                ..Default::default()
            },
            filters: &[("isActive", "true".into())],
            keyword,
            search_root: Some("name"),
            fields: &[("id", "code"), ("name", "name")],
            offset: (page - 1) * size,
            limit: size,
        })?,
        _ => return Err(invalid("选项类型无效。")),
    };
    for item in &mut items {
        if let Some(id) = item["id"].as_i64() {
            item["id"] = json!(id.to_string());
        }
    }
    Ok(json!({"items":items,"totalCount":total,"pageNumber":page,"pageSize":size}))
}

pub(super) fn values(row: &Value) -> Result<FormValues> {
    serde_json::from_value(row.get("formValues").cloned().unwrap_or(json!({})))
        .map_err(|_| invalid("表单数据格式无效。"))
}
pub(super) fn audience(form: &FormDefinition, department: &str) -> bool {
    form.department_ids.is_empty() || form.department_ids.iter().any(|id| id == department)
}
pub(super) fn catalog(tx: &Connection, actor: &Actor) -> Result<Value> {
    let settings = settings_store::registry(tx, &actor.company)?;
    let mut items = vec![];
    for publication in settings["publications"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|p| p["enabled"] == true)
    {
        let key = text(publication, "key");
        let Some(variant) = key.strip_prefix("general:") else {
            continue;
        };
        let (rule, version) = definitions::published(tx, &actor.company, "general", variant)?;
        if let Some(form) = rule.form {
            if form.department_ids.is_empty()
                || actor.admin
                || actor.departments.iter().any(|d| audience(&form, d))
            {
                items.push(json!({"key":key,"name":rule.name,"version":version,"form":form}));
            }
        }
    }
    Ok(json!({"items":items}))
}
pub(super) fn validate_definition(
    tx: &Connection,
    company: &str,
    form: &FormDefinition,
) -> Result<()> {
    for department in &form.department_ids {
        let identity = store::department_identity(company, department)?;
        if !tx
            .find_identity("departments", &identity)?
            .is_some_and(|d| d["isActive"] == true)
        {
            return Err(invalid("适用部门必须是本单位的启用部门。"));
        }
    }
    Ok(())
}
pub(super) fn bind(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    let key = text(row, "definitionKey");
    if key.is_empty() {
        if !values(row)?.is_empty() {
            return Err(invalid("表单数据缺少事项定义。"));
        }
        return Ok(());
    }
    let variant = key
        .strip_prefix("general:")
        .filter(|v| export_doc_domain::oa_form::field_key(v))
        .ok_or_else(|| invalid("自定义事项编号无效。"))?;
    let (rule, version) = definitions::published(tx, &actor.company, "general", variant)?;
    if row["definitionVersion"].as_i64() != Some(version) {
        return Err(conflict("事项版本已更新，请重新打开申请表单。"));
    }
    let form = rule.form.ok_or_else(|| invalid("事项未定义自定义表单。"))?;
    row["formDefinition"] = serde_json::to_value(form)?;
    row["applicationName"] = json!(rule.name);
    validate(tx, actor, row, false)
}
pub(super) fn validate(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    submitting: bool,
) -> Result<()> {
    if text(row, "definitionKey").is_empty() {
        return Ok(());
    }
    let form: FormDefinition = serde_json::from_value(row["formDefinition"].clone())
        .map_err(|_| super::super::error::unavailable("申请表单版本缺失或损坏。"))?;
    if !audience(&form, &text(row, "departmentId")) {
        return Err(error(403, "当前申请部门不在此事项的适用范围内。"));
    }
    let values = values(row)?;
    form.validate_values(&values, submitting).map_err(invalid)?;
    row["formDisplayValues"] =
        serde_json::to_value(display_values(tx, actor, &form.fields, &values)?)?;
    Ok(())
}
fn display_values(
    tx: &Connection,
    actor: &Actor,
    fields: &[export_doc_domain::oa_form::FormField],
    values: &FormValues,
) -> Result<FormValues> {
    use export_doc_domain::oa_form::FieldKind;
    let mut display = values.clone();
    for field in fields {
        let Some(value) = values.get(&field.key) else {
            continue;
        };
        match field.kind {
            FieldKind::Person if value.as_str().is_some_and(|v| !v.is_empty()) => {
                let id = value
                    .as_str()
                    .and_then(|v| v.parse().ok())
                    .ok_or_else(|| invalid("人员编号无效。"))?;
                let person =
                    super::super::personnel_organization::active_person(tx, &actor.company, id)?;
                display.insert(
                    field.key.clone(),
                    json!(format!("{}（{id}）", text(&person["profile"], "fullName"))),
                );
            }
            FieldKind::Department if value.as_str().is_some_and(|v| !v.is_empty()) => {
                let identity =
                    store::department_identity(&actor.company, value.as_str().unwrap_or(""))?;
                let department = tx
                    .find_identity("departments", &identity)?
                    .filter(|d| d["isActive"] == true)
                    .ok_or_else(|| invalid("请选择本单位的启用部门。"))?;
                display.insert(
                    field.key.clone(),
                    json!(format!(
                        "{}（{}）",
                        text(&department, "name"),
                        value.as_str().unwrap_or("")
                    )),
                );
            }
            FieldKind::Table => {
                let mut rows = vec![];
                for row in value.as_array().into_iter().flatten() {
                    let values = serde_json::from_value(row.clone())
                        .map_err(|_| invalid("明细格式无效。"))?;
                    rows.push(display_values(tx, actor, &field.columns, &values)?);
                }
                display.insert(field.key.clone(), json!(rows));
            }
            _ => {}
        }
    }
    Ok(display)
}
