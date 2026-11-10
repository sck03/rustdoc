//! Read-only simulation runs the production router against public personnel data.
use super::*;
use export_doc_domain::approval_policy::{ApprovalRule, ConditionContext};
use rust_decimal::Decimal;
use std::str::FromStr;

pub(in crate::engine) fn simulate(
    store: &store::Store,
    actor: &Actor,
    body: Value,
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        super::super::units::demand(&actor, &actor.company, "organization.workflows")?;
        simulate_in(tx, &actor, &body)
    })
}
pub(super) fn simulate_in(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    let mut rule: ApprovalRule = serde_json::from_value(body["definition"].clone())
        .map_err(|_| invalid("流程定义无效。"))?;
    if rule.calendar.is_none() {
        rule.calendar = Some(
            serde_json::from_value(settings_store::section(tx, &actor.company, "calendar")?)
                .map_err(|_| super::super::error::unavailable("模拟日历缺失。"))?,
        );
    }
    super::business_nodes::instantiate_standard(&mut rule)?;
    rule.validate().map_err(invalid)?;
    let mut person = super::super::personnel_organization::active_person(
        tx,
        &actor.company,
        records::positive(&body, "employeeId", "模拟申请人员")?,
    )?;
    let appointment = super::super::personnel_organization::appointment_by_id(
        tx,
        &person,
        &actor.company,
        &text(&body, "appointmentId"),
    )?;
    if let Some(reporting) = body.get("reporting").filter(|v| v.is_object()) {
        if rule.kind != "personnel" || rule.variant != "Hire" {
            return Err(invalid("拟入职汇报样例只用于入职流程。"));
        }
        let mut proposed: export_doc_domain::personnel_organization::PersonnelOrganization =
            serde_json::from_value(reporting.clone())
                .map_err(|_| invalid("拟入职汇报样例无效。"))?;
        super::super::personnel_reporting::normalize(tx, &actor.company, &mut proposed)?;
        let proposed = serde_json::to_value(proposed)?;
        let primary = person["organization"]["primaryAssignmentId"] == appointment.id;
        let target = if primary {
            &mut person["organization"]
        } else {
            person["organization"]["additionalAssignments"]
                .as_array_mut()
                .and_then(|a| a.iter_mut().find(|a| a["id"] == appointment.id))
                .ok_or_else(|| invalid("拟入职路线任职不存在。"))?
        };
        for key in [
            "managerEmployeeId",
            "managerAssignmentId",
            "functionalManagerEmployeeIds",
            "functionalManagerAssignmentIds",
        ] {
            target[key] = proposed[key].clone();
        }
    }
    let decimal = |name| -> Result<Option<Decimal>> {
        let value = text(&body, name);
        if value.is_empty() {
            return Ok(None);
        }
        let value =
            Decimal::from_str(&value).map_err(|_| invalid("金额和时长须为精确十进制数。"))?;
        if value < Decimal::ZERO {
            return Err(invalid("金额和时长不能为负数。"));
        }
        Ok(Some(value))
    };
    let context = ConditionContext {
        form_values: super::custom_forms::values(body)?,
        amount: decimal("amount")?,
        currency: text(&body, "currency"),
        duration: decimal("duration")?,
        department: appointment.department_id.clone(),
        category: text(&body, "category"),
        personnel_action: rule.variant.clone(),
        returnable: body["isReturnable"]
            .as_bool()
            .map(|value| value.to_string())
            .unwrap_or_default(),
    };
    if let Some(form) = &rule.form {
        form.validate_values(&context.form_values, true)
            .map_err(invalid)?;
        let mut sample = json!({"definitionKey":rule.key,"departmentId":appointment.department_id,"formDefinition":form,"formValues":context.form_values});
        custom_forms::validate(tx, actor, &mut sample, true)?;
    }
    let mut row = json!({"kind":rule.kind,"employeeId":person["id"],"departmentId":appointment.department_id,"applicantAppointmentId":appointment.id,"companyScope":actor.company,"ownerUserId":person["account"]["id"].as_i64().unwrap_or(0),"applicantAccountId":person["account"]["id"]});
    if rule.kind == "personnel" && rule.variant == "Hire" {
        row["employeeId"] = json!(0);
        row["applicantAccountId"] = Value::Null;
        row["ownerUserId"] = json!(actor.id);
    }
    row["projectId"] = body["projectId"].clone();
    super::approval_routing::build_plan(tx, &actor, &mut row, &person, &rule, 1, &context)?;
    row["approvalPlan"]["policyVersion"] = json!(0);
    Ok(json!({"approvalPlan":row["approvalPlan"],"notes":row["approvalRoutingNotes"]}))
}

pub(in crate::engine) fn units(store: &store::Store, actor: &Actor) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        super::super::units::demand(&actor, &actor.company, "organization.workflows")?;
        let items = store::matching(
            tx,
            "companies",
            &[("isActive", "true".into())],
            &[
                ("id", "id"),
                ("code", "code"),
                ("name", "name"),
                ("unitKind", "unitKind"),
                ("parentCompanyCode", "parentCompanyCode"),
            ],
        )?;
        let by_code: std::collections::HashMap<_, _> = items
            .iter()
            .map(|unit| (text(unit, "code"), unit))
            .collect();
        let mut units = vec![];
        for unit in items.iter().filter(|unit| unit["unitKind"] != "Group") {
            let mut parent = text(unit, "parentCompanyCode");
            let mut seen = std::collections::HashSet::new();
            let mut ancestors = vec![];
            while !parent.is_empty() {
                if !seen.insert(parent.clone()) || seen.len() > 32 {
                    return Err(super::super::error::unavailable("审批来源的单位层级无效。"));
                }
                let previous = by_code.get(&parent).ok_or_else(|| {
                    super::super::error::unavailable("审批来源的上级单位不可用。")
                })?;
                if previous["unitKind"] != "Group" {
                    ancestors.push(previous["id"].clone());
                }
                parent = text(previous, "parentCompanyCode");
            }
            let mut value = unit.clone();
            value["ancestorCompanyIds"] = json!(ancestors);
            units.push(value);
        }
        Ok(json!({"items":units}))
    })
}
