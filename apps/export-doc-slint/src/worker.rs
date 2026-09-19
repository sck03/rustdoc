use crate::form_model::Lookups;
use export_doc_engine::{
    api::ApiClient,
    engine::catalog,
    generated_api::*,
    jobs,
    paths::RuntimePaths,
    pdf::{self, PdfPageImage},
    workspace,
};
use serde_json::Value;
use std::{
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
        mpsc::{self, Receiver},
    },
    thread::JoinHandle,
    time::Duration,
};
pub mod files;
mod lookups;
pub mod ocr;

pub enum Work {
    InvoicePackage(std::path::PathBuf),
    ImportInvoicePackage {
        name: String,
        bytes: Arc<Vec<u8>>,
        body: Value,
    },
    OpenOcr(ocr::Source),
    RecognizeOcr {
        name: String,
        bytes: Arc<Vec<u8>>,
    },
    PackingView {
        container: ApiContainerDimensionsDto,
        analysis: Box<ApiContainerPackingAnalysisDto>,
        projection: i32,
        angle: f64,
        filled: bool,
        selected: i32,
    },
    FollowUpContacts {
        customer_id: i64,
    },
    SaveBackupCopy {
        file_name: String,
        destination: std::path::PathBuf,
    },
    Attachments {
        query: Vec<(&'static str, String)>,
    },
    AttachmentPreview {
        parameters: Vec<(&'static str, String)>,
        content_type: String,
    },
    BinarySave {
        operation: Operation,
        parameters: Vec<(&'static str, String)>,
        query: Vec<(&'static str, String)>,
        destination: std::path::PathBuf,
        body: Option<Value>,
        limit: u64,
    },
    OrganizationManagers {
        company: String,
    },
    PersonnelDirectory {
        query: Vec<(&'static str, String)>,
    },
    Image {
        operation: Operation,
        parameters: Vec<(&'static str, String)>,
        reply: String,
    },
    Upload {
        operation: Operation,
        parameters: Vec<(&'static str, String)>,
        metadata: Value,
        source: std::path::PathBuf,
        limit: u64,
        reply: String,
    },
    Login(String, String),
    Request {
        operation: Operation,
        parameters: Vec<(&'static str, String)>,
        query: Vec<(&'static str, String)>,
        body: Option<Value>,
        reply: String,
    },
    SaveInvoice(Box<ApiInvoiceDetailDto>),
    FileJob {
        operation: Operation,
        parameters: Vec<(&'static str, String)>,
        body: Value,
        destination: std::path::PathBuf,
    },
    SaveJobOutput {
        job_id: String,
        destination: std::path::PathBuf,
    },
    PreviewReport {
        operation: Operation,
        record_id: i64,
        body: Value,
    },
    DocumentPackagePreview {
        invoice: i64,
        items: Vec<Value>,
    },
    PaymentOptions,
    RecoveryStatus,
    Page {
        bytes: Arc<Vec<u8>>,
        index: u32,
    },
    Lookups,
}
pub enum Event {
    InvoicePackage(String, Arc<Vec<u8>>, Value),
    OcrImage(ocr::Image),
    Image(String, files::ImageFrame),
    Login(ApiClient, ApiUserDto),
    Data(String, Value),
    Invoice(Box<ApiInvoiceDetailDto>),
    Pdf(Arc<Vec<u8>>, PdfPageImage),
    Page(PdfPageImage),
    Lookups(Lookups),
    SavedFile(Option<std::path::PathBuf>),
}
pub struct Task {
    pub events: Receiver<Result<Event, String>>,
    pub cancelled: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
}
impl Task {
    pub fn spawn(client: ApiClient, paths: RuntimePaths, work: Work) -> Self {
        let (sender, events) = mpsc::channel();
        let seconds = match &work {
            Work::Upload { operation, .. } | Work::Request { operation, .. }
                if [
                    UPLOAD_LETTER_OF_CREDIT_DOCUMENT,
                    IMPORT_LETTER_OF_CREDIT_DOCUMENT,
                ]
                .contains(operation) =>
            {
                600
            }
            Work::Request { operation, .. } if *operation == REVIEW_LETTER_OF_CREDIT_COMPLIANCE => {
                125
            }
            _ => 120,
        };
        let scope = export_doc_engine::operation::OperationScope::new(Duration::from_secs(seconds));
        let cancelled = scope.cancellation_flag();
        let cancellation = cancelled.clone();
        let handle = std::thread::spawn(move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                scope.run(|| execute(client, paths, work, &cancellation))
            }))
            .unwrap_or_else(|_| Err("后台操作异常，当前草稿已保留。".into()));
            let _ = sender.send(result);
        });
        Self {
            events,
            cancelled,
            handle: Some(handle),
        }
    }
    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::Relaxed);
    }
    pub fn finish(&mut self) {
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}
impl Drop for Task {
    fn drop(&mut self) {
        self.cancel();
        if self.handle.as_ref().is_some_and(|h| h.is_finished()) {
            self.finish();
        }
    }
}
fn execute(
    client: ApiClient,
    paths: RuntimePaths,
    work: Work,
    cancelled: &AtomicBool,
) -> Result<Event, String> {
    match work {
        Work::InvoicePackage(source) => {
            let name = source
                .file_name()
                .and_then(|s| s.to_str())
                .ok_or("单据包文件名无效。")?
                .to_owned();
            let bytes = Arc::new(files::read(&source, 25 * 1024 * 1024)?);
            let value = client
                .upload(
                    PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE,
                    &[],
                    serde_json::json!({}),
                    &name,
                    &bytes,
                )
                .map_err(|e| e.to_string())?;
            Ok(Event::InvoicePackage(name, bytes, value))
        }
        Work::ImportInvoicePackage { name, bytes, body } => client
            .upload(
                IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
                &[],
                body,
                &name,
                &bytes,
            )
            .map(|v| Event::Data("invoice-package-imported".into(), v))
            .map_err(|e| e.to_string()),
        Work::OpenOcr(source) => ocr::load(source).map(Event::OcrImage),
        Work::RecognizeOcr { name, bytes } => client
            .upload(UPLOAD_OCR_IMAGE, &[], serde_json::json!({}), &name, &bytes)
            .map(|value| Event::Data("ocr:result".into(), value))
            .map_err(|e| e.to_string()),
        Work::PackingView {
            container,
            analysis,
            projection,
            angle,
            filled,
            selected,
        } => {
            let svg = export_doc_engine::packing_view::diagram(
                &container, &analysis, projection, angle, filled, selected,
            )
            .map_err(|e| e.to_string())?;
            Ok(Event::Data("packing:view".into(), serde_json::json!(svg)))
        }
        Work::FollowUpContacts { customer_id } => {
            let rows = lookups::rows(
                &client,
                QUERY_CRM_CONTACTS,
                &[("customerId", customer_id.to_string())],
                &[],
                "items",
            )?;
            Ok(Event::Data(
                "followup-contacts".into(),
                serde_json::json!(rows),
            ))
        }
        Work::SaveBackupCopy {
            file_name,
            destination,
        } => {
            if !export_doc_engine::paths::valid_file_name(&file_name)
                || !file_name.ends_with(".sqlite3")
            {
                return Err("备份文件名无效。".into());
            }
            files::copy(
                &paths.data_root.join("Backups").join(file_name),
                &destination,
            )?;
            Ok(Event::SavedFile(Some(destination)))
        }
        Work::Attachments { query } => {
            let category_query: Vec<_> = query
                .iter()
                .filter(|(key, _)| *key == "invoiceId")
                .cloned()
                .collect();
            let categories: Value = client
                .json(
                    LIST_BUSINESS_ATTACHMENT_CATEGORIES,
                    &[],
                    &category_query,
                    None,
                )
                .map_err(|cause| cause.to_string())?;
            let page: Value = client
                .json(LIST_BUSINESS_ATTACHMENTS, &[], &query, None)
                .map_err(|cause| cause.to_string())?;
            let invoices = lookups::rows(&client, LIST_INVOICES, &[], &[], "items")?;
            Ok(Event::Data(
                "attachments".into(),
                serde_json::json!({"categories":categories,"page":page,"invoices":invoices}),
            ))
        }
        Work::OrganizationManagers { company } => {
            let rows = lookups::rows(
                &client,
                LIST_ORGANIZATION_MANAGERS,
                &[],
                &[("companyCode", company)],
                "items",
            )?;
            Ok(Event::Data(
                "organization-managers".into(),
                export_doc_engine::contracts::page(rows.clone(), rows.len(), 1, rows.len().max(1)),
            ))
        }
        Work::AttachmentPreview {
            parameters,
            content_type,
        } => {
            let bytes = client
                .bytes(
                    DOWNLOAD_BUSINESS_ATTACHMENT,
                    &parameters,
                    &[],
                    None,
                    16 * 1024 * 1024,
                )
                .map_err(|cause| cause.to_string())?;
            if content_type == "application/pdf" {
                let bytes = Arc::new(bytes);
                let page = pdf::render_page(&bytes, 0, &paths.pdfium_path())?;
                Ok(Event::Pdf(bytes, page))
            } else if content_type.starts_with("image/") {
                Ok(Event::Image("attachment".into(), files::image(&bytes)?))
            } else if content_type.starts_with("text/") {
                let text = String::from_utf8(bytes)
                    .map_err(|_| "文本不是有效的 UTF-8，请保存后使用对应软件打开。")?;
                if text.chars().count() > 100_000 {
                    return Err("文本过长，请保存原文件后查看。".into());
                }
                Ok(Event::Data(
                    "attachment-text".into(),
                    serde_json::json!(text),
                ))
            } else {
                Err("请保存原文件后使用对应软件查看此格式。".into())
            }
        }
        Work::BinarySave {
            operation,
            parameters,
            query,
            destination,
            body,
            limit,
        } => {
            let bytes = client
                .bytes(operation, &parameters, &query, body, limit)
                .map_err(|cause| cause.to_string())?;
            export_doc_engine::operation::check().map_err(|cause| cause.to_string())?;
            export_doc_engine::paths::atomic_write(&destination, &bytes)?;
            Ok(Event::SavedFile(Some(destination)))
        }
        Work::PersonnelDirectory { query } => {
            let options: Value = client
                .json(GET_PERSONNEL_OPTIONS, &[], &[], None)
                .map_err(|cause| cause.to_string())?;
            let page: Value = client
                .json(LIST_PERSONNEL, &[], &query, None)
                .map_err(|cause| cause.to_string())?;
            Ok(Event::Data(
                "personnel-directory".into(),
                serde_json::json!({"options":options,"page":page}),
            ))
        }
        Work::Image {
            operation,
            parameters,
            reply,
        } => {
            let bytes = client
                .bytes(operation, &parameters, &[], None, 5 * 1024 * 1024)
                .map_err(|cause| cause.to_string())?;
            Ok(Event::Image(reply, files::image(&bytes)?))
        }
        Work::Upload {
            operation,
            parameters,
            metadata,
            source,
            limit,
            reply,
        } => {
            let bytes = files::read(&source, limit)?;
            let file_name = source
                .file_name()
                .and_then(|name| name.to_str())
                .ok_or("文件名不是有效的 Unicode 文本。")?;
            let value = client
                .upload(operation, &parameters, metadata, file_name, &bytes)
                .map_err(|cause| cause.to_string())?;
            Ok(Event::Data(reply, value))
        }
        Work::FileJob {
            operation,
            parameters,
            body,
            destination,
        } => {
            let job = client
                .json(operation, &parameters, &[], Some(body))
                .map_err(|e| e.to_string())?;
            jobs::wait_for_completion(&client, job, cancelled).map_err(|e| e.to_string())?;
            Ok(Event::SavedFile(Some(destination)))
        }
        Work::SaveJobOutput {
            job_id,
            destination,
        } => {
            let bytes = client
                .bytes(
                    DOWNLOAD_JOB_RESULT,
                    &[("jobId", job_id)],
                    &[],
                    None,
                    jobs::PDF_LIMIT,
                )
                .map_err(|e| e.to_string())?;
            export_doc_engine::operation::check().map_err(|e| e.to_string())?;
            export_doc_engine::paths::atomic_write(&destination, &bytes)?;
            Ok(Event::SavedFile(Some(destination)))
        }
        Work::Login(username, password) => {
            let (client, user) = client
                .login(username, password)
                .map_err(|e| e.to_string())?;
            Ok(Event::Login(client, user))
        }
        Work::Request {
            operation,
            parameters,
            query,
            body,
            reply,
        } => {
            if cancelled.load(Ordering::Relaxed) {
                return Err("操作已取消。".into());
            }
            Ok(Event::Data(
                reply,
                client
                    .json(operation, &parameters, &query, body)
                    .map_err(|e| e.to_string())?,
            ))
        }
        Work::SaveInvoice(invoice) => Ok(Event::Invoice(Box::new(
            client.save_invoice(&invoice).map_err(|e| e.to_string())?,
        ))),
        Work::PreviewReport {
            operation,
            record_id,
            body,
        } => {
            let bytes = Arc::new(
                client
                    .preview_report_pdf(operation, &parameters(operation, record_id), &body)
                    .map_err(|e| e.to_string())?,
            );
            if cancelled.load(Ordering::Relaxed) {
                return Err("操作已取消。".into());
            }
            let page = pdf::render_page(&bytes, 0, &paths.pdfium_path())?;
            Ok(Event::Pdf(bytes, page))
        }
        Work::DocumentPackagePreview { invoice, items } => {
            let bytes = Arc::new(
                client
                    .preview_document_package_pdf(invoice, &serde_json::json!({"items": items}))
                    .map_err(|cause| cause.to_string())?,
            );
            if cancelled.load(Ordering::Relaxed) {
                return Err("操作已取消。".into());
            }
            let page = pdf::render_page(&bytes, 0, &paths.pdfium_path())?;
            Ok(Event::Pdf(bytes, page))
        }
        Work::PaymentOptions => {
            let mut options = serde_json::Map::new();
            for (key, kind) in [
                ("paymentMethod", "PaymentMethod"),
                ("payerName", "PaymentPayerName"),
            ] {
                options.insert(
                    key.into(),
                    client
                        .json(
                            LIST_CUSTOM_OPTIONS,
                            &[("optionType", kind.into())],
                            &[],
                            None,
                        )
                        .map_err(|cause| cause.to_string())?,
                );
            }
            Ok(Event::Data(
                "payment-options".into(),
                Value::Object(options),
            ))
        }
        Work::RecoveryStatus => {
            let status: Value = client
                .json(GET_DISASTER_RECOVERY_STATUS, &[], &[], None)
                .map_err(|cause| cause.to_string())?;
            let cloud = client.json::<Value>(GET_CLOUD_BACKUP_STATUS, &[], &[], None);
            Ok(Event::Data(
                "recovery:status".into(),
                match cloud {
                    Ok(cloud) => serde_json::json!({"status":status,"cloud":cloud}),
                    Err(cause) => serde_json::json!({
                        "status":status,
                        "cloud":null,
                        "cloudError":cause.to_string()
                    }),
                },
            ))
        }
        Work::Page { bytes, index } => Ok(Event::Page(pdf::render_page(
            &bytes,
            index,
            &paths.pdfium_path(),
        )?)),
        Work::Lookups => {
            let mut result = Lookups::new();
            for (kind, key, label, id_key) in [
                ("customers", "customerId", "customerNameEN", "id"),
                ("exporters", "exporterId", "exporterNameEN", "id"),
                ("products", "productId", "productCode", "id"),
                ("payees", "payeeId", "name", "id"),
                ("crm-customers", "crmCustomerId", "name", "id"),
                ("suppliers", "supplierCompanyId", "name", "id"),
                ("people", "employeeId", "fullName", "id"),
                ("rooms", "meetingRoomId", "name", "id"),
                ("supplies", "officeSupplyId", "name", "id"),
                ("companies", "companyCode", "name", "code"),
                ("departments", "departmentId", "name", "code"),
            ] {
                if cancelled.load(Ordering::Relaxed) {
                    return Err("操作已取消。".into());
                }
                let Some(resource) = catalog::resource(kind) else {
                    continue;
                };
                // The permission probe preserves 403 as an absent optional
                // catalog; IO and schema failures still fail the load.
                if let Err(cause) =
                    client.json::<Value>(resource.list, &[], &[("pageSize", "1".into())], None)
                {
                    if cause.status == Some(403) {
                        continue;
                    }
                    return Err(cause.to_string());
                }
                let field = if matches!(kind, "companies" | "departments") {
                    kind
                } else {
                    "items"
                };
                let rows = lookups::rows(&client, resource.list, &[], &[], field)?;
                result.insert(
                    key.into(),
                    rows.iter()
                        .map(|v| (v[id_key].clone(), workspace::display(&v[label])))
                        .collect(),
                );
            }
            if let Some(value) = result.get("companyCode").cloned() {
                result.insert("companyScope".into(), value);
            }
            if let Some(value) = result.get("customerId").cloned() {
                result.insert("linkedDocumentCustomerId".into(), value);
            }
            Ok(Event::Lookups(result))
        }
    }
}
pub fn parameters(operation: Operation, id: i64) -> Vec<(&'static str, String)> {
    operation
        .path
        .split('{')
        .skip(1)
        .filter_map(|part| part.split_once('}').map(|(name, _)| (name, id.to_string())))
        .collect()
}
