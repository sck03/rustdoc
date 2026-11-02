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
    "applicantAppointmentId",
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
    "workflowTasks",
    "fulfillmentState",
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
    plan: Option<&export_doc_domain::approval::ApprovalPlan>,
) -> Result<Vec<MaterialPolicy>> {
    let mut current = vec![];
    let mut history = vec![];
    let mut participant = false;
    if row["ownerUserId"] == actor.id && text(row, "_requesterContext") == actor.context {
        return Ok(vec![MaterialPolicy {
            allow_restricted_attachments: restricted,
            ..Default::default()
        }]);
    }
    if let Some(plan) = plan {
        let resource = approval::resource(&text(row, "kind"))
            .ok_or_else(|| super::super::error::unavailable("申请材料类型损坏。"))?;
        let principals = approval::object_approvers(tx, actor, resource, row)?;
        let principal_people = super::super::personnel_accounts::links(
            tx,
            &principals.iter().map(|p| p.user_id).collect::<Vec<_>>(),
        )?;
        let actor_person = super::super::personnel_accounts::person_for_account(tx, actor.id)?
            .and_then(|p| p["id"].as_i64());
        let active = plan.current_indices();
        let actionable = approval::ReviewSnapshot::read(tx, actor, row)?.task_keys();
        for (index, step) in plan.steps.iter().enumerate() {
            if step.approver_user_id == actor.id
                && step.approver_context == actor.context
                && step.approver_person_id == actor_person
                || step.acted_by_user_id == Some(actor.id)
                    && step.acted_by_context == actor.context
                    && step.acted_by_person_id == actor_person
                || actionable.contains(&step.task_key)
                || step.status == "Waiting"
                    && principals.iter().any(|p| {
                        p.user_id == step.approver_user_id
                            && p.context == step.approver_context
                            && principal_people
                                .get(&p.user_id)
                                .and_then(|person| person["id"].as_i64())
                                == step.approver_person_id
                    })
            {
                participant = true;
                let node = &plan.nodes[step.stage];
                if step.acted_by_user_id != Some(actor.id)
                    && !super::actor_binding::origin_available(
                        tx,
                        row,
                        step.approver_person_id
                            .ok_or_else(|| super::super::error::unavailable("任务人员缺失。"))?,
                        &step.origin,
                    )?
                {
                    continue;
                }
                if active.contains(&index) && !node.started_at.is_empty() {
                    current.push(node.material_policy.clone());
                } else if !node.started_at.is_empty() {
                    history.push(node.material_policy.clone());
                }
            }
        }
        for node in &plan.nodes {
            for binding in node.actors.iter().chain(
                node.escalation_targets
                    .iter()
                    .take(usize::from(node.escalation_cursor))
                    .flatten(),
            ) {
                if super::fulfillment::identity_matches(tx, actor, binding)? {
                    participant = true;
                    if !super::fulfillment::binding_available(tx, actor, row, binding)? {
                        continue;
                    }
                    if node.state == export_doc_domain::approval_graph::NodeState::Active
                        && !node.started_at.is_empty()
                    {
                        current.push(node.material_policy.clone());
                    } else if !node.started_at.is_empty() {
                        history.push(node.material_policy.clone());
                    }
                }
            }
        }
    }
    if !current.is_empty() {
        return Ok(current);
    }
    if !history.is_empty() || participant {
        // A future assignment grants progress access, but no material policy.
        // Past participation cannot widen a currently active node's policy.
        return Ok(history);
    }
    Ok(vec![MaterialPolicy {
        allow_restricted_attachments: restricted,
        ..Default::default()
    }])
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
    if let Some(nodes) = value
        .get_mut("approvalPlan")
        .and_then(|p| p.get_mut("nodes"))
        .and_then(Value::as_array_mut)
    {
        for node in nodes {
            if node["execution"].is_object() {
                node["execution"]["note"] = json!("详细办理记录按材料权限保留。");
            }
        }
    }
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
    value: Value,
) -> Result<Value> {
    let plan = approval::plan(row)?;
    project_with_plan(tx, actor, row, value, plan.as_ref())
}
pub(in crate::engine) fn project_with_plan(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    mut value: Value,
    plan: Option<&export_doc_domain::approval::ApprovalPlan>,
) -> Result<Value> {
    let restricted = private_access(tx, actor, row)?;
    let policies = policies(tx, actor, row, restricted, plan)?;
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
        if !restricted {
            if let Some(nodes) = plan.get_mut("nodes").and_then(Value::as_array_mut) {
                for (index, node) in nodes.iter_mut().enumerate() {
                    let fields = node["materialPolicy"]["verificationFields"]
                        .as_array()
                        .cloned()
                        .unwrap_or_default();
                    for field in fields.into_iter().filter(|f| f["restricted"] == true) {
                        if let Some(values) =
                            node["execution"]["verificationValues"].as_object_mut()
                        {
                            let key = field["key"].as_str().unwrap_or("");
                            if values.remove(key).is_some() {
                                omitted.push(format!(
                                    "approvalPlan.nodes.{index}.execution.verificationValues.{key}"
                                ));
                            }
                        }
                    }
                }
            }
            let subflows = plan
                .get("subflows")
                .and_then(Value::as_array)
                .cloned()
                .unwrap_or_default();
            if let Some(progress) = plan
                .get_mut("subflowProgress")
                .and_then(Value::as_object_mut)
            {
                for child in &subflows {
                    let key = child["key"].as_str().unwrap_or("");
                    for mapping in child["outputs"].as_array().into_iter().flatten() {
                        let private = nodes
                            .iter()
                            .find(|n| n["key"] == mapping["nodeKey"])
                            .and_then(|n| n["materialPolicy"]["verificationFields"].as_array())
                            .is_some_and(|fields| {
                                fields.iter().any(|f| {
                                    f["key"] == mapping["verificationKey"]
                                        && f["restricted"] == true
                                })
                            });
                        if private {
                            if let Some(values) = progress
                                .get_mut(key)
                                .and_then(|p| p["outputs"].as_object_mut())
                            {
                                let target = mapping["target"].as_str().unwrap_or("");
                                if values.remove(target).is_some() {
                                    omitted.push(format!(
                                        "approvalPlan.subflowProgress.{key}.outputs.{target}"
                                    ));
                                }
                            }
                        }
                    }
                }
            }
        }
        if let Some(subflows) = plan.get_mut("subflows").and_then(Value::as_array_mut) {
            for child in subflows {
                let input = child["inputSnapshot"]
                    .as_object_mut()
                    .ok_or_else(|| super::super::error::unavailable("子流程输入快照缺失。"))?;
                let sources = input
                    .remove("sources")
                    .ok_or_else(|| super::super::error::unavailable("子流程输入权限来源缺失。"))?;
                for field in [
                    "amount",
                    "currency",
                    "duration",
                    "department",
                    "category",
                    "personnelAction",
                    "returnable",
                ] {
                    let paths = sources[field]
                        .as_array()
                        .filter(|paths| !paths.is_empty())
                        .ok_or_else(|| {
                            super::super::error::unavailable("子流程输入权限来源损坏。")
                        })?;
                    let visible = paths
                        .iter()
                        .all(|path| path.as_str().is_some_and(|path| allowed(path, &policies)));
                    if !visible && input.remove(field).is_some() {
                        omitted.push(format!("approvalPlan.subflows.inputSnapshot.{field}"));
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
    let mut request = snapshot["request"].clone();
    request["approvalPlan"] = snapshot["approvalPlan"].clone();
    let projected = project(tx, actor, row, request)?;
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
    let plan = approval::plan(row)?;
    let policy = policies(tx, actor, row, restricted, plan.as_ref())?;
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
