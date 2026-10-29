use super::super::{error::conflict, store::Store};
use super::*;

pub(in crate::engine) fn directory(
    store: &Store,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx,actor)?;
        let target = query.iter().find(|(k,_)| *k == "companyCode").map(|(_,v)| v.as_str()).unwrap_or(&actor.company);
        demand(&actor,target,"organization.roles")?;
        company(tx,target)?;
        let mut items = store::matching(tx,KIND,&[("companyCode",target.into())],&[])?;
        for item in &mut items { item["userName"] = json!(text(&store::get(tx,"users",item["userId"].as_i64().unwrap_or(0))?,"fullName")); }
        let mut users = vec![];
        for user in store::matching(tx,"users",&[("isActive","true".into())],&[("id","id"),("fullName","fullName"),("username","username"),("companyScope","companyScope"),("role","role")])? {
            if user["role"] == "Admin" { continue; }
            if !actor.admin && user["companyScope"] != target {
                let person = super::super::personnel_accounts::person_for_account(tx,user["id"].as_i64().unwrap_or(0))?;
                if !person.as_ref().map(|p| super::super::personnel_organization::departments_in(tx,p,target)).transpose()?.is_some_and(|d| !d.is_empty()) { continue; }
            }
            let mut appointments = vec![];
            if let Some(person) = super::super::personnel_accounts::person_for_account(tx,user["id"].as_i64().unwrap_or(0))? {
                let mut names = super::super::organization::DepartmentNames::default();
                for department in super::super::personnel_organization::departments_in(tx,&person,target)? {
                    let appointment = super::super::personnel_organization::appointment_in(tx,&person,target,&department)?;
                    appointments.push(json!({"id":appointment.id,"departmentId":department,"departmentName":names.get(tx,target,&department)?,"jobTitle":appointment.job_title}));
                }
            }
            users.push(json!({"id":user["id"],"name":format!("{} · {}",text(&user,"fullName"),text(&user,"username")),"companyCode":user["companyScope"],"appointments":appointments}));
        }
        let mut resources = crate::contracts::contract()["permissions"]["resources"].as_array().ok_or_else(|| unavailable("权限目录缺失。"))?.clone();
        resources.retain(|r| r["isTechnical"] != true && r["supportsDataScope"] == true && !text(r,"key").starts_with("system.") && !text(r,"key").starts_with("organization."));
        for resource in &mut resources { for action in resource["actions"].as_array_mut().into_iter().flatten() { action["presetLevel"] = action["navigationAccessLevel"].clone(); } }
        let ceiling = if actor.admin { Value::Null } else {
            let id = actor.context.strip_prefix("unit:").and_then(|v| v.split_once(':')).and_then(|(id,_)| id.parse::<i64>().ok()).ok_or_else(|| error(403,"单位管理身份无效。"))?;
            store::get(tx,KIND,id)?["permissionGrants"].clone()
        };
        let mut schemes = vec![];
        for template in store::all(tx,"permission-templates")? {
            if template["isActive"] != true { continue; }
            if super::super::permission_templates::validate_owner(&template,&company(tx,target)?).is_err() { continue; }
            let grants = super::super::permission_templates::direct_grants(&template)?;
            if export_doc_domain::units::validate_grants(&grants).is_err() { continue; }
            if let Some(ceiling) = ceiling.as_array() {
                let ceiling: Vec<Grant> = serde_json::from_value(json!(ceiling))?;
                if !export_doc_domain::units::within_ceiling(&grants,&ceiling) { continue; }
            }
            schemes.push(super::super::permission_templates::project(template)?);
        }
        Ok(json!({"items":items,"users":users,"companies":companies(tx,&actor)?,"resources":resources,"grantCeiling":ceiling,"permissionTemplates":schemes}))
    })
}

pub(in crate::engine) fn approvers(store: &Store, actor: &Actor) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx,actor)?;
        demand(&actor,&actor.company,"organization.workflows")?;
        let mut users = vec![];
        for mut user in store::matching(tx,"users",&[("isActive","true".into())],&[("id","id"),("fullName","fullName"),("username","username"),("departmentId","departmentId")])? {
            let id = user["id"].as_i64().ok_or_else(|| unavailable("账号编号损坏。"))?;
            match approval_actor(tx,id,actor.edition,&actor.company) {
                Ok(candidate) => {
                    let options = contexts(tx,&candidate)?.into_iter().filter(|c| c["companyCode"] == actor.company && c["mode"] != "Administration").map(|c| json!({"id":c["id"],"name":c["name"],"departmentId":c["departmentId"]})).collect::<Vec<_>>();
                    user["contexts"] = json!(options);
                    users.push(user);
                },
                Err(e) if matches!(e.status,Some(400|403|404)) => {},
                Err(e) => return Err(e),
            }
        }
        Ok(json!({"users":users}))
    })
}

pub(in crate::engine) fn save(store: &Store, actor: &Actor, input: Value) -> Result<Value> {
    store.transaction_as(actor, |tx, actor| {
        let mut body = crate::contracts::dto(crate::contracts::schema("UnitAccessSave"), input);
        let id = body["id"].as_i64().unwrap_or(0);
        let user_id = super::super::records::positive(&body, "userId", "账号")?;
        let unit = text(&body, "companyCode");
        demand(actor, &unit, "organization.roles")?;
        let unit_record = company(tx, &unit)?;
        let user = store::get(tx, "users", user_id)?;
        let activating = body["isActive"] == true;
        if user["role"] == "Admin" || (activating && user["isActive"] != true) {
            return Err(invalid("启用单位授权须选择启用的普通账号。"));
        }
        let previous = if id > 0 {
            Some(store::get(tx, KIND, id)?)
        } else {
            None
        };
        if let Some(previous) = &previous {
            store::check_version(previous, store::expected(&body))?;
            if previous["companyCode"] != unit
                || previous["userId"] != user_id
                || previous["mode"] != body["mode"]
            {
                return Err(invalid(
                    "已有授权的单位、账号与身份类型不能修改，请结束后另建。",
                ));
            }
            if activating
                && previous["mode"] == "Business"
                && previous["appointmentId"] != body["appointmentId"]
            {
                return Err(invalid(
                    "已有身份不能改绑其他任职，请先交接职责并结束旧身份，再建立新授权。",
                ));
            }
        }
        super::super::records::required(&body, "name", "单位身份名称", 120)?;
        if !body["permissionGrants"].is_null() {
            body["permissionTemplateId"] = Value::Null;
        }
        if body["mode"] == "Administration" && body["permissionGrants"].is_null() {
            return Err(invalid("管理授权的下放上限须独立配置。"));
        }
        if let Some(template_id) = body["permissionTemplateId"].as_i64() {
            let template = store::get(tx, "permission-templates", template_id)?;
            super::super::permission_templates::validate_owner(&template, &unit_record)?;
            if template["isActive"] != true && activating {
                return Err(invalid("不能启用使用已停用方案的身份。"));
            }
        }
        let profile = super::super::account_permissions::profile(tx, &body)?;
        let grants = profile.direct;
        let grants = export_doc_domain::units::validate_grants(&grants).map_err(invalid)?;
        if !body["permissionGrants"].is_null() {
            body["permissionGrants"] = serde_json::to_value(&grants)?;
        }
        body["authorityRoleId"] = Value::Null;
        let disabled: Vec<String> = serde_json::from_value(body["disabledModules"].clone())
            .map_err(|_| invalid("关闭模块格式无效。"))?;
        body["disabledModules"] =
            json!(permissions::profile::disabled_modules(&disabled).map_err(invalid)?);
        let management: Management = serde_json::from_value(body["management"].clone())
            .map_err(|_| invalid("管理能力格式无效。"))?;
        let parse = |key| -> Result<Option<chrono::DateTime<chrono::FixedOffset>>> {
            let text = text(&body, key);
            if text.is_empty() {
                return Ok(None);
            }
            chrono::DateTime::parse_from_rfc3339(&text)
                .map(Some)
                .map_err(|_| invalid("有效期须为包含时区的时间。"))
        };
        if let (Some(start), Some(end)) = (parse("startsAt")?, parse("endsAt")?) {
            if end <= start {
                return Err(invalid("授权结束时间须晚于开始时间。"));
            }
        }
        let mode = text(&body, "mode");
        if mode == "Administration" {
            if !actor.admin || !management.any() {
                return Err(error(
                    403,
                    "分级管理授权由系统管理员配置，且须选择管理能力。",
                ));
            }
            body["departmentId"] = json!("");
            body["appointmentId"] = json!("");
        } else {
            if management.any()
                || body["includeDescendants"] == true
                || unit_record["unitKind"] == "Group"
            {
                return Err(invalid(
                    "业务身份不能含管理转授权或下级单位范围，须选择经营单位。",
                ));
            }
            let person = super::super::personnel_accounts::person_for_account(tx, user_id)?;
            if activating
                && !person
                    .as_ref()
                    .is_some_and(|p| matches!(p["status"].as_str(), Some("Active" | "Probation")))
            {
                return Err(invalid("启用授权须关联在职人员。"));
            }
            if mode == "Business" {
                if activating {
                    let appointment = super::super::personnel_organization::appointment_by_id(
                        tx,
                        person.as_ref().unwrap(),
                        &unit,
                        &text(&body, "appointmentId"),
                    )?;
                    body["departmentId"] = json!(appointment.department_id);
                } else if let Some(previous) = &previous {
                    body["appointmentId"] = previous["appointmentId"].clone();
                    body["departmentId"] = previous["departmentId"].clone();
                }
            } else if mode == "Collaboration" {
                if text(&body, "endsAt").is_empty() {
                    return Err(invalid("跨单位协作须明确结束时间。"));
                }
                if grants.iter().any(|g| {
                    !super::super::oa::approval::resource_for_collaboration(&g.resource_key)
                        || !matches!(g.action.as_str(), "view" | "approve")
                }) {
                    return Err(invalid(
                        "协作身份只授予 OA 查看和任务审批，其他工作请建立正式任职。",
                    ));
                }
                body["departmentId"] = json!("");
                body["appointmentId"] = json!("");
                body["oaAssignedOnly"] = json!(true);
            } else {
                return Err(invalid("身份类型无效。"));
            }
            if !actor.admin {
                if user_id == actor.id {
                    return Err(error(403, "不能修改本人单位授权。"));
                }
                if activating
                    && person
                        .as_ref()
                        .map(|p| super::super::personnel_organization::departments_in(tx, p, &unit))
                        .transpose()?
                        .is_none_or(|d| d.is_empty())
                {
                    return Err(error(403, "跨单位外部协作由系统管理员授权。"));
                }
                let role_id = actor
                    .context
                    .strip_prefix("unit:")
                    .and_then(|v| v.split_once(':'))
                    .and_then(|(v, _)| v.parse::<i64>().ok())
                    .ok_or_else(|| error(403, "单位管理身份无效。"))?;
                let scope = store::get(tx, KIND, role_id)?;
                let ceiling: Vec<Grant> =
                    serde_json::from_value(scope["permissionGrants"].clone())?;
                if !export_doc_domain::units::within_ceiling(&grants, &ceiling) {
                    return Err(error(403, "请求权限超过本单位可下放的授权上限。"));
                }
                body["authorityRoleId"] = json!(role_id);
            }
        }
        if id == 0
            && tx.record_exists(
                KIND,
                &[
                    ("userId", user_id.to_string()),
                    ("companyCode", unit.clone()),
                    ("name", text(&body, "name")),
                    ("isActive", "true".into()),
                ],
            )?
        {
            return Err(conflict("此账号在单位中已有同名身份。"));
        }
        body["companyScope"] = json!(unit);
        body["ownerUserId"] = json!(user_id);
        let scope = body.clone();
        let mut saved = store::save_in_scope(
            tx,
            KIND,
            id,
            body,
            None,
            actor,
            "unit-authorization",
            Some(&scope),
        )?;
        validate_user(tx, actor, &user)?;
        super::role_permissions::validate_dependents(
            tx,
            actor,
            saved["id"]
                .as_i64()
                .ok_or_else(|| unavailable("授权编号缺失。"))?,
        )?;
        super::role_permissions::invalidate_user(tx, actor, user_id)?;
        saved["userName"] = json!(text(&user, "fullName"));
        Ok(saved)
    })
}
