use super::error::{Result, conflict, error, unavailable};
use crate::{contracts, paths::RuntimePaths};
use chrono::Utc;
pub use export_doc_storage::Connection;
#[cfg(feature = "postgres")]
use export_doc_storage::pool::PoolOptions;
use export_doc_storage::{
    AuditWrite, RecordWrite,
    metrics::Metrics,
    pool::{Lease, Pool},
};
use serde_json::{Value, json};
use std::{
    fs::File,
    sync::{Mutex, MutexGuard, TryLockError},
};
use unicode_normalization::UnicodeNormalization;
mod sqlite_layout;

pub struct Store {
    pub(crate) edition: export_doc_domain::permissions::ProductEdition,
    pub(crate) data_root: std::path::PathBuf,
    connection: Pool,
    writes: Mutex<()>,
    write_waits: Metrics,
    transactions: Metrics,
    #[cfg(feature = "postgres")]
    postgres_connection: Option<zeroize::Zeroizing<String>>,
    _lock: Option<File>,
}
#[derive(Clone, Debug)]
pub struct Actor {
    pub edition: export_doc_domain::permissions::ProductEdition,
    pub id: i64,
    pub name: String,
    pub company: String,
    pub department: String,
    pub admin: bool,
    pub grants: Vec<Value>,
}

impl Store {
    pub fn open(paths: &RuntimePaths) -> Result<Self> {
        let lock = sqlite_layout::prepare(paths)?;
        let database_path = paths.sqlite_database_path();
        super::team_backup::disaster::apply_pending(paths)?;
        let connection = Connection::sqlite(&database_path)?;
        Ok(Self {
            edition: Default::default(),
            data_root: paths.data_root.clone(),
            connection: Pool::single(connection),
            writes: Mutex::new(()),
            write_waits: Default::default(),
            transactions: Default::default(),
            #[cfg(feature = "postgres")]
            postgres_connection: None,
            _lock: Some(lock),
        })
    }

    pub fn connection(&self) -> Result<Lease<'_>> {
        crate::operation::check()?;
        if self.writes.is_poisoned() {
            return Err(unavailable("数据库写入协调状态异常。"));
        }
        let connection = self
            .connection
            .acquire(|| crate::operation::check().is_err());
        crate::operation::check()?;
        connection.map_err(Into::into)
    }
    pub fn metrics(&self) -> Value {
        let mut value = self.connection.snapshot();
        value["writeWait"] = self.write_waits.snapshot();
        value["writeTransactions"] = self.transactions.snapshot();
        value["writeCoordinatorFailed"] = json!(self.writes.is_poisoned());
        value
    }
    pub fn health(&self) -> Result<()> {
        if self.writes.is_poisoned() {
            return Err(unavailable("数据库写入协调状态异常。"));
        }
        self.connection.health().map_err(Into::into)
    }
    fn write_guard(&self) -> Result<MutexGuard<'_, ()>> {
        let observation = self.write_waits.start();
        loop {
            crate::operation::check()?;
            match self.writes.try_lock() {
                Ok(guard) => {
                    observation.finish(true);
                    return Ok(guard);
                }
                Err(TryLockError::Poisoned(_)) => {
                    return Err(unavailable("数据库写入协调状态异常。"));
                }
                Err(TryLockError::WouldBlock) => {
                    std::thread::sleep(std::time::Duration::from_millis(10))
                }
            }
        }
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres(paths: &RuntimePaths, connection_string: &str) -> Result<Self> {
        Self::open_postgres_with_pool(paths, connection_string, PoolOptions::default())
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres_with_pool(
        paths: &RuntimePaths,
        connection_string: &str,
        options: PoolOptions,
    ) -> Result<Self> {
        Ok(Self {
            edition: Default::default(),
            data_root: paths.data_root.clone(),
            connection: Pool::postgres(connection_string, options)?,
            writes: Mutex::new(()),
            write_waits: Default::default(),
            transactions: Default::default(),
            postgres_connection: Some(zeroize::Zeroizing::new(connection_string.into())),
            _lock: None,
        })
    }
    pub fn transaction<T>(&self, operation: impl FnOnce(&Connection) -> Result<T>) -> Result<T> {
        let observation = self.transactions.start();
        let _write = self.write_guard()?;
        let connection = self.connection()?;
        connection.begin()?;
        let result = match operation(&connection) {
            Ok(value) => {
                if let Err(error) = crate::operation::check() {
                    connection.rollback()?;
                    return Err(error);
                }
                connection.commit()?;
                Ok(value)
            }
            Err(error) => {
                connection.rollback()?;
                Err(error)
            }
        };
        observation.finish(result.is_ok());
        result
    }
    pub fn read<T>(&self, operation: impl FnOnce(&Connection) -> Result<T>) -> Result<T> {
        let connection = self.connection()?;
        connection.begin_read()?;
        match operation(&connection) {
            Ok(value) => {
                crate::operation::check()?;
                connection.commit()?;
                Ok(value)
            }
            Err(cause) => {
                connection.rollback()?;
                Err(cause)
            }
        }
    }
    /// Refresh account status, organization and grants after acquiring the write
    /// transaction, so a queued request cannot write with a stale authorization.
    pub fn transaction_as<T>(
        &self,
        actor: &Actor,
        operation: impl FnOnce(&Connection, &Actor) -> Result<T>,
    ) -> Result<T> {
        self.transaction(|connection| {
            let current = super::auth::current_actor_in(connection, actor.id, self.edition)?;
            operation(connection, &current)
        })
    }
    #[cfg(feature = "postgres")]
    pub fn postgres_connection(&self) -> Result<&str> {
        self.postgres_connection
            .as_ref()
            .map(|value| value.as_str())
            .ok_or_else(|| unavailable("当前不是 PostgreSQL 数据库。"))
    }
    pub fn get(&self, kind: &str, id: i64) -> Result<Value> {
        get(&*self.connection()?, kind, id)
    }
    pub fn all(&self, kind: &str) -> Result<Vec<Value>> {
        all(&*self.connection()?, kind)
    }
    pub fn catalog(&self, kind: &str) -> Result<Vec<Value>> {
        self.read(|tx| {
            let mut rows = Vec::new();
            loop {
                crate::operation::check()?;
                let (total, page) = tx.query_generic(&export_doc_storage::GenericQuery {
                    kind,
                    scope: export_doc_storage::DataScope {
                        all: true,
                        ..Default::default()
                    },
                    filters: &[],
                    keyword: "",
                    search_root: None,
                    fields: &[],
                    offset: rows.len() as i64,
                    limit: 200,
                })?;
                let empty = page.is_empty();
                rows.extend(page);
                if empty || rows.len() as i64 >= total {
                    return Ok(rows);
                }
            }
        })
    }
    pub fn settings(&self, name: &str) -> Result<Option<Value>> {
        Ok(self.connection()?.settings(name)?)
    }
    pub fn close(&self) -> Result<()> {
        Ok(self.connection()?.checkpoint()?)
    }
    pub fn provider(&self) -> Result<&'static str> {
        Ok(self.connection()?.provider())
    }
}

pub fn get(connection: &Connection, kind: &str, id: i64) -> Result<Value> {
    connection
        .get(kind, id)?
        .ok_or_else(|| error(404, "记录不存在。"))
}
pub fn all(connection: &Connection, kind: &str) -> Result<Vec<Value>> {
    Ok(connection.all(kind)?)
}
pub fn normalize(text: &str) -> String {
    text.nfc().collect::<String>().trim().to_lowercase()
}
pub fn timestamp() -> String {
    Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true)
}
pub fn check_version(previous: &Value, expected: i64) -> Result<()> {
    if expected <= 0 || previous["versionNumber"].as_i64() != Some(expected) {
        return Err(conflict(
            "记录已被修改或缺少版本号。请重新读取后核对，当前草稿已保留。",
        ));
    }
    Ok(())
}
pub fn expected(body: &Value) -> i64 {
    body["expectedVersion"]
        .as_i64()
        .or_else(|| body["versionNumber"].as_i64())
        .or_else(|| {
            body["rowVersion"]
                .as_str()
                .and_then(|version| version.strip_prefix("native:")?.parse().ok())
        })
        .unwrap_or(0)
}

pub fn save(
    connection: &Connection,
    kind: &str,
    id: i64,
    body: Value,
    identity: Option<String>,
    actor: &Actor,
    action: &str,
) -> Result<Value> {
    save_in_scope(connection, kind, id, body, identity, actor, action, None)
}

/// A child record inherits the already-authorized parent's data scope. The
/// authenticated actor remains the audit author, including cross-company admins.
pub fn save_in_scope(
    connection: &Connection,
    kind: &str,
    id: i64,
    mut body: Value,
    identity: Option<String>,
    actor: &Actor,
    action: &str,
    scope: Option<&Value>,
) -> Result<Value> {
    let now = timestamp();
    let previous = if id > 0 {
        Some(get(connection, kind, id)?)
    } else {
        None
    };
    let version = if id > 0 {
        let previous = previous.as_ref().expect("existing record");
        check_version(previous, expected(&body))?;
        previous["versionNumber"].as_i64().unwrap_or(0) + 1
    } else {
        1
    };
    body["ownerUserId"] = json!(actor.id);
    if kind != "users" {
        body["companyScope"] = json!(actor.company);
    }
    if !matches!(kind, "people" | "users" | "bookings" | "supply-requests") {
        body["departmentId"] = json!(actor.department);
    } else if kind != "users" && body["departmentId"].as_str().is_none_or(str::is_empty) {
        body["departmentId"] = json!(actor.department);
    }
    if id == 0 {
        body["createdAt"] = json!(now);
        if let Some(scope) = scope {
            for key in ["ownerUserId", "companyScope", "departmentId"] {
                if let Some(value) = scope.get(key) {
                    body[key] = value.clone();
                }
            }
        }
    } else {
        let previous = previous.as_ref().expect("existing record");
        for key in ["ownerUserId", "companyScope", "departmentId", "createdAt"] {
            if (key == "departmentId" && matches!(kind, "people" | "users"))
                || (key == "companyScope" && kind == "users")
            {
                continue;
            }
            if previous.get(key).is_some() {
                body[key] = previous[key].clone();
            }
        }
    }
    body["updatedAt"] = json!(now);
    body["versionNumber"] = json!(version);
    body["rowVersion"] = json!(format!("native:{version}"));
    if let Some(object) = body.as_object_mut() {
        object.remove("expectedVersion");
        object.remove("resetPassword");
    }
    // The original invoice key is company + invoice number + data type. A
    // length-delimited JSON tuple keeps both data versions and tenant keys distinct.
    let identity = if kind == "invoices" {
        Some(serde_json::to_string(&[
            &body["companyScope"],
            &body["invoiceNo"],
            &body["type"],
        ])?)
    } else {
        identity
            .filter(|key| !key.is_empty())
            .map(|key| normalize(&key))
    };
    let record = RecordWrite {
        kind,
        identity: identity.as_deref(),
        body: &body,
    };
    let record_id = if id == 0 {
        connection.insert(&record)?
    } else {
        if !connection.update(id, version - 1, &record)? {
            return Err(conflict("记录版本已变化，保存已取消。"));
        }
        id
    };
    body["id"] = json!(record_id);
    if kind == "invoices" {
        for item in body["items"].as_array_mut().into_iter().flatten() {
            item["invoiceId"] = json!(record_id);
        }
    }
    if kind == "people" {
        body["employee"]["id"] = json!(record_id);
    }
    connection.set_body(record_id, &body)?;
    connection.append_audit_details(
        &AuditWrite {
            kind,
            record_id,
            version,
            action,
            actor_id: actor.id,
            occurred_at: &now,
            note: "",
        },
        &super::audit_values::changes(previous.as_ref(), Some(&body)),
    )?;
    Ok(body)
}

pub fn history(connection: &Connection, kind: &str, id: i64) -> Result<Vec<Value>> {
    Ok(connection.history(Some(kind), Some(id))?)
}

pub fn paged(mut items: Vec<Value>, query: &[(&str, String)]) -> Value {
    let query_value = |name: &str| {
        query
            .iter()
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let keyword = normalize(query_value("keyword"));
    let status = query_value("status");
    items.retain(|item| {
        (keyword.is_empty() || normalize(&item.to_string()).contains(&keyword))
            && (status.is_empty() || item["status"] == status)
    });
    page_only(items, query)
}
pub fn page_only(items: Vec<Value>, query: &[(&str, String)]) -> Value {
    let (page, size, start) = page_parameters(query);
    let count = items.len();
    contracts::page(
        items.into_iter().skip(start as usize).take(size).collect(),
        count,
        page,
        size,
    )
}
pub fn page_parameters(query: &[(&str, String)]) -> (usize, usize, i64) {
    let query_value = |name: &str| {
        query
            .iter()
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let size = query_value("pageSize")
        .parse::<usize>()
        .unwrap_or(30)
        .clamp(1, 200);
    let page = query_value("pageNumber")
        .parse::<usize>()
        .unwrap_or(1)
        .max(1);
    let start = i64::try_from(page.saturating_sub(1).saturating_mul(size)).unwrap_or(i64::MAX);
    (page, size, start)
}
