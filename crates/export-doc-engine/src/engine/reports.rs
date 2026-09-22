//! Report use cases. Storage, permissions and jobs stay here; physical layout
//! and encoding live in export-doc-report.
use super::report_templates::{report_type, validate_bytes, validate_content};
use super::{
    NativeService, auth,
    error::{Result, conflict, error, invalid, unavailable, unsupported},
    records::text,
    report_assets,
    store::{Actor, Store},
    tasks::TaskOutput,
};
use crate::{contracts, designer::Design, generated_api::*, invoice::InvoiceDraft, paths};
use base64::Engine;
use export_doc_report::{self as render, Builtin, Document, ReportData};
use serde_json::{Value, json};
use std::{path::PathBuf, sync::atomic::AtomicBool};

pub const OPERATIONS: &[Operation] = &[
    LIST_REPORT_TEMPLATES,
    GET_REPORT_TEMPLATE_CONTENT,
    PREVIEW_REPORT_TEMPLATE_CONTENT,
    PREVIEW_INVOICE_REPORT_HTML,
    PREVIEW_INVOICE_REPORT_DRAFT_HTML,
    PREVIEW_PAYMENT_VOUCHER_HTML,
    PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML,
    START_INVOICE_REPORT_PDF_DOWNLOAD_JOB,
    START_INVOICE_REPORT_PDF_SAVE_TO_PATH_JOB,
    START_INVOICE_REPORT_PDF_ZIP_DOWNLOAD_JOB,
    START_INVOICE_REPORT_PDF_ZIP_SAVE_TO_PATH_JOB,
    START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB,
    START_PAYMENT_VOUCHER_PDF_SAVE_TO_PATH_JOB,
];
pub const LOCAL_OPERATIONS: &[Operation] = &[
    START_INVOICE_REPORT_PDF_SAVE_TO_PATH_JOB,
    START_INVOICE_REPORT_PDF_ZIP_SAVE_TO_PATH_JOB,
    START_PAYMENT_VOUCHER_PDF_SAVE_TO_PATH_JOB,
];
enum Template {
    Builtin(Builtin),
    Custom {
        design: Design,
        content: String,
        name: String,
        path: String,
    },
    File {
        design: Design,
        content: Vec<u8>,
        name: String,
        path: String,
    },
}
impl Template {
    fn label(&self) -> &str {
        match self {
            Self::Builtin(v) => v.label(),
            Self::Custom { name, .. } | Self::File { name, .. } => name,
        }
    }
    fn path(&self) -> &str {
        match self {
            Self::Builtin(v) => v.path(),
            Self::Custom { path, .. } | Self::File { path, .. } => path,
        }
    }
    fn content(&self) -> &[u8] {
        match self {
            Self::Builtin(v) => v.source(),
            Self::Custom { content, .. } => content.as_bytes(),
            Self::File { content, .. } => content,
        }
    }
    fn render(&self, data: &ReportData, cancelled: &AtomicBool) -> Result<Document> {
        match self {
            Self::Builtin(v) => render::render_builtin(*v, data, cancelled).map_err(Into::into),
            Self::Custom { design, .. } | Self::File { design, .. } => {
                render::render_design(data, design, cancelled).map_err(Into::into)
            }
        }
    }
}
fn query_value<'a>(query: &'a [(&str, String)], key: &str) -> &'a str {
    query
        .iter()
        .find(|(k, _)| *k == key)
        .map(|(_, v)| v.as_str())
        .unwrap_or("")
}
fn default_path(kind: &str) -> &'static str {
    if kind == "PaymentVoucher" {
        Builtin::PaymentVoucher.path()
    } else {
        Builtin::Invoice.path()
    }
}

fn template(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    reference: &str,
    kind: &str,
    require_published: bool,
) -> Result<Template> {
    let reference = if reference.is_empty() {
        default_path(kind)
    } else {
        reference
    };
    let preset = match reference {
        "native:invoice" => Some(Builtin::Invoice),
        "native:packing-list" => Some(Builtin::PackingList),
        _ => Builtin::find(reference),
    };
    if let Some(preset) = preset {
        if preset.report_type() != kind {
            return Err(invalid("模板与单据的数据域不一致。"));
        }
        return Ok(Template::Builtin(preset));
    }
    if let Some(id) = reference.strip_prefix("user-template:") {
        let id = id
            .parse::<i64>()
            .ok()
            .filter(|id| *id > 0)
            .ok_or_else(|| invalid("模板编号无效。"))?;
        let saved = store.get("report-templates", id)?;
        if !report_assets::template_visible(actor, &saved) {
            return Err(error(403, "没有读取此模板的权限。"));
        }
        if require_published && saved["status"] != "Published" {
            return Err(conflict("请先发布报表模板。"));
        }
        let content = text(&saved, "contentHtml");
        let design = validate_content(kind, &content)?;
        if design.report_type != kind {
            return Err(invalid("模板与单据的数据域不一致。"));
        }
        return Ok(Template::Custom {
            design,
            content,
            name: text(&saved, "name"),
            path: reference.into(),
        });
    }
    if reference.starts_with("builtin:")
        || reference.starts_with("user:")
        || reference.starts_with("Templates/")
        || PathBuf::from(reference).is_absolute()
    {
        let (display, stored, content, _) =
            super::report_template_files::load_resolved_template(paths, kind, reference)?;
        let design = validate_bytes(kind, &content)?;
        return Ok(Template::File {
            design,
            content,
            name: display,
            path: stored,
        });
    }
    Err(unsupported(
        "此模板尚未提供原生排版。请使用内置模板、受管文件模板或已发布的 V3 模板。",
    ))
}
fn catalog(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    kind: &str,
) -> Result<Value> {
    auth::authorize(actor, "document.report-templates", "view")?;
    let mut rows:Vec<_>=render::BUILTINS.iter().filter(|v|v.report_type()==kind).map(|v|json!({"reportType":kind,"displayName":v.label(),"templatePath":v.path(),"withSealDefault":false})).collect();
    rows.extend(super::report_template_files::catalog_entries(paths, kind)?);
    for saved in store.all("report-templates")? {
        if text(&saved, "reportType") == kind
            && saved["status"] == "Published"
            && report_assets::template_visible(actor, &saved)
        {
            rows.push(json!({"reportType":kind,"displayName":saved["name"],"templatePath":format!("user-template:{}",saved["id"]),"withSealDefault":false}));
        }
    }
    Ok(json!(rows))
}
fn subject(store: &Store, actor: &Actor, kind: &str, id: i64, action: &str) -> Result<Value> {
    let record = store.get(kind, id)?;
    let (permission, output) = if kind == "payments" {
        ("document.payments", "document.payment-output")
    } else {
        ("document.invoices", "document.invoice-output")
    };
    if !auth::visible(actor, permission, "view", &record)
        || !auth::visible(actor, output, action, &record)
    {
        return Err(error(403, "没有读取或输出此单据的权限。"));
    }
    Ok(record)
}
fn party(store: &Store, actor: &Actor, kind: &str, id: Option<i64>) -> Result<Value> {
    let Some(id) = id.filter(|v| *v > 0) else {
        return Ok(json!({}));
    };
    let value = store.connection()?.get(kind, id)?;
    let Some(value) = value else {
        return Err(unavailable("单据关联的往来单位不存在。"));
    };
    if !auth::visible(actor, "document.reference-data", "view", &value)
        && !auth::visible(actor, "document.master-data", "view", &value)
    {
        return Err(error(403, "没有读取单据关联资料的权限。"));
    }
    Ok(value)
}
fn invoice_data(
    store: &Store,
    actor: &Actor,
    invoice: Value,
    with_seal: bool,
) -> Result<ReportData> {
    let dto: ApiInvoiceDetailDto = serde_json::from_value(invoice.clone())
        .map_err(|e| invalid(format!("发票内容无效：{e}")))?;
    let dto = InvoiceDraft::from_dto(dto).build().map_err(invalid)?;
    let customer = party(store, actor, "customers", invoice["customerId"].as_i64())?;
    let exporter = party(store, actor, "exporters", invoice["exporterId"].as_i64())?;
    ReportData::invoice(&dto, customer, exporter, with_seal).map_err(Into::into)
}
fn payment_data(store: &Store, actor: &Actor, payment: Value) -> Result<ReportData> {
    let payee = party(store, actor, "payees", payment["payeeId"].as_i64())?;
    let payment: ApiPaymentDto = serde_json::from_value(payment)?;
    export_doc_domain::payment::validate(&payment).map_err(|cause| invalid(cause.to_string()))?;
    ReportData::payment(&payment, payee).map_err(Into::into)
}

pub fn preview_document(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<(Document, Value)> {
    let payment = [
        PREVIEW_PAYMENT_VOUCHER_HTML,
        PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML,
    ]
    .contains(&operation);
    let draft = [
        PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML,
        PREVIEW_INVOICE_REPORT_DRAFT_HTML,
    ]
    .contains(&operation);
    if !payment
        && ![
            PREVIEW_INVOICE_REPORT_HTML,
            PREVIEW_INVOICE_REPORT_DRAFT_HTML,
        ]
        .contains(&operation)
    {
        return Err(invalid("此操作不是单据预览。"));
    }
    let (kind, resource, field, id_field) = if payment {
        ("PaymentVoucher", "payments", "payment", "paymentId")
    } else {
        ("ExportDocument", "invoices", "invoice", "invoiceId")
    };
    if body["reportType"]
        .as_str()
        .is_some_and(|requested| !requested.is_empty() && requested != kind)
    {
        return Err(invalid("预览模板与单据的数据域不一致。"));
    }
    let id = if draft {
        body[field]["id"].as_i64().unwrap_or(0)
    } else {
        super::records::id(parameters)?
    };
    if id < 0 {
        return Err(invalid("单据编号不能小于零。"));
    }
    let (mut data, template) = if draft {
        if id > 0 {
            subject(&service.store, actor, resource, id, "preview")?;
        }
        let data = if payment {
            payment_data(&service.store, actor, body[field].clone())?
        } else {
            invoice_data(
                &service.store,
                actor,
                body[field].clone(),
                body["withSeal"] == true,
            )?
        };
        let template = template(
            &service.store,
            &service.paths,
            actor,
            &text(body, "templatePath"),
            kind,
            true,
        )?;
        (data, template)
    } else {
        prepare(
            &service.store,
            &service.paths,
            actor,
            id,
            body,
            payment,
            "preview",
        )?
    };
    report_assets::hydrate(
        &service.store,
        actor,
        &mut data,
        Some(template.content()),
    )?;
    let document = template.render(&data, &crate::operation::cancellation_flag())?;
    let mut metadata = json!({"reportType":kind,"templatePath":template.path(),"storagePolicy":"预览只使用当前草稿，不写入正式单据。"});
    metadata[id_field] = json!(id);
    if !payment {
        metadata["withSeal"] = json!(body["withSeal"] == true);
    }
    Ok((document, metadata))
}
fn prepare(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    id: i64,
    body: &Value,
    payment: bool,
    action: &str,
) -> Result<(ReportData, Template)> {
    let kind = if payment {
        "PaymentVoucher"
    } else {
        report_type(&text(body, "reportType"))?
    };
    if !payment && kind != "ExportDocument" {
        return Err(invalid("发票只能使用出口单据模板。"));
    }
    let source = subject(
        store,
        actor,
        if payment { "payments" } else { "invoices" },
        id,
        action,
    )?;
    let mut data = if payment {
        payment_data(store, actor, source)?
    } else {
        invoice_data(store, actor, source, body["withSeal"] == true)?
    };
    let template = template(store, paths, actor, &text(body, "templatePath"), kind, true)?;
    report_assets::hydrate(
        store,
        actor,
        &mut data,
        Some(template.content()),
    )?;
    Ok((data, template))
}
fn destination(body: &Value, local: bool, zip: bool) -> Result<Option<PathBuf>> {
    if !local {
        return Ok(None);
    }
    let path = PathBuf::from(text(body, "destinationPath"));
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    if !path
        .file_name()
        .and_then(|v| v.to_str())
        .is_some_and(paths::valid_file_name)
        || !path
            .extension()
            .is_some_and(|v| v.eq_ignore_ascii_case(if zip { "zip" } else { "pdf" }))
        || !path.parent().is_some_and(|p| p.is_dir())
    {
        return Err(invalid("请选择合法的输出文件及现有目录。"));
    }
    Ok(Some(path))
}
fn output_name(data: &ReportData, template: &Template, id: i64) -> String {
    let number = if data.report_type == "PaymentVoucher" {
        data.text("Payment.VoucherNo")
    } else {
        data.text("Invoice.InvoiceNo")
    };
    paths::suggested_pdf_name(&format!(
        "{}-{}-{id}",
        if number.is_empty() {
            "单据".into()
        } else {
            number
        },
        template.label()
    ))
}
pub(super) fn invoice_document(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    id: i64,
    item: &Value,
    action: &str,
    cancelled: &AtomicBool,
) -> Result<(Document, String)> {
    let (data, template) = prepare(store, paths, actor, id, item, false, action)?;
    Ok((
        template.render(&data, cancelled)?,
        output_name(&data, &template, id),
    ))
}
fn start(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let payment = [
        START_PAYMENT_VOUCHER_PDF_DOWNLOAD_JOB,
        START_PAYMENT_VOUCHER_PDF_SAVE_TO_PATH_JOB,
    ]
    .contains(&operation);
    let zip = [
        START_INVOICE_REPORT_PDF_ZIP_DOWNLOAD_JOB,
        START_INVOICE_REPORT_PDF_ZIP_SAVE_TO_PATH_JOB,
    ]
    .contains(&operation);
    let ids = if zip {
        let ids: Vec<i64> = serde_json::from_value(body["invoiceIds"].clone())
            .map_err(|_| invalid("请选择发票。"))?;
        if ids.is_empty()
            || ids.len() > 100
            || ids.iter().any(|v| *v <= 0)
            || ids.iter().collect::<std::collections::BTreeSet<_>>().len() != ids.len()
        {
            return Err(invalid("每次请选择 1–100 张不同的发票。"));
        }
        ids
    } else {
        vec![super::records::id(parameters)?]
    };
    let target = destination(body, LOCAL_OPERATIONS.contains(&operation), zip)?;
    if target.is_some() && service.provider()? != "SQLite" {
        return Err(error(403, "服务器不能保存到客户端本机路径。"));
    }
    for id in &ids {
        prepare(
            &service.store,
            &service.paths,
            actor,
            *id,
            body,
            payment,
            "export-pdf",
        )?;
    }
    let store = service.store.clone();
    let paths = service.paths.clone();
    let body = body.clone();
    let actor_id = actor.id;
    let replay = super::tasks::retry::Replay::new(operation, parameters, &body);
    service.jobs.start_replayable(
        actor,
        if zip {
            "InvoiceReportPdfZip"
        } else if payment {
            "PaymentVoucherPdf"
        } else {
            "InvoiceReportPdf"
        },
        if zip {
            "批量单据 PDF"
        } else if payment {
            "付款报销 PDF"
        } else {
            "单据 PDF"
        },
        Some(replay),
        move |cancelled| {
            let mut files = vec![];
            for id in &ids {
                crate::operation::check()?;
                let actor = auth::current_actor(&store, actor_id)?;
                auth::authorize_operation(&actor, operation, &[])?;
                let (data, template) =
                    prepare(&store, &paths, &actor, *id, &body, payment, "export-pdf")?;
                export_doc_report::configure(&paths.font_path);
                let document = template.render(&data, cancelled)?;
                let pdf = render::pdf_document(&document, &paths.font_path, cancelled)?;
                files.push((output_name(&data, &template, *id), pdf));
            }
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            for id in &ids {
                subject(
                    &store,
                    &actor,
                    if payment { "payments" } else { "invoices" },
                    *id,
                    "export-pdf",
                )?;
            }
            let (name, media, bytes) = if zip {
                (
                    "单据报表.zip".into(),
                    "application/zip",
                    render::zip_documents(files)?,
                )
            } else {
                let (name, bytes) = files.pop().ok_or_else(|| unavailable("PDF 输出缺失。"))?;
                (name, "application/pdf", bytes)
            };
            let mut output = TaskOutput::file(name, media, bytes);
            output.destination = target;
            Ok(output)
        },
    )
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let kind = report_type(if body["reportType"].is_string() {
        body["reportType"].as_str().unwrap()
    } else {
        query_value(query, "reportType")
    })?;
    match operation {
        LIST_REPORT_TEMPLATES => catalog(&service.store, &service.paths, actor, kind),
        GET_REPORT_TEMPLATE_CONTENT => {
            let template = template(
                &service.store,
                &service.paths,
                actor,
                query_value(query, "templatePath"),
                kind,
                false,
            )?;
            Ok(
                json!({"reportType":kind,"displayName":template.label(),"templatePath":template.path(),"content":base64::engine::general_purpose::STANDARD.encode(template.content()),"contentEncoding":"base64","revision":super::media::digest(template.content()),"withSealDefault":false,"storagePolicy":"内置模板随程序提供，用户模板存入业务数据库。"}),
            )
        }
        PREVIEW_INVOICE_REPORT_HTML
        | PREVIEW_INVOICE_REPORT_DRAFT_HTML
        | PREVIEW_PAYMENT_VOUCHER_HTML
        | PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML => {
            let (document, mut response) =
                preview_document(service, actor, operation, parameters, body)?;
            response["html"] = json!(document.html()?);
            Ok(response)
        }
        PREVIEW_REPORT_TEMPLATE_CONTENT => {
            auth::authorize(
                actor,
                if kind == "PaymentVoucher" {
                    "document.payments"
                } else {
                    "document.invoices"
                },
                "view",
            )?;
            let content = text(body, "content");
            let template = if let Some(preset) = render::BUILTINS
                .into_iter()
                .find(|v| v.source() == content.as_bytes())
            {
                Template::Builtin(preset)
            } else {
                let design = validate_content(kind, &content)?;
                Template::Custom {
                    design,
                    content,
                    name: "模板预览".into(),
                    path: "".into(),
                }
            };
            let mut data = if kind == "ExportDocument" {
                let date = service.clock.now().map_err(unavailable)?.today.to_string();
                ReportData::invoice(
                    &InvoiceDraft::demo(&date, "PREVIEW-001")
                        .build()
                        .map_err(invalid)?,
                    json!({}),
                    json!({}),
                    body["withSeal"] == true,
                )?
            } else {
                let value = contracts::overlay(
                    contracts::initial(contracts::schema("ApiPaymentDto")),
                    &json!({"payerName":"示例公司","payeeName":"示例收款单位","department":"业务部","project":"费用支付","cnyAmount":1234.56,"usdAmount":100,"invoiceNo":"PREVIEW-001","paymentMethod":"电汇"}),
                );
                ReportData::payment(&serde_json::from_value(value)?, json!({}))?
            };
            report_assets::hydrate(
                &service.store,
                actor,
                &mut data,
                Some(template.content()),
            )?;
            let html = template
                .render(&data, &crate::operation::cancellation_flag())?
                .html()?;
            Ok(json!({"reportType":kind,"withSeal":body["withSeal"],"html":html}))
        }
        _ => start(service, actor, operation, parameters, body),
    }
}
