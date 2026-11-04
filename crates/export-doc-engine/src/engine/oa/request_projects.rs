//! A project is a finite approval scope, independent of customer and document data.
use super::*;
use export_doc_domain::identity::{EffectivePeriod, StableId};

pub(super) fn options(tx: &Connection, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    let argument = |key| crate::engine::request_values::find(query, key).unwrap_or("");
    let kind = argument("kind");
    let resource = approval::resource(kind).ok_or_else(|| invalid("项目申请业务无效。"))?;
    let body = json!({"employeeId":argument("employeeId").parse::<i64>().ok(),"appointmentId":argument("appointmentId"),"departmentId":argument("departmentId")});
    let person =
        if super::super::units::demand(actor, &actor.company, "organization.workflows").is_ok() {
            super::super::personnel_organization::active_person(
                tx,
                &actor.company,
                records::positive(&body, "employeeId", "样例人员")?,
            )?
        } else {
            auth::authorize(actor, resource, "create")?;
            validation::employee(tx, actor, &body, false)?
        };
    let appointment = request_identity::appointment(tx, actor, &person, &body)?;
    let unit = super::super::units::company(tx, &actor.company)?;
    let (page, size) = paging(query)?;
    Ok(tx.foundation_project_options(
        records::positive(&unit, "id", "项目单位")?,
        &appointment.id,
        &super::super::units::today(tx)?.to_string(),
        (page - 1) * size,
        size,
    )?)
}

pub(super) fn bind(tx: &Connection, actor: &Actor, row: &mut Value, person: &Value) -> Result<()> {
    let Some(id) = row
        .get("projectId")
        .and_then(Value::as_str)
        .filter(|id| !id.is_empty())
    else {
        return Ok(());
    };
    if row["kind"] == "personnel" && row["employeeId"] == 0 {
        return Err(invalid(
            "拟入职人员尚无项目成员任职，不能借用现有人员的项目身份。",
        ));
    }
    let id = StableId::parse(id).map_err(invalid)?.number();
    let unit = super::super::units::company(tx, &actor.company)?;
    let project = tx
        .foundation_get("projects", records::positive(&unit, "id", "项目单位")?, id)?
        .filter(|p| p["isActive"] == true)
        .ok_or_else(|| conflict("申请项目已停用或不属于当前经营单位。"))?;
    let appointment = request_identity::appointment(tx, actor, person, row)?;
    let day = super::super::units::today(tx)?;
    let mut member = project["ownerAppointmentId"] == appointment.id;
    for item in project["members"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|m| m["appointmentId"] == appointment.id)
    {
        let period: EffectivePeriod = serde_json::from_value(item["period"].clone())
            .map_err(|_| super::super::error::unavailable("项目成员期间损坏。"))?;
        member |= period
            .contains(day)
            .map_err(super::super::error::unavailable)?;
    }
    if !member {
        return Err(error(403, "申请任职不是该项目的有效成员或负责人。"));
    }
    row["projectName"] = project["name"].clone();
    row["projectCode"] = project["code"].clone();
    Ok(())
}
