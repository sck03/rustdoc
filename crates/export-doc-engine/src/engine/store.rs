use super::error::{Result, conflict, error, unavailable};
use crate::{
    contracts,
    paths::{RuntimePaths, ensure_safe_absolute},
};
use chrono::Utc;
pub use export_doc_storage::Connection;
use export_doc_storage::{AuditWrite, RecordWrite};
use fs2::FileExt;
use serde_json::{Value, json};
use std::{
    fs::{File, OpenOptions},
    sync::{Mutex, MutexGuard},
};
use unicode_normalization::UnicodeNormalization;

pub struct Store {
    pub(crate) data_root: std::path::PathBuf,
    connection: Mutex<Connection>,
    #[cfg(feature = "postgres")]
    postgres_connection: Option<zeroize::Zeroizing<String>>,
    _lock: Option<File>,
}
#[derive(Clone, Debug)]
pub struct Actor {
    pub id: i64,
    pub name: String,
    pub company: String,
    pub department: String,
    pub admin: bool,
    pub grants: Vec<Value>,
}

impl Store {
    pub fn open(paths: &RuntimePaths) -> Result<Self> {
        let lock_path = paths.data_root.join("native-instance.lock");
        let database_path = paths.data_root.join("exportdoc-native.db");
        for path in [&lock_path, &database_path] {
            ensure_safe_absolute(path).map_err(unavailable)?;
        }
        let lock = OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(lock_path)?;
        lock.try_lock_exclusive().map_err(|cause| {
            if cause.raw_os_error() == fs2::lock_contended_error().raw_os_error() {
                error(429, "此数据目录已由另一个原生程序打开。")
            } else {
                unavailable(format!("无法取得数据库实例锁：{cause}"))
            }
        })?;
        super::team_backup::disaster::apply_pending(paths)?;
        let connection = Connection::sqlite(&database_path)?;
        Ok(Self {
            data_root: paths.data_root.clone(),
            connection: Mutex::new(connection),
            #[cfg(feature = "postgres")]
            postgres_connection: None,
            _lock: Some(lock),
        })
    }

    pub fn connection(&self) -> Result<MutexGuard<'_, Connection>> {
        crate::operation::check()?;
        let connection = self
            .connection
            .lock()
            .map_err(|_| unavailable("数据库状态异常，已停止继续写入。"))?;
        crate::operation::check()?;
        Ok(connection)
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres(paths: &RuntimePaths, connection_string: &str) -> Result<Self> {
        Ok(Self {
            data_root: paths.data_root.clone(),
            connection: Mutex::new(Connection::postgres(connection_string)?),
            postgres_connection: Some(zeroize::Zeroizing::new(connection_string.into())),
            _lock: None,
        })
    }
    pub fn transaction<T>(&self, operation: impl FnOnce(&Connection) -> Result<T>) -> Result<T> {
        let connection = self.connection()?;
        connection.begin()?;
        match operation(&connection) {
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
        }
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
    let count = items.len();
    let start = page.saturating_sub(1).saturating_mul(size);
    contracts::page(
        items.into_iter().skip(start).take(size).collect(),
        count,
        page,
        size,
    )
}
