//! Organization facts, personnel periods and duties share one current identity.
use super::*;
use export_doc_domain::identity::{self, StableId};
use export_doc_storage::AuditWrite;
pub(super) fn handle(
    store: &store::Store,
    actor: &Actor,
    meta: &Value,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let kind = text(meta, "kind");
    if meta["action"] == "list" {
        return store.read(|tx| {
            let actor = auth::refresh_actor_in(tx, actor)?;
            let unit = match query.iter().find(|(k, _)| *k == "unitId") {
                Some((_, id)) => store::get(
                    tx,
                    "companies",
                    StableId::parse(id).map_err(invalid)?.number(),
                )?,
                None => company(tx, &actor.company)?,
            };
            let code = text(&unit, "code");
            auth::authorize(
                &actor,
                if kind == "employments" {
                    "office.people"
                } else {
                    "organization.directory"
                },
                if kind == "employments" {
                    "view-details"
                } else {
                    "view"
                },
            )?;
            if !actor.admin && actor.company != code {
                return Err(error(403, "人员基础资料超出当前身份单位。"));
            }
            let number = |key, default| -> Result<i64> {
                query
                    .iter()
                    .find(|(k, _)| *k == key)
                    .map(|(_, value)| {
                        value
                            .parse::<i64>()
                            .map_err(|_| invalid("页码或每页数量无效。"))
                    })
                    .transpose()
                    .map(|v| v.unwrap_or(default))
            };
            let page = number("pageNumber", 1)?;
            let size = number("pageSize", 20)?;
            if page < 1 || !(1..=100).contains(&size) {
                return Err(invalid("页码或每页数量超出范围。"));
            }
            let unit = unit["id"]
                .as_i64()
                .ok_or_else(|| unavailable("单位编号缺失。"))?;
            let scope = auth::data_scope(&actor, "office.people", "view-details");
            if kind == "candidates" {
                let keyword = query
                    .iter()
                    .find(|(k, _)| *k == "keyword")
                    .map(|(_, v)| v.as_str())
                    .unwrap_or("");
                return Ok(tx.foundation_candidates(
                    unit,
                    keyword,
                    &today(tx)?.to_string(),
                    (page - 1).saturating_mul(size),
                    size,
                )?);
            }
            Ok(tx.foundation_list(&kind, unit, &scope, (page - 1).saturating_mul(size), size)?)
        });
    }
    let mut body = body.clone();
    super::super::records::normalize(&mut body, 0)?;
    let (id, unit, expected) = validate(&kind, &body)?;
    store.transaction_as(actor, |tx, actor| {
        let unit_row = store::get(tx, "companies", unit)?;
        let code = text(&unit_row, "code");
        demand(actor, &code, "organization.appointments")?;
        company(tx, &code)?;
        if unit_row["unitKind"] == "Group" {
            return Err(invalid("岗位和职责须属于经营单位。"));
        }
        if let Some(department) = body["departmentId"].as_str() {
            let row = store::get(
                tx,
                "departments",
                StableId::parse(department).map_err(invalid)?.number(),
            )?;
            if row["companyCode"] != code || row["isActive"] != true {
                return Err(invalid("部门不属于当前经营单位或已停用。"));
            }
        }
        if kind == "projects" {
            appointment(tx, unit, &code, &text(&body, "ownerAppointmentId"))?;
        }
        let children = if kind == "projects" {
            &body["members"]
        } else {
            &body["assignments"]
        };
        for entry in children.as_array().into_iter().flatten() {
            let assigned = appointment(tx, unit, &code, &text(entry, "appointmentId"))?;
            if body["departmentId"].is_string() && assigned["departmentId"] != body["departmentId"]
            {
                return Err(invalid("部门职责只能分配给该部门任职。"));
            }
            if let Some(project) = entry["projectId"].as_str() {
                let project = tx
                    .foundation_get(
                        "projects",
                        unit,
                        StableId::parse(project).map_err(invalid)?.number(),
                    )?
                    .ok_or_else(|| invalid("项目不属于当前单位。"))?;
                if project["isActive"] != true {
                    return Err(invalid("项目已停用。"));
                }
            }
        }
        let saved = tx.foundation_save(&kind, id, unit, expected, &body)?;
        let row_id = StableId::parse(saved["id"].as_str().unwrap_or(""))
            .map_err(unavailable)?
            .number();
        tx.append_audit_details(
            &AuditWrite {
                kind: &format!("organization-{kind}"),
                record_id: row_id,
                version: saved["versionNumber"].as_i64().unwrap_or(1),
                action: if id.is_some() { "edit" } else { "create" },
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: &text(&body, "note"),
            },
            &json!({"unitId":unit.to_string(),"actorContext":actor.context}),
        )?;
        Ok(saved)
    })
}
fn appointment(tx: &Connection, unit: i64, code: &str, key: &str) -> Result<Value> {
    let row = tx
        .foundation_appointment(unit, key)?
        .ok_or_else(|| invalid("任职不属于当前单位。"))?;
    let person = store::get(
        tx,
        "people",
        row["personId"]
            .as_i64()
            .ok_or_else(|| unavailable("任职人员引用失效。"))?,
    )?;
    super::super::personnel_organization::appointment_by_id(tx, &person, code, key)?;
    Ok(row)
}
fn validate(kind: &str, body: &Value) -> Result<(Option<i64>, i64, i64)> {
    let common = |id: Option<StableId>, unit: StableId, expected: i64, note: &str| -> Result<_> {
        identity::label(note, 500).map_err(invalid)?;
        if expected < 0 {
            return Err(invalid("版本不能为负数。"));
        }
        Ok((id.map(|id| id.number()), unit.number(), expected))
    };
    let names = |code: &str, name: &str| -> Result<()> {
        identity::label(code, 120).map_err(invalid)?;
        identity::label(name, 120).map_err(invalid)
    };
    match kind {
        "positions" => {
            let value: identity::Position = serde_json::from_value(body.clone())
                .map_err(|_| invalid("岗位字段不完整或包含未知字段。"))?;
            names(&value.code, &value.name)?;
            common(value.id, value.unit_id, value.expected_version, &value.note)
        }
        "responsibilities" => {
            let value: identity::Responsibility = serde_json::from_value(body.clone())
                .map_err(|_| invalid("职责字段不完整或包含未知字段。"))?;
            names(&value.code, &value.name)?;
            identity::assignments(&value.assignments).map_err(invalid)?;
            let allowed = &crate::contracts::schema("IdentityResponsibilitySave")["properties"]["workflowKinds"]
                ["items"]["enum"];
            let mut kinds = std::collections::HashSet::new();
            if value.workflow_kinds.is_empty()
                || value.workflow_kinds.len() > 9
                || value.workflow_kinds.iter().any(|kind| {
                    !allowed
                        .as_array()
                        .is_some_and(|allowed| allowed.contains(&json!(kind)))
                        || !kinds.insert(kind)
                })
            {
                return Err(invalid("职责业务类别为空、重复或无效。"));
            }
            common(value.id, value.unit_id, value.expected_version, &value.note)
        }
        "projects" => {
            let value: identity::Project = serde_json::from_value(body.clone())
                .map_err(|_| invalid("项目字段不完整或包含未知字段。"))?;
            names(&value.code, &value.name)?;
            identity::label(&value.owner_appointment_id, 64).map_err(invalid)?;
            identity::assignments(&value.members).map_err(invalid)?;
            if value
                .members
                .iter()
                .any(|m| !matches!(m.role.as_str(), "Member" | "Leader") || m.project_id.is_some())
            {
                return Err(invalid("项目成员角色无效。"));
            }
            common(value.id, value.unit_id, value.expected_version, &value.note)
        }
        _ => Err(invalid("未知人员基础实体。")),
    }
}
