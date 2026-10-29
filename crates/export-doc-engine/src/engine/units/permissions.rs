//! Unit roles reuse the shared permission scheme resolver and retain delegation provenance.
use super::super::account_permissions::{self, Profile};
use super::*;

pub(super) fn profile(tx: &Connection, row: &Value) -> Result<Profile> {
    let mut profile = account_permissions::profile(tx, row)?;
    profile.direct =
        export_doc_domain::units::validate_grants(&profile.direct).map_err(|e| error(403, e))?;
    if row["mode"] == "Collaboration"
        && profile.direct.iter().any(|g| {
            !super::super::oa::approval::resource_for_collaboration(&g.resource_key)
                || !matches!(g.action.as_str(), "view" | "approve")
        })
    {
        return Err(error(403, "协作方案包含非审批操作，请重新配置授权。"));
    }
    if let Some(id) = row["authorityRoleId"].as_i64() {
        let authority = store::get(tx, KIND, id)?;
        let issuer = store::get(
            tx,
            "users",
            authority["userId"]
                .as_i64()
                .ok_or_else(|| unavailable("授权来源账号缺失。"))?,
        )?;
        let parent = account_permissions::profile(tx, &authority)?;
        if issuer["isActive"] != true
            || authority["mode"] != "Administration"
            || authority["management"]["roles"] != true
            || !window(&authority)?
            || !covers(&authority, &text(row, "companyCode"), &tree(tx)?)
            || !export_doc_domain::units::within_ceiling(&profile.direct, &parent.direct)
        {
            return Err(error(
                403,
                "此身份超过当前授权来源的范围或权限上限，请由单位管理员重新授权。",
            ));
        }
        // A management ceiling may close a module as well as remove operations.
        profile.disabled.extend(parent.disabled);
        profile
            .disabled
            .extend(account_permissions::modules(&issuer)?);
        profile.assigned_only |= parent.assigned_only || issuer["oaAssignedOnly"] == true;
    }
    Ok(profile)
}

pub(in crate::engine) fn validate_scheme_users(
    tx: &Connection,
    actor: &Actor,
    template_id: i64,
) -> Result<()> {
    let mut users = std::collections::BTreeSet::new();
    for user in store::matching(
        tx,
        "users",
        &[("permissionTemplateId", template_id.to_string())],
        &[],
    )? {
        if user["permissionGrants"].is_null() {
            if let Some(id) = user["id"].as_i64() {
                users.insert(id);
            }
        }
    }
    for role in store::matching(
        tx,
        KIND,
        &[("permissionTemplateId", template_id.to_string())],
        &[],
    )? {
        if let Some(id) = role["userId"].as_i64() {
            users.insert(id);
        }
    }
    for user_id in users {
        validate_user(tx, actor, &store::get(tx, "users", user_id)?)?;
        invalidate_user(tx, actor, user_id)?;
    }
    Ok(())
}

pub(super) fn validate_dependents(tx: &Connection, actor: &Actor, authority_id: i64) -> Result<()> {
    for role in store::matching(
        tx,
        KIND,
        &[("authorityRoleId", authority_id.to_string())],
        &[],
    )? {
        let id = role["userId"]
            .as_i64()
            .ok_or_else(|| unavailable("授权账号引用损坏。"))?;
        validate_user(tx, actor, &store::get(tx, "users", id)?)?;
        invalidate_user(tx, actor, id)?;
    }
    Ok(())
}

pub(in crate::engine) fn validate_issuer(
    tx: &Connection,
    actor: &Actor,
    user_id: i64,
) -> Result<()> {
    for authority in records(tx, user_id)?
        .iter()
        .filter(|r| r["mode"] == "Administration")
    {
        validate_dependents(
            tx,
            actor,
            authority["id"]
                .as_i64()
                .ok_or_else(|| unavailable("授权编号损坏。"))?,
        )?;
    }
    Ok(())
}

pub(super) fn invalidate_user(tx: &Connection, actor: &Actor, id: i64) -> Result<()> {
    let mut user = store::get(tx, "users", id)?;
    user["expectedVersion"] = user["versionNumber"].clone();
    let scope = user.clone();
    let username = text(&user, "username");
    store::save_in_scope(
        tx,
        "users",
        id,
        user,
        Some(username),
        actor,
        "unit-permissions-changed",
        Some(&scope),
    )?;
    Ok(())
}
