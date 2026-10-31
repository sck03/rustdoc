//! Personnel proposals stay separate from the live employee until final approval.
use super::*;

pub(in crate::engine) fn required_actions() -> Value {
    json!(["Confirm", "Transfer", "Depart", "Rehire"])
}
pub(in crate::engine) fn ensure_clearance(
    tx: &Connection,
    person: &Value,
    departure: bool,
) -> Result<()> {
    let check = super::super::office_queries::clearance(tx, person)?;
    if check["isClear"] != true {
        return Err(conflict(
            "仍有未结申请、审批、办理或业务归属，请先交接后重新办理。",
        ));
    }
    if departure && check["canDepart"] != true {
        return Err(conflict(
            "请先移交部门、公司领导审批负责人及直属/职能汇报职责。",
        ));
    }
    Ok(())
}
pub(super) fn prepare(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    today: chrono::NaiveDate,
    clearance: bool,
) -> Result<Value> {
    let change = &row["personnelChange"];
    let proposed = effective_date(row)?;
    let today = if clearance {
        today
    } else {
        today.max(proposed)
    };
    let action = text(change, "action");
    let primary = text(change, "primaryAppointmentId");
    if !primary.is_empty() && action != "Transfer" {
        return Err(invalid("主任职调整仅适用于调岗申请。"));
    }
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
            ensure_clearance(tx, &value, action == "Depart")?;
        }
        let state = text(&value, "status");
        match action.as_str() {
            "Confirm" if state == "Probation" => {
                value["status"] = json!("Active");
                value["confirmedOn"] = json!(effective);
            }
            "Transfer" if state != "Departed" => {
                if primary.is_empty() {
                    value["departmentId"] = change["departmentId"].clone();
                    value["jobTitle"] = change["jobTitle"].clone();
                } else {
                    super::super::personnel_primary::prepare(
                        tx, actor, &mut value, &primary, date,
                    )?;
                }
            }
            "Depart" if state != "Departed" => {
                value["status"] = json!("Departed");
                value["departedOn"] = json!(effective);
            }
            "Rehire" if state == "Departed" => {
                let departed = text(&value, "departedOn");
                if effective <= departed {
                    return Err(invalid("返聘日期须晚于上一雇佣期间的最后任职日。"));
                }
                let mut organization = change
                    .get("organization")
                    .filter(|v| !v.is_null())
                    .cloned()
                    .unwrap_or(json!({}));
                let mut assignments = organization
                    .get("additionalAssignments")
                    .and_then(Value::as_array)
                    .cloned()
                    .unwrap_or_default();
                for mut old in super::super::personnel_organization::assignments(&value)? {
                    if old.ends_on.is_empty() || old.ends_on > departed {
                        old.ends_on = departed.clone();
                    }
                    if !assignments.iter().any(|a| a["id"] == old.id) {
                        assignments.push(serde_json::to_value(old)?);
                    }
                }
                organization["additionalAssignments"] = json!(assignments);
                value["organization"] = organization;
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
    if primary.is_empty() && matches!(action.as_str(), "Transfer" | "Rehire") {
        let organization = if action == "Rehire" {
            value["organization"].clone()
        } else {
            change
                .get("organization")
                .filter(|value| !value.is_null())
                .cloned()
                .unwrap_or_else(|| {
                    if action == "Rehire" {
                        json!({})
                    } else {
                        value.get("organization").cloned().unwrap_or(json!({}))
                    }
                })
        };
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
    for key in ["status", "companyScope", "departmentId", "jobTitle"] {
        value["employee"][key] = value[key].clone();
    }
    // Persisted requests own the proposed identities. Repeated validation, routing,
    // submission snapshots and final application must resolve the same appointment.
    if action == "Hire"
        && let Some(id) = row["id"].as_i64().filter(|id| *id > 0)
    {
        value["organization"]["primaryAssignmentId"] = json!(format!("hire-{id}-primary"));
        for assignment in value["organization"]["additionalAssignments"]
            .as_array_mut()
            .into_iter()
            .flatten()
        {
            let identity = super::super::media::digest(&serde_json::to_vec(&json!([
                "hire",
                id,
                assignment["companyCode"],
                assignment["departmentId"],
                assignment["jobTitle"],
                assignment["startsOn"],
                assignment["endsOn"]
            ]))?);
            assignment["id"] = json!(identity);
        }
    }
    if action == "Rehire"
        && let Some(id) = row["id"].as_i64().filter(|id| *id > 0)
    {
        value["organization"]["primaryAssignmentId"] = json!(format!("rehire-{id}-primary"));
    }
    Ok(value)
}

pub(super) fn effective_date(row: &Value) -> Result<chrono::NaiveDate> {
    let change = &row["personnelChange"];
    let value = if change["action"] == "Hire" {
        text(&change["registration"], "hireDate")
    } else {
        text(change, "effectiveDate")
    };
    export_doc_domain::business_date::parse(&value).map_err(invalid)
}

pub(super) fn identity(
    tx: &Connection,
    actor: &Actor,
    body: &Value,
    today: chrono::NaiveDate,
) -> Result<Value> {
    let value = prepare(tx, actor, body, today, false)?;
    let source_company = if body["personnelChange"]["action"] == "Hire" {
        text(&value, "companyScope")
    } else {
        text(
            &store::get(
                tx,
                "people",
                records::positive(body, "employeeId", "变动人员")?,
            )?,
            "companyScope",
        )
    };
    if source_company != actor.company {
        return Err(error(403, "人员审批须由所属公司的获授权业务身份发起。"));
    }
    // Changes are authorized against the employee's current department. A hire
    // uses the proposed department; neither is inferred from an approver's role.
    if matches!(
        body["personnelChange"]["action"].as_str(),
        Some("Hire" | "Rehire")
    ) {
        Ok(
            json!({"id":if body["personnelChange"]["action"] == "Hire" {json!(0)} else {value["id"].clone()},"fullName":value["profile"]["fullName"],"employeeNumber":value["employeeNumber"],"departmentId":value["departmentId"]}),
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
    let requester = super::super::units::resolve(
        tx,
        records::positive(row, "ownerUserId", "申请人")?,
        actor.edition,
        &text(row, "_requesterContext"),
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
    let requester = super::super::units::resolve(
        tx,
        records::positive(row, "ownerUserId", "申请人")?,
        actor.edition,
        &text(row, "_requesterContext"),
    )?;
    let mut value = prepare(tx, &requester, row, today, true)?;
    let id = row["employeeId"].as_i64().unwrap_or(0);
    value["approvalRequestId"] = row["id"].clone();
    value["ownerUserId"] = row["ownerUserId"].clone();
    let action = if !text(&row["personnelChange"], "primaryAppointmentId").is_empty() {
        "PromotePersonnelAppointment".into()
    } else if row["personnelChange"]["action"] == "Hire" {
        "create".into()
    } else {
        format!("{}Personnel", text(&row["personnelChange"], "action"))
    };
    let note = format!("人员变动审批 #{}：{}", row["id"], text(row, "reason"));
    let result = super::super::personnel::save(tx, actor, id, value, &action, &note)?;
    records::positive(&result["employee"], "id", "人员")
}

/// Both immediate and scheduled execution publish personnel facts and completion atomically.
pub(super) fn commit(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    mut row: Value,
    today: chrono::NaiveDate,
    event: &str,
    note: &str,
) -> Result<Value> {
    if effective_date(&row)? > today {
        return Err(conflict("人员变动尚未到生效日期。"));
    }
    validate(tx, actor, &row, today)?;
    row["status"] = json!("Completed");
    let saved = if row["personnelChange"]["action"] == "Hire" {
        row["employeeId"] = json!(apply(tx, actor, &row, today)?);
        save(tx, actor, meta, row.clone(), event, note)?
    } else {
        // Clearance must observe this request's completion within the same transaction.
        let saved = save(tx, actor, meta, row.clone(), event, note)?;
        apply(tx, actor, &row, today)?;
        saved
    };
    tx.personnel_order_state(records::positive(&row, "id", "人员申请")?, "Applied", "")?;
    Ok(saved)
}
