//! The same material projection protects details, originals and PDF output.
use super::*;
use export_doc_domain::approval::MaterialPolicy;
const SYSTEM: &[&str] = &[
    "id",
    "kind",
    "status",
    "versionNumber",
    "rowVersion",
    "ownerUserId",
    "companyScope",
    "departmentId",
    "employeeId",
    "employeeName",
    "employeeNumber",
    "applicantAccountId",
    "applicantName",
    "meetingRoomId",
    "officeSupplyId",
    "createdAt",
    "updatedAt",
    "requestKey",
    "submissionId",
    "submissionNumber",
    "approvalPlan",
    "approvalDocument",
    "canReview",
    "reviewTaskKeys",
    "workflowActions",
    "personnelExecution",
    "canHandle",
    "handlerNames",
    "approvalStageLabels",
    "approvalRoutingNotes",
];
fn prefix(pattern: &str, path: &str) -> bool {
    let parts = pattern.split('.').collect::<Vec<_>>();
    let actual = path.split('.').collect::<Vec<_>>();
    parts.len() <= actual.len() && parts.iter().zip(actual).all(|(a, b)| *a == "*" || *a == b)
}
fn allowed(path: &str, policies: &[MaterialPolicy]) -> bool {
    policies.iter().any(|p| {
        (p.visible_fields.is_empty()
            || p.visible_fields
                .iter()
                .any(|f| prefix(f, path) || prefix(path, f)))
            && !p.hidden_fields.iter().any(|f| prefix(f, path))
    })
}
fn private_access(tx: &Connection, actor: &Actor, row: &Value) -> Result<bool> {
    let self_person = super::super::personnel_accounts::person_for_account(tx, actor.id)?;
    if self_person
        .as_ref()
        .is_some_and(|p| row["employeeId"] == p["id"])
    {
        return Ok(true);
    }
    let person = if let Some(id) = row["employeeId"].as_i64().filter(|id| *id > 0) {
        store::get(tx, "people", id)?
    } else {
        json!({"companyScope":row["companyScope"],"ownerUserId":row["ownerUserId"],"departmentId":row["departmentId"]})
    };
    Ok(auth::visible(
        actor,
        "office.people",
        "view-details",
        &person,
    ))
}
fn policies(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    restricted: bool,
) -> Result<Vec<MaterialPolicy>> {
    let mut result = vec![];
    if let Some(plan) = approval::plan(row)? {
        let principals = settings::principals(tx, actor)?;
        for step in &plan.steps {
            if step.approver_user_id == actor.id && step.approver_context == actor.context
                || step.acted_by_user_id == Some(actor.id) && step.acted_by_context == actor.context
                || step.status == "Waiting"
                    && principals
                        .iter()
                        .any(|(id, _)| *id == step.approver_user_id)
            {
                result.push(plan.nodes[step.stage].material_policy.clone());
            }
        }
    }
    if result.is_empty() {
        let mut policy = MaterialPolicy::default();
        policy.allow_restricted_attachments = restricted;
        result.push(policy);
    }
    Ok(result)
}
fn filter(
    value: &mut Value,
    path: &str,
    policies: &[MaterialPolicy],
    restricted: bool,
    omitted: &mut Vec<String>,
) {
    match value {
        Value::Object(fields) => {
            let keys = fields.keys().cloned().collect::<Vec<_>>();
            for key in keys {
                let next = if path.is_empty() {
                    key.clone()
                } else {
                    format!("{path}.{key}")
                };
                if !allowed(&next, policies) {
                    fields.remove(&key);
                    omitted.push(next);
                    continue;
                }
                if key == "profile" && !restricted {
                    if let Some(profile) = fields.get_mut(&key) {
                        let before = profile.clone();
                        super::super::personnel_privacy::public_profile(profile);
                        if before != *profile {
                            omitted.push(format!("{next}.private"));
                        }
                    }
                } else if let Some(v) = fields.get_mut(&key) {
                    filter(v, &next, policies, restricted, omitted);
                }
            }
        }
        Value::Array(values) => {
            for (i, v) in values.iter_mut().enumerate() {
                filter(v, &format!("{path}.{i}"), policies, restricted, omitted)
            }
        }
        _ => {}
    }
}
fn redact_notes(value: &mut Value) {
    if let Some(steps) = value
        .get_mut("approvalPlan")
        .and_then(|p| p.get_mut("steps"))
        .and_then(Value::as_array_mut)
    {
        for step in steps {
            if !text(step, "note").is_empty() {
                step["note"] = json!("详细意见按材料权限保留。");
            }
        }
    }
}
pub(in crate::engine) fn project(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    mut value: Value,
) -> Result<Value> {
    let restricted = private_access(tx, actor, row)?;
    let policies = policies(tx, actor, row, restricted)?;
    let mut omitted = vec![];
    if let Some(fields) = value.as_object_mut() {
        for key in fields.keys().cloned().collect::<Vec<_>>() {
            if SYSTEM.contains(&key.as_str()) || key == "attachments" || key == "materialAccess" {
                continue;
            }
            if !allowed(&key, &policies) {
                fields.remove(&key);
                omitted.push(key);
            } else if let Some(v) = fields.get_mut(&key) {
                filter(v, &key, &policies, restricted, &mut omitted);
            }
        }
    }
    if let Some(attachments) = value["attachments"].as_array_mut() {
        attachments.retain(|file| {
            let accepted = allowed("attachments", &policies)
                && (file["classification"] != "Restricted"
                    || restricted && policies.iter().any(|p| p.allow_restricted_attachments));
            if !accepted {
                omitted.push("attachments.restricted".into());
            }
            accepted
        });
    }
    // Verification fields are stored in signed facts but have their own classification.
    if let Some(plan) = value.get_mut("approvalPlan").and_then(Value::as_object_mut) {
        let nodes = plan
            .get("nodes")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        if let Some(steps) = plan.get_mut("steps").and_then(Value::as_array_mut) {
            for (index, step) in steps.iter_mut().enumerate() {
                let stage = step["stage"].as_u64().unwrap_or(0) as usize;
                if !restricted {
                    for field in nodes
                        .get(stage)
                        .and_then(|n| n["materialPolicy"]["verificationFields"].as_array())
                        .into_iter()
                        .flatten()
                    {
                        if field["restricted"] == true {
                            if let Some(values) = step["verificationValues"].as_object_mut() {
                                let key = field["key"].as_str().unwrap_or("");
                                if values.remove(key).is_some() {
                                    omitted.push(format!(
                                        "approvalPlan.steps.{index}.verificationValues.{key}"
                                    ));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    omitted.sort();
    omitted.dedup();
    if !omitted.is_empty() {
        value["approvalRoutingNotes"] = json!([]);
        redact_notes(&mut value);
    }
    value["materialAccess"] = json!({"isPartial":!omitted.is_empty(),"omittedFields":omitted,"canDownloadArchive":omitted.is_empty()});
    Ok(value)
}
pub(in crate::engine) fn history(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    events: Vec<Value>,
    keyword: &str,
) -> Result<Vec<Value>> {
    let mut request = row.clone();
    request["attachments"] = json!(children(tx, row, "oa-attachment", 0, 20)?.1);
    let partial = project(tx, actor, row, request)?["materialAccess"]["isPartial"] == true;
    if partial && !keyword.is_empty() {
        return Err(error(403, "当前材料权限不支持按详细历史内容检索。"));
    }
    events
        .into_iter()
        .map(|mut event| {
            if event["approvalPlan"].is_object() {
                let mut policy_row = row.clone();
                policy_row["approvalPlan"] = event["approvalPlan"].clone();
                let plan = project(
                    tx,
                    actor,
                    &policy_row,
                    json!({"approvalPlan":event["approvalPlan"]}),
                )?;
                event["approvalPlan"] = plan["approvalPlan"].clone();
            }
            if partial {
                event["note"] = json!("详细意见按材料权限保留。");
                redact_notes(&mut event);
            }
            Ok(event)
        })
        .collect()
}
pub(in crate::engine) fn authorize_edit(tx: &Connection, actor: &Actor, row: &Value) -> Result<()> {
    if project(tx, actor, row, row.clone())?["materialAccess"]["isPartial"] == true {
        return Err(error(403, "当前只能查看部分材料，不能改写完整申请正文。"));
    }
    Ok(())
}
pub(super) fn snapshot(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    mut snapshot: Value,
) -> Result<Value> {
    let mut policy_row = row.clone();
    policy_row["approvalPlan"] = snapshot["approvalPlan"].clone();
    let mut request = snapshot["request"].clone();
    request["approvalPlan"] = snapshot["approvalPlan"].clone();
    let projected = project(tx, actor, &policy_row, request)?;
    snapshot["approvalPlan"] = projected["approvalPlan"].clone();
    snapshot["materialAccess"] = projected["materialAccess"].clone();
    snapshot["request"] = projected;
    if snapshot["materialAccess"]["isPartial"] == true {
        snapshot["materialProjectionNotice"] = json!(
            "本审批单按当前材料权限展示，部分材料未包含。完整原件须由具备完整材料权限的人员核验。"
        );
    }
    Ok(snapshot)
}
pub(super) fn full(tx: &Connection, actor: &Actor, row: &Value, snapshot: &Value) -> Result<bool> {
    Ok(self::snapshot(tx, actor, row, snapshot.clone())?["materialAccess"]["isPartial"] != true)
}
pub(in crate::engine) fn attachment(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    file: &Value,
) -> Result<()> {
    let restricted = private_access(tx, actor, row)?;
    let policy = policies(tx, actor, row, restricted)?;
    if !allowed("attachments", &policy)
        || file["classification"] == "Restricted"
            && (!restricted || !policy.iter().any(|p| p.allow_restricted_attachments))
    {
        return Err(error(403, "当前身份或节点没有此原件的材料权限。"));
    }
    Ok(())
}
pub(super) fn validate_policy(policy: &MaterialPolicy) -> Result<()> {
    policy.validate().map_err(invalid)?;
    if policy
        .visible_fields
        .iter()
        .chain(&policy.hidden_fields)
        .any(|path| SYSTEM.contains(&path.split('.').next().unwrap_or("")))
    {
        return Err(invalid(
            "流程身份、状态和操作元数据不能作为可隐藏材料字段。",
        ));
    }
    Ok(())
}
