use eframe::egui;
use export_doc_native::{
    api::ApiClient,
    designer::{Field, field_catalog},
    generated_api::*,
    jobs,
    pdf::{self, PdfPageImage},
};
use serde_json::Value;
use std::sync::{
    Arc,
    atomic::{AtomicBool, Ordering},
    mpsc::{self, Receiver},
};

#[derive(Clone, Copy)]
pub enum Reply {
    Data(&'static str),
    Saved(&'static str),
    Action(&'static str),
    Edit(&'static str),
}

pub enum Work {
    Login(String, String),
    ListInvoices(i64, String),
    LoadInvoice(i64),
    SaveInvoice(Box<ApiInvoiceDetailDto>),
    LoadTemplate(i64),
    SaveTemplate {
        name: String,
        html: String,
        previous: Option<ApiUserReportTemplateDto>,
    },
    Pdf {
        invoice_id: i64,
        template: String,
    },
    Page {
        bytes: Arc<Vec<u8>>,
        index: u32,
    },
    Request {
        operation: Operation,
        parameters: Vec<(&'static str, String)>,
        query: Vec<(&'static str, String)>,
        body: Option<Value>,
        reply: Reply,
    },
    Lookups,
}
pub enum Event {
    Login {
        client: ApiClient,
        user: ApiUserDto,
        fields: Vec<Field>,
        templates: Vec<ApiReportTemplateDto>,
    },
    Invoices(ApiPagedResponseOfApiInvoiceListItemDto),
    Invoice(Box<ApiInvoiceDetailDto>),
    Template(ApiUserReportTemplateDto),
    Pdf(Arc<Vec<u8>>),
    Page(PdfPageImage),
    PreviewError(String),
    Progress(String, f32),
    Done(Result<(), String>),
    Data {
        value: Value,
        reply: Reply,
    },
    Lookups(std::collections::BTreeMap<String, Vec<(Value, String)>>),
}
pub struct Task {
    pub events: Receiver<Event>,
    pub cancelled: Arc<AtomicBool>,
    pub cancellable: bool,
}
impl Task {
    pub fn spawn(
        client: ApiClient,
        work: Work,
        context: egui::Context,
        pdfium_path: std::path::PathBuf,
    ) -> Self {
        let (sender, events) = mpsc::channel();
        let cancelled = Arc::new(AtomicBool::new(false));
        let cancellation = cancelled.clone();
        let cancellable = matches!(work, Work::Pdf { .. });
        std::thread::spawn(move || {
            let send = |event| {
                let _ = sender.send(event);
                context.request_repaint();
            };
            let result = execute(&client, work, &cancellation, &send, &pdfium_path);
            send(Event::Done(result));
        });
        Self {
            events,
            cancelled,
            cancellable,
        }
    }
    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::Relaxed);
    }
}
impl Drop for Task {
    fn drop(&mut self) {
        self.cancel();
    }
}

fn execute(
    client: &ApiClient,
    work: Work,
    cancellation: &AtomicBool,
    send: &impl Fn(Event),
    pdfium_path: &std::path::Path,
) -> Result<(), String> {
    match work {
        Work::Login(username, password) => {
            let (client, user) = client
                .login(username, password)
                .map_err(|error| error.to_string())?;
            let contract: Value = client
                .json(GET_REPORT_TEMPLATE_V3_CONTRACT, &[], &[], None)
                .map_err(|error| error.to_string())?;
            if contract["schemaVersion"] != 3 || contract["coordinateUnit"] != "hundredth-mm" {
                return Err("后端 V3 契约已变化，请重新验证原生客户端。".into());
            }
            let catalog: ApiReportTemplateFieldCatalogResponse = client
                .json(
                    GET_REPORT_TEMPLATE_FIELD_CATALOG,
                    &[],
                    &[("reportType", "ExportDocument".into())],
                    None,
                )
                .map_err(|error| error.to_string())?;
            let templates: Vec<ApiReportTemplateDto> = client
                .json(
                    LIST_REPORT_TEMPLATES,
                    &[],
                    &[("reportType", "ExportDocument".into())],
                    None,
                )
                .map_err(|error| error.to_string())?;
            send(Event::Login {
                client,
                user,
                fields: field_catalog(&catalog),
                templates,
            });
        }
        Work::ListInvoices(page, keyword) => send(Event::Invoices(
            client
                .json(
                    LIST_INVOICES,
                    &[],
                    &[
                        ("pageNumber", page.to_string()),
                        ("pageSize", "30".into()),
                        ("keyword", keyword),
                    ],
                    None,
                )
                .map_err(|error| error.to_string())?,
        )),
        Work::LoadInvoice(id) => send(Event::Invoice(Box::new(
            client.get_invoice(id).map_err(|error| error.to_string())?,
        ))),
        Work::SaveInvoice(invoice) => send(Event::Invoice(Box::new(
            client
                .save_invoice(&invoice)
                .map_err(|error| error.to_string())?,
        ))),
        Work::LoadTemplate(id) => send(Event::Template(
            client
                .json(
                    GET_USER_REPORT_TEMPLATE,
                    &[("id", id.to_string())],
                    &[],
                    None,
                )
                .map_err(|error| error.to_string())?,
        )),
        Work::SaveTemplate {
            name,
            html,
            previous,
        } => {
            let template = client
                .save_template(&name, &html, previous.as_ref())
                .map_err(|error| error.to_string())?;
            // Surface the committed version even if publication subsequently fails.
            send(Event::Template(template.clone()));
            let published = client
                .publish_template(&template)
                .map_err(|error| error.to_string())?;
            send(Event::Template(published));
        }
        Work::Pdf {
            invoice_id,
            template,
        } => {
            let bytes = jobs::render_pdf(client, invoice_id, &template, cancellation, &mut |job| {
                send(Event::Progress(
                    job.status_text.clone(),
                    job.progress_percent.unwrap_or(0) as f32 / 100.,
                ))
            })
            .map_err(|error| error.to_string())?;
            let bytes = Arc::new(bytes);
            send(Event::Pdf(bytes.clone()));
            if !cancellation.load(Ordering::Relaxed) {
                match pdf::render_page(&bytes, 0, pdfium_path) {
                    Ok(page) => send(Event::Page(page)),
                    Err(error) => send(Event::PreviewError(error)),
                }
            }
        }
        Work::Page { bytes, index } => {
            send(Event::Page(pdf::render_page(&bytes, index, pdfium_path)?))
        }
        Work::Request {
            operation,
            parameters,
            query,
            body,
            reply,
        } => {
            let value: Value = client
                .json(operation, &parameters, &query, body)
                .map_err(|error| error.to_string())?;
            send(Event::Data { value, reply });
        }
        Work::Lookups => {
            let mut lookups = std::collections::BTreeMap::new();
            for (operation, keys, name) in [
                (
                    LIST_CUSTOMERS_PAGE,
                    &["customerId", "linkedDocumentCustomerId"][..],
                    "customerNameEN",
                ),
                (QUERY_CRM_CUSTOMERS, &["crmCustomerId"][..], "name"),
                (
                    QUERY_SUPPLIERS,
                    &["supplierId", "supplierCompanyId"][..],
                    "name",
                ),
                (LIST_PRODUCTS, &["productId"][..], "styleNo"),
                (LIST_EXPORTERS_PAGE, &["exporterId"][..], "exporterNameEN"),
                (LIST_PAYEES_PAGE, &["payeeId"][..], "name"),
                (
                    LIST_PERSONNEL,
                    &["employeeId", "managerEmployeeId"][..],
                    "fullName",
                ),
                (LIST_MEETING_ROOMS, &["meetingRoomId"][..], "name"),
                (LIST_OFFICE_SUPPLIES, &["officeSupplyId"][..], "name"),
                (
                    LIST_PERMISSION_TEMPLATES,
                    &["permissionTemplateId"][..],
                    "name",
                ),
            ] {
                match client.json::<Value>(operation, &[], &[("pageSize", "200".into())], None) {
                    Ok(value) => {
                        let items = value
                            .as_array()
                            .or_else(|| value["items"].as_array())
                            .cloned()
                            .unwrap_or_default();
                        let options = items
                            .iter()
                            .filter(|item| {
                                item["isActive"] != false && item["status"] != "Departed"
                            })
                            .map(|item| {
                                (
                                    item["id"].clone(),
                                    item[name]
                                        .as_str()
                                        .or_else(|| item["employee"][name].as_str())
                                        .unwrap_or("")
                                        .to_owned(),
                                )
                            })
                            .collect::<Vec<_>>();
                        for key in keys {
                            lookups.insert((*key).to_owned(), options.clone());
                        }
                    }
                    Err(error) if matches!(error.status, Some(403 | 501)) => {}
                    Err(error) => return Err(error.to_string()),
                }
            }
            match client.json::<Value>(GET_ORGANIZATION_DIRECTORY, &[], &[], None) {
                Ok(value) => {
                    for (field, key) in [
                        ("companies", "companyCode"),
                        ("departments", "departmentId"),
                    ] {
                        let options = value[field]
                            .as_array()
                            .cloned()
                            .unwrap_or_default()
                            .into_iter()
                            .filter(|item| item["isActive"] == true)
                            .map(|item| {
                                (
                                    item["code"].clone(),
                                    item["name"].as_str().unwrap_or("").to_owned(),
                                )
                            })
                            .collect::<Vec<_>>();
                        lookups.insert(key.into(), options.clone());
                        if field == "departments" {
                            lookups.insert("parentCode".into(), options);
                        } else {
                            lookups.insert("companyScope".into(), options);
                        }
                    }
                }
                Err(error) if error.status == Some(403) => {}
                Err(error) => return Err(error.to_string()),
            }
            send(Event::Lookups(lookups));
        }
    }
    Ok(())
}
