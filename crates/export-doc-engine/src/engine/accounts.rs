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
    auth::username_key(&username)?;
    let password = body["resetPassword"].as_str().unwrap_or("");
    if text(&body, "fullName").encode_utf16().count() > 100 {
        return Err(invalid("姓名不能超过 100 个字符。"));
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
    store.transaction_as(actor, |tx, actor| {
        auth::authorize(actor, "system.users", "manage")?;
        let previous = if id > 0 {
            Some(store::get(tx, "users", id)?)
        } else {
            None
        };
        for key in ["permissionGrants", "disabledModules", "oaAssignedOnly"] {
            if !body
                .as_object()
                .is_some_and(|value| value.contains_key(key))
            {
                if let Some(value) = previous.as_ref().and_then(|user| user.get(key)) {
                    body[key] = value.clone();
                }
            }
        }
        super::account_permissions::validate(&mut body)?;
        if body["role"] != "Admin"
            && body["permissionGrants"].is_null()
            && body["permissionTemplateId"].is_null()
        {
            let template = tx
                .find_identity(
                    "permission-templates",
                    &store::normalize(&text(&body, "role")),
                )?
                .ok_or_else(|| invalid("预置权限方案缺失，请明确选择权限方案。"))?;
            body["permissionTemplateId"] = template["id"].clone();
        }
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
                || previous["departmentId"] != body["departmentId"]
                || previous["isActive"] == true && body["isActive"] != true)
                && super::office_queries::account_clearance(tx, previous)?["isClear"] != true
            {
                return Err(conflict(
                    "仍有未结申请、审批、办理职责或业务资料，请先完成交接。",
                ));
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
            if template["ownerUnitId"].as_i64().is_some() {
                return Err(invalid("单位方案请在单位身份中分配；主账号使用平台方案。"));
            }
            if template["isActive"] != true
                && previous
                    .as_ref()
                    .is_none_or(|user| user["permissionTemplateId"] != template_id)
            {
                return Err(invalid("不能新分配已停用的权限方案。"));
            }
        }
        let person = if id > 0 {
            super::personnel_accounts::linked_person(tx, id)?
        } else {
            None
        };
        if let Some(person) = &person {
            if body["role"] == "Admin"
                || body["fullName"] != person["fullName"]
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
            if body["isActive"] != true
                && previous
                    .as_ref()
                    .is_some_and(|user| user["isActive"] == true)
                && super::office_queries::clearance(
                    tx,
                    &super::personnel_accounts::person_for_account(tx, id)?
                        .ok_or_else(|| invalid("人员关联失效。"))?,
                )?["canDepart"]
                    != true
            {
                return Err(conflict(
                    "账号仍承担申请、审批、办理或组织负责人职责，请先交接再停用。",
                ));
            }
        }
        body["organizationDepartments"] = json!(match &person {
            Some(person) if person["status"] != "Departed" =>
                super::personnel_organization::departments(person)?,
            _ => vec![text(&body, "departmentId")],
        });
        let mut saved = store::save(
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
        super::account_permissions::validate_references(tx, actor, &saved)?;
        super::units::validate_issuer(
            tx,
            actor,
            saved["id"]
                .as_i64()
                .ok_or_else(|| invalid("账号编号缺失。"))?,
        )?;
        if let Some(previous) = &previous {
            if previous["companyScope"] != saved["companyScope"] {
                super::oa::approval_references::validate_account_in(
                    tx,
                    actor,
                    &saved,
                    &text(previous, "companyScope"),
                )?;
            }
        }
        if let Some((salt, hash)) = &credential {
            tx.set_credential(
                saved["id"].as_i64().unwrap_or(0),
                salt,
                hash,
                auth::ITERATIONS,
            )?;
        }
        saved["employee"] = person
            .map(super::personnel_accounts::summary)
            .unwrap_or(Value::Null);
        Ok(json!({"success":true,"message":"账号已保存","user":saved}))
    })
}
