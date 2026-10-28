//! Company-owned approval rules; settings changes never rewrite submitted plans.
use super::*;
use export_doc_domain::{approval::delegation_window, approval_policy::ApprovalRule};
use std::collections::HashSet;

const STORAGE_KIND: &str = "oa-approval-settings";
fn default_rule(kind: &str) -> Value {
    json!({"kind":kind,"mode":"Configured","approverUserIds":[],"stages":[
        {"source":"DepartmentManager","approverUserIds":[]},
        {"source":"CompanyExecutive","approverUserIds":[]}
    ]})
}
fn personnel_actions(value: &Value) -> std::result::Result<(), &'static str> {
    let actions = value
        .as_array()
        .filter(|a| a.len() <= 5)
        .ok_or("人员审批开关无效。")?;
    let mut seen = HashSet::new();
    for action in actions {
        let action = action.as_str().ok_or("人员变动类型无效。")?;
        if !["Hire", "Confirm", "Transfer", "Depart", "Rehire"].contains(&action)
            || !seen.insert(action)
        {
            return Err("人员审批类型无效或重复。");
        }
    }
    Ok(())
}
pub(in crate::engine) fn load(tx: &Connection, company: &str) -> Result<Value> {
    let mut value = tx.find_identity(STORAGE_KIND, &store::normalize(company))?.unwrap_or_else(|| {
        json!({"versionNumber":0,"rules":KINDS.iter().map(|kind| default_rule(kind.trim_start_matches("oa-"))).collect::<Vec<_>>(),"delegations":[]})
    });
    let rules = value["rules"]
        .as_array_mut()
        .ok_or_else(|| super::super::error::unavailable("审批规则损坏。"))?;
    if !rules.iter().any(|rule| rule["kind"] == "personnel") {
        rules.push(default_rule("personnel"));
    }
    if value.get("personnelApprovalActions").is_none() {
        value["personnelApprovalActions"] = json!([]);
    }
    personnel_actions(&value["personnelApprovalActions"])
        .map_err(super::super::error::unavailable)?;
    Ok(value)
}
fn account(tx: &Connection, actor: &Actor, id: i64) -> Result<Actor> {
    let user = auth::current_actor_in(tx, id, actor.edition)?;
    if user.company != actor.company {
        return Err(invalid("审批人和代理人必须是本公司的启用账号。"));
    }
    Ok(user)
}
pub(super) fn handle(tx: &Connection, actor: &Actor, action: &str, body: &Value) -> Result<Value> {
    if !actor.admin {
        return Err(error(403, "只有管理员可以设置审批规则和代理。"));
    }
    let mut previous = load(tx, &actor.company)?;
    if previous.get("sheetTemplates").is_none() {
        previous["sheetTemplates"] = sheet_templates::defaults()?;
    }
    if action == "settings-get" {
        previous["sheetFieldCatalog"] = sheet_templates::catalog();
        return Ok(contracts::dto(
            contracts::schema("OaApprovalSettings"),
            previous,
        ));
    }
    if previous["versionNumber"] == 0 {
        if store::expected(body) != 0 {
            return Err(conflict("审批设置版本已变化，请重新读取。"));
        }
    } else {
        store::check_version(&previous, store::expected(body))?;
    }
    let rules = body["rules"]
        .as_array()
        .filter(|r| r.len() == KINDS.len())
        .ok_or_else(|| invalid("须配置全部申请类型的审批规则。"))?;
    let mut kinds = HashSet::new();
    for rule in rules {
        let kind = text(rule, "kind");
        if !KINDS.contains(&format!("oa-{kind}").as_str()) || !kinds.insert(kind.clone()) {
            return Err(invalid("申请类型缺失或重复。"));
        }
        let typed: ApprovalRule =
            serde_json::from_value(rule.clone()).map_err(|_| invalid("审批规则格式无效。"))?;
        typed.validate().map_err(invalid)?;
        for id in typed.account_ids() {
            let reviewer = account(tx, actor, id)?;
            let resource =
                super::approval::resource(&kind).ok_or_else(|| invalid("申请类型无效。"))?;
            if auth::scope_rank(&reviewer, resource, "approve")
                .min(auth::scope_rank(&reviewer, resource, "view"))
                < 2
            {
                return Err(invalid(
                    "指定审批人须有该类申请的部门或公司查看及审批权限；本人范围不能审批同事申请。",
                ));
            }
        }
    }
    let delegations = body["delegations"]
        .as_array()
        .filter(|d| d.len() <= 50)
        .ok_or_else(|| invalid("代理记录最多 50 条。"))?;
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
            account(tx, actor, principal)?;
            account(tx, actor, delegate)?;
            if windows
                .iter()
                .any(|(p, a, b)| *p == principal && start < *b && *a < end)
            {
                return Err(invalid("同一审批人的代理有效期不能重叠。"));
            }
            windows.push((principal, start, end));
        }
    }
    let mut value = contracts::dto(contracts::schema("OaApprovalSettingsSave"), body.clone());
    value["sheetTemplates"] = body
        .get("sheetTemplates")
        .cloned()
        .unwrap_or_else(|| previous["sheetTemplates"].clone());
    sheet_templates::validate(&value["sheetTemplates"])?;
    let actions = body
        .get("personnelApprovalActions")
        .cloned()
        .unwrap_or_else(|| previous["personnelApprovalActions"].clone());
    personnel_actions(&actions).map_err(invalid)?;
    value["personnelApprovalActions"] = json!(actions);
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
    handling::validate(tx, actor, &services, &previous["handlingServices"])?;
    value["handlingServices"] = services;
    value["versionNumber"] = previous["versionNumber"].clone();
    let saved = store::save(
        tx,
        STORAGE_KIND,
        previous["id"].as_i64().unwrap_or(0),
        value,
        Some(store::normalize(&actor.company)),
        actor,
        "approval-settings",
    )?;
    super::handling_handover::notify(tx, actor, &previous, &saved)?;
    Ok(contracts::dto(contracts::schema("OaApprovalSettings"), {
        let mut saved = saved;
        saved["sheetFieldCatalog"] = sheet_templates::catalog();
        saved
    }))
}
pub(super) fn principals(tx: &Connection, actor: &Actor) -> Result<Vec<(i64, String)>> {
    let settings = load(tx, &actor.company)?;
    let mut result = vec![];
    let now = chrono::Utc::now();
    for d in settings["delegations"]
        .as_array()
        .ok_or_else(|| super::super::error::unavailable("审批代理设置损坏。"))?
    {
        if d["isActive"] != true || d["delegateUserId"] != actor.id {
            continue;
        }
        let (start, end) = delegation_window(&text(d, "startsAt"), &text(d, "endsAt"))
            .map_err(super::super::error::unavailable)?;
        if start <= now && now < end {
            result.push((
                records::positive(d, "principalUserId", "原审批人")?,
                text(d, "key"),
            ));
        }
    }
    Ok(result)
}
