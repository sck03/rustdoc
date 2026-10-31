#[cfg(feature = "postgres")]
mod postgres;
#[cfg(feature = "postgres")]
pub use postgres::tools::{
    ClientParameters as PostgresClientParameters, MaintenanceLease as PostgresMaintenanceLease,
    client_parameters as postgres_client_parameters,
};
mod audit_query;
mod business_query;
pub use audit_query::AuditQuery;
mod crm_query;
pub use crm_query::{CrmList, CrmQuery};
mod communication;
mod generic_query;
pub use business_query::{BusinessList, BusinessQuery};
mod hs_query;
mod import_preview_query;
pub use import_preview_query::ImportPreviewUsage;
mod job_query;
pub use hs_query::HsQuery;
mod office_query;
mod ordered_query;
mod organization_query;
pub use ordered_query::RecordOrder;
pub use organization_query::OrganizationReference;
mod personnel_links;
mod personnel_memberships;
mod personnel_query;
mod resource_open_query;
mod supplier_overview_query;
pub use job_query::{JobOutputRetention, JobQuery, JobRetention};
pub use office_query::OfficeQuery;
pub use personnel_query::PersonnelQuery;
mod record_query;
mod related_query;
mod resource_query;
pub use generic_query::ScopedQuery;
pub use related_query::RelatedQuery;
mod sql;
mod template_history_query;
mod template_query;
pub use generic_query::{DataScope, GenericQuery, ScopeFilter};
pub use template_history_query::TemplateHistoryUsage;
pub mod maintenance;
pub mod metrics;
pub mod operation;
pub mod pool;
pub use template_query::{ReportTemplateQuery, TemplateAudience, TemplateVersionQuery};
struct QuerySql {
    filter: String,
    order: String,
    values: Vec<String>,
}
mod access_scope;
mod command_receipts;
mod foundation;
mod identity_runtime;
mod permission_versions;
mod schema;
mod workflow;
mod workflow_runtime;
mod workflow_schedule;
pub use communication::{CommunicationQuery, CommunicationView, NotificationScope};
pub use workflow::{WorkflowProgress, WorkflowSummaryQuery, WorkflowTaskQuery};
mod sqlite;
pub use schema::{MIN_SUPPORTED_SCHEMA_VERSION, SCHEMA_VERSION};
use serde_json::Value;
use std::{cell::Cell, path::Path};

pub type Result<T> = std::result::Result<T, Error>;
pub fn validate_schema_version(version: i64) -> Result<()> {
    schema::validate(version)
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
    pub context: String,
}
/// Indexed, bounded reads with authorization scope applied before pagination.
#[derive(Default)]
pub struct RecordQuery<'a> {
    pub restrictions: Vec<ScopeFilter>,
    pub kind: &'a str,
    pub company: &'a str,
    pub department: Option<&'a str>,
    pub departments: Option<&'a [String]>,
    pub owner: Option<i64>,
    pub employee: Option<i64>,
    /// Candidate OA periods: inclusive business dates for leave/travel, exact
    /// RFC 3339 instants for overtime. Values are parameter-bound by adapters.
    pub period: Option<(&'a str, &'a str)>,
    pub parent: Option<i64>,
    pub status: Option<&'a str>,
    pub statuses: &'a [&'a str],
    pub approvers: Option<&'a [RecordApprover]>,
    pub approval_actor: Option<i64>,
    pub visible_user: Option<i64>,
    /// None is reserved for internal account-wide responsibility audits.
    pub visible_context: Option<&'a str>,
    pub visible_principals: &'a [RecordApprover],
    /// Explicit participation extends ordinary visibility, without changing its scope.
    pub include_participants: bool,
    pub handling_key: Option<&'a str>,
    /// Additional assigned-service access, combined with the ordinary owner/department scope.
    pub handling_keys: Option<&'a [String]>,
    pub handling_states: &'a [&'a str],
    pub handling_only: bool,
    /// Project after authorization and pagination; an empty list returns the body.
    pub fields: &'a [(&'a str, &'a str)],
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
pub struct BlobInfo {
    pub file_name: String,
    pub media_type: String,
    pub digest: String,
    pub size: u64,
}
pub struct Credential {
    pub salt: Vec<u8>,
    pub hash: Vec<u8>,
    pub iterations: u32,
}

use adapter::Adapter;
mod adapter;
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
        let result = operation::check()
            .and_then(|_| self.health())
            .and_then(|_| operation(&*self.adapter));
        let result = operation::check().and(result);
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
        let result = operation::without_control(|| self.adapter.rollback());
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
    access_restrictions(kind:&str,id:i64,resolved:bool)->Vec<Value>;
    save_access_restriction(kind:&str,id:i64,body:&Value)->Value;
    permission_scheme_versions(scheme:i64,offset:i64,limit:i64)->Value;
    permission_scheme_version(scheme:i64,version:i64)->Option<Value>;
    publish_permission_scheme(scheme:i64,body:&Value,actor:i64,time:&str)->i64;
    foundation_candidates(unit:i64,keyword:&str,date:&str,offset:i64,limit:i64)->Value;
    foundation_list(kind:&str,unit:i64,scope:&DataScope<'_>,offset:i64,limit:i64)->Value;
    foundation_appointment(unit:i64,key:&str)->Option<Value>;
    foundation_get(kind:&str,unit:i64,id:i64)->Option<Value>;
    foundation_save(kind:&str,id:Option<i64>,unit:i64,expected:i64,body:&Value)->Value;
    responsibility_appointments(unit:i64,responsibility:i64,project:Option<i64>,kind:&str,date:&str)->Vec<Value>;
    records_by_ids(kind:&str,ids:&[i64],fields:&[(&str,&str)])->Vec<Value>;
    unit_accounts(company:&str,date:&str,all:bool)->Vec<Value>;
    unit_account_page(company:&str,date:&str,all:bool,before:Option<i64>,limit:i64)->Vec<Value>;
    resource_has_open_requests(kind:&str,parent:i64)->bool;
    supplier_overview(scope:&DataScope<'_>)->Value;
    query_workflow_summary(query:&WorkflowSummaryQuery<'_>)->(i64,Vec<Value>);
    query_workflow_tasks(query:&WorkflowTaskQuery<'_>)->(i64,Vec<Value>);
    freeze_request_revision(id:i64,snapshot:&Value)->();
    due_personnel_orders(now:&str)->Vec<Value>;
    due_workflow_nodes(now:&str)->Vec<Value>;
    schedule_personnel_order(entry:&Value)->();
    personnel_order_state(id:i64,state:&str,error:&str)->();
    personnel_order(id:i64)->Option<Value>;
    workflow_reminder_level(entry:&Value,level:i64)->bool;
    command_receipt(actor:i64,context:&str,key:&str)->Option<Value>;
    put_command_receipt(receipt:&Value,result:&Value)->();
    append_workflow_operation(entry:&Value)->();
    save_workflow_state(request:i64,revision:i64,progress:&WorkflowProgress<'_>,plan:&Value,active_tasks:&[String])->();
    query_audits(query:&AuditQuery<'_>)->(i64,Vec<Value>);
    query_ordered_records(query:&GenericQuery<'_>,order:RecordOrder)->(i64,Vec<Value>);
    record_exists(kind:&str,filters:&[(&str,String)])->bool;
    organization_referenced(reference:OrganizationReference,code:&str,company:&str,exclude:i64,kinds:&[&str])->bool;
    personnel_exists(query:&PersonnelQuery<'_>)->bool;
    personnel_for_accounts(users:&[i64],fields:&[(&str,&str)])->Vec<Value>;
    appointment_references(people:&[i64])->Vec<Value>;
    attachment_category_counts(company:&str)->std::collections::BTreeMap<i64,i64>;
    import_preview_usage(owner:i64)->ImportPreviewUsage;
    expired_import_previews(before:&str)->Vec<Value>;
    query_hs(query:&HsQuery<'_>)->Vec<Value>;
    query_jobs(query:&JobQuery<'_>)->(i64,Vec<Value>);
    job_counts()->Value;
    job_output_bytes()->u64;
    job_input_bytes()->u64;
    expired_job_outputs(policy:&JobOutputRetention)->Vec<Value>;
    expired_job_inputs(policy:&JobOutputRetention)->Vec<Value>;
    query_generic(query:&GenericQuery<'_>)->(i64, Vec<Value>);
    scan_generic(query:&GenericQuery<'_>,before_id:Option<i64>)->(Option<i64>, Vec<Value>);
    resource_references(kind:&str,offset:i64,limit:i64)->(i64,Vec<Value>);
    query_scoped(query:&ScopedQuery<'_>)->(i64,Vec<Value>);
    query_business(query:&BusinessQuery<'_>)->(i64, Vec<Value>);
    query_crm(query:&CrmQuery<'_>)->(i64, Vec<Value>);
    query_related(query:&RelatedQuery<'_>)->(i64, Vec<Value>);
    scan_crm(query:&CrmQuery<'_>,after_id:i64)->Vec<Value>;
    query_personnel(query:&PersonnelQuery<'_>)->(i64, Vec<Value>);
    query_office(query:&OfficeQuery<'_>)->(i64, Vec<Value>);
    query_report_templates(query: &ReportTemplateQuery<'_>)->(i64, Vec<Value>);
    report_template_metadata(id:i64)->Option<Value>;
    query_template_versions(query: &TemplateVersionQuery<'_>)->(i64, Vec<Value>);
    template_history_usage(query:&TemplateVersionQuery<'_>,keep:i64)->TemplateHistoryUsage;
    template_history_candidates(query:&TemplateVersionQuery<'_>)->Vec<Value>;
    query_email_template_versions(query: &TemplateVersionQuery<'_>)->(i64, Vec<Value>);
    template_version(kind:&str,id:i64,version:i64)->Option<Value>;
    query_communications(query: &CommunicationQuery<'_>)->(i64, Vec<Value>);
    checkpoint()->();
    get(kind:&str,id:i64)->Option<Value>;
    find_identity(kind:&str,identity:&str)->Option<Value>;
    all(kind:&str)->Vec<Value>;
    insert(record:&RecordWrite<'_>)->i64;
    personnel_private_profile(id:i64)->Option<Value>;
    set_personnel_private_profile(id:i64,body:&Value)->();
    update(id:i64,expected:i64,record:&RecordWrite<'_>)->bool;
    set_body(id:i64,body:&Value)->();
    delete(kind:&str,id:i64,expected:i64)->bool;
    append_audit_details(audit:&AuditWrite<'_>,details:&Value)->();
    audit_events(before_id:i64,limit:i64)->Vec<Value>;
    delete_audits(ids:&[i64])->usize;
    history(kind:Option<&str>,id:Option<i64>)->Vec<Value>;
    has_history_except(kind:&str,id:i64,allowed:&[&str])->bool;
    credential(id:i64)->Option<Credential>;
    set_credential(id:i64,salt:&[u8],hash:&[u8],iterations:u32)->();
    settings(name:&str)->Option<Value>;
    set_settings(name:&str,version:i64,body:&Value)->();
    attachment_bytes(invoice_id:i64)->i64;
    insert_blob(blob:&BlobWrite<'_>)->();
    blob(record_id:i64,kind:&str)->Option<Blob>;
    blob_info(record_id:i64,kind:&str)->Option<BlobInfo>;
    delete_blobs(record_id:i64)->();
    delete_blob(record_id:i64,kind:&str)->();
    backup(path:&Path)->();
    verify_backup(path:&Path)->();
}
