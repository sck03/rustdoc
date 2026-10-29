//! Resolve an organizational duty by stable unit and appointment identifiers.
use super::*;
use export_doc_domain::approval_policy::{ApprovalStage, OrganizationRouteMode};

pub(super) fn unit(tx: &Connection, actor: &Actor, stage: &ApprovalStage) -> Result<Value> {
    let active = |row: &Value| row["isActive"] == true && row["unitKind"] != "Group";
    match stage.organization_route.mode {
        OrganizationRouteMode::Specified => {
            let row = store::get(
                tx,
                "companies",
                stage
                    .organization_route
                    .unit_id
                    .ok_or_else(|| invalid("缺少管理单位。"))?,
            )?;
            if !active(&row) {
                return Err(conflict("指定管理单位已停用或属于集团目录。"));
            }
            Ok(row)
        }
        OrganizationRouteMode::Current | OrganizationRouteMode::Ancestor => {
            let mut row = tx
                .find_identity("companies", &store::normalize(&actor.company))?
                .ok_or_else(|| conflict("申请单位不存在。"))?;
            if stage.organization_route.mode == OrganizationRouteMode::Current {
                return Ok(row);
            }
            let mut seen = std::collections::HashSet::new();
            loop {
                let parent = text(&row, "parentCompanyCode");
                if parent.is_empty() {
                    return Err(conflict("上级经营单位中没有配置审批主负责人。"));
                }
                if !seen.insert(parent.clone()) || seen.len() > 32 {
                    return Err(super::super::error::unavailable("单位隶属关系损坏。"));
                }
                row = tx
                    .find_identity("companies", &store::normalize(&parent))?
                    .ok_or_else(|| super::super::error::unavailable("上级单位不存在。"))?;
                if row["isActive"] != true {
                    return Err(conflict("上级管理单位已停用。"));
                }
                if active(&row) && row["executiveEmployeeId"].as_i64().is_some() {
                    return Ok(row);
                }
            }
        }
    }
}

pub(super) fn account(
    tx: &Connection,
    company: &str,
    employee: i64,
    appointment: &str,
) -> Result<i64> {
    let person = store::get(tx, "people", employee)?;
    super::super::personnel_organization::appointment_by_id(tx, &person, company, appointment)?;
    records::positive(&person["account"], "id", "负责人关联账号")
}
