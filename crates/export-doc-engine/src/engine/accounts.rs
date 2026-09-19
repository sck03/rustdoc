//! Account changes, organization assignment and personnel-link constraints.
use super::{
    auth,
    error::{Result, conflict, invalid},
    records::text,
    store::{self, Actor, Store},
};
use crate::contracts;
use export_doc_domain::permissions;
use serde_json::{Value, json};
use unicode_normalization::UnicodeNormalization;

pub fn save(store: &Store, actor: &Actor, id: i64, mut body: Value) -> Result<Value> {
    auth::authorize(actor, "system.users", "manage")?;
    for key in ["username", "fullName", "companyScope", "departmentId"] {
        body[key] = json!(text(&body, key).nfc().collect::<String>());
    }
    let username = text(&body, "username");
    let password = body["resetPassword"].as_str().unwrap_or("");
    if username.is_empty()
        || username.encode_utf16().count() > 80
        || text(&body, "fullName").encode_utf16().count() > 100
    {
        return Err(invalid("账号须为 1–80 个字符，姓名不能超过 100 个字符。"));
    }
    if !permissions::catalog()
        .roles
        .iter()
        .any(|role| body["role"] == role.code)
    {
        return Err(invalid("请选择有效角色。"));
    }
    if (id == 0 || !password.is_empty()) && !(8..=128).contains(&password.chars().count()) {
        return Err(invalid("新账号或重置密码须为 8–128 字。"));
    }
    let credential = if id == 0 || !password.is_empty() {
        Some(auth::password_hash(password)?)
    } else {
        None
    };
    store.transaction(|tx| {
        let previous = if id > 0 {
            Some(store::get(tx, "users", id)?)
        } else {
            None
        };
        if let Some(previous) = &previous {
            store::check_version(previous, store::expected(&body))?;
            if previous["role"] == "Admin"
                && previous["isActive"] == true
                && (body["role"] != "Admin" || body["isActive"] != true)
                && store::all(tx, "users")?
                    .iter()
                    .filter(|user| user["role"] == "Admin" && user["isActive"] == true)
                    .count()
                    <= 1
            {
                return Err(invalid("必须保留至少一个启用的管理员。"));
            }
            if (previous["companyScope"] != body["companyScope"]
                || previous["departmentId"] != body["departmentId"])
                && super::office_queries::account_clearance(tx, previous)?["isClear"] != true
            {
                return Err(conflict("仍有未结清的预约或领用申请，请先完成交接。"));
            }
        }
        super::organization::validate_assignment(
            tx,
            &text(&body, "companyScope"),
            &text(&body, "departmentId"),
        )?;
        if let Some(template_id) = body["permissionTemplateId"].as_i64() {
            if template_id <= 0 {
                return Err(invalid("权限方案编号无效。"));
            }
            let template = tx
                .get("permission-templates", template_id)?
                .ok_or_else(|| invalid("权限方案不存在。"))?;
            if template["isActive"] != true
                && previous
                    .as_ref()
                    .is_none_or(|user| user["permissionTemplateId"] != template_id)
            {
                return Err(invalid("不能新分配已停用的权限方案。"));
            }
        }
        if id > 0 {
            if let Some(person) = store::all(tx, "people")?
                .iter()
                .find(|person| person["account"]["id"] == id)
            {
                if body["role"] == "Admin"
                    || body["fullName"] != person["profile"]["fullName"]
                    || body["departmentId"] != person["departmentId"]
                    || body["companyScope"] != person["companyScope"]
                {
                    return Err(conflict(
                        "已关联人员的姓名和组织须从人员档案维护，不能改为管理员账号。",
                    ));
                }
                if person["status"] == "Departed" && body["isActive"] == true {
                    return Err(conflict("离职人员须先办理返聘，才能重新启用账号。"));
                }
            }
        }
        let saved = store::save(
            tx,
            "users",
            id,
            contracts::overlay(
                contracts::initial(contracts::schema("ApiUserAccountDto")),
                &body,
            ),
            Some(username.clone()),
            actor,
            if id == 0 { "create" } else { "edit" },
        )?;
        if let Some((salt, hash)) = &credential {
            tx.set_credential(
                saved["id"].as_i64().unwrap_or(0),
                salt,
                hash,
                auth::ITERATIONS,
            )?;
        }
        Ok(json!({"success":true,"message":"账号已保存","user":saved}))
    })
}
