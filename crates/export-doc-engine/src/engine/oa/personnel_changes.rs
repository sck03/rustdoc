//! Personnel proposals stay separate from the live employee until final approval.
use super::*;

pub(in crate::engine) fn required_actions(tx: &Connection, actor: &Actor) -> Result<Value> {
    if actor.admin {
        return Ok(json!([]));
    }
    Ok(settings::load(tx, &actor.company)?["personnelApprovalActions"].clone())
}
pub(in crate::engine) fn authorize_direct(
    tx: &Connection,
    actor: &Actor,
    action: &str,
) -> Result<()> {
    if required_actions(tx, actor)?
        .as_array()
        .is_some_and(|actions| actions.iter().any(|a| a == action))
    {
        return Err(conflict(
            "本公司已为此项人员变动启用审批，请提交人员变动申请。",
        ));
    }
    Ok(())
}
pub(in crate::engine) fn direct(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    operation: &str,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    let action = operation
        .strip_suffix("Personnel")
        .ok_or_else(|| invalid("人员变动类型无效。"))?;
    authorize_direct(tx, actor, action)?;
    records::required(body, "note", "任职变动说明", 500)?;
    let mut change = body.clone();
    change["action"] = json!(action);
    change["expectedPersonnelVersion"] = body["expectedVersion"].clone();
    let row = json!({"employeeId":id,"personnelChange":change});
    let value = prepare(tx, actor, &row, today, true)?;
    super::super::personnel::save(tx, actor, id, value, operation, &text(body, "note"))
}

pub(super) fn prepare(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    today: chrono::NaiveDate,
    clearance: bool,
) -> Result<Value> {
    let change = &row["personnelChange"];
    let action = text(change, "action");
    let mut value = if action == "Hire" {
        auth::authorize(actor, "office.people", "create")?;
        let mut registration = contracts::dto(
            contracts::schema("PersonnelCreateRequest"),
            change["registration"].clone(),
        );
        registration["requestKey"] = row["requestKey"].clone();
        records::required(&registration, "employeeNumber", "工号", 50)?;
        if tx
            .find_identity(
                "people",
                &store::normalize(&text(&registration, "employeeNumber")),
            )?
            .is_some()
        {
            return Err(conflict("该工号已经建立人员档案。"));
        }
        let mut value = contracts::overlay(
            contracts::initial(contracts::schema("PersonnelRecord")),
            &registration,
        );
        value["companyScope"] = json!(actor.company);
        value["ownerUserId"] = json!(actor.id);
        records::normalize(&mut value, 0)?;
        super::super::office::validate(tx, actor, "people", 0, &json!({}), &mut value, today)?;
        if !auth::visible(actor, "office.people", "create", &value) {
            return Err(error(403, "入职部门不在当前人事权限范围内。"));
        }
        let hire = export_doc_domain::oa::date(&text(&value, "hireDate")).map_err(invalid)?;
        if hire > today {
            return Err(invalid("入职日期不能晚于公司业务日期。"));
        }
        value
    } else {
        let id = records::positive(row, "employeeId", "变动人员")?;
        let mut value = super::super::personnel::person(tx, actor, id, "transition")?;
        if !actor.admin && value["companyScope"] != actor.company {
            return Err(error(403, "只能办理本公司的人员变动。"));
        }
        store::check_version(
            &value,
            change["expectedPersonnelVersion"].as_i64().unwrap_or(0),
        )?;
        let effective = text(change, "effectiveDate");
        let date = export_doc_domain::oa::date(&effective).map_err(invalid)?;
        if effective < text(&value, "lastEffectiveDate") || date > today {
            return Err(invalid(
                "任职生效日期不能早于上一项变动或晚于公司业务日期。",
            ));
        }
        if clearance && matches!(action.as_str(), "Transfer" | "Depart") {
            let check = super::super::office_queries::clearance(tx, &value)?;
            if check["isClear"] != true {
                return Err(conflict(
                    "仍有未结申请、审批或办理职责，请先交接后重新审批。",
                ));
            }
            if action == "Depart" && check["canDepart"] != true {
                return Err(conflict(
                    "请先移交部门、公司领导审批负责人及直属/职能汇报职责。",
                ));
            }
        }
        let state = text(&value, "status");
        match action.as_str() {
            "Confirm" if state == "Probation" => {
                value["status"] = json!("Active");
                value["confirmedOn"] = json!(effective);
            }
            "Transfer" if state != "Departed" => {
                value["departmentId"] = change["departmentId"].clone();
                value["jobTitle"] = change["jobTitle"].clone();
            }
            "Depart" if state != "Departed" => {
                value["status"] = json!("Departed");
                value["departedOn"] = json!(effective);
            }
            "Rehire" if state == "Departed" => {
                value["departmentId"] = change["departmentId"].clone();
                value["jobTitle"] = change["jobTitle"].clone();
                value["status"] = json!(if change["onProbation"] == true {
                    "Probation"
                } else {
                    "Active"
                });
                value["hireDate"] = json!(effective);
                for key in [
                    "confirmedOn",
                    "departedOn",
                    "probationEndsOn",
                    "contractEndsOn",
                ] {
                    value[key] = Value::Null;
                }
            }
            _ => return Err(conflict("当前任职状态不能办理此项变动。")),
        }
        value["lastEffectiveDate"] = json!(effective);
        value
    };
    records::required(&value, "jobTitle", "职位", 120)?;
    if matches!(action.as_str(), "Transfer" | "Rehire") {
        let organization = change
            .get("organization")
            .filter(|value| !value.is_null())
            .cloned()
            .unwrap_or_else(|| {
                if action == "Rehire" {
                    json!({})
                } else {
                    value.get("organization").cloned().unwrap_or(json!({}))
                }
            });
        super::super::personnel_organization::validate(
            tx,
            actor,
            &mut value,
            &organization,
            "transition",
        )?;
    }
    let department = text(&value, "departmentId");
    if !store::all(tx, "departments")?.iter().any(|d| {
        d["code"] == department
            && d["companyCode"] == value["companyScope"]
            && d["isActive"] == true
    }) {
        return Err(invalid("请选择本公司启用的部门。"));
    }
    for key in ["status", "departmentId", "jobTitle"] {
        value["employee"][key] = value[key].clone();
    }
    Ok(value)
}

pub(super) fn identity(
    tx: &Connection,
    actor: &Actor,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    let value = prepare(tx, actor, body, today, false)?;
    if value["companyScope"] != actor.company {
        return Err(error(
            403,
            "人员审批须由所属公司的账号发起，管理员可直接办理。",
        ));
    }
    // Changes are authorized against the employee's current department. A hire
    // uses the proposed department; neither is inferred from an approver's role.
    if body["personnelChange"]["action"] == "Hire" {
        Ok(
            json!({"id":0,"fullName":value["profile"]["fullName"],"employeeNumber":value["employeeNumber"],"departmentId":value["departmentId"]}),
        )
    } else {
        let previous = store::get(
            tx,
            "people",
            records::positive(body, "employeeId", "变动人员")?,
        )?;
        Ok(
            json!({"id":previous["id"],"fullName":previous["profile"]["fullName"],"employeeNumber":previous["employeeNumber"],"departmentId":previous["departmentId"]}),
        )
    }
}

pub(super) fn validate(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    today: chrono::NaiveDate,
) -> Result<()> {
    let requester = auth::current_actor_in(
        tx,
        records::positive(row, "ownerUserId", "申请人")?,
        actor.edition,
    )?;
    if !auth::visible(&requester, "office.personnel-changes", "create", row) {
        return Err(error(403, "申请人已失去人员变动权限，请撤回后重新办理。"));
    }
    prepare(tx, &requester, row, today, false)?;
    Ok(())
}

pub(super) fn apply(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    today: chrono::NaiveDate,
) -> Result<i64> {
    let requester = auth::current_actor_in(
        tx,
        records::positive(row, "ownerUserId", "申请人")?,
        actor.edition,
    )?;
    let mut value = prepare(tx, &requester, row, today, true)?;
    let id = row["employeeId"].as_i64().unwrap_or(0);
    value["approvalRequestId"] = row["id"].clone();
    value["ownerUserId"] = row["ownerUserId"].clone();
    let action = if row["personnelChange"]["action"] == "Hire" {
        "create".into()
    } else {
        format!("{}Personnel", text(&row["personnelChange"], "action"))
    };
    let note = format!("人员变动审批 #{}：{}", row["id"], text(row, "reason"));
    let result = super::super::personnel::save(tx, actor, id, value, &action, &note)?;
    records::positive(&result["employee"], "id", "人员")
}
