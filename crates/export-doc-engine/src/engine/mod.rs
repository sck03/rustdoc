mod account_permissions;
mod accounts;
#[cfg(feature = "ai")]
mod ai;
mod archive;
mod attachment_categories;
mod attachments;
pub mod audit;
mod audit_values;
mod auth;
mod authenticated;
#[cfg(test)]
mod authorization_tests;
pub mod backup_io;
mod capabilities;
pub mod catalog;
mod command_receipts;
pub mod communication;
mod crm;
mod crm_dashboard;
mod crm_queries;
mod custom_options;
mod dashboard;
mod diagnostics;
mod document_formats;
mod document_packages;
mod document_upload;
mod downloads;
mod electronic_evidence;
#[cfg(feature = "mail")]
mod email;
#[cfg(feature = "mail")]
mod email_templates;
pub mod error;
#[cfg(feature = "excel")]
pub mod excel;
#[cfg(feature = "exchange-rates")]
mod exchange;
mod file_store;
mod handling;
mod hs;
#[cfg(feature = "excel")]
mod hs_files;
mod hs_history;
mod hs_learning;
#[cfg(feature = "excel")]
mod hs_package;
mod hs_queries;
#[cfg(feature = "hs-remote")]
mod hs_remote;
mod hs_search;
mod import_previews;
mod invoice_maintenance;
mod invoice_query;
#[cfg(feature = "invoice-transfer")]
mod invoice_transfer;
mod letter_of_credit;
mod licensing;
mod lifecycle;
mod maintenance;
mod media;
mod operation_lock;
mod request_values;
#[cfg(feature = "s3-storage")]
mod s3_transport;
pub use media::{SEAL_LIMIT, read_seal_image};
pub mod oa;
#[cfg(feature = "ocr")]
pub(crate) mod ocr;
mod office;
mod office_events;
mod office_queries;
mod office_workflows;
mod operation_sets;
mod organization;
mod organization_scope;
mod packing;
#[cfg(feature = "excel")]
mod party_files;
mod pdf_merge;
mod permission_templates;
mod personnel;
mod personnel_accounts;
mod personnel_files;
mod personnel_labels;
mod personnel_organization;
mod personnel_primary;
mod personnel_privacy;
mod personnel_queries;
mod personnel_reporting;
mod product_options;
mod record_documents;
mod records;
mod related_records;
mod report_assets;
mod report_template_files;
mod report_templates;
pub mod reports;
mod sales;
mod scheduler_health;
mod scope_policies;
mod settings;
#[cfg(feature = "single-window")]
mod single_window;
mod store;
mod supplier_overview;
pub mod tasks;
mod team_backup;
#[cfg(test)]
pub(crate) mod test_support;
mod units;
mod workflow_tasks;
pub use team_backup::UploadAuthorization;
#[cfg(feature = "postgres")]
pub use team_backup::migration::stage_file as stage_postgres_migration_file;
pub use team_backup::postgres::apply_pending as apply_pending_postgres_restore;
mod workflows;
mod worklist;

use crate::contracts;
use crate::{generated_api::*, paths::RuntimePaths};
use error::{Result, error, invalid, unavailable, unsupported};
pub use operation_sets::{OFFICE_ACTIONS, SPECIAL_OPERATIONS};
use serde_json::{Value, json};
use std::sync::{Arc, RwLock};
use store::{Actor, Store};

pub struct NativeService {
    maintenance: RwLock<()>,
    pub paths: RuntimePaths,
    report_fonts: Arc<export_doc_report::Fonts>,
    report_previews: reports::page_preview::Cache,
    protector: crate::secrets::Protector,
    store: Arc<Store>,
    sessions: auth::Sessions,
    jobs: tasks::Jobs,
    workflow_scheduler: tasks::scheduled::Scheduler,
    backup_scheduler: team_backup::scheduled::Scheduler,
    backup_tickets: team_backup::Tickets,
    bootstrap_token: String,
    clock: crate::clock::BusinessClock,
    license_gate: std::sync::Mutex<()>,
    license_cache: licensing::SignatureCache,
    #[cfg(feature = "ai")]
    ai_reviewer: ai::Reviewer,
    packing_gate: std::sync::Mutex<()>,
    oa_document_gate: std::sync::Mutex<()>,
    #[cfg(feature = "ocr")]
    ocr_gate: std::sync::Mutex<()>,
    #[cfg(feature = "exchange-rates")]
    exchange: export_doc_exchange::ExchangeRates,
    #[cfg(feature = "exchange-rates")]
    exchange_scheduler: exchange::Scheduler,
}
impl NativeService {
    pub(crate) fn authorize_operation(
        &self,
        actor: &Actor,
        operation: Operation,
        query: &[(&str, String)],
    ) -> Result<()> {
        auth::authorize_operation(actor, operation, query)?;
        licensing::check_operation(self, operation)
    }
    pub fn provider(&self) -> Result<&'static str> {
        self.store.provider()
    }
    pub fn session_actor(&self, token: &str) -> Result<Actor> {
        self.sessions.actor(&self.store, token)
    }
    /// Session management remains available after a unit grant expires. Business
    /// endpoints always resolve the selected context and never fall back to it.
    pub fn session_actor_for_operation(&self, token: &str, operation: Operation) -> Result<Actor> {
        if matches!(operation, SWITCH_UNIT_CONTEXT | LOGOUT) {
            self.sessions.account_actor(&self.store, token)
        } else {
            self.session_actor(token)
        }
    }
    /// Reject unauthorized requests before their potentially large body is received.
    /// Use cases still revalidate current permissions and record scope at publication.
    pub fn authorize_request_body(
        &self,
        token: &str,
        operation: Operation,
        query: &[(&str, String)],
    ) -> Result<()> {
        let actor = self.session_actor_for_operation(token, operation)?;
        self.authorize_operation(&actor, operation, query)
    }
    pub fn authorize_administrator(&self, token: &str) -> Result<()> {
        auth::authorize(
            &self.sessions.actor(&self.store, token)?,
            "system.settings",
            "manage",
        )
    }
    pub fn dispatch_with_bootstrap(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
        token: &str,
        bootstrap_token: &str,
    ) -> Result<Vec<u8>> {
        if operation == LOGIN {
            let request = body.as_ref().ok_or_else(|| invalid("缺少登录请求。"))?;
            auth::bootstrap(
                &self.store,
                &records::text(request, "username"),
                request["password"].as_str().unwrap_or(""),
                &self.bootstrap_token,
                bootstrap_token,
            )?;
        }
        self.dispatch(operation, parameters, query, body, token)
    }
    pub fn dispatch(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
        token: &str,
    ) -> Result<Vec<u8>> {
        if let Some(probe) = export_doc_contracts::request_limits::probe(operation.id) {
            if probe == export_doc_contracts::request_limits::Probe::Readiness {
                self.health()?;
            }
            return Ok(Vec::new());
        }
        #[cfg(feature = "excel")]
        if operation == EXPORT_HS_CODE_KNOWLEDGE {
            crate::operation::check()?;
            return self
                .prepare_file_download(operation, query, token)?
                .into_memory()
                .map(|output| output.content);
        }
        if operation == RESTORE_DATABASE_BACKUP {
            let actor = self.session_actor_for_operation(token, operation)?;
            self.authorize_operation(&actor, operation, query)?;
            if self.provider()? != "SQLite" {
                return Err(unsupported(
                    "此恢复入口仅支持 SQLite；PostgreSQL 请使用独立维护恢复。",
                ));
            }
        }
        let _read = if operation != RESTORE_DATABASE_BACKUP {
            Some(crate::engine::maintenance::acquire_read(&self.maintenance)?)
        } else {
            None
        };
        let _write = if operation == RESTORE_DATABASE_BACKUP {
            Some(maintenance::acquire(&self.maintenance)?)
        } else {
            None
        };
        if !ALL_OPERATIONS.contains(&operation) {
            return Err(invalid("请求不属于已生成的 API 契约。"));
        }
        crate::operation::check()?;
        let authenticated_actor = if operation.requires_authentication {
            Some(self.session_actor_for_operation(token, operation)?)
        } else {
            None
        };
        if !Self::supports(operation) {
            return Err(unsupported(format!(
                "此功能尚未完成 Rust 原生迁移：{}。",
                operation.id
            )));
        }
        // Every JSON use-case result crosses the same bounded encoder.
        macro_rules! respond_json {
            ($result:expr) => {
                return crate::json_response::encode(operation, &$result?);
            };
        }
        if let Some(actor) = &authenticated_actor {
            self.authorize_operation(actor, operation, query)?;
        }
        let body_value = body.unwrap_or_else(|| json!({}));
        if contracts::contract()["operations"][operation.id]["validatesDecimalInput"]
            .as_bool()
            .expect("generated decimal input metadata")
        {
            export_doc_contracts::validation::provided_fields(
                contracts::request(operation.id),
                &body_value,
            )
            .map_err(invalid)?;
        }
        let value = match operation {
            LOGIN => self.sessions.login(
                &self.store,
                &self.clock,
                &records::text(&body_value, "username"),
                body_value["password"].as_str().unwrap_or(""),
            )?,
            GET_HEALTH => {
                self.health()?;
                diagnostics::health(&self.paths, self.provider()?)?
            }
            _ => {
                let actor = match authenticated_actor {
                    Some(actor) => actor,
                    None => {
                        let actor = self.session_actor_for_operation(token, operation)?;
                        self.authorize_operation(&actor, operation, query)?;
                        actor
                    }
                };
                if operation == GET_RUNTIME_METRICS {
                    auth::authorize(&actor, "system.settings", "view")?;
                    respond_json!(self.runtime_metrics());
                }
                if communication::metadata(operation).is_some() {
                    respond_json!(communication::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if oa::metadata(operation).is_some() {
                    respond_json!(oa::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value
                    ));
                }
                if operation == START_PDF_MERGE_SAVE_TO_PATH_JOB {
                    respond_json!(pdf_merge::local(self, &actor, &body_value));
                }
                if licensing::OPERATIONS.contains(&operation) {
                    respond_json!(licensing::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if team_backup::OPERATIONS.contains(&operation) {
                    respond_json!(team_backup::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                #[cfg(feature = "invoice-transfer")]
                if invoice_transfer::OPERATIONS.contains(&operation) {
                    return invoice_transfer::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        &body_value,
                    );
                }
                #[cfg(feature = "excel")]
                if invoice_query::EXPORTS.contains(&operation) {
                    respond_json!(invoice_query::export(self, &actor, operation, &body_value));
                }
                #[cfg(feature = "single-window")]
                if single_window::OPERATIONS.contains(&operation) {
                    return single_window::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    );
                }
                #[cfg(feature = "ai")]
                if operation == REVIEW_LETTER_OF_CREDIT_COMPLIANCE {
                    respond_json!(self.ai_reviewer.review(
                        &self.store,
                        &self.protector,
                        &self.paths.cache_root,
                        &body_value,
                    ));
                }
                if operation == IMPORT_LETTER_OF_CREDIT_DOCUMENT {
                    respond_json!(letter_of_credit::local(self, &body_value));
                }
                #[cfg(feature = "ocr")]
                if operation == RECOGNIZE_OCR_IMAGE {
                    respond_json!(ocr::local(self, &body_value));
                }
                #[cfg(feature = "mail")]
                if email::OPERATIONS.contains(&operation) {
                    respond_json!(email::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value
                    ));
                }
                #[cfg(feature = "mail")]
                if email_templates::OPERATIONS.contains(&operation) {
                    respond_json!(email_templates::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if document_packages::OPERATIONS.contains(&operation) {
                    respond_json!(document_packages::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        &body_value,
                    ));
                }
                #[cfg(feature = "hs-remote")]
                if hs_remote::OPERATIONS.contains(&operation) {
                    respond_json!(hs_remote::handle(
                        self,
                        &actor,
                        operation,
                        query,
                        &body_value
                    ));
                }
                #[cfg(feature = "excel")]
                if hs_files::OPERATIONS.contains(&operation) {
                    respond_json!(hs_files::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value
                    ));
                }
                if hs::OPERATIONS.contains(&operation) {
                    respond_json!(hs::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value
                    ));
                }
                if packing::OPERATIONS.contains(&operation) {
                    return packing::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    );
                }
                if attachment_categories::OPERATIONS.contains(&operation) {
                    respond_json!(attachment_categories::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if related_records::OPERATIONS.contains(&operation) {
                    respond_json!(related_records::handle(
                        &self.store,
                        &actor,
                        &self.clock,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                #[cfg(feature = "excel")]
                if party_files::OPERATIONS.contains(&operation) {
                    return party_files::handle(self, &actor, operation, query, &body_value);
                }
                if invoice_maintenance::OPERATIONS.contains(&operation) {
                    respond_json!(invoice_maintenance::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        &body_value,
                    ));
                }
                if operation == SEARCH_SUPPLIER_PRODUCT_OPTIONS {
                    respond_json!(product_options::search(&self.store, &actor, query));
                }
                if crm::OPERATIONS.contains(&operation) {
                    respond_json!(crm::handle(
                        &self.store,
                        &actor,
                        &self.clock,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if sales::OPERATIONS.contains(&operation) {
                    respond_json!(sales::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                #[cfg(feature = "exchange-rates")]
                if exchange::OPERATIONS.contains(&operation) {
                    respond_json!(exchange::handle(self, &actor, operation, query));
                }
                let is_audit = audit::OPERATIONS.contains(&operation);
                #[cfg(feature = "excel")]
                let is_audit = is_audit || audit::EXPORTS.contains(&operation);
                if is_audit {
                    return audit::handle(self, &actor, operation, query, &body_value);
                }
                if report_templates::OPERATIONS.contains(&operation) {
                    respond_json!(report_templates::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if reports::OPERATIONS.contains(&operation) {
                    respond_json!(reports::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value
                    ));
                }
                if operation == DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE {
                    return Ok(report_assets::download(self, &actor, parameters)?.content);
                }
                if report_assets::OPERATIONS.contains(&operation) {
                    respond_json!(report_assets::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                if report_template_files::OPERATIONS.contains(&operation) {
                    respond_json!(report_template_files::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    ));
                }
                #[cfg(feature = "excel")]
                if excel::OPERATIONS.contains(&operation) {
                    respond_json!(excel::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        &body_value
                    ));
                }
                if operation == DOWNLOAD_PERSONNEL_ATTACHMENT {
                    return Ok(personnel_files::download(&self.store, &actor, parameters)?.content);
                }
                if operation == DELETE_PERSONNEL_ATTACHMENT {
                    return crate::json_response::encode(
                        operation,
                        &contracts::dto(
                            contracts::response(operation.id),
                            personnel_files::remove(&self.store, &actor, parameters, &body_value)?,
                        ),
                    );
                }
                if personnel::OPERATIONS.contains(&operation) {
                    return personnel::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    );
                }
                if attachments::OPERATIONS.contains(&operation) {
                    return attachments::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        body_value,
                    );
                }
                if [
                    GET_JOB,
                    CANCEL_JOB,
                    RETRY_JOB,
                    DELETE_JOB,
                    DOWNLOAD_JOB_RESULT,
                ]
                .contains(&operation)
                {
                    let job_id = crate::engine::request_values::find(parameters, "jobId")
                        .ok_or_else(|| invalid("缺少文件任务编号。"))?;
                    if operation == RETRY_JOB {
                        respond_json!(tasks::retry::execute(self, &actor, job_id));
                    }
                    return self.jobs.operation(operation, &actor, job_id);
                }
                if workflows::ACTIONS.contains(&operation) {
                    workflows::action(
                        &self.store,
                        &actor,
                        operation,
                        records::id(parameters)?,
                        body_value,
                        self.clock.now().map_err(unavailable)?.today,
                    )?
                } else if OFFICE_ACTIONS.contains(&operation) {
                    office_workflows::action(
                        self,
                        &actor,
                        operation.id,
                        records::id(parameters)?,
                        body_value,
                        self.clock.now().map_err(unavailable)?.today,
                    )?
                } else {
                    self.authenticated(operation, parameters, query, body_value, &actor, token)?
                }
            }
        };
        crate::json_response::encode(
            operation,
            &contracts::project(contracts::response(operation.id), value),
        )
    }
}
