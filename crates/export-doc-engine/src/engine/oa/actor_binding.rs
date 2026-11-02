//! Source resolution freezes stable people and appointments, never role names.
use super::*;
use export_doc_domain::{
    approval_graph::{ActorBinding, AssignmentOrigin, HandlingAction, NodeKind, Phase},
    approval_policy::{ApprovalStage, ApproverSource, OrganizationRouteMode},
};

mod availability;
mod escalations;
mod organization;
pub(super) use availability::origin_available;
pub(super) use escalations::targets as escalation_targets;

pub(super) fn action(stage: &ApprovalStage) -> &'static str {
    node_action(stage.phase, stage.kind, stage.handling_action)
}
pub(super) fn node_action(phase: Phase, kind: NodeKind, handling: HandlingAction) -> &'static str {
    if kind == NodeKind::Copy {
        return "view";
    }
    if phase == Phase::Decision {
        return "approve";
    }
    match handling {
        HandlingAction::Issue => "issue",
        HandlingAction::Return => "return",
        _ => "complete",
    }
}
fn source_unit(
    tx: &Connection,
    actor: &Actor,
    stage: &ApprovalStage,
    kind: &str,
    id: i64,
) -> Result<Value> {
    if stage.organization_route.mode != OrganizationRouteMode::Ancestor {
        return super::organization_routing::unit(tx, actor, stage);
    }
    let mut unit = super::super::units::company(tx, &actor.company)?;
    let mut seen = std::collections::HashSet::new();
    loop {
        let parent = text(&unit, "parentCompanyCode");
        if parent.is_empty() {
            return Err(conflict("上级经营单位没有所选岗位、职责或项目。"));
        }
        if !seen.insert(parent.clone()) || seen.len() > 32 {
            return Err(super::super::error::unavailable("上级单位关系无效。"));
        }
        unit = super::super::units::company(tx, &parent)?;
        if unit["unitKind"] != "Group"
            && tx
                .foundation_get(kind, records::positive(&unit, "id", "单位")?, id)?
                .is_some()
        {
            return Ok(unit);
        }
    }
}
pub(super) fn validate_project_source(
    tx: &Connection,
    actor: &Actor,
    stage: &ApprovalStage,
) -> Result<()> {
    let id = stage
        .source_id
        .as_ref()
        .ok_or_else(|| invalid("项目来源缺少稳定编号。"))?
        .number();
    let unit = source_unit(tx, actor, stage, "projects", id)?;
    tx.foundation_get("projects", records::positive(&unit, "id", "项目单位")?, id)?
        .filter(|p| p["isActive"] == true)
        .ok_or_else(|| invalid("项目来源已停用或不属于路由单位。"))?;
    Ok(())
}
pub(super) fn resolve(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    employee: &Value,
    stage: &ApprovalStage,
) -> Result<Vec<ActorBinding>> {
    let mut origins = std::collections::HashMap::new();
    let ids = match stage.source {
        ApproverSource::Position
        | ApproverSource::Responsibility
        | ApproverSource::ProjectLeader => {
            let id = stage
                .source_id
                .as_ref()
                .ok_or_else(|| invalid("职责来源缺少稳定编号。"))?
                .number();
            let kind = match stage.source {
                ApproverSource::Position => "positions",
                ApproverSource::Responsibility => "responsibilities",
                _ => "projects",
            };
            let unit = source_unit(tx, actor, stage, kind, id)?;
            if stage.source == ApproverSource::ProjectLeader && row["projectId"] != id.to_string() {
                return Err(invalid(
                    "申请须选择该审批项目，并使用该项目的有效成员任职。",
                ));
            }
            let unit_id = records::positive(&unit, "id", "来源单位")?;
            tx.foundation_get(kind, unit_id, id)?
                .filter(|r| r["isActive"] == true)
                .ok_or_else(|| invalid("所选职责来源不属于目标单位或已停用。"))?;
            let today = super::super::units::today(tx)?.to_string();
            let rows = if stage.source == ApproverSource::Responsibility {
                tx.responsibility_appointments(
                    unit_id,
                    id,
                    row["projectId"].as_str().and_then(|id| id.parse().ok()),
                    &text(row, "kind"),
                    &today,
                )?
            } else {
                tx.placement_appointments(
                    unit_id,
                    if stage.source == ApproverSource::Position {
                        "Position"
                    } else {
                        "ProjectLeader"
                    },
                    id,
                    &today,
                )?
            };
            let mut ids = vec![];
            for source in rows {
                let person = store::get(
                    tx,
                    "people",
                    records::positive(&source, "personId", "职责人员")?,
                )?;
                let appointment = text(&source, "appointmentId");
                super::super::personnel_organization::appointment_by_id(
                    tx,
                    &person,
                    &text(&unit, "code"),
                    &appointment,
                )?;
                let user = records::positive(&person["account"], "id", "职责人员关联账号")?;
                origins.insert(
                    user,
                    AssignmentOrigin {
                        source: Some(stage.source),
                        source_id: stage.source_id.clone(),
                        unit_code: text(&unit, "code"),
                        appointment_id: appointment,
                        ..Default::default()
                    },
                );
                ids.push(user);
            }
            ids
        }
        ApproverSource::Requester => row["applicantAccountId"]
            .as_i64()
            .or_else(|| row["ownerUserId"].as_i64())
            .into_iter()
            .collect(),
        ApproverSource::Handlers => {
            let key = text(row, "handlingKey");
            if !key.is_empty() {
                handling::services(tx, &actor.company)?
                    .into_iter()
                    .find(|s| s["key"] == key)
                    .ok_or_else(|| conflict("办理分工缺失。"))?["handlerUserIds"]
                    .as_array()
                    .ok_or_else(|| invalid("办理人员配置无效。"))?
                    .iter()
                    .map(|id| id.as_i64().ok_or_else(|| invalid("办理人员编号无效。")))
                    .collect::<Result<Vec<_>>>()?
            } else {
                let mut ids = vec![];
                let mut cursor = None;
                let today = super::super::units::today(tx)?.to_string();
                loop {
                    let users =
                        tx.workflow_notification_account_page(&actor.company, &today, cursor, 100)?;
                    if users.is_empty() {
                        break;
                    }
                    cursor = users.last().and_then(|u| u["id"].as_i64());
                    for user in users {
                        let id = records::positive(&user, "id", "办理候选")?;
                        match candidate(tx, actor, row, stage, id, AssignmentOrigin::default()) {
                            Ok(binding) => {
                                let candidate = super::super::units::resolve(
                                    tx,
                                    id,
                                    actor.edition,
                                    &binding.context,
                                )?;
                                let resource = approval::resource(&text(row, "kind"))
                                    .ok_or_else(|| invalid("申请类型无效。"))?;
                                if (!candidate.oa_assigned_only
                                    || row["ownerUserId"] == candidate.id)
                                    && auth::visible(&candidate, resource, action(stage), row)
                                {
                                    ids.push(id);
                                }
                            }
                            Err(cause)
                                if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) => {}
                            Err(cause) => return Err(cause),
                        }
                    }
                    if ids.len() > 200 {
                        return Err(error(429, "办理候选超过二百人，请配置明确办理分工。"));
                    }
                }
                ids
            }
        }
        ApproverSource::Named => stage.approver_user_ids.clone(),
        _ => {
            let (unit, appointments) = organization::appointments(tx, actor, row, employee, stage)?;
            let department = if matches!(
                stage.source,
                ApproverSource::DepartmentManager | ApproverSource::DepartmentLeaders
            ) {
                let code = if text(row, "routingDepartment").is_empty() {
                    text(row, "departmentId")
                } else {
                    text(row, "routingDepartment")
                };
                let department = tx
                    .find_identity("departments", &store::department_identity(&unit, &code)?)?
                    .ok_or_else(|| conflict("路由部门已失效。"))?;
                Some(
                    export_doc_domain::identity::StableId::parse(
                        &records::positive(&department, "id", "部门")?.to_string(),
                    )
                    .map_err(invalid)?,
                )
            } else {
                None
            };
            let mut ids = vec![];
            for (person, appointment) in appointments {
                let user = super::organization_routing::account(tx, &unit, person, &appointment)?;
                origins.insert(
                    user,
                    AssignmentOrigin {
                        source: Some(stage.source),
                        source_id: None,
                        unit_code: unit.clone(),
                        appointment_id: appointment,
                        department_id: department.clone(),
                        subject_person_id: employee["id"].as_i64().filter(|id| *id > 0),
                        subject_appointment_id: text(row, "applicantAppointmentId"),
                    },
                );
                ids.push(user);
            }
            ids
        }
    };
    let mut result = vec![];
    let mut people = std::collections::HashSet::new();
    for id in ids {
        crate::operation::check()?;
        let origin = origins.remove(&id).unwrap_or(AssignmentOrigin {
            source: Some(stage.source),
            source_id: None,
            unit_code: actor.company.clone(),
            appointment_id: String::new(),
            ..Default::default()
        });
        let binding = candidate(tx, actor, row, stage, id, origin)?;
        if let Some(person) = binding.person_id {
            if !people.insert(person) {
                continue;
            }
        }
        result.push(binding);
    }
    if result.is_empty() {
        return Err(conflict(
            "节点没有有效的责任人员，请补齐任职、权限或办理分工。",
        ));
    }
    Ok(result)
}
fn candidate(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    stage: &ApprovalStage,
    id: i64,
    mut origin: AssignmentOrigin,
) -> Result<ActorBinding> {
    let action = action(stage);
    let candidate = match approval::reviewer_for(tx, actor, id, row, action, false) {
        Ok(candidate) => candidate,
        Err(cause)
            if cause.status == Some(403)
                && stage.kind == NodeKind::Handling
                && stage.allow_administration =>
        {
            let base = auth::current_actor_in(tx, id, actor.edition)?;
            if !base.admin {
                return Err(cause);
            }
            let context = if base.company == actor.company {
                String::new()
            } else {
                format!("company:{}", actor.company)
            };
            let candidate = super::super::units::resolve(tx, id, actor.edition, &context)?;
            let resource =
                approval::resource(&text(row, "kind")).ok_or_else(|| invalid("申请类型无效。"))?;
            auth::authorize(&candidate, resource, action)?;
            candidate
        }
        Err(cause) => return Err(cause),
    };
    let person = if candidate.admin {
        None
    } else {
        Some(super::qualification::person_id(tx, &candidate)?)
    };
    if origin.appointment_id.is_empty() && !candidate.admin {
        origin.appointment_id =
            super::super::units::selected_appointment_id(tx, &candidate)?.unwrap_or_default();
    }
    Ok(ActorBinding {
        user_id: id,
        person_id: person,
        name: candidate.name,
        context: candidate.context,
        origin,
        administration: candidate.admin,
    })
}
