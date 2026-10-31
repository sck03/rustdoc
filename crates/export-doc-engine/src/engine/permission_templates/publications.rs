//! Publication and application are separate transactions over immutable grants.
use super::super::error::{error, unavailable};
use super::super::{
    auth, command_receipts,
    store::{Actor, Store},
    units,
};
use super::*;
use export_doc_domain::identity::StableId;
pub(in crate::engine) fn handle(
    store: &Store,
    actor: &Actor,
    meta: &Value,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let action = text(meta, "action");
    let scheme = if matches!(action.as_str(), "versions" | "version") {
        query
            .iter()
            .find(|(k, _)| *k == "schemeId")
            .map(|(_, v)| v.as_str())
            .unwrap_or("")
    } else {
        body["schemeId"].as_str().unwrap_or("")
    };
    let id = StableId::parse(scheme).map_err(invalid)?.number();
    if action == "preview" {
        return store.preview_as(actor, |tx, actor| {
            let draft = store::get(tx, "permission-templates", id)?;
            demand(tx, actor, &draft)?;
            let mut result = application(tx, actor, id, body, true)?;
            result["applied"] = json!(false);
            for target in result["targets"].as_array_mut().into_iter().flatten() {
                let key = target["id"].clone();
                let kind = target["kind"].clone();
                target["versionNumber"] = body["targets"]
                    .as_array()
                    .and_then(|items| {
                        items
                            .iter()
                            .find(|item| item["id"] == key && item["kind"] == kind)
                    })
                    .map(|item| item["expectedVersion"].clone())
                    .unwrap_or(Value::Null);
            }
            Ok(result)
        });
    }
    if matches!(action.as_str(), "versions" | "version") {
        return store.read(|tx| {
            let actor = auth::refresh_actor_in(tx, actor)?;
            let draft = store::get(tx, "permission-templates", id)?;
            demand(tx, &actor, &draft)?;
            if action == "version" {
                let version = query
                    .iter()
                    .find(|(k, _)| *k == "version")
                    .and_then(|(_, v)| v.parse::<i64>().ok())
                    .filter(|v| *v > 0)
                    .ok_or_else(|| invalid("发布版本无效。"))?;
                return project_with_usage(
                    tx,
                    tx.permission_scheme_version(id, version)?
                        .ok_or_else(|| error(404, "发布版本不存在。"))?,
                );
            }
            if action == "versions" {
                let page = query
                    .iter()
                    .find(|(k, _)| *k == "pageNumber")
                    .map(|(_, v)| v.parse::<i64>())
                    .transpose()
                    .map_err(|_| invalid("页码无效。"))?
                    .unwrap_or(1)
                    .max(1);
                let size = query
                    .iter()
                    .find(|(k, _)| *k == "pageSize")
                    .map(|(_, v)| v.parse::<i64>())
                    .transpose()
                    .map_err(|_| invalid("页码无效。"))?
                    .unwrap_or(20)
                    .clamp(1, 100);
                return Ok(tx.permission_scheme_versions(
                    id,
                    (page - 1).saturating_mul(size),
                    size,
                )?);
            }
            application(tx, &actor, id, body, false)
        });
    }
    required(body, "note", "操作原因", 500)?;
    store.transaction_as(actor,|tx,actor| {
        let mut draft=store::get(tx,"permission-templates",id)?;demand(tx,actor,&draft)?;
        if action=="apply"{authorize_targets(tx,actor,id,body)?;}
        if command_receipts::prepare(tx,actor,&mut draft,&format!("permission-{action}"),body)? {
            return tx.command_receipt(actor.id,&actor.context,&text(body,"commandKey"))?.map(|r|r["result"].clone()).ok_or_else(||unavailable("操作回执缺失。"));
        }
        let result=if action=="publish" {
            store::check_version(&draft,store::expected(body))?;
            let time=store::timestamp();let version=tx.publish_permission_scheme(id,&draft,actor.id,&time)?;
            let username=text(&draft,"code");let scope=draft.clone();
            // Advancing the draft version makes a lost publish response safe to retry.
            draft["expectedVersion"]=draft["versionNumber"].clone();
            let mut saved_draft=draft.clone();saved_draft.as_object_mut().map(|v|v.remove(command_receipts::FIELD));
            let saved=store::save_in_scope(tx,"permission-templates",id,saved_draft,Some(username),actor,"publish",Some(&scope))?;
            json!({"schemeId":id.to_string(),"version":version,"versionNumber":saved["versionNumber"],"publishedAt":time})
        }else if action=="apply" {application(tx,actor,id,body,true)?}else{return Err(invalid("未知权限版本操作。"));};
        if !draft[command_receipts::FIELD].is_null(){tx.put_command_receipt(&draft[command_receipts::FIELD],&result)?;}
        Ok(result)
    })
}
fn demand(tx: &Connection, actor: &Actor, draft: &Value) -> Result<()> {
    if let Some(unit) = draft["ownerUnitId"].as_i64() {
        let unit = store::get(tx, "companies", unit)?;
        units::demand(actor, &text(&unit, "code"), "organization.roles")?;
    } else if !actor.admin {
        return Err(error(403, "平台权限方案由平台权限管理员维护。"));
    }
    Ok(())
}
fn targets(body: &Value) -> Result<Vec<(String, i64, i64)>> {
    let entries = body["targets"]
        .as_array()
        .filter(|v| !v.is_empty() && v.len() <= 200)
        .ok_or_else(|| invalid("请选择 1–200 个明确的身份。"))?;
    let mut seen = std::collections::HashSet::new();
    entries
        .iter()
        .map(|entry| {
            let kind = text(entry, "kind");
            let id = StableId::parse(entry["id"].as_str().unwrap_or(""))
                .map_err(invalid)?
                .number();
            let version = entry["expectedVersion"]
                .as_i64()
                .filter(|v| *v > 0)
                .ok_or_else(|| invalid("身份版本缺失。"))?;
            if !matches!(kind.as_str(), "Account" | "Unit") || !seen.insert((kind.clone(), id)) {
                return Err(invalid("身份类型无效或重复。"));
            }
            Ok((kind, id, version))
        })
        .collect()
}
fn authorize_targets(tx: &Connection, actor: &Actor, scheme: i64, body: &Value) -> Result<()> {
    for (kind, id, _) in targets(body)? {
        let target = store::get(
            tx,
            if kind == "Account" {
                "users"
            } else {
                "unit-access"
            },
            id,
        )?;
        if target["permissionTemplateId"] != scheme || !target["permissionGrants"].is_null() {
            return Err(conflict(
                "只可明确应用到已经绑定此方案的身份；独立授权不会被覆盖。",
            ));
        }
        if kind == "Account" {
            if !actor.admin || target["role"] == "Admin" {
                return Err(error(
                    403,
                    "主账号方案由平台管理员分配，管理员账号不参与普通方案应用。",
                ));
            }
        } else {
            units::demand(actor, &text(&target, "companyCode"), "organization.roles")?;
            if target["mode"] == "Administration" {
                return Err(error(403, "管理转授权上限须独立维护。"));
            }
        }
    }
    Ok(())
}
fn application(
    tx: &Connection,
    actor: &Actor,
    scheme: i64,
    body: &Value,
    apply: bool,
) -> Result<Value> {
    authorize_targets(tx, actor, scheme, body)?;
    let version = body["version"]
        .as_i64()
        .filter(|v| *v > 0)
        .ok_or_else(|| invalid("请选择已发布版本。"))?;
    let publication = tx
        .permission_scheme_version(scheme, version)?
        .ok_or_else(|| invalid("发布版本不存在。"))?;
    let grants = direct_grants(&publication)?;
    let mut changes = vec![];
    let mut affected = std::collections::BTreeMap::new();
    for (kind, id, expected) in targets(body)? {
        let table = if kind == "Account" {
            "users"
        } else {
            "unit-access"
        };
        let mut target = store::get(tx, table, id)?;
        store::check_version(&target, expected)?;
        let old = super::super::account_permissions::profile(tx, &target)?.direct;
        let keys = |grants: &[Grant]| {
            grants
                .iter()
                .map(|g| format!("{}:{}:{}", g.resource_key, g.action, g.data_scope))
                .collect::<std::collections::BTreeSet<_>>()
        };
        let old_keys = keys(&old);
        let new_keys = keys(&grants);
        let from = target["permissionSchemeVersion"].clone();
        target["permissionSchemeVersion"] = json!(version);
        target["expectedVersion"] = json!(expected);
        if kind == "Unit" {
            let company = tx
                .find_identity(
                    "companies",
                    &store::normalize(&text(&target, "companyCode")),
                )?
                .ok_or_else(|| unavailable("单位不存在。"))?;
            validate_owner(&publication, &company)?;
            export_doc_domain::units::validate_grants(&grants).map_err(invalid)?;
        }
        if apply {
            let identity = if kind == "Account" {
                Some(text(&target, "username"))
            } else {
                None
            };
            let scope = target.clone();
            target = store::save_in_scope(
                tx,
                table,
                id,
                target,
                identity,
                actor,
                "permission-version-apply",
                Some(&scope),
            )?;
            let user = if kind == "Account" {
                id
            } else {
                target["userId"]
                    .as_i64()
                    .ok_or_else(|| unavailable("身份账号引用损坏。"))?
            };
            affected
                .entry(user)
                .and_modify(|touch| *touch |= kind == "Unit")
                .or_insert(kind == "Unit");
        }
        changes.push(json!({"kind":kind,"id":id.to_string(),"versionNumber":if apply{expected+1}else{expected},"fromVersion":from,"toVersion":version,"addedActions":new_keys.difference(&old_keys).cloned().collect::<Vec<_>>(),"removedActions":old_keys.difference(&new_keys).cloned().collect::<Vec<_>>()}));
    }
    if apply {
        for (id, touch) in affected {
            let user = store::get(tx, "users", id)?;
            units::validate_user(tx, actor, &user)?;
            units::validate_issuer(tx, actor, id)?;
            if touch {
                units::invalidate_user(tx, actor, id)?;
            }
        }
        for change in &mut changes {
            let table = if change["kind"] == "Account" {
                "users"
            } else {
                "unit-access"
            };
            let id = StableId::parse(change["id"].as_str().unwrap_or(""))
                .map_err(unavailable)?
                .number();
            change["versionNumber"] = store::get(tx, table, id)?["versionNumber"].clone();
        }
    }
    Ok(json!({"schemeId":scheme.to_string(),"version":version,"targets":changes,"applied":apply}))
}
