#[cfg(feature = "postgres")]
mod postgres;
#[cfg(feature = "postgres")]
pub use postgres::tools::{
    ClientParameters as PostgresClientParameters, MaintenanceLease as PostgresMaintenanceLease,
    client_parameters as postgres_client_parameters,
};
mod sqlite;
use serde_json::Value;
use std::{cell::Cell, path::Path};

pub type Result<T> = std::result::Result<T, Error>;
pub const SCHEMA_VERSION: i64 = 4;
pub fn verify_sqlite_backup(path: &Path) -> Result<()> {
    sqlite::Sqlite::verify_file(path)
}
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ErrorKind {
    Conflict,
    Busy,
    Unavailable,
    Timeout,
    Unsupported,
}
#[derive(Debug)]
pub struct Error {
    pub kind: ErrorKind,
    pub message: String,
}
impl Error {
    pub fn new(kind: ErrorKind, message: impl Into<String>) -> Self {
        Self {
            kind,
            message: message.into(),
        }
    }
    pub fn unavailable(message: impl Into<String>) -> Self {
        Self::new(ErrorKind::Unavailable, message)
    }
}
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}
impl std::error::Error for Error {}
impl From<serde_json::Error> for Error {
    fn from(e: serde_json::Error) -> Self {
        Self::unavailable(format!("数据库记录不符合契约：{e}"))
    }
}
impl From<std::io::Error> for Error {
    fn from(e: std::io::Error) -> Self {
        Self::unavailable(format!("存储文件操作失败：{e}"))
    }
}

pub struct RecordWrite<'a> {
    pub kind: &'a str,
    pub identity: Option<&'a str>,
    pub body: &'a Value,
}
pub struct AuditWrite<'a> {
    pub kind: &'a str,
    pub record_id: i64,
    pub version: i64,
    pub action: &'a str,
    pub actor_id: i64,
    pub occurred_at: &'a str,
    pub note: &'a str,
}
pub struct BlobWrite<'a> {
    pub kind: &'a str,
    pub record_id: i64,
    pub file_name: &'a str,
    pub media_type: &'a str,
    pub digest: &'a str,
    pub content: &'a [u8],
    pub created_at: &'a str,
}
pub struct Blob {
    pub file_name: String,
    pub media_type: String,
    pub digest: String,
    pub content: Vec<u8>,
}
pub struct Credential {
    pub salt: Vec<u8>,
    pub hash: Vec<u8>,
    pub iterations: u32,
}

/// All business operations use this semantic storage boundary, never provider SQL.
trait Adapter: Send {
    fn provider(&self) -> &'static str;
    fn health(&self) -> Result<()>;
    fn begin(&self) -> Result<()>;
    fn commit(&self) -> Result<()>;
    fn rollback(&self) -> Result<()>;
    fn checkpoint(&self) -> Result<()>;
    fn get(&self, kind: &str, id: i64) -> Result<Option<Value>>;
    fn find_identity(&self, kind: &str, identity: &str) -> Result<Option<Value>>;
    fn all(&self, kind: &str) -> Result<Vec<Value>>;
    fn insert(&self, record: &RecordWrite<'_>) -> Result<i64>;
    fn update(&self, id: i64, expected: i64, record: &RecordWrite<'_>) -> Result<bool>;
    fn set_body(&self, id: i64, body: &Value) -> Result<()>;
    fn delete(&self, kind: &str, id: i64, expected: i64) -> Result<bool>;
    fn append_audit_details(&self, audit: &AuditWrite<'_>, details: &Value) -> Result<()>;
    fn audit_events(&self, before_id: i64, limit: i64) -> Result<Vec<Value>>;
    fn delete_audits(&self, ids: &[i64]) -> Result<usize>;
    fn history(&self, kind: Option<&str>, id: Option<i64>) -> Result<Vec<Value>>;
    fn credential(&self, id: i64) -> Result<Option<Credential>>;
    fn set_credential(&self, id: i64, salt: &[u8], hash: &[u8], iterations: u32) -> Result<()>;
    fn settings(&self, name: &str) -> Result<Option<Value>>;
    fn set_settings(&self, name: &str, version: i64, body: &Value) -> Result<()>;
    fn attachment_bytes(&self, invoice_id: i64) -> Result<i64>;
    fn insert_blob(&self, blob: &BlobWrite<'_>) -> Result<()>;
    fn blob(&self, record_id: i64, kind: &str) -> Result<Option<Blob>>;
    fn delete_blobs(&self, record_id: i64) -> Result<()>;
    fn delete_blob(&self, record_id: i64, kind: &str) -> Result<()>;
    fn backup(&self, _path: &Path) -> Result<()> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "该数据库应使用对应的数据库备份模块。",
        ))
    }
    fn verify_backup(&self, _path: &Path) -> Result<()> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "该数据库应使用对应的数据库备份模块。",
        ))
    }
    fn restore(&self, _path: &Path) -> Result<()> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "该数据库应使用对应的数据库恢复模块。",
        ))
    }
}
pub struct Connection {
    adapter: Box<dyn Adapter>,
    failed: Cell<bool>,
}
impl Connection {
    pub fn sqlite(path: &Path) -> Result<Self> {
        Ok(Self {
            adapter: Box::new(sqlite::Sqlite::open(path)?),
            failed: Cell::new(false),
        })
    }
    #[cfg(feature = "postgres")]
    pub fn postgres(connection_string: &str) -> Result<Self> {
        Ok(Self {
            adapter: Box::new(postgres::Postgres::open(connection_string)?),
            failed: Cell::new(false),
        })
    }
    /// Schema creation uses a separately supplied maintenance connection.
    /// Existing databases are validated, never migrated or replaced.
    #[cfg(feature = "postgres")]
    pub fn initialize_postgres(connection_string: &str, owner: &str) -> Result<()> {
        postgres::initialize(connection_string, owner)
    }
    pub fn provider(&self) -> &'static str {
        self.adapter.provider()
    }
    pub fn health(&self) -> Result<()> {
        if self.failed.get() {
            return Err(Error::unavailable("数据库事务状态异常，已停止服务。"));
        }
        let result = self.adapter.health();
        if result.is_err() {
            self.failed.set(true);
        }
        result
    }
    pub fn begin(&self) -> Result<()> {
        self.health()?;
        self.adapter.begin()
    }
    pub fn commit(&self) -> Result<()> {
        let result = self.adapter.commit();
        if result.is_err() {
            self.failed.set(true);
        }
        result
    }
    pub fn rollback(&self) -> Result<()> {
        let result = self.adapter.rollback();
        if result.is_err() {
            self.failed.set(true);
        }
        result
    }
    pub fn restore(&self, path: &Path) -> Result<()> {
        self.health()?;
        let result = self.adapter.restore(path);
        if result.is_err() {
            self.failed.set(true);
        }
        result
    }
}
macro_rules! delegate{
    ($($name:ident($($arg:ident:$ty:ty),*)->$result:ty;)+)=>{
        impl Connection{$(pub fn $name(&self,$($arg:$ty),*)->Result<$result>{self.health()?;self.adapter.$name($($arg),*)})+}
    }
}
impl Connection {
    pub fn append_audit(&self, audit: &AuditWrite<'_>) -> Result<()> {
        self.append_audit_details(audit, &serde_json::json!({}))
    }
}
delegate! {
    checkpoint()->();
    get(kind:&str,id:i64)->Option<Value>;
    find_identity(kind:&str,identity:&str)->Option<Value>;
    all(kind:&str)->Vec<Value>;
    insert(record:&RecordWrite<'_>)->i64;
    update(id:i64,expected:i64,record:&RecordWrite<'_>)->bool;
    set_body(id:i64,body:&Value)->();
    delete(kind:&str,id:i64,expected:i64)->bool;
    append_audit_details(audit:&AuditWrite<'_>,details:&Value)->();
    audit_events(before_id:i64,limit:i64)->Vec<Value>;
    delete_audits(ids:&[i64])->usize;
    history(kind:Option<&str>,id:Option<i64>)->Vec<Value>;
    credential(id:i64)->Option<Credential>;
    set_credential(id:i64,salt:&[u8],hash:&[u8],iterations:u32)->();
    settings(name:&str)->Option<Value>;
    set_settings(name:&str,version:i64,body:&Value)->();
    attachment_bytes(invoice_id:i64)->i64;
    insert_blob(blob:&BlobWrite<'_>)->();
    blob(record_id:i64,kind:&str)->Option<Blob>;
    delete_blobs(record_id:i64)->();
    delete_blob(record_id:i64,kind:&str)->();
    backup(path:&Path)->();
    verify_backup(path:&Path)->();
}
