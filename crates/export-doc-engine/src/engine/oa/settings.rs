//! Company-owned approval rules; settings changes never rewrite submitted plans.
use super::*;
use export_doc_domain::{approval::delegation_window, approval_policy::ApprovalRule};
use std::collections::HashSet;

pub(super) const STORAGE_KIND: &str = "oa-approval-settings";

pub(in crate::engine) fn revoke_delegations(
    tx: &Connection,
    actor: &Actor,
    account: i64,
) -> Result<()> {
    super::settings_store::revoke_delegations(tx, actor, account)
}
fn response(tx: &Connection, company: &str, mut settings: Value) -> Result<Value> {
    if !settings["revisions"].is_object() {
        settings["revisions"] = json!({});
    }
    settings["sheetFieldCatalog"] = sheet_templates::catalog();
    settings["sheetDefaultContent"] = json!(sheet_templates::default_content()?);
    settings["activations"] = json!(tx.workflow_activation_list(company, None)?);
    Ok(contracts::dto(
        contracts::schema("OaApprovalSettings"),
        settings,
    ))
}
fn default_rule(kind: &str, variant: &str, name: &str) -> Value {
    let key = format!(
        "{kind}{}",
        if variant.is_empty() {
            String::new()
        } else {
            format!(":{variant}")
        }
    );
    json!({"key":key,"kind":kind,"variant":variant,"name":name,"notifyUserIds":[],"stages":[
        {"key":"department","name":"部门负责人审批","dependsOn":[],"source":"DepartmentManager","approverUserIds":[],"decision":"All","selfApproval":"Skip","repeatedApprover":"Allow","conditions":[],"dueHours":48},
        {"key":"executive","name":"公司领导审批","dependsOn":["department"],"source":"CompanyExecutive","approverUserIds":[],"decision":"All","selfApproval":"Reject","repeatedApprover":"Allow","conditions":[],"dueHours":48}
    ]})
}
pub(super) fn defaults() -> Result<Vec<Value>> {
    let mut rules: Vec<_> = [
        ("leave", "请假审批"),
        ("overtime", "加班审批"),
        ("expense", "费用报销审批"),
        ("travel", "出差审批"),
        ("purchase", "采购审批"),
        ("general", "通用事项审批"),
        ("room", "会议室预约审批"),
        ("supply", "物品领用审批"),
    ]
    .iter()
    .map(|(kind, name)| default_rule(kind, "", name))
    .collect();
    rules.extend(
        [
            ("Hire", "入职审批"),
            ("Confirm", "转正审批"),
            ("Transfer", "调岗审批"),
            ("Depart", "离职审批"),
            ("Rehire", "返聘审批"),
        ]
        .iter()
        .map(|(action, name)| default_rule("personnel", action, name)),
    );
    for rule in &mut rules {
        let leaves = vec!["executive".to_owned()];
        let kind = text(rule, "kind");
        let additions = super::business_nodes::defaults(&kind, &leaves)?;
        let stages = rule["stages"]
            .as_array_mut()
            .ok_or_else(|| invalid("默认流程节点损坏。"))?;
        for node in additions {
            stages.push(serde_json::to_value(node)?);
        }
    }
    Ok(rules)
}
pub(in crate::engine) fn load(tx: &Connection, company: &str) -> Result<Value> {
    super::settings_store::load(tx, company)
}
fn account(tx: &Connection, actor: &Actor, id: i64) -> Result<Actor> {
    let user = super::super::units::approval_actor(tx, id, actor.edition, &actor.company)?;
    if user.company != actor.company {
        return Err(invalid("审批人和代理人必须是本公司的启用账号。"));
    }
    Ok(user)
}
pub(super) fn handle(
    tx: &Connection,
    actor: &Actor,
    action: &str,
    body: &Value,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
) -> Result<Value> {
    super::super::units::demand(actor, &actor.company, "organization.workflows")?;
    if action == "settings-exceptions" {
        let (page, size) = paging(query)?;
        return Ok(tx.workflow_exceptions(&actor.company, (page - 1) * size, size)?);
    }
    if action == "settings-retry-system" {
        return super::exception_operations::retry(tx, actor, body);
    }
    let mut previous = load(tx, &actor.company)?;
    if matches!(
        action,
        "settings-versions" | "settings-diff" | "settings-routing-sources"
    ) {
        return super::definition_queries::read(tx, actor, action, query);
    }
    if action == "settings-cancel-activation" {
        let id = parameters
            .iter()
            .find(|(k, _)| *k == "activationId")
            .and_then(|(_, v)| v.parse::<i64>().ok())
            .filter(|id| *id > 0)
            .ok_or_else(|| invalid("调度编号无效。"))?;
        if super::super::command_receipts::prepare(
            tx,
            actor,
            &mut previous,
            &format!("CancelOaActivation:{id}"),
            body,
        )? {
            return response(tx, &actor.company, previous);
        }
        let entry = tx
            .workflow_activation_list(&actor.company, None)?
            .into_iter()
            .find(|e| e["id"] == id.to_string())
            .ok_or_else(|| error(404, "没有可取消的发布调度。"))?;
        if entry["definitionKey"] != body["definitionKey"]
            || !tx.workflow_activation_state(id, store::expected(body), "Cancelled", "")?
        {
            return Err(conflict("发布调度已变化，请刷新后重试。"));
        }
        let receipt = previous
            .as_object_mut()
            .and_then(|r| r.remove(super::super::command_receipts::FIELD))
            .ok_or_else(|| super::super::error::unavailable("取消调度回执缺失。"))?;
        tx.put_command_receipt(&receipt, &previous)?;
        return response(tx, &actor.company, previous);
    }
    if action == "settings-schedule" {
        let key = text(body, "commandKey");
        super::super::command_receipts::validate_key(&key)?;
        if let Some(receipt) = tx.workflow_activation_receipt(actor.id, &actor.context, &key)? {
            let mut request = body.clone();
            request.sort_all_objects();
            if receipt["companyCode"] != actor.company
                || receipt["requestDigest"]
                    != super::super::media::digest(&serde_json::to_vec(&request)?)
            {
                return Err(conflict("同一调度操作编号不能用于不同发布内容。"));
            }
            return response(tx, &actor.company, previous);
        }
    }
    if previous.get("sheetTemplates").is_none() {
        previous["sheetTemplates"] = sheet_templates::defaults()?;
    }
    if action == "settings-get" {
        return response(tx, &actor.company, previous);
    }
    let patch = body;
    let patched = if action == "settings-save" {
        Some(super::settings_patch::merge(&previous, body)?)
    } else {
        None
    };
    let body = patched.as_ref().unwrap_or(body);
    if action == "settings-publish" {
        if previous["id"].as_i64().is_none_or(|id| id <= 0) {
            return Err(conflict("请先保存流程草稿，再通过样例仿真发布。"));
        }
        if super::super::command_receipts::prepare(
            tx,
            actor,
            &mut previous,
            "PublishOaDefinition",
            body,
        )? {
            return response(tx, &actor.company, load(tx, &actor.company)?);
        }
    }
    if previous["versionNumber"] == 0 {
        if store::expected(body) != 0 {
            return Err(conflict("审批设置版本已变化，请重新读取。"));
        }
    } else {
        store::check_version(&previous, store::expected(body))?;
    }
    if matches!(
        action,
        "settings-publish" | "settings-disable" | "settings-schedule"
    ) {
        let revision_key = format!("rule:{}", text(body, "definitionKey"));
        if body["expectedRevision"].as_i64()
            != Some(super::settings_patch::revision(&previous, &revision_key))
        {
            return Err(conflict("当前流程草稿已改变，请重新仿真后发布。"));
        }
        super::definitions::change(tx, actor, &mut previous, action, body)?;
        if action != "settings-disable" {
            super::settings_store::save(
                tx,
                actor,
                &json!({"section":"rules","definitionKey":body["definitionKey"],"expectedRevision":body["expectedRevision"]}),
                &previous,
            )?;
        }
        let saved = super::settings_store::save_registry(tx, actor, previous, action)?;
        return response(tx, &actor.company, saved);
    }
    let rules = body["rules"]
        .as_array()
        .filter(|r| r.len() >= 13)
        .ok_or_else(|| invalid("须保留内置流程。"))?;
    for builtin in defaults()? {
        if !rules
            .iter()
            .any(|r| r["key"] == builtin["key"] && r["form"].is_null())
        {
            return Err(invalid("不能删除内置流程或替换其业务类型。"));
        }
    }
    let mut calendar = body
        .get("calendar")
        .filter(|c| !c.is_null())
        .cloned()
        .unwrap_or_else(|| previous["calendar"].clone());
    if calendar != previous["calendar"] {
        calendar["version"] = json!(previous["calendar"]["version"].as_u64().unwrap_or(0) + 1);
    }
    serde_json::from_value::<export_doc_domain::business_calendar::BusinessCalendar>(
        calendar.clone(),
    )
    .map_err(|_| invalid("公司工作日历格式无效。"))?
    .validate()
    .map_err(invalid)?;
    let mut kinds = HashSet::new();
    for rule in rules {
        let kind = text(rule, "kind");
        let typed: ApprovalRule =
            serde_json::from_value(rule.clone()).map_err(|_| invalid("审批规则格式无效。"))?;
        if typed.key
            != format!(
                "{}{}",
                typed.kind,
                if typed.variant.is_empty() {
                    String::new()
                } else {
                    format!(":{}", typed.variant)
                }
            )
            || !kinds.insert(typed.key.clone())
        {
            return Err(invalid("流程编号与业务类型不一致或重复。"));
        }
        typed.validate_draft().map_err(invalid)?;
        if previous["rules"]
            .as_array()
            .is_some_and(|items| items.iter().any(|old| old == rule))
        {
            continue;
        }
        let resource = super::approval::resource(&kind).ok_or_else(|| invalid("申请类型无效。"))?;
        for stage in &typed.stages {
            let mut requirements = stage
                .approver_user_ids
                .iter()
                .map(|id| (*id, super::actor_binding::action(stage)))
                .collect::<Vec<_>>();
            for escalation in &stage.escalations {
                if let Some(target) = &escalation.target {
                    requirements.extend(target.user_ids.iter().map(|id|(*id,if escalation.action==export_doc_domain::approval_graph::EscalationAction::NotifySupervisor{"view"}else{super::actor_binding::action(stage)})));
                }
            }
            for (id, action) in requirements {
                let reviewer = super::super::units::for_operation(
                    tx,
                    id,
                    actor.edition,
                    &actor.company,
                    resource,
                    action,
                )?;
                auth::authorize(&reviewer, resource, "view")?;
                auth::authorize(&reviewer, resource, action)?;
                if stage.kind == export_doc_domain::approval_graph::NodeKind::Approval {
                    super::qualification::person_id(tx, &reviewer)?;
                }
            }
        }
    }
    let delegations = body["delegations"]
        .as_array()
        .ok_or_else(|| invalid("代理记录格式无效。"))?;
    let mut keys = HashSet::new();
    let mut windows = vec![];
    for delegation in delegations {
        required(delegation, "key", "代理编号", 100)?;
        let key = text(delegation, "key");
        if !keys.insert(key) {
            return Err(invalid("代理编号重复。"));
        }
        let principal = records::positive(delegation, "principalUserId", "原审批人")?;
        let delegate = records::positive(delegation, "delegateUserId", "代理人")?;
        if principal == delegate {
            return Err(invalid("不能代理本人。"));
        }
        let (start, end) =
            delegation_window(&text(delegation, "startsAt"), &text(delegation, "endsAt"))
                .map_err(invalid)?;
        if delegation["isActive"] == true {
            if previous["delegations"] != body["delegations"] {
                account(tx, actor, principal)?;
                account(tx, actor, delegate)?;
            }
            if windows
                .iter()
                .any(|(p, a, b)| *p == principal && start < *b && *a < end)
            {
                return Err(invalid("同一审批人的代理有效期不能重叠。"));
            }
            windows.push((principal, start, end));
        }
    }
    let mut value = json!({"rules":body["rules"],"delegations":body["delegations"],"expectedVersion":body["expectedVersion"]});
    value["calendar"] = calendar;
    value["publications"] = previous["publications"].clone();
    value["sheetTemplates"] = body
        .get("sheetTemplates")
        .cloned()
        .unwrap_or_else(|| previous["sheetTemplates"].clone());
    if value["sheetTemplates"] != previous["sheetTemplates"] {
        sheet_templates::validate(&value["sheetTemplates"])?;
    }
    let mut services = body.get("handlingServices").cloned().unwrap_or_else(|| {
        previous
            .get("handlingServices")
            .cloned()
            .unwrap_or(json!([]))
    });
    for service in services.as_array_mut().into_iter().flatten() {
        use unicode_normalization::UnicodeNormalization;
        if !service.is_object() {
            return Err(invalid("办理分工必须为对象。"));
        }
        for field in ["key", "name"] {
            service[field] = json!(text(service, field).nfc().collect::<String>());
        }
        service
            .as_object_mut()
            .ok_or_else(|| invalid("办理分工必须为对象。"))?
            .remove("handlerNames");
    }
    if services != previous["handlingServices"] {
        handling::validate(tx, actor, &services, &previous["handlingServices"])?;
    }
    value["handlingServices"] = services;
    value["versionNumber"] = previous["versionNumber"].clone();
    let saved = super::settings_store::save(tx, actor, patch, &value)?;
    super::handling_handover::notify(tx, actor, &previous, &saved)?;
    response(tx, &actor.company, saved)
}
pub(super) fn principals(tx: &Connection, actor: &Actor) -> Result<Vec<(i64, String)>> {
    Ok(active_delegations(tx, &actor.company)?
        .into_iter()
        .filter(|(_, delegate, _)| *delegate == actor.id)
        .map(|(principal, _, key)| (principal, key))
        .collect())
}
pub(in crate::engine) fn delegates_for(
    tx: &Connection,
    company: &str,
    principals: &std::collections::BTreeSet<i64>,
) -> Result<Vec<i64>> {
    Ok(active_delegations(tx, company)?
        .into_iter()
        .filter(|(principal, _, _)| principals.contains(principal))
        .map(|(_, delegate, _)| delegate)
        .collect())
}
fn active_delegations(tx: &Connection, company: &str) -> Result<Vec<(i64, i64, String)>> {
    let delegations = super::settings_store::section(tx, company, "delegations")?;
    let mut result = vec![];
    let now = chrono::Utc::now();
    for d in delegations
        .as_array()
        .ok_or_else(|| super::super::error::unavailable("审批代理设置损坏。"))?
    {
        if d["isActive"] != true {
            continue;
        }
        let (start, end) = delegation_window(&text(d, "startsAt"), &text(d, "endsAt"))
            .map_err(super::super::error::unavailable)?;
        if start <= now && now < end {
            result.push((
                records::positive(d, "principalUserId", "原审批人")?,
                records::positive(d, "delegateUserId", "代理人")?,
                text(d, "key"),
            ));
        }
    }
    Ok(result)
}
