//! Published routing obligations are checked against the same resolver used by submission.
use super::*;
use export_doc_domain::approval_policy::{
    ApprovalRule, ApproverSource, ConditionContext, ConditionField,
};
use std::collections::HashSet;

pub(in crate::engine) fn company(tx: &Connection, actor: &Actor, company: &str) -> Result<()> {
    for definition in published_rules(tx, company)? {
        rule(tx, actor, company, &definition).map_err(|e| {
            if matches!(e.status, Some(400 | 403 | 404 | 409)) {
                conflict(format!(
                    "流程“{}”的职责无法解析：{}。请先交接负责人、重新发布或停用该流程。",
                    definition.name, e
                ))
            } else {
                e
            }
        })?;
    }
    Ok(())
}

/// Changes to a management unit can affect published definitions in other units.
pub(in crate::engine) fn organization_changed(
    tx: &Connection,
    actor: &Actor,
    changed: &str,
) -> Result<()> {
    use export_doc_domain::approval_policy::OrganizationRouteMode;
    let units = store::matching(tx, "companies", &[], &[("id", "id"), ("code", "code")])?;
    let changed_id = units
        .iter()
        .find(|u| u["code"] == changed)
        .and_then(|u| u["id"].as_i64());
    for unit in units {
        let code = text(&unit, "code");
        for definition in published_rules(tx, &code)? {
            if code == changed
                || definition
                    .stages
                    .iter()
                    .any(|s| match s.organization_route.mode {
                        OrganizationRouteMode::Specified => {
                            s.organization_route.unit_id == changed_id
                        }
                        // A changed parent edge can alter which ancestor supplies a duty.
                        OrganizationRouteMode::Ancestor => true,
                        OrganizationRouteMode::Current => false,
                    })
            {
                rule(tx, actor, &code, &definition).map_err(|e| {
                    if matches!(e.status, Some(400 | 403 | 404 | 409)) {
                        conflict(format!(
                            "组织调整影响单位 {code} 的流程“{}”：{e}。请先交接或调整发布流程。",
                            definition.name
                        ))
                    } else {
                        e
                    }
                })?;
            }
        }
    }
    Ok(())
}

fn published_rules(tx: &Connection, company: &str) -> Result<Vec<ApprovalRule>> {
    let settings = settings::load(tx, company)?;
    let mut rules = vec![];
    for publication in settings["publications"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|p| p["enabled"] == true)
    {
        let key = text(publication, "key");
        let (kind, variant) = key.split_once(':').unwrap_or((&key, ""));
        rules.push(definitions::published(tx, company, kind, variant)?.0);
    }
    Ok(rules)
}

fn applies_to_department(
    stage: &export_doc_domain::approval_policy::ApprovalStage,
    department: &str,
) -> Result<bool> {
    let context = ConditionContext {
        department: department.into(),
        ..Default::default()
    };
    for condition in stage
        .conditions
        .iter()
        .filter(|c| c.field == ConditionField::Department)
    {
        if !condition.matches(&context).map_err(invalid)? {
            return Ok(false);
        }
    }
    Ok(true)
}

/// A top-level employee may have no reporting edge. Removing a configured edge
/// for an existing active appointment requires handing it over or disabling its definition.
pub(in crate::engine) fn personnel_changed(
    tx: &Connection,
    previous: &Value,
    saved: &Value,
) -> Result<()> {
    if !matches!(previous["status"].as_str(), Some("Active" | "Probation"))
        || !matches!(saved["status"].as_str(), Some("Active" | "Probation"))
    {
        return Ok(());
    }
    let companies: HashSet<_> = super::super::personnel_organization::assignments(previous)?
        .into_iter()
        .map(|a| a.company_code)
        .collect();
    for company in companies {
        let departments =
            super::super::personnel_organization::departments_in(tx, saved, &company)?;
        for rule in published_rules(tx, &company)? {
            if rule.kind == "personnel" && rule.variant == "Hire" {
                continue;
            }
            for stage in &rule.stages {
                let functional = match stage.source {
                    ApproverSource::LineManager => false,
                    ApproverSource::FunctionalManagers => true,
                    _ => continue,
                };
                for department in
                    super::super::personnel_organization::departments_in(tx, previous, &company)?
                {
                    if departments.contains(&department)
                        && applies_to_department(stage, &department)?
                        && !super::super::personnel_organization::appointment_managers(
                            tx,
                            previous,
                            &company,
                            &department,
                            functional,
                        )?
                        .is_empty()
                        && super::super::personnel_organization::appointment_managers(
                            tx,
                            saved,
                            &company,
                            &department,
                            functional,
                        )?
                        .is_empty()
                    {
                        return Err(conflict(format!(
                            "流程“{}”仍使用此任职的{}，请移交负责人或停用该流程。",
                            rule.name, stage.name
                        )));
                    }
                }
            }
        }
    }
    Ok(())
}

pub(super) fn rule(
    tx: &Connection,
    actor: &Actor,
    company: &str,
    rule: &ApprovalRule,
) -> Result<()> {
    let mut scope = actor.clone();
    scope.company = company.into();
    let mut verified = HashSet::new();
    // Hire routing uses the prospective registration, whose reporting edges do
    // not exist yet. It is validated in preview/submission and then by frozen tasks.
    let prospective = rule.kind == "personnel" && rule.variant == "Hire";
    let people = if rule.stages.iter().any(|s| {
        matches!(
            s.source,
            ApproverSource::LineManager | ApproverSource::FunctionalManagers
        )
    }) {
        super::super::personnel_organization::directory(tx, company)?
    } else {
        vec![]
    };
    let departments = if rule.stages.iter().any(|s| {
        matches!(
            s.source,
            ApproverSource::DepartmentManager | ApproverSource::DepartmentLeaders
        )
    }) {
        store::matching(
            tx,
            "departments",
            &[("companyCode", company.into()), ("isActive", "true".into())],
            &[("code", "code")],
        )?
    } else {
        vec![]
    };
    for stage in &rule.stages {
        crate::operation::check()?;
        if prospective
            && matches!(
                stage.source,
                ApproverSource::LineManager | ApproverSource::FunctionalManagers
            )
        {
            continue;
        }
        let contexts = match stage.source {
            ApproverSource::LineManager | ApproverSource::FunctionalManagers => {
                let mut contexts = vec![];
                for person in people
                    .iter()
                    .filter(|p| matches!(p["status"].as_str(), Some("Active" | "Probation")))
                {
                    for department in
                        super::super::personnel_organization::departments_in(tx, person, company)?
                    {
                        if !super::super::personnel_organization::appointment_managers(
                            tx,
                            person,
                            company,
                            &department,
                            stage.source == ApproverSource::FunctionalManagers,
                        )?
                        .is_empty()
                        {
                            contexts.push((department, person.clone()));
                        }
                    }
                }
                contexts
            }
            ApproverSource::DepartmentManager | ApproverSource::DepartmentLeaders => departments
                .iter()
                .map(|d| (text(d, "code"), Value::Null))
                .collect(),
            _ => vec![(String::new(), Value::Null)],
        };
        let mut resolved = false;
        for (department, employee) in contexts {
            if !department.is_empty() && !applies_to_department(stage, &department)? {
                continue;
            }
            let row = json!({"kind":rule.kind,"departmentId":department});
            let ids = approval_routing::resolve(tx, &scope, &row, &employee, stage)?;
            if ids.is_empty() {
                return Err(invalid(format!("节点“{}”缺少负责人", stage.name)));
            }
            for id in ids {
                if verified.insert(id) {
                    approval::reviewer(tx, &scope, id, &row).map_err(|e| {
                        if matches!(e.status, Some(400 | 403 | 404)) {
                            invalid(format!(
                                "流程“{}”节点“{}”的账号 {id} 无法承担职责：{e}",
                                rule.name, stage.name
                            ))
                        } else {
                            e
                        }
                    })?;
                }
            }
            resolved = true;
        }
        if !resolved {
            return Err(invalid(format!(
                "节点“{}”没有可解析的任职或部门",
                stage.name
            )));
        }
    }
    for id in &rule.notify_user_ids {
        let resource = approval::resource(&rule.kind).ok_or_else(|| invalid("未知审批类型。"))?;
        let user =
            super::super::units::for_operation(tx, *id, actor.edition, company, resource, "view")?;
        if user.company != company {
            return Err(invalid("抄送账号已调离公司。"));
        }
        auth::authorize(&user, resource, "view")?;
    }
    Ok(())
}
