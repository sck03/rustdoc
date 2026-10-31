//! Company-owned approval rules; settings changes never rewrite submitted plans.
use super::*;
use export_doc_domain::{approval::delegation_window, approval_policy::ApprovalRule};
use std::collections::HashSet;

const STORAGE_KIND: &str = "oa-approval-settings";

pub(in crate::engine) fn revoke_delegations(
    tx: &Connection,
    actor: &Actor,
    account: i64,
) -> Result<()> {
    for mut settings in store::all(tx, STORAGE_KIND)? {
        let mut changed = false;
        for delegation in settings["delegations"].as_array_mut().into_iter().flatten() {
            if delegation["isActive"] == true
                && (delegation["principalUserId"] == account
                    || delegation["delegateUserId"] == account)
            {
                delegation["isActive"] = json!(false);
                changed = true;
            }
        }
        if changed {
            let id = records::positive(&settings, "id", "流程配置")?;
            settings["expectedVersion"] = settings["versionNumber"].clone();
            let scope = settings.clone();
            let identity = text(&settings, "companyScope");
            store::save_in_scope(
                tx,
                STORAGE_KIND,
                id,
                settings,
                Some(identity),
                actor,
                "revoke-delegations",
                Some(&scope),
            )?;
        }
    }
    Ok(())
}
fn response(mut settings: Value) -> Result<Value> {
    settings["sheetFieldCatalog"] = sheet_templates::catalog();
    settings["sheetDefaultContent"] = json!(sheet_templates::default_content()?);
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
fn defaults() -> Vec<Value> {
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
    rules
}
pub(in crate::engine) fn load(tx: &Connection, company: &str) -> Result<Value> {
    let mut value = tx
        .find_identity(STORAGE_KIND, &store::normalize(company))?
        .unwrap_or_else(
            || json!({"versionNumber":0,"rules":defaults(),"publications":[],"delegations":[]}),
        );
    if value["calendar"].is_null() {
        let mut calendar = export_doc_domain::business_calendar::BusinessCalendar::default();
        calendar.time_zone = tx
            .clock
            .now()
            .map_err(super::super::error::unavailable)?
            .time_zone
            .into();
        value["calendar"] = serde_json::to_value(calendar)?;
    }
    Ok(value)
}
fn account(tx: &Connection, actor: &Actor, id: i64) -> Result<Actor> {
    let user = super::super::units::approval_actor(tx, id, actor.edition, &actor.company)?;
    if user.company != actor.company {
        return Err(invalid("审批人和代理人必须是本公司的启用账号。"));
    }
    Ok(user)
}
pub(super) fn handle(tx: &Connection, actor: &Actor, action: &str, body: &Value) -> Result<Value> {
    super::super::units::demand(actor, &actor.company, "organization.workflows")?;
    let mut previous = load(tx, &actor.company)?;
    if previous.get("sheetTemplates").is_none() {
        previous["sheetTemplates"] = sheet_templates::defaults()?;
    }
    if action == "settings-get" {
        return response(previous);
    }
    if previous["versionNumber"] == 0 {
        if store::expected(body) != 0 {
            return Err(conflict("审批设置版本已变化，请重新读取。"));
        }
    } else {
        store::check_version(&previous, store::expected(body))?;
    }
    if matches!(action, "settings-publish" | "settings-disable") {
        super::definitions::change(tx, actor, &mut previous, action, body)?;
        let id = previous["id"].as_i64().unwrap_or(0);
        let saved = store::save(
            tx,
            STORAGE_KIND,
            id,
            previous,
            Some(store::normalize(&actor.company)),
            actor,
            action,
        )?;
        return response(saved);
    }
    let rules = body["rules"]
        .as_array()
        .filter(|r| r.len() == 13)
        .ok_or_else(|| invalid("须配置全部申请类型的审批规则。"))?;
    let mut calendar = body
        .get("calendar")
        .filter(|c| !c.is_null())
        .cloned()
        .unwrap_or_else(|| previous["calendar"].clone());
    calendar["version"] = json!(previous["versionNumber"].as_u64().unwrap_or(0) + 1);
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
        for id in typed.account_ids() {
            let resource =
                super::approval::resource(&kind).ok_or_else(|| invalid("申请类型无效。"))?;
            let reviewer = super::super::units::for_operation(
                tx,
                id,
                actor.edition,
                &actor.company,
                resource,
                "approve",
            )?;
            auth::authorize(&reviewer, resource, "view")?;
            auth::authorize(&reviewer, resource, "approve")?;
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
    value["calendar"] = calendar;
    value["publications"] = previous["publications"].clone();
    value["sheetTemplates"] = body
        .get("sheetTemplates")
        .cloned()
        .unwrap_or_else(|| previous["sheetTemplates"].clone());
    sheet_templates::validate(&value["sheetTemplates"])?;
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
    response(saved)
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
