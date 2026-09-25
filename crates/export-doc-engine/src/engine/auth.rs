use super::{
    error::{Result, error, invalid, unavailable},
    records::text,
    store::{self, Actor, Store},
};
use crate::{contracts, paths::nonce};
use chrono::{Duration, Utc};
use export_doc_domain::permissions::{self, Grant};
use serde_json::{Value, json};
use sha2::Sha256;
use std::{collections::HashMap, sync::Mutex, time::Instant};
use subtle::ConstantTimeEq;

pub(super) const ITERATIONS: u32 = 600_000;
pub struct Sessions {
    values: Mutex<HashMap<String, (i64, chrono::DateTime<Utc>, (i64, i64))>>,
    attempts: Mutex<HashMap<String, (u32, Instant)>>,
}
impl Default for Sessions {
    fn default() -> Self {
        Self {
            values: Mutex::new(HashMap::new()),
            attempts: Mutex::new(HashMap::new()),
        }
    }
}

pub fn seed(store: &Store) -> Result<()> {
    if store.provider()? != "SQLite" {
        return Err(unavailable("团队模式不能创建空密码账号。"));
    }
    seed_admin(store, "")
}

fn seed_admin(store: &Store, password: &str) -> Result<()> {
    if !store.all("users")?.is_empty() {
        return Ok(());
    }
    let admin = Actor {
        id: 1,
        name: "系统管理员".into(),
        company: "DEFAULT".into(),
        department: "GENERAL".into(),
        admin: true,
        grants: vec![],
    };
    let (salt, hash) = password_hash(password)?;
    store.transaction(|transaction| {
        if !store::all(transaction, "users")?.is_empty() {
            return Ok(());
        }
        let body = contracts::overlay(contracts::initial(contracts::schema("ApiUserAccountDto")), &json!({"username":"admin","fullName":"系统管理员","role":"Admin","isActive":true,"companyScope":"DEFAULT","departmentId":"GENERAL"}));
        let saved = store::save(transaction,"users",0,body,Some("admin".into()),&admin,"create")?;
        transaction.set_credential(saved["id"].as_i64().unwrap_or(0),&salt,&hash,ITERATIONS)?;
        store::save(transaction,"companies",0,json!({"code":"DEFAULT","name":"本公司","isActive":true}),Some("DEFAULT".into()),&admin,"create")?;
        store::save(transaction,"departments",0,json!({"code":"GENERAL","name":"综合部","companyCode":"DEFAULT","parentCode":null,"managerEmployeeId":null,"isActive":true}),Some("GENERAL".into()),&admin,"create")?;
        for role in contracts::contract()["permissions"]["roles"].as_array().ok_or_else(|| unavailable("权限角色目录缺失。"))? {
            let mut template = contracts::overlay(contracts::initial(contracts::schema("ApiPermissionTemplateDto")), role);
            template["isActive"] = json!(true);
            template["isSystem"] = json!(true);
            store::save(transaction, "permission-templates", 0, template, Some(role["code"].as_str().unwrap_or("").into()), &admin, "create")?;
        }
        Ok(())
    })
}

pub fn bootstrap(
    store: &Store,
    username: &str,
    password: &str,
    expected: &str,
    submitted: &str,
) -> Result<()> {
    if store.provider()? == "SQLite" || !store.all("users")?.is_empty() {
        return Ok(());
    }
    if expected.len() < 32 || expected.as_bytes().ct_eq(submitted.as_bytes()).unwrap_u8() != 1 {
        return Err(error(401, "首次管理员初始化需要有效的一次性部署令牌。"));
    }
    if store::normalize(username) != "admin" || !(8..=128).contains(&password.chars().count()) {
        return Err(invalid("首次管理员须使用 admin 和 8–128 字密码。"));
    }
    seed_admin(store, password)
}

pub(super) fn password_hash(password: &str) -> Result<(Vec<u8>, Vec<u8>)> {
    let mut salt = vec![0; 32];
    getrandom::fill(&mut salt).map_err(|_| unavailable("安全随机数不可用。"))?;
    let hash =
        pbkdf2::pbkdf2_hmac_array::<Sha256, 32>(password.as_bytes(), &salt, ITERATIONS).to_vec();
    Ok((salt, hash))
}

impl Sessions {
    pub fn clear(&self) -> Result<()> {
        self.values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?
            .clear();
        Ok(())
    }
    pub fn login(
        &self,
        store: &Store,
        clock: &crate::clock::BusinessClock,
        username: &str,
        password: &str,
    ) -> Result<Value> {
        let key = store::normalize(username);
        if key.is_empty() || key.len() > 160 || password.len() > 512 {
            return Err(invalid("账号或密码长度无效。"));
        }
        {
            let mut attempts = self
                .attempts
                .lock()
                .map_err(|_| unavailable("登录状态异常。"))?;
            attempts.retain(|_, (_, last)| last.elapsed().as_secs() < 300);
            if attempts.get(&key).is_some_and(|(count, _)| *count >= 10) {
                return Err(error(429, "登录尝试过多，请五分钟后重试。"));
            }
            if attempts.len() >= 1024 && !attempts.contains_key(&key) {
                return Err(error(429, "登录请求过多，请稍后重试。"));
            }
        }
        let record = store
            .all("users")?
            .into_iter()
            .find(|user| store::normalize(user["username"].as_str().unwrap_or("")) == key);
        let credentials = if let Some(record) = &record {
            store
                .connection()?
                .credential(record["id"].as_i64().unwrap_or(0))?
                .map(|c| (c.salt, c.hash, c.iterations))
        } else {
            None
        };
        let (salt, expected, rounds) =
            credentials.unwrap_or((vec![0; 32], vec![0; 32], ITERATIONS));
        if !(ITERATIONS..=2_000_000).contains(&rounds) {
            return Err(unavailable("账号凭证参数损坏。"));
        }
        let actual = pbkdf2::pbkdf2_hmac_array::<Sha256, 32>(password.as_bytes(), &salt, rounds);
        if actual.as_slice().ct_eq(&expected).unwrap_u8() != 1
            || record.as_ref().is_none_or(|user| user["isActive"] != true)
        {
            let mut attempts = self
                .attempts
                .lock()
                .map_err(|_| unavailable("登录状态异常。"))?;
            let attempt = attempts.entry(key).or_insert((0, Instant::now()));
            attempt.0 += 1;
            attempt.1 = Instant::now();
            return Err(error(401, "账号或密码错误，或账号已停用。"));
        }
        self.attempts
            .lock()
            .map_err(|_| unavailable("登录状态异常。"))?
            .remove(&key);
        let record = record.ok_or_else(|| error(401, "登录失败。"))?;
        let token = nonce().map_err(unavailable)? + &nonce().map_err(unavailable)?;
        self.values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?
            .insert(
                token.clone(),
                (
                    record["id"].as_i64().unwrap_or(0),
                    Utc::now() + Duration::hours(12),
                    session_version(store, &record)?,
                ),
            );
        Ok(
            json!({"accessToken":token,"tokenType":"Bearer","expiresAt":(Utc::now()+Duration::hours(12)).to_rfc3339(),"expiresAtUtc":(Utc::now()+Duration::hours(12)).to_rfc3339(),"user":user_dto(store,&record,clock)?}),
        )
    }
    pub fn actor(&self, store: &Store, token: &str) -> Result<Actor> {
        let (id, version) = {
            let mut values = self
                .values
                .lock()
                .map_err(|_| unavailable("会话状态异常。"))?;
            values.retain(|_, (_, expires, _)| *expires > Utc::now());
            values
                .get(token)
                .map(|(id, _, version)| (*id, *version))
                .ok_or_else(|| error(401, "会话已过期，请重新登录。"))?
        };
        let user = store.get("users", id)?;
        if user["isActive"] != true {
            return Err(error(401, "账号已停用。"));
        }
        if session_version(store, &user)? != version {
            self.logout(token)?;
            return Err(error(401, "账号或权限已更新，请重新登录。"));
        }
        actor_from(store, &user)
    }
    pub fn logout(&self, token: &str) -> Result<()> {
        self.values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?
            .remove(token);
        Ok(())
    }

    pub fn renew(
        &self,
        store: &Store,
        token: &str,
        clock: &crate::clock::BusinessClock,
    ) -> Result<Value> {
        let actor = self.actor(store, token)?;
        let replacement = nonce().map_err(unavailable)? + &nonce().map_err(unavailable)?;
        let expires = Utc::now() + Duration::hours(12);
        let user = user_dto(store, &store.get("users", actor.id)?, clock)?;
        let mut values = self
            .values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?;
        if values.remove(token).is_none() {
            return Err(error(401, "会话已撤销，请重新登录。"));
        }
        values.insert(
            replacement.clone(),
            (
                actor.id,
                expires,
                session_version(store, &store.get("users", actor.id)?)?,
            ),
        );
        Ok(
            json!({"accessToken":replacement,"tokenType":"Bearer","expiresAt":expires.to_rfc3339(),"user":user}),
        )
    }
}

fn session_version(store: &Store, user: &Value) -> Result<(i64, i64)> {
    let template = match user["permissionTemplateId"].as_i64().filter(|id| *id > 0) {
        Some(id) => store
            .connection()?
            .get("permission-templates", id)?
            .and_then(|item| item["versionNumber"].as_i64())
            .unwrap_or(0),
        None => 0,
    };
    Ok((user["versionNumber"].as_i64().unwrap_or(0), template))
}

fn actor_from(store: &Store, user: &Value) -> Result<Actor> {
    actor_from_connection(&*store.connection()?, user)
}

fn actor_from_connection(
    connection: &export_doc_storage::Connection,
    user: &Value,
) -> Result<Actor> {
    let grants = if let Some(template) = user["permissionTemplateId"].as_i64().filter(|id| *id > 0)
    {
        let template = connection.get("permission-templates", template)?;
        if let Some(template) = template.filter(|template| template["isActive"] == true) {
            let grants: Vec<Grant> = serde_json::from_value(template["grants"].clone())?;
            permissions::effective(&grants).map_err(unavailable)?
        } else {
            vec![]
        }
    } else {
        permissions::role_grants(user["role"].as_str().unwrap_or("")).map_err(unavailable)?
    };
    Ok(Actor {
        id: user["id"].as_i64().unwrap_or(0),
        name: user["fullName"].as_str().unwrap_or("").into(),
        company: user["companyScope"].as_str().unwrap_or("").into(),
        department: user["departmentId"].as_str().unwrap_or("").into(),
        admin: user["role"] == "Admin",
        grants: grants
            .into_iter()
            .map(|grant| serde_json::to_value(grant).expect("permission grant"))
            .collect(),
    })
}

pub fn current_actor(store: &Store, id: i64) -> Result<Actor> {
    current_actor_in(&*store.connection()?, id)
}

pub fn current_actor_in(connection: &export_doc_storage::Connection, id: i64) -> Result<Actor> {
    let user = store::get(connection, "users", id)?;
    if user["isActive"] != true {
        return Err(error(403, "账号已停用。"));
    }
    actor_from_connection(connection, &user)
}

pub fn user_dto(
    store: &Store,
    record: &Value,
    clock: &crate::clock::BusinessClock,
) -> Result<Value> {
    let actor = actor_from(store, record)?;
    let mut user = contracts::overlay(contracts::initial(contracts::schema("ApiUserDto")), record);
    let now = clock.now().map_err(unavailable)?;
    user["businessDate"] = json!(now.today.to_string());
    user["businessTimeZone"] = json!(now.time_zone);
    user["businessDateValidUntilUtc"] = json!(now.valid_until.to_rfc3339());
    let grants: Vec<Grant> = serde_json::from_value(json!(actor.grants))?;
    let modules = permissions::module_access(&grants);
    let enabled: Vec<_> = modules.keys().cloned().collect();
    let access: Vec<_> = modules
        .iter()
        .map(|(module, level)| json!({"moduleKey":module,"accessLevel":level}))
        .collect();
    user["capabilities"] = json!({"canManageSettings":actor.admin,"canManageUsers":actor.admin,"canViewAllBusinessData":actor.admin,"canUseDocumentWorkspace":enabled.iter().any(|module|module.starts_with("document.")),"canUseSalesWorkspace":enabled.iter().any(|module|module.starts_with("sales.")),"productEdition":"Full","enabledModules":enabled,"moduleAccess":access,"permissions":actor.grants,"usesOfficeRegister":store.provider()? == "SQLite","availableFeatures":["worklist","business-attachments"]});
    Ok(user)
}

pub fn authorize(actor: &Actor, resource: &str, action: &str) -> Result<()> {
    let action = permissions::service_action(resource, action);
    if actor.admin
        || (!resource.starts_with("system.")
            && actor
                .grants
                .iter()
                .any(|grant| grant["resourceKey"] == resource && grant["action"] == action))
    {
        Ok(())
    } else {
        Err(error(403, "当前账号没有此操作权限。"))
    }
}
pub fn visible(actor: &Actor, resource: &str, action: &str, record: &Value) -> bool {
    if actor.admin {
        return true;
    }
    let action = permissions::service_action(resource, action);
    actor
        .grants
        .iter()
        .filter(|grant| grant["resourceKey"] == resource && grant["action"] == action)
        .any(|grant| {
            let scope = grant["dataScope"].as_str().unwrap_or("");
            match scope {
                "all" => true,
                "company" => record["companyScope"] == actor.company,
                "department" => {
                    record["companyScope"] == actor.company
                        && record["departmentId"] == actor.department
                }
                "own" => {
                    record["companyScope"] == actor.company && record["ownerUserId"] == actor.id
                }
                _ => false,
            }
        })
}

pub fn authorize_operation(
    actor: &Actor,
    operation: crate::generated_api::Operation,
    query: &[(&str, String)],
) -> Result<()> {
    let policy = &contracts::contract()["operations"][operation.id]["policy"];
    if policy.is_null() {
        return Err(unavailable("API 缺少授权元数据。"));
    }
    let grants: Vec<Grant> = serde_json::from_value(json!(actor.grants))?;
    if permissions::allows_operation(&grants, actor.admin, operation, query) {
        Ok(())
    } else {
        Err(error(403, "当前账号没有此功能权限。"))
    }
}
pub fn operation_action(
    operation: crate::generated_api::Operation,
    resource: &str,
    scope_action: &'static str,
) -> Result<&'static str> {
    let policy = &contracts::contract()["operations"][operation.id]["policy"];
    let requirements = policy["requirements"]
        .as_array()
        .ok_or_else(|| unavailable("业务动作缺少生成的权限元数据。"))?;
    // Current endpoints also inherit module policies. authorize_operation checks
    // that policy; the use case action determines the record's data scope.
    if requirements.is_empty() && policy["permissions"].is_object() {
        return Ok(scope_action);
    }
    requirements
        .iter()
        .find(|item| item["resourceKey"] == resource)
        .and_then(|requirement| requirement["action"].as_str())
        .ok_or_else(|| unavailable("业务动作缺少生成的权限元数据。"))
}

pub fn template_visible(actor: &Actor, permission: &str, template: &Value) -> bool {
    if actor.admin {
        return true;
    }
    if authorize(actor, permission, "view").is_err() {
        return false;
    }
    if template["ownerUserId"] == actor.id {
        return true;
    }
    if template["status"] != "Published"
        || !actor.grants.iter().any(|grant| {
            grant["resourceKey"] == permission
                && grant["action"] == "view"
                && ["department", "company", "all"].contains(&text(grant, "dataScope").as_str())
        })
    {
        return false;
    }
    match text(template, "shareScope").as_str() {
        "All" => true,
        "Company" => !actor.company.is_empty() && template["companyScope"] == actor.company,
        "Department" => {
            !actor.company.is_empty()
                && !actor.department.is_empty()
                && template["companyScope"] == actor.company
                && template["departmentId"] == actor.department
        }
        _ => false,
    }
}
