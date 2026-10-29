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
        let actor = auth::refresh_actor_in(tx,actor)?;
        super::super::units::demand(&actor,&actor.company,"organization.workflows")?;
        let rule: ApprovalRule = serde_json::from_value(body["definition"].clone()).map_err(|_| invalid("流程定义无效。"))?;
        rule.validate().map_err(invalid)?;
        let person = super::super::personnel_organization::active_person(tx,&actor.company,records::positive(&body,"employeeId","模拟申请人员")?)?;
        let appointment = super::super::personnel_organization::appointment_by_id(tx,&person,&actor.company,&text(&body,"appointmentId"))?;
        let decimal = |name| -> Result<Option<Decimal>> {
            let value = text(&body,name);
            if value.is_empty() { return Ok(None); }
            let value = Decimal::from_str(&value).map_err(|_| invalid("金额和时长须为精确十进制数。"))?;
            if value < Decimal::ZERO { return Err(invalid("金额和时长不能为负数。")); }
            Ok(Some(value))
        };
        let context = ConditionContext { amount:decimal("amount")?,currency:text(&body,"currency"),duration:decimal("duration")?,department:appointment.department_id.clone(),category:text(&body,"category"),personnel_action:rule.variant.clone() };
        let mut row = json!({"kind":rule.kind,"departmentId":appointment.department_id,"companyScope":actor.company,"ownerUserId":person["account"]["id"].as_i64().unwrap_or(0),"applicantAccountId":person["account"]["id"]});
        super::approval_routing::build_plan(tx,&actor,&mut row,&person,&rule,1,&context)?;
        row["approvalPlan"]["policyVersion"] = json!(0);
        Ok(json!({"approvalPlan":row["approvalPlan"],"notes":row["approvalRoutingNotes"]}))
    })
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
            ],
        )?
        .into_iter()
        .filter(|u| u["unitKind"] != "Group")
        .collect::<Vec<_>>();
        Ok(json!({"items":items}))
    })
}
