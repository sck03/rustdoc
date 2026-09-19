mod accounts;
#[cfg(feature = "ai")]
mod ai;
mod attachment_categories;
mod attachments;
pub mod audit;
mod audit_values;
mod auth;
mod capabilities;
pub mod catalog;
mod crm;
mod crm_dashboard;
mod custom_options;
mod dashboard;
mod diagnostics;
mod document_packages;
#[cfg(feature = "mail")]
mod email;
#[cfg(feature = "mail")]
mod email_templates;
pub mod error;
#[cfg(feature = "excel")]
pub mod excel;
#[cfg(feature = "exchange-rates")]
mod exchange;
mod hs;
#[cfg(feature = "excel")]
mod hs_files;
mod hs_learning;
#[cfg(feature = "excel")]
mod hs_package;
#[cfg(feature = "hs-remote")]
mod hs_remote;
mod hs_search;
#[cfg(feature = "excel")]
mod import_previews;
mod invoice_maintenance;
mod invoice_query;
#[cfg(feature = "invoice-transfer")]
mod invoice_transfer;
mod letter_of_credit;
mod licensing;
mod maintenance;
mod media;
#[cfg(feature = "ocr")]
mod ocr;
mod office;
mod office_events;
mod office_queries;
mod office_workflows;
mod operation_sets;
mod organization;
mod packing;
#[cfg(feature = "excel")]
mod party_files;
mod pdf_merge;
mod permission_templates;
mod personnel;
mod personnel_queries;
mod product_options;
mod records;
mod related_records;
mod report_assets;
#[allow(dead_code)]
mod report_template_files;
mod report_templates;
pub mod reports;
mod sales;
mod settings;
#[cfg(feature = "single-window")]
mod single_window;
mod store;
mod supplier_overview;
pub mod tasks;
#[allow(dead_code)]
mod team_backup;
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
    protector: crate::secrets::Protector,
    store: Arc<Store>,
    sessions: auth::Sessions,
    jobs: tasks::Jobs,
    bootstrap_token: String,
    clock: crate::clock::BusinessClock,
    packing_gate: std::sync::Mutex<()>,
    #[cfg(feature = "ocr")]
    ocr_gate: std::sync::Mutex<()>,
    #[cfg(feature = "exchange-rates")]
    exchange: export_doc_exchange::ExchangeRates,
}
impl NativeService {
    pub fn open(paths: RuntimePaths) -> Result<Arc<Self>> {
        Self::open_with_retention(paths, Default::default())
    }
    pub fn open_with_retention(
        paths: RuntimePaths,
        retention: tasks::retention::Retention,
    ) -> Result<Arc<Self>> {
        let store = Arc::new(Store::open(&paths)?);
        auth::seed(&store)?;
        packing::seed(&store)?;
        #[cfg(feature = "mail")]
        email::recover(&store)?;
        let jobs = tasks::Jobs::with_retention(store.clone(), retention)?;
        Ok(Arc::new(Self {
            maintenance: RwLock::new(()),
            protector: crate::secrets::Protector::new(&paths.data_root),
            paths,
            store,
            sessions: auth::Sessions::default(),
            jobs,
            bootstrap_token: String::new(),
            clock: crate::clock::BusinessClock::default(),
            packing_gate: Default::default(),
            #[cfg(feature = "ocr")]
            ocr_gate: Default::default(),
            #[cfg(feature = "exchange-rates")]
            exchange: Default::default(),
        }))
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres(
        paths: RuntimePaths,
        connection_string: &str,
        bootstrap_token: String,
        clock: crate::clock::BusinessClock,
    ) -> Result<Arc<Self>> {
        Self::open_postgres_with_retention(
            paths,
            connection_string,
            bootstrap_token,
            clock,
            Default::default(),
        )
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres_with_retention(
        paths: RuntimePaths,
        connection_string: &str,
        bootstrap_token: String,
        clock: crate::clock::BusinessClock,
        retention: tasks::retention::Retention,
    ) -> Result<Arc<Self>> {
        let store = Arc::new(Store::open_postgres(connection_string)?);
        packing::seed(&store)?;
        #[cfg(feature = "mail")]
        email::recover(&store)?;
        if store.all("users")?.is_empty() && bootstrap_token.len() < 32 {
            return Err(invalid("空库首次启动需要至少 32 字符的一次性部署令牌。"));
        }
        let jobs = tasks::Jobs::with_retention(store.clone(), retention)?;
        Ok(Arc::new(Self {
            maintenance: RwLock::new(()),
            protector: crate::secrets::Protector::new(&paths.data_root),
            paths,
            store,
            sessions: auth::Sessions::default(),
            jobs,
            bootstrap_token,
            clock,
            packing_gate: Default::default(),
            #[cfg(feature = "ocr")]
            ocr_gate: Default::default(),
            #[cfg(feature = "exchange-rates")]
            exchange: Default::default(),
        }))
    }
    pub fn health(&self) -> Result<()> {
        self.jobs.health()?;
        self.store.connection()?.health().map_err(Into::into)
    }
    pub fn download_job(&self, job_id: &str, token: &str) -> Result<tasks::FileOutput> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        self.jobs.download(&actor, job_id)
    }
    pub fn provider(&self) -> Result<&'static str> {
        self.store.provider()
    }
    /// Native preview uses the same use case and permissions as HTTP HTML
    /// preview, then encodes its layout as PDF without writing a business row.
    pub fn preview_report_pdf(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        body: &Value,
        token: &str,
    ) -> Result<Vec<u8>> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        auth::authorize_operation(&actor, operation, &[])?;
        let (document, _) = reports::preview_document(self, &actor, operation, parameters, body)?;
        export_doc_report::pdf_document(
            &document,
            &self.paths.font_path,
            &crate::operation::cancellation_flag(),
        )
        .map_err(Into::into)
    }
    pub fn session_actor(&self, token: &str) -> Result<Actor> {
        self.sessions.actor(&self.store, token)
    }
    pub fn document_package_preview_pdf(
        &self,
        actor: &Actor,
        invoice_id: i64,
        body: &Value,
    ) -> Result<Vec<u8>> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        document_packages::preview_pdf(self, actor, invoice_id, body)
    }
    pub fn download_report_resource(
        &self,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<tasks::FileOutput> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        auth::authorize_operation(&actor, DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE, &[])?;
        report_assets::download(&self.store, &actor, parameters)
    }
    /// Direct file downloads that are not backed by a persisted job result.
    pub fn download_file(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<tasks::FileOutput> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        auth::authorize_operation(&actor, operation, &[])?;
        match operation {
            DOWNLOAD_REPORT_TEMPLATE_FILE | DOWNLOAD_REPORT_TEMPLATE_PACKAGE => {
                report_template_files::download(self, &actor, operation, parameters)
            }
            DOWNLOAD_SUPPORT_PACKAGE => licensing::download(self, &actor, operation, parameters),
            DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET => {
                team_backup::download(self, &actor, operation, parameters)
            }
            _ => Err(unsupported("此下载尚未完成原生迁移。")),
        }
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
                &records::text(request, "password"),
                &self.bootstrap_token,
                bootstrap_token,
            )?;
        }
        self.dispatch(operation, parameters, query, body, token)
    }
    pub fn close(&self) -> Result<()> {
        self.jobs.close()?;
        self.store.close()
    }
    pub fn dispatch(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
        token: &str,
    ) -> Result<Vec<u8>> {
        let _read = if operation != RESTORE_DATABASE_BACKUP {
            Some(
                self.maintenance
                    .read()
                    .map_err(|_| unavailable("数据库维护状态异常。"))?,
            )
        } else {
            None
        };
        let _write = if operation == RESTORE_DATABASE_BACKUP {
            Some(
                self.maintenance
                    .write()
                    .map_err(|_| unavailable("数据库维护状态异常。"))?,
            )
        } else {
            None
        };
        if !ALL_OPERATIONS.contains(&operation) {
            return Err(invalid("请求不属于已生成的 API 契约。"));
        }
        crate::operation::check()?;
        if operation.requires_authentication {
            self.sessions.actor(&self.store, token)?;
        }
        if !Self::supports(operation) {
            return Err(unsupported(format!(
                "此功能尚未完成 Rust 原生迁移：{}。",
                operation.id
            )));
        }
        let body_value = body.clone().unwrap_or_else(|| json!({}));
        let value = match operation {
            LOGIN => self.sessions.login(
                &self.store,
                &self.clock,
                &records::text(&body_value, "username"),
                &records::text(&body_value, "password"),
            )?,
            GET_HEALTH | GET_LIVENESS | GET_READINESS => {
                self.health()?;
                diagnostics::health(&self.paths, self.provider()?)?
            }
            RUN_SHUTDOWN_MAINTENANCE => {
                self.close()?;
                json!({"success":true,"message":"关闭维护完成"})
            }
            _ => {
                let actor = self.sessions.actor(&self.store, token)?;
                auth::authorize_operation(&actor, operation, query)?;
                if operation == START_PDF_MERGE_SAVE_TO_PATH_JOB {
                    return serde_json::to_vec(&pdf_merge::local(self, &actor, &body_value)?)
                        .map_err(Into::into);
                }
                if licensing::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&licensing::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if team_backup::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&team_backup::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
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
                    return serde_json::to_vec(&invoice_query::export(
                        self,
                        &actor,
                        operation,
                        &body_value,
                    )?)
                    .map_err(Into::into);
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
                    return serde_json::to_vec(&ai::review(self, &body_value)?).map_err(Into::into);
                }
                if operation == IMPORT_LETTER_OF_CREDIT_DOCUMENT {
                    return serde_json::to_vec(&letter_of_credit::local(self, &body_value)?)
                        .map_err(Into::into);
                }
                #[cfg(feature = "ocr")]
                if operation == RECOGNIZE_OCR_IMAGE {
                    return serde_json::to_vec(&ocr::local(self, &body_value)?).map_err(Into::into);
                }
                #[cfg(feature = "mail")]
                if email::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&email::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                #[cfg(feature = "mail")]
                if email_templates::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&email_templates::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if document_packages::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&document_packages::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                #[cfg(feature = "hs-remote")]
                if hs_remote::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&hs_remote::handle(
                        self,
                        &actor,
                        operation,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                #[cfg(feature = "excel")]
                if operation == EXPORT_HS_CODE_KNOWLEDGE {
                    return hs_package::export(self, query);
                }
                #[cfg(feature = "excel")]
                if hs_files::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&hs_files::handle(
                        self,
                        &actor,
                        operation,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if hs::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&hs::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
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
                    return serde_json::to_vec(&attachment_categories::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if related_records::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&related_records::handle(
                        &self.store,
                        &actor,
                        &self.clock,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                #[cfg(feature = "excel")]
                if party_files::OPERATIONS.contains(&operation) {
                    return party_files::handle(self, &actor, operation, query, &body_value);
                }
                if invoice_maintenance::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&invoice_maintenance::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if operation == SEARCH_SUPPLIER_PRODUCT_OPTIONS {
                    return serde_json::to_vec(&product_options::search(
                        &self.store,
                        &actor,
                        query,
                    )?)
                    .map_err(Into::into);
                }
                if crm::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&crm::handle(
                        &self.store,
                        &actor,
                        &self.clock,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if sales::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&sales::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                #[cfg(feature = "exchange-rates")]
                if exchange::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&exchange::handle(self, &actor, operation, query)?)
                        .map_err(Into::into);
                }
                let is_audit = audit::OPERATIONS.contains(&operation);
                #[cfg(feature = "excel")]
                let is_audit = is_audit || audit::EXPORTS.contains(&operation);
                if is_audit {
                    return audit::handle(self, &actor, operation, query, &body_value);
                }
                if report_templates::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&report_templates::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if reports::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&reports::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                if operation == DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE {
                    return Ok(report_assets::download(&self.store, &actor, parameters)?.content);
                }
                if report_assets::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&report_assets::handle(
                        &self.store,
                        &actor,
                        operation,
                        parameters,
                        query,
                        &body_value,
                    )?)
                    .map_err(Into::into);
                }
                #[cfg(feature = "excel")]
                if excel::OPERATIONS.contains(&operation) {
                    return serde_json::to_vec(&excel::handle(
                        self,
                        &actor,
                        operation,
                        parameters,
                        &body_value,
                    )?)
                    .map_err(Into::into);
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
                    let job_id = parameters
                        .iter()
                        .find(|(key, _)| *key == "jobId")
                        .map(|(_, value)| value.as_str())
                        .ok_or_else(|| invalid("缺少文件任务编号。"))?;
                    if operation == RETRY_JOB {
                        return serde_json::to_vec(&tasks::retry::execute(self, &actor, job_id)?)
                            .map_err(Into::into);
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
                    )?
                } else if OFFICE_ACTIONS.contains(&operation) {
                    office::action(
                        &self.store,
                        &actor,
                        operation.id,
                        records::id(parameters)?,
                        body_value,
                        self.clock.now().map_err(unavailable)?.today,
                    )?
                } else {
                    self.authenticated(operation, parameters, query, body, &actor, token)?
                }
            }
        };
        serde_json::to_vec(&contracts::project(
            contracts::response(operation.id),
            value,
        ))
        .map_err(Into::into)
    }
    fn authenticated(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
        actor: &Actor,
        token: &str,
    ) -> Result<Value> {
        let body_value = body.clone().unwrap_or_else(|| json!({}));
        match operation {
            LOGOUT => {
                self.sessions.logout(token)?;
                Ok(json!({"success":true}))
            }
            GET_CURRENT_USER => auth::user_dto(
                &self.store,
                &self.store.get("users", actor.id)?,
                &self.clock,
            ),
            RENEW_SESSION => self.sessions.renew(&self.store, token, &self.clock),
            GET_ORGANIZATION_DIRECTORY => {
                auth::authorize(actor, "system.organization", "view")?;
                Ok(
                    json!({"companies":self.store.all("companies")?,"departments":organization::departments(&self.store)?}),
                )
            }
            LIST_ORGANIZATION_MANAGERS => {
                auth::authorize(actor, "system.organization", "view")?;
                organization::managers(&self.store, query)
            }
            GET_PERSONNEL_OPTIONS => personnel_queries::options(&self.store, actor),
            LIST_PERSONNEL => personnel_queries::directory(&self.store, actor, &self.clock, query),
            GET_PERSONNEL => personnel_queries::read(&self.store, actor, records::id(parameters)?),
            GET_PERSONNEL_HISTORY => {
                personnel_queries::history(&self.store, actor, records::id(parameters)?, query)
            }
            GET_MEETING_ROOM_AVAILABILITY => {
                office_queries::availability(&self.store, actor, records::id(parameters)?, query)
            }
            GET_REPORT_TEMPLATE_FIELD_CATALOG => {
                auth::authorize(actor, "document.report-templates", "view")?;
                let kind = query
                    .iter()
                    .find(|(key, _)| *key == "reportType")
                    .map(|(_, value)| value.as_str())
                    .unwrap_or("ExportDocument");
                serde_json::to_value(report_templates::fields(kind)?).map_err(Into::into)
            }
            GET_REPORT_TEMPLATE_V3_CONTRACT => {
                serde_json::from_str(include_str!("../../resources/report-v3-contract.json"))
                    .map_err(Into::into)
            }
            LIST_JOBS => self.jobs.list(actor, query),
            CLEAR_FINISHED_JOBS => self.jobs.clear_finished(actor),
            LIST_DATABASE_BACKUPS => maintenance::list(&self.store, &self.paths, actor),
            CLEANUP_DATABASE_BACKUPS => {
                maintenance::cleanup(&self.store, &self.paths, actor, &body_value)
            }
            CREATE_DATABASE_BACKUP => {
                let path = maintenance::create(&self.store, &self.paths, actor)?;
                self.jobs.completed(
                    actor,
                    "DatabaseBackup",
                    "数据库备份",
                    &format!("备份已保存：{}", path.display()),
                )
            }
            RESTORE_DATABASE_BACKUP => {
                self.jobs.ensure_idle()?;
                maintenance::restore(&self.store, &self.paths, actor, &body_value)?;
                self.sessions.clear()?;
                self.jobs.recover()?;
                let mut job = self.jobs.completed(
                    actor,
                    "DatabaseRestore",
                    "数据库恢复",
                    "恢复完成，请重新登录",
                )?;
                job["requiresLogin"] = json!(true);
                Ok(job)
            }
            ANALYZE_INVOICE_PROFIT => {
                auth::authorize(actor, "document.invoices", "view")?;
                let dto: ApiInvoiceDetailDto =
                    serde_json::from_value(body_value["invoice"].clone())
                        .map_err(|error| invalid(error.to_string()))?;
                let invoice = crate::invoice::InvoiceDraft::from_dto(dto)
                    .build()
                    .map_err(invalid)?;
                let rate = invoice
                    .exchange_rate
                    .filter(|rate| *rate > rust_decimal::Decimal::ZERO)
                    .or_else(|| {
                        if invoice.currency.eq_ignore_ascii_case("CNY")
                            || invoice.currency.eq_ignore_ascii_case("RMB")
                        {
                            Some(rust_decimal::Decimal::ONE)
                        } else {
                            None
                        }
                    });
                let sales_rmb = invoice
                    .total_amount
                    .checked_mul(rate.unwrap_or_default())
                    .ok_or_else(|| invalid("利润计算超出范围。"))?;
                let profit = if rate.is_some() {
                    sales_rmb - invoice.total_purchase_amount + invoice.total_tax_refund_amount
                } else {
                    rust_decimal::Decimal::ZERO
                };
                let margin = if sales_rmb > rust_decimal::Decimal::ZERO {
                    profit / sales_rmb
                } else {
                    rust_decimal::Decimal::ZERO
                };
                Ok(
                    json!({"currency":invoice.currency,"salesTotal":invoice.total_amount,"exchangeRate":rate,"salesRmb":sales_rmb,"purchaseCost":invoice.total_purchase_amount,"taxRefund":invoice.total_tax_refund_amount,"grossProfit":profit,"margin":margin,"salesTotalText":format!("{} {:.2}",invoice.currency,invoice.total_amount),"exchangeRateText":rate.map(|rate|format!("{rate:.4}")).unwrap_or("未设置".into()),"salesRmbText":format!("¥ {sales_rmb:.2}"),"purchaseCostText":format!("- ¥ {:.2}",invoice.total_purchase_amount),"taxRefundText":format!("+ ¥ {:.2}",invoice.total_tax_refund_amount),"grossProfitText":format!("¥ {profit:.2}"),"marginText":format!("{:.2}%",margin*rust_decimal::Decimal::from(100)),"storagePolicy":"本机数据"}),
                )
            }
            GET_DASHBOARD => dashboard::query(&self.store, actor, &self.clock),
            GET_CRM_DASHBOARD => crm_dashboard::query(&self.store, actor, &self.clock),
            GET_SUPPLIER_ASSESSMENT_OVERVIEW => supplier_overview::query(&self.store, actor),
            GET_WORKLIST => worklist::query(&self.store, actor, &self.clock, query),
            LIST_QUERIED_INVOICES => invoice_query::list(&self.store, actor, query),
            REVIEW_INVOICE => {
                auth::authorize(actor, "document.invoices", "view")?;
                workflows::review(&body_value)
            }
            GET_MEETING_BOOKING_HISTORY
            | GET_OFFICE_SUPPLY_REQUEST_HISTORY
            | GET_OFFICE_STOCK_HISTORY => office_events::history(
                &self.store,
                actor,
                operation,
                records::id(parameters)?,
                query,
            ),
            LIST_INVOICE_STATUS_HISTORY => {
                let kind = "invoices";
                let resource = catalog::resource(kind).unwrap();
                auth::authorize(actor, resource.permission, "view")?;
                let id = records::id(parameters)?;
                let record = self.store.get(kind, id)?;
                if !auth::visible(actor, resource.permission, "view", &record) {
                    return Err(error(403, "没有读取历史的权限。"));
                }
                Ok(json!(store::history(&*self.store.connection()?, kind, id)?))
            }
            GET_SETTINGS => settings::read(&self.store, actor),
            UPDATE_SETTINGS | VALIDATE_SETTINGS => settings::save(
                &self.store,
                &self.paths,
                &self.protector,
                actor,
                operation,
                &body_value,
            ),
            LIST_CUSTOM_OPTIONS | SAVE_CUSTOM_OPTION => {
                custom_options::handle(&self.store, actor, operation, parameters, &body_value)
            }
            LIST_CUSTOMERS | LIST_EXPORTERS | LIST_PAYEES | LIST_UNITS | LIST_PORTS => {
                let kind = match operation {
                    LIST_CUSTOMERS => "customers",
                    LIST_EXPORTERS => "exporters",
                    LIST_PAYEES => "payees",
                    LIST_UNITS => "units",
                    _ => "ports",
                };
                records::list(
                    &self.store,
                    actor,
                    catalog::resource(kind).unwrap(),
                    operation,
                    query,
                    parameters,
                )
            }
            _ => {
                let resource = records::find(operation)
                    .ok_or_else(|| unsupported("此项原生操作尚未接入。"))?;
                records::handle(
                    &self.clock,
                    &self.store,
                    actor,
                    resource,
                    operation,
                    parameters,
                    query,
                    body,
                )
            }
        }
    }
}
