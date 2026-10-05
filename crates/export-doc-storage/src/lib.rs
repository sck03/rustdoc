#[cfg(feature = "postgres")]
mod postgres;
#[cfg(feature = "postgres")]
pub use postgres::tools::{
    ClientParameters as PostgresClientParameters, MaintenanceLease as PostgresMaintenanceLease,
    client_parameters as postgres_client_parameters,
};
mod communication;
mod generic_query;
mod job_query;
pub use job_query::{JobQuery, JobRetention};
mod record_query;
mod sql;
mod template_query;
pub use generic_query::{DataScope, GenericQuery};
pub mod metrics;
pub mod pool;
pub use template_query::{ReportTemplateQuery, TemplateAudience, TemplateVersionQuery};
struct QuerySql {
    filter: String,
    order: &'static str,
    values: Vec<String>,
}
mod migrations;
pub use communication::{CommunicationQuery, CommunicationView, NotificationScope};
mod sqlite;
pub use migrations::{MIN_SUPPORTED_SCHEMA_VERSION, SCHEMA_VERSION};
use serde_json::Value;
use std::{cell::Cell, path::Path};

pub type Result<T> = std::result::Result<T, Error>;
pub fn validate_schema_version(version: i64) -> Result<()> {
    migrations::pending(version).map(|_| ())
}
pub fn verify_sqlite_backup(path: &Path) -> Result<()> {
    sqlite::Sqlite::verify_file(path)
}
pub fn prepare_sqlite_restore(source: &Path, destination: &Path) -> Result<()> {
    sqlite::Sqlite::prepare_restore_file(source, destination)
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
/// One eligible approval principal and its optional department restriction.
pub struct RecordApprover {
    pub user_id: i64,
    pub department: Option<String>,
}
/// Indexed, bounded reads with authorization scope applied before pagination.
#[derive(Default)]
pub struct RecordQuery<'a> {
    pub kind: &'a str,
    pub company: &'a str,
    pub department: Option<&'a str>,
    pub owner: Option<i64>,
    pub employee: Option<i64>,
    pub parent: Option<i64>,
    pub status: Option<&'a str>,
    pub approvers: Option<&'a [RecordApprover]>,
    pub approval_actor: Option<i64>,
    pub exclude_owner: Option<i64>,
    pub handling_key: Option<&'a str>,
    /// Additional assigned-service access, combined with the ordinary owner/department scope.
    pub handling_keys: Option<&'a [String]>,
    pub handling_states: &'a [&'a str],
    pub handling_only: bool,
    pub offset: i64,
    pub limit: i64,
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
    fn is_closed(&self) -> bool {
        false
    }
    fn query_jobs(&self, query: &JobQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        if query.retention.is_some() {
            let (invalid, _) = self.query_page(
                &job_query::invalid_times(pg),
                if pg { "r.body::text" } else { "r.body" },
                0,
                1,
            )?;
            if invalid > 0 {
                return Err(Error::unavailable(
                    "文件任务时间或所有者损坏，已停止自动清理。",
                ));
            }
        }
        let (sql, projection) = job_query::build(query, pg)?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn job_counts(&self) -> Result<Value>;
    fn query_generic(&self, query: &GenericQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = generic_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn provider(&self) -> &'static str;
    fn health(&self) -> Result<()>;
    fn begin(&self) -> Result<()>;
    fn begin_read(&self) -> Result<()>;
    fn commit(&self) -> Result<()>;
    fn rollback(&self) -> Result<()>;
    fn checkpoint(&self) -> Result<()>;
    fn get(&self, kind: &str, id: i64) -> Result<Option<Value>>;
    fn find_identity(&self, kind: &str, identity: &str) -> Result<Option<Value>>;
    fn all(&self, kind: &str) -> Result<Vec<Value>>;
    fn query_records(&self, query: &RecordQuery<'_>) -> Result<(i64, Vec<Value>)>;
    fn query_page(
        &self,
        sql: &QuerySql,
        projection: &str,
        offset: i64,
        limit: i64,
    ) -> Result<(i64, Vec<Value>)>;
    fn query_report_templates(&self, query: &ReportTemplateQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        self.query_page(
            &template_query::catalog(query, pg),
            &template_query::metadata(pg, false),
            query.offset,
            query.limit,
        )
    }
    fn report_template_metadata(&self, id: i64) -> Result<Option<Value>> {
        let pg = self.provider() == "PostgreSQL";
        let sql = QuerySql {
            filter: format!("r.kind='report-templates' AND r.id={id}"),
            order: "r.id",
            values: vec![],
        };
        Ok(self
            .query_page(&sql, &template_query::metadata(pg, false), 0, 1)?
            .1
            .pop())
    }
    fn query_template_versions(
        &self,
        query: &TemplateVersionQuery<'_>,
    ) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        self.query_page(
            &template_query::versions(query, pg),
            &template_query::metadata(pg, true),
            query.offset,
            query.limit,
        )
    }
    fn template_version(&self, kind: &str, id: i64, version: i64) -> Result<Option<Value>> {
        let pg = self.provider() == "PostgreSQL";
        let mut sql = template_query::versions(
            &TemplateVersionQuery {
                kind,
                template_id: id,
                offset: 0,
                limit: 1,
            },
            pg,
        );
        sql.filter.push_str(&format!(
            " AND CAST({} AS BIGINT)={version}",
            template_query::field("content.versionNumber", pg)
        ));
        Ok(self
            .query_page(&sql, if pg { "r.body::text" } else { "r.body" }, 0, 1)?
            .1
            .pop())
    }
    fn query_communications(&self, query: &CommunicationQuery<'_>) -> Result<(i64, Vec<Value>)>;
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
    in_transaction: Cell<bool>,
    metrics: std::sync::Arc<metrics::Metrics>,
}
impl Connection {
    fn from_adapter(adapter: Box<dyn Adapter>) -> Self {
        Self {
            adapter,
            failed: Cell::new(false),
            in_transaction: Cell::new(false),
            metrics: Default::default(),
        }
    }
    fn observed<T>(&self, operation: impl FnOnce(&dyn Adapter) -> Result<T>) -> Result<T> {
        let observation = self.metrics.start();
        let result = self.health().and_then(|_| operation(&*self.adapter));
        if self.adapter.is_closed() {
            self.failed.set(true);
        }
        observation.finish(result.is_ok());
        result
    }
    pub fn query_records(&self, query: &RecordQuery<'_>) -> Result<(i64, Vec<Value>)> {
        self.observed(|adapter| adapter.query_records(query))
    }
    pub fn sqlite(path: &Path) -> Result<Self> {
        Ok(Self::from_adapter(Box::new(sqlite::Sqlite::open(path)?)))
    }
    #[cfg(feature = "postgres")]
    pub fn postgres(connection_string: &str) -> Result<Self> {
        Ok(Self::from_adapter(Box::new(postgres::Postgres::open(
            connection_string,
        )?)))
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
        self.adapter.begin()?;
        self.in_transaction.set(true);
        Ok(())
    }
    pub fn begin_read(&self) -> Result<()> {
        self.health()?;
        self.adapter.begin_read()?;
        self.in_transaction.set(true);
        Ok(())
    }
    pub fn commit(&self) -> Result<()> {
        if let Err(cause) = self.health() {
            let _ = self.rollback();
            return Err(cause);
        }
        let result = self.adapter.commit();
        if result
            .as_ref()
            .is_err_and(|cause| matches!(cause.kind, ErrorKind::Conflict | ErrorKind::Busy))
        {
            if self.rollback().is_err() {
                return Err(Error::unavailable("事务提交失败且无法回滚。"));
            }
        } else if result.is_err() {
            self.failed.set(true);
        } else {
            self.in_transaction.set(false);
        }
        result
    }
    pub fn rollback(&self) -> Result<()> {
        let result = self.adapter.rollback();
        if result.is_err() {
            self.failed.set(true);
        } else {
            self.in_transaction.set(false);
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
        impl Connection{$(pub fn $name(&self,$($arg:$ty),*)->Result<$result>{self.observed(|adapter| adapter.$name($($arg),*))})+}
    }
}
impl Connection {
    pub fn append_audit(&self, audit: &AuditWrite<'_>) -> Result<()> {
        self.append_audit_details(audit, &serde_json::json!({}))
    }
}
delegate! {
    query_jobs(query:&JobQuery<'_>)->(i64,Vec<Value>);
    job_counts()->Value;
    query_generic(query:&GenericQuery<'_>)->(i64, Vec<Value>);
    query_report_templates(query: &ReportTemplateQuery<'_>)->(i64, Vec<Value>);
    report_template_metadata(id:i64)->Option<Value>;
    query_template_versions(query: &TemplateVersionQuery<'_>)->(i64, Vec<Value>);
    template_version(kind:&str,id:i64,version:i64)->Option<Value>;
    query_communications(query: &CommunicationQuery<'_>)->(i64, Vec<Value>);
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
