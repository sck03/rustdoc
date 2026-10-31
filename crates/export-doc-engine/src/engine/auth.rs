use super::{
    error::{Result, error, invalid, unavailable},
    records::text,
    store::{self, Actor, Store},
};
use crate::{contracts, paths::nonce};
use chrono::{Duration, Utc};
use export_doc_domain::permissions;
use serde_json::{Value, json};
use sha2::Sha256;
use std::{collections::HashMap, sync::Mutex, time::Instant};
use subtle::ConstantTimeEq;
use unicode_normalization::UnicodeNormalization;

pub(super) fn username_key(username: &str) -> Result<String> {
    let username = username.trim().nfc().collect::<String>();
    if username.is_empty() || username.encode_utf16().count() > 80 {
        return Err(invalid("账号须为 1–80 个字符。"));
    }
    Ok(store::normalize(&username))
}

pub(super) const ITERATIONS: u32 = 600_000;
const MAX_USER_SESSIONS: usize = 32;
const MAX_SESSIONS: usize = 4096;
#[derive(Clone, Debug)]
struct Session {
    user_id: i64,
    expires: chrono::DateTime<Utc>,
    version: (i64, i64),
    context: String,
}
pub struct Sessions {
    values: Mutex<HashMap<String, Session>>,
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

fn has_users(connection: &store::Connection) -> Result<bool> {
    Ok(connection
        .query_generic(&export_doc_storage::GenericQuery {
            kind: "users",
            scope: export_doc_storage::DataScope {
                all: true,
                ..Default::default()
            },
            filters: &[],
            keyword: "",
            search_root: None,
            fields: &[("id", "id")],
            offset: 0,
            limit: 1,
        })?
        .0
        > 0)
}

fn seed_admin(store: &Store, password: &str) -> Result<()> {
    if has_users(&store.connection()?)? {
        return Ok(());
    }
    let admin = Actor {
        scope_rules: vec![],
        context: String::new(),
        edition: store.edition,
        id: 1,
        name: "系统管理员".into(),
        company: "DEFAULT".into(),
        department: "GENERAL".into(),
        admin: true,
        grants: vec![],
        departments: vec![],
        oa_assigned_only: false,
    };
    let (salt, hash) = password_hash(password)?;
    store.transaction(|transaction| {
        if has_users(transaction)? {
            return Ok(());
        }
        let body = contracts::overlay(contracts::initial(contracts::schema("ApiUserAccountDto")), &json!({"username":"admin","fullName":"系统管理员","role":"Admin","isActive":true,"companyScope":"DEFAULT","departmentId":"GENERAL"}));
        let saved = store::save(transaction,"users",0,body,Some("admin".into()),&admin,"create")?;
        transaction.set_credential(saved["id"].as_i64().unwrap_or(0),&salt,&hash,ITERATIONS)?;
        store::save(transaction,"companies",0,json!({"code":"DEFAULT","name":"本公司","isActive":true}),Some("DEFAULT".into()),&admin,"create")?;
        store::save(transaction,"departments",0,json!({"code":"GENERAL","name":"综合部","companyCode":"DEFAULT","parentCode":null,"managerEmployeeId":null,"isActive":true}),Some("GENERAL".into()),&admin,"create")?;
        for role in contracts::contract()["permissions"]["roles"].as_array().ok_or_else(|| unavailable("权限角色目录缺失。"))? {
            let mut template = contracts::overlay(contracts::initial(contracts::schema("ApiPermissionTemplateDto")), role);
            template["grants"] = json!(permissions::catalog().roles.iter().find(|r| role["code"] == r.code).ok_or_else(|| unavailable("权限预置目录缺失。"))?.grants);
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
    if store.provider()? == "SQLite" || has_users(&store.connection()?)? {
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
        let key = username_key(username)?;
        if password.chars().count() > 128 {
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
        let record = store.connection()?.find_identity("users", &key)?;
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
        let version = store.transaction(|tx| {
            let current = store::get(tx, "users", record["id"].as_i64().unwrap_or(0))?;
            if current["isActive"] != true || current["versionNumber"] != record["versionNumber"] {
                return Err(error(401, "账号在登录期间发生变化，请重新登录。"));
            }
            session_version_in(tx, &current)
        })?;
        let expires = Utc::now() + Duration::hours(12);
        let user = user_dto(store, &record, clock)?;
        let token = self.issue(record["id"].as_i64().unwrap_or(0), expires, version)?;
        Ok(
            json!({"accessToken":token,"tokenType":"Bearer","expiresAt":expires.to_rfc3339(),"expiresAtUtc":expires.to_rfc3339(),"user":user}),
        )
    }
    fn issue(
        &self,
        user_id: i64,
        expires: chrono::DateTime<Utc>,
        version: (i64, i64),
    ) -> Result<String> {
        let token = nonce().map_err(unavailable)? + &nonce().map_err(unavailable)?;
        let mut values = self
            .values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?;
        values.retain(|_, session| session.expires > Utc::now());
        if values.len() >= MAX_SESSIONS
            || values
                .values()
                .filter(|session| session.user_id == user_id)
                .count()
                >= MAX_USER_SESSIONS
        {
            return Err(error(429, "活跃会话数量已达上限，请退出已有会话后重试。"));
        }
        values.insert(
            token.clone(),
            Session {
                user_id,
                expires,
                version,
                context: String::new(),
            },
        );
        Ok(token)
    }
    pub fn actor(&self, store: &Store, token: &str) -> Result<Actor> {
        let session = self.validated_session(store, token)?;
        store.read(|tx| super::units::resolve(tx, session.user_id, store.edition, &session.context))
    }
    pub fn account_actor(&self, store: &Store, token: &str) -> Result<Actor> {
        let session = self.validated_session(store, token)?;
        current_actor(store, session.user_id)
    }
    fn validated_session(&self, store: &Store, token: &str) -> Result<Session> {
        let session = self.identity(token)?;
        let user = store.get("users", session.user_id)?;
        if user["isActive"] != true {
            self.logout(token)?;
            return Err(error(401, "账号已停用。"));
        }
        if session_version(store, &user)? != session.version {
            self.logout(token)?;
            return Err(error(401, "账号或权限已更新，请重新登录。"));
        }
        Ok(session)
    }
    fn identity(&self, token: &str) -> Result<Session> {
        let mut values = self
            .values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?;
        if values
            .get(token)
            .is_some_and(|session| session.expires <= Utc::now())
        {
            values.remove(token);
        }
        values
            .get(token)
            .cloned()
            .ok_or_else(|| error(401, "会话已过期，请重新登录。"))
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
        self.replace_context(store, token, clock, &actor.context)
    }

    pub fn replace_context(
        &self,
        store: &Store,
        token: &str,
        clock: &crate::clock::BusinessClock,
        context: &str,
    ) -> Result<Value> {
        let original = self.validated_session(store, token)?;
        let actor =
            store.read(|tx| super::units::resolve(tx, original.user_id, store.edition, context))?;
        let record = store.get("users", actor.id)?;
        if session_version(store, &record)? != original.version {
            return Err(error(401, "账号或权限已变化，请重新登录。"));
        }
        let replacement = nonce().map_err(unavailable)? + &nonce().map_err(unavailable)?;
        let expires = Utc::now() + Duration::hours(12);
        let user = user_dto_for(store, &record, clock, &actor)?;
        let mut values = self
            .values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?;
        let previous = values
            .remove(token)
            .ok_or_else(|| error(401, "会话已撤销，请重新登录。"))?;
        values.insert(
            replacement.clone(),
            Session {
                user_id: actor.id,
                expires,
                version: previous.version,
                context: actor.context,
            },
        );
        Ok(
            json!({"accessToken":replacement,"tokenType":"Bearer","expiresAt":expires.to_rfc3339(),"user":user}),
        )
    }
}

#[test]
fn expired_sessions_never_resolve_and_do_not_disturb_other_logins() {
    let sessions = Sessions::default();
    let now = Utc::now();
    sessions.values.lock().unwrap().extend([
        (
            "expired".into(),
            Session {
                user_id: 1,
                expires: now - Duration::seconds(1),
                version: (2, 3),
                context: String::new(),
            },
        ),
        (
            "active".into(),
            Session {
                user_id: 4,
                expires: now + Duration::hours(1),
                version: (5, 6),
                context: String::new(),
            },
        ),
    ]);
    assert_eq!(sessions.identity("expired").unwrap_err().status, Some(401));
    assert_eq!(sessions.identity("missing").unwrap_err().status, Some(401));
    let active = sessions.identity("active").unwrap();
    assert_eq!((active.user_id, active.version), (4, (5, 6)));
    assert!(!sessions.values.lock().unwrap().contains_key("expired"));
    sessions.logout("active").unwrap();
    assert_eq!(sessions.identity("active").unwrap_err().status, Some(401));
}

#[test]
fn session_capacity_preserves_existing_logins_and_recovers_after_logout_or_expiry() {
    let sessions = Sessions::default();
    let expires = Utc::now() + Duration::hours(12);
    let tokens: Vec<_> = (0..MAX_USER_SESSIONS)
        .map(|_| sessions.issue(7, expires, (1, 2)).unwrap())
        .collect();
    assert_eq!(
        sessions.issue(7, expires, (1, 2)).unwrap_err().status,
        Some(429)
    );
    let active = sessions.identity(&tokens[0]).unwrap();
    assert_eq!((active.user_id, active.version), (7, (1, 2)));
    sessions.issue(8, expires, (1, 2)).unwrap();
    sessions.logout(&tokens[0]).unwrap();
    sessions.issue(7, expires, (1, 2)).unwrap();
    sessions
        .values
        .lock()
        .unwrap()
        .get_mut(&tokens[1])
        .unwrap()
        .expires = Utc::now() - Duration::seconds(1);
    sessions.issue(7, expires, (1, 2)).unwrap();
    let mut values = sessions.values.lock().unwrap();
    while values.len() < MAX_SESSIONS {
        let index = values.len();
        values.insert(
            format!("capacity-{index}"),
            Session {
                user_id: 100 + index as i64,
                expires,
                version: (1, 2),
                context: String::new(),
            },
        );
    }
    drop(values);
    assert_eq!(
        sessions.issue(9, expires, (1, 2)).unwrap_err().status,
        Some(429)
    );
}

fn session_version(store: &Store, user: &Value) -> Result<(i64, i64)> {
    session_version_in(&store.connection()?, user)
}
fn session_version_in(
    connection: &crate::engine::store::Connection,
    user: &Value,
) -> Result<(i64, i64)> {
    let template = super::account_permissions::template(connection, user)?
        .and_then(|item| item["versionNumber"].as_i64())
        .unwrap_or(0);
    Ok((user["versionNumber"].as_i64().unwrap_or(0), template))
}

fn actor_from(store: &Store, user: &Value) -> Result<Actor> {
    actor_from_connection(&store.connection()?, user, store.edition)
}

fn actor_from_connection(
    connection: &crate::engine::store::Connection,
    user: &Value,
    edition: permissions::ProductEdition,
) -> Result<Actor> {
    let grants = super::account_permissions::grants(connection, user)?;
    let mut actor = Actor {
        scope_rules: super::scope_policies::rules(
            connection,
            "Account",
            user["id"].as_i64().unwrap_or(0),
        )?,
        context: String::new(),
        edition,
        id: user["id"].as_i64().unwrap_or(0),
        name: user["fullName"].as_str().unwrap_or("").into(),
        company: user["companyScope"].as_str().unwrap_or("").into(),
        department: user["departmentId"].as_str().unwrap_or("").into(),
        departments: super::organization_scope::departments(
            connection,
            user,
            user["role"] != "Admin" && grants.iter().any(|grant| grant.data_scope == "department"),
        )?,
        oa_assigned_only: super::account_permissions::assigned_only(connection, user)?,
        admin: user["role"] == "Admin",
        grants: grants
            .into_iter()
            .filter(|grant| edition.allows(&grant.resource_key))
            .collect(),
    };
    super::scope_policies::restrict(&mut actor);
    Ok(actor)
}

pub fn current_actor(store: &Store, id: i64) -> Result<Actor> {
    current_actor_in(&store.connection()?, id, store.edition)
}

pub(super) fn refresh_actor_in(
    connection: &crate::engine::store::Connection,
    actor: &Actor,
) -> Result<Actor> {
    let current = super::units::resolve(connection, actor.id, actor.edition, &actor.context)?;
    if current.company != actor.company {
        return Err(error(403, "单位身份已变化，请重新打开业务后操作。"));
    }
    Ok(current)
}
pub(super) fn refresh_actor(store: &Store, actor: &Actor) -> Result<Actor> {
    store.read(|tx| refresh_actor_in(tx, actor))
}

pub fn current_actor_in(
    connection: &crate::engine::store::Connection,
    id: i64,
    edition: permissions::ProductEdition,
) -> Result<Actor> {
    let user = store::get(connection, "users", id)?;
    if user["isActive"] != true {
        return Err(error(403, "账号已停用。"));
    }
    actor_from_connection(connection, &user, edition)
}

pub fn user_dto(
    store: &Store,
    record: &Value,
    clock: &crate::clock::BusinessClock,
) -> Result<Value> {
    let actor = actor_from(store, record)?;
    user_dto_for(store, record, clock, &actor)
}
pub(super) fn user_dto_for(
    store: &Store,
    record: &Value,
    clock: &crate::clock::BusinessClock,
    actor: &Actor,
) -> Result<Value> {
    let mut user = contracts::overlay(contracts::initial(contracts::schema("ApiUserDto")), record);
    user["employee"] = if !actor.admin && actor.edition.allows("office.people") {
        store
            .read(|tx| super::personnel_accounts::linked_person(tx, actor.id))?
            .map(super::personnel_accounts::summary)
            .unwrap_or(Value::Null)
    } else {
        Value::Null
    };
    if !actor.context.is_empty() {
        user["employee"] = store.read(|tx| -> Result<Value> {
            let Some(person) = super::personnel_accounts::person_for_account(tx, actor.id)? else {
                return Ok(Value::Null);
            };
            if actor.department.is_empty() {
                return Ok(Value::Null);
            }
            let appointment = super::personnel_organization::appointment_in(
                tx,
                &person,
                &actor.company,
                &actor.department,
            )?;
            let mut summary = super::personnel_accounts::summary(person);
            summary["jobTitle"] = json!(appointment.job_title);
            summary["departmentId"] = json!(actor.department);
            summary["companyScope"] = json!(actor.company);
            Ok(summary)
        })?;
    }
    user["primaryCompanyScope"] = record["companyScope"].clone();
    user["companyScope"] = json!(actor.company);
    user["departmentId"] = json!(actor.department);
    user["contextId"] = json!(actor.context);
    user["permissionSource"] =
        store.read(|tx| super::account_permissions::source(tx, actor, &record))?;
    user["unitContexts"] = json!(store.read(|tx| super::units::contexts(tx, actor))?);
    let now = clock.now().map_err(unavailable)?;
    user["businessDate"] = json!(now.today.to_string());
    user["effectiveDepartmentIds"] = json!(actor.departments);
    user["businessTimeZone"] = json!(now.time_zone);
    user["businessDateValidUntilUtc"] = json!(now.valid_until.to_rfc3339());
    let grants = &actor.grants;
    let modules = permissions::module_access(grants);
    let enabled: Vec<_> = modules.keys().cloned().collect();
    let access: Vec<_> = modules
        .iter()
        .map(|(module, level)| json!({"moduleKey":module,"accessLevel":level}))
        .collect();
    let workspace = |name| {
        grants.iter().any(|grant| {
            permissions::resource(&grant.resource_key)
                .is_some_and(|resource| !resource.is_technical && resource.workspace == name)
        })
    };
    user["capabilities"] = json!({"canManageSettings":actor.admin,"canManageUsers":actor.admin && actor.edition.allows("system.users"),"canViewAllBusinessData":actor.admin,"canUseDocumentWorkspace":workspace("document"),"canUseSalesWorkspace":workspace("sales"),"productEdition":actor.edition.name(),"enabledModules":enabled,"moduleAccess":access,"permissions":actor.grants,"availableFeatures":["worklist","business-attachments"]});
    user["capabilities"]["oaAssignedOnly"] = json!(actor.oa_assigned_only);
    user["capabilities"]["unitManagement"] = json!(super::units::management(actor));
    Ok(user)
}

/// A responsibility assignment grants access only to its routed tasks.
pub fn is_company_approver(actor: &Actor) -> bool {
    permissions::catalog().resources.iter().any(|r| {
        r.workspace == "office"
            && r.actions.iter().any(|a| a.key == "approve")
            && authorize(actor, &r.key, "view").is_ok()
            && authorize(actor, &r.key, "approve").is_ok()
    })
}

pub fn authorize(actor: &Actor, resource: &str, action: &str) -> Result<()> {
    if !actor.edition.allows(resource) {
        return Err(error(403, "当前产品版本不包含此功能。"));
    }
    let action = permissions::service_action(resource, action);
    if actor.admin
        || (!resource.starts_with("system.")
            && actor
                .grants
                .iter()
                .any(|grant| grant.resource_key == resource && grant.action == action))
    {
        Ok(())
    } else {
        Err(error(403, "当前账号没有此操作权限。"))
    }
}
pub(super) fn scope_rank(actor: &Actor, resource: &str, action: &str) -> u8 {
    if authorize(actor, resource, action).is_err() {
        return 0;
    }
    if actor.admin {
        return 4;
    }
    let action = permissions::service_action(resource, action);
    if actor.scope_rules.iter().any(|r| {
        r.resource_key == resource
            && r.action == action
            && matches!(
                r.mode,
                export_doc_domain::access_scope::ScopeMode::Participation
            )
    }) {
        return 1;
    }
    actor
        .grants
        .iter()
        .filter(|grant| grant.resource_key == resource && grant.action == action)
        .map(|grant| permissions::scope_rank(&grant.data_scope))
        .max()
        .unwrap_or(0)
}
pub(super) fn data_scope<'a>(
    actor: &'a Actor,
    resource: &str,
    action: &str,
) -> export_doc_storage::DataScope<'a> {
    let mut scope = export_doc_storage::DataScope {
        company: &actor.company,
        department: &actor.department,
        departments: &actor.departments,
        personnel_memberships: resource == "office.people" && action == "view",
        user_id: actor.id,
        ..Default::default()
    };
    if !actor.edition.allows(resource) {
        return scope;
    }
    scope.all = actor.admin;
    let action = permissions::service_action(resource, action);
    for grant in actor
        .grants
        .iter()
        .filter(|grant| grant.resource_key == resource && grant.action == action)
    {
        match grant.data_scope.as_str() {
            "all" => scope.all = true,
            "company" => scope.company_wide = true,
            "department" => scope.department_wide = true,
            "own" => scope.own = true,
            _ => {}
        }
    }
    scope.restrictions = scope_filters(actor, resource, action);
    if actor.scope_rules.iter().any(|r| {
        r.resource_key == resource
            && r.action == action
            && matches!(
                r.mode,
                export_doc_domain::access_scope::ScopeMode::Participation
            )
    }) {
        scope.all = false;
        scope.company_wide = false;
        scope.department_wide = false;
        scope.own = true;
    }
    scope
}
pub(super) fn scope_filters(
    actor: &Actor,
    resource: &str,
    action: &str,
) -> Vec<export_doc_storage::ScopeFilter> {
    use export_doc_domain::access_scope::ScopeMode;
    use export_doc_storage::ScopeFilter;
    let action = permissions::service_action(resource, action);
    actor
        .scope_rules
        .iter()
        .filter(|r| r.resource_key == resource && r.action == action)
        .filter_map(|r| match r.mode {
            ScopeMode::Deny => Some(ScopeFilter::Deny),
            ScopeMode::Units => Some(ScopeFilter::Units(r.unit_codes.iter().cloned().collect())),
            ScopeMode::Departments => Some(ScopeFilter::Departments(
                r.departments.iter().cloned().collect(),
            )),
            ScopeMode::Projects => Some(ScopeFilter::Projects(
                r.project_ids.iter().cloned().collect(),
            )),
            ScopeMode::Participation | ScopeMode::Unrestricted => None,
        })
        .collect()
}
pub fn visible(actor: &Actor, resource: &str, action: &str, record: &Value) -> bool {
    if !actor.edition.allows(resource) {
        return false;
    }
    if actor.admin {
        return true;
    }
    let action = permissions::service_action(resource, action);
    if actor
        .scope_rules
        .iter()
        .filter(|r| r.resource_key == resource && r.action == action)
        .any(|r| {
            !r.accepts(
                record["companyScope"].as_str().unwrap_or(""),
                record["departmentId"].as_str().unwrap_or(""),
                record["projectId"].as_str(),
                record["ownerUserId"].as_i64().unwrap_or(0),
                actor.id,
            )
        })
    {
        return false;
    }
    actor
        .grants
        .iter()
        .filter(|grant| grant.resource_key == resource && grant.action == action)
        .any(|grant| {
            let scope = grant.data_scope.as_str();
            match scope {
                "all" => true,
                "company" => record["companyScope"] == actor.company,
                "department" => {
                    record["companyScope"] == actor.company
                        && (record["departmentId"] == actor.department
                            || actor
                                .departments
                                .iter()
                                .any(|department| record["departmentId"] == *department)
                            || (resource == "office.people"
                                && action == "view"
                                && record["organization"]["additionalAssignments"]
                                    .as_array()
                                    .is_some_and(|assignments| {
                                        assignments.iter().any(|assignment| {
                                            assignment["companyCode"] == actor.company
                                                && (assignment["departmentId"] == actor.department
                                                    || actor.departments.iter().any(|department| {
                                                        assignment["departmentId"] == *department
                                                    }))
                                        })
                                    })))
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
    if permissions::allows_edition_operation(
        &actor.grants,
        actor.admin,
        actor.edition,
        operation,
        query,
    ) {
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
            grant.resource_key == permission
                && grant.action == "view"
                && (permissions::resource(permission)
                    .is_some_and(|resource| !resource.supports_data_scope)
                    || ["department", "company", "all"].contains(&grant.data_scope.as_str()))
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
                && (template["departmentId"] == actor.department
                    || actor
                        .departments
                        .iter()
                        .any(|department| template["departmentId"] == *department))
        }
        _ => false,
    }
}
