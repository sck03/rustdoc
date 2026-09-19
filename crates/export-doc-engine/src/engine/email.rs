//! SMTP use cases and minimal, durable delivery summaries. No message body or
//! attachment path is persisted; an uncertain attempt is never auto-replayed.
use super::{
    NativeService, auth,
    error::{Result, conflict, error, invalid, unavailable},
    media,
    records::text,
    settings,
    store::{self, Actor, Store},
    tasks::{TaskOutput, retry::Replay},
};
use crate::{contracts, generated_api::*, operation, paths};
use export_doc_mail::{attachment, recipient, transport};
use serde_json::{Value, json};
use std::{collections::BTreeSet, path::PathBuf, time::Duration};
pub const OPERATIONS: &[Operation] = &[
    GET_EMAIL_TOOL_STATUS,
    LIST_EMAIL_DELIVERIES,
    SUGGEST_EMAIL_SERVER_CONFIG,
    SEND_EMAIL,
    TEST_EMAIL_CONNECTION,
    START_INVOICE_DOCUMENT_EMAIL_JOB,
];
const KIND: &str = "email-deliveries";
const PERMISSION: &str = "common.email-delivery";
const POLICY: &str = "仅保存投递摘要和幂等标识；正文与附件不留存，桌面附件从用户选择的位置读取。";

fn config(
    store: &Store,
    protector: &crate::secrets::Protector,
) -> Result<(Value, transport::Config)> {
    let settings = settings::current(store)?;
    let email = &settings["email"];
    let from = text(email, "fromAddress");
    let from = if from.trim().is_empty() {
        text(email, "userName")
    } else {
        from
    };
    let config = transport::Config {
        host: text(email, "smtpHost").trim().into(),
        port: email["smtpPort"]
            .as_u64()
            .and_then(|n| u16::try_from(n).ok())
            .filter(|p| *p > 0)
            .ok_or_else(|| invalid("SMTP 端口无效。"))?,
        tls: email["enableSsl"] == true,
        user: text(email, "userName").trim().into(),
        password: settings::credential(store, protector, "/email/password")?,
        from: from.trim().into(),
        display_name: text(email, "fromDisplayName").trim().into(),
    };
    Ok((email.clone(), config))
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    if operation == START_INVOICE_DOCUMENT_EMAIL_JOB {
        return document_email(service, actor, parameters, body);
    }
    if operation == LIST_EMAIL_DELIVERIES {
        let keyword = super::hs::read(query, "keyword").trim();
        let status = super::hs::read(query, "status");
        if keyword.chars().count() > 100
            || !["", "Sent", "Attempting", "Uncertain"].contains(&status)
        {
            return Err(invalid("邮件投递查询条件无效。"));
        }
        let page = super::hs::read(query, "pageNumber");
        let size = super::hs::read(query, "pageSize");
        if (!page.is_empty() && page.parse::<u32>().ok().filter(|n| *n > 0).is_none())
            || (!size.is_empty()
                && size
                    .parse::<u32>()
                    .ok()
                    .filter(|n| (1..=100).contains(n))
                    .is_none())
        {
            return Err(invalid("邮件投递页码或每页数量无效。"));
        }
        let keyword = store::normalize(keyword);
        let mut rows = service.store.all(KIND)?;
        rows.retain(|row| {
            auth::visible(actor, PERMISSION, "view-delivery", row)
                && (status.is_empty() || row["status"] == status)
                && ["recipient", "subject"]
                    .iter()
                    .any(|k| store::normalize(&text(row, k)).contains(&keyword))
        });
        rows.sort_by(|a, b| b["createdAt"].as_str().cmp(&a["createdAt"].as_str()));
        return Ok(contracts::project(
            contracts::response(operation.id),
            store::page_only(rows, query),
        ));
    }
    if operation == SUGGEST_EMAIL_SERVER_CONFIG {
        return suggestion(body);
    }
    let email = settings::current(&service.store)?["email"].clone();
    let from = text(&email, "fromAddress");
    let from = if from.trim().is_empty() {
        text(&email, "userName")
    } else {
        from
    };
    if operation == GET_EMAIL_TOOL_STATUS {
        return Ok(
            json!({"isConfigured":!text(&email,"smtpHost").trim().is_empty()&&!from.trim().is_empty(),"smtpHost":email["smtpHost"],"smtpPort":email["smtpPort"],"enableSsl":email["enableSsl"],"fromAddress":from,"fromDisplayName":email["fromDisplayName"],"storagePolicy":POLICY}),
        );
    }
    if operation == TEST_EMAIL_CONNECTION && !actor.admin {
        return Err(error(403, "只有管理员可以测试邮件连接。"));
    }
    let test = operation == TEST_EMAIL_CONNECTION;
    let body = if test {
        json!({"toAddress":from,"subject":"ExportDocManager SMTP Test","body":"<p>This is a test email from ExportDocManager.</p>","attachmentPaths":[]})
    } else {
        body.clone()
    };
    let sent = send(service, actor, parameters, &body, test)?;
    if test {
        Ok(
            json!({"success":true,"message":"连接测试成功，测试邮件已发送到发件人地址。","fromAddress":from,"smtpHost":email["smtpHost"],"storagePolicy":POLICY}),
        )
    } else {
        Ok(sent)
    }
}
fn attachments(service: &NativeService, body: &Value) -> Result<Vec<attachment::Attachment>> {
    let inputs: Vec<String> = serde_json::from_value(
        body.get("attachmentPaths")
            .filter(|v| !v.is_null())
            .cloned()
            .unwrap_or_else(|| json!([])),
    )
    .map_err(|_| invalid("附件路径必须是文本列表。"))?;
    if inputs.iter().any(|p| !p.trim().is_empty()) && service.provider()? != "SQLite" {
        return Err(error(403, "网页或容器邮件不得读取服务器本地附件路径。"));
    }
    let mut paths = BTreeSet::new();
    let mut files = vec![];
    let mut total = 0;
    for input in inputs.iter().map(|s| s.trim()).filter(|s| !s.is_empty()) {
        operation::check()?;
        let path = PathBuf::from(input);
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        let path = std::fs::canonicalize(&path).map_err(|cause| {
            if cause.kind() == std::io::ErrorKind::NotFound {
                error(404, "所选邮件附件不存在。")
            } else {
                unavailable("无法读取所选邮件附件。")
            }
        })?;
        let key = if cfg!(windows) {
            path.to_string_lossy().to_lowercase()
        } else {
            path.to_string_lossy().into_owned()
        };
        if !paths.insert(key) {
            continue;
        }
        if files.len() >= attachment::MAX_COUNT {
            return Err(invalid("邮件附件最多 10 个。"));
        }
        let name = path
            .file_name()
            .and_then(|s| s.to_str())
            .filter(|s| paths::valid_file_name(s))
            .ok_or_else(|| invalid("邮件附件文件名无效。"))?
            .to_owned();
        let bytes = media::read_local(&path, attachment::MAX_SINGLE)?;
        total += bytes.len();
        if total > attachment::MAX_TOTAL {
            return Err(error(413, "邮件附件总计不能超过 18 MiB。"));
        }
        let mime = attachment::inspect(&name, &bytes).map_err(invalid)?;
        files.push(attachment::Attachment { name, mime, bytes });
    }
    Ok(files)
}
fn send(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
    test: bool,
) -> Result<Value> {
    let (email, config) = config(&service.store, &service.protector)?;
    let recipient = recipient::mailbox(&text(body, "toAddress")).map_err(invalid)?;
    if !test
        && !recipient::allowed(
            &recipient,
            &text(&email, "recipientAllowList"),
            &text(&email, "recipientBlockList"),
        )
        .map_err(invalid)?
    {
        return Err(error(403, "收件人被邮件外发规则禁止。"));
    }
    let recipient = recipient.email.to_string();
    let subject = text(body, "subject").trim().to_owned();
    let html = text(body, "body");
    let files = attachments(service, body)?;
    deliver(
        &service.store,
        actor,
        &recipient,
        &subject,
        &html,
        files,
        if test { "ConnectionTest" } else { "EmailTool" },
        &config,
        parameters,
    )
}
#[allow(clippy::too_many_arguments)]
fn deliver(
    store: &Store,
    actor: &Actor,
    recipient: &str,
    subject: &str,
    html: &str,
    files: Vec<attachment::Attachment>,
    kind: &str,
    config: &transport::Config,
    parameters: &[(&str, String)],
) -> Result<Value> {
    let count = files.len();
    let digests: Vec<_> = files
        .iter()
        .map(|f| json!({"name":f.name,"sha256":media::digest(&f.bytes)}))
        .collect();
    let fingerprint = media::digest(&serde_json::to_vec(&json!([
        recipient, subject, html, digests
    ]))?);
    let message = transport::message(config, recipient, subject, html, files).map_err(invalid)?;
    let delivery_id = parameters
        .iter()
        .find(|(key, _)| key.eq_ignore_ascii_case("Idempotency-Key"))
        .map(|(_, value)| value.trim().to_owned())
        .filter(|s| !s.is_empty())
        .unwrap_or(paths::nonce().map_err(unavailable)?);
    if !(8..=120).contains(&delivery_id.len())
        || !delivery_id.is_ascii()
        || delivery_id.chars().any(char::is_control)
    {
        return Err(invalid(
            "Idempotency-Key 必须为 8 至 120 个可显示 ASCII 字符。",
        ));
    }
    let identity = format!("{}:{delivery_id}", actor.id);
    let attempt=store.transaction(|tx|{
        if let Some(row)=tx.find_identity(KIND,&store::normalize(&identity))?{
            if row["requestFingerprint"]!=fingerprint{return Err(conflict("此幂等标识已用于其他邮件内容。"));}
            if row["status"]=="Sent"{return Ok(None);}
            return Err(conflict("此邮件已尝试投递，结果尚不确定，请核实投递记录后再操作。"));
        }
        store::save(tx,KIND,0,json!({"deliveryId":delivery_id,"requestFingerprint":fingerprint,"jobId":"","kind":kind,"recipient":recipient,"subject":subject,"attachmentCount":count,"status":"Attempting","errorMessage":"","sentAt":null}),Some(identity.clone()),actor,"send").map(Some)
    })?;
    let duplicate = attempt.is_none();
    if let Some(mut row) = attempt {
        let result = transport::send(&config, message, &|| {
            operation::check().map_err(|e| e.to_string())
        });
        let failure = result.as_ref().err().map(transport::Failure::message);
        row["status"] = json!(if result.is_ok() { "Sent" } else { "Uncertain" });
        row["errorMessage"] = json!(failure.clone().unwrap_or_default());
        if result.is_ok() {
            row["sentAt"] = json!(store::timestamp());
        }
        // Persist SMTP acknowledgement independently of HTTP cancellation.
        operation::OperationScope::new(Duration::from_secs(10)).run(|| {
            store.transaction(|tx| {
                store::save(
                    tx,
                    KIND,
                    row["id"].as_i64().unwrap(),
                    row,
                    Some(identity),
                    actor,
                    if result.is_ok() { "sent" } else { "uncertain" },
                )
            })
        })?;
        if let Err(failed) = result {
            return Err(error(
                if matches!(
                    failed,
                    transport::Failure::Timeout | transport::Failure::Cancelled(_)
                ) {
                    504
                } else {
                    503
                },
                failure.unwrap(),
            ));
        }
    }
    Ok(
        json!({"success":true,"message":if duplicate{"邮件已发送（幂等请求）。"}else{"邮件已发送。"},"toAddress":recipient,"subject":subject,"attachmentCount":count,"storagePolicy":POLICY}),
    )
}
const DEFAULT_DOCUMENT_EMAIL_SUBJECT: &str = "Export Documents for Invoice {InvoiceNo}";
const DEFAULT_DOCUMENT_EMAIL_BODY: &str =
    "Dear Customer,\r\n\r\nPlease find the attached export documents.\r\n\r\nBest regards,";

fn document_email(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let invoice_id = super::records::id(parameters)?;
    let items = super::document_packages::items(body)?;
    let invoice = service.store.get("invoices", invoice_id)?;
    if !auth::visible(actor, "document.invoice-output", "send-email", &invoice) {
        return Err(error(403, "单据不在当前账号的邮件输出范围内。"));
    }
    let to = text(body, "toAddress").trim().to_owned();
    if !to.is_empty() {
        recipient::mailbox(&to).map_err(invalid)?;
    }
    let subject = text(body, "subject").trim().to_owned();
    let html = text(body, "body");
    let merged = body["includeMergedPdf"] == true;
    let invoice_no = invoice["invoiceNo"].as_str().unwrap_or("").to_owned();
    let customer_id = invoice["customerId"].as_i64().unwrap_or(0);
    let actor_id = actor.id;
    let idempotency = media::digest(&serde_json::to_vec(&json!([
        invoice_id, &to, &subject, &html, merged, items
    ]))?);
    let store = service.store.clone();
    let font = service.paths.font_path.clone();
    let clock = service.clock.clone();
    let protector = service.protector.clone();
    let (email_check, _) = config(&service.store, &service.protector)?;
    if text(&email_check, "smtpHost").trim().is_empty()
        || text(&email_check, "fromAddress").trim().is_empty()
            && text(&email_check, "userName").trim().is_empty()
    {
        return Err(invalid(
            "邮件服务尚未配置，请先在设置中填写 SMTP 服务器和发件人。",
        ));
    }
    service.jobs.start_replayable(
        actor,
        "ReportDocumentEmail",
        "发票单据邮件发送",
        Some(Replay::new(
            START_INVOICE_DOCUMENT_EMAIL_JOB,
            parameters,
            body,
        )),
        move |cancelled| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, START_INVOICE_DOCUMENT_EMAIL_JOB, &[])?;
            let invoice = store.get("invoices", invoice_id)?;
            if !auth::visible(&actor, "document.invoice-output", "send-email", &invoice) {
                return Err(error(403, "生成期间单据邮件权限发生变化。"));
            }
            let files = super::document_packages::files(
                &store,
                &actor,
                invoice_id,
                &items,
                merged,
                "send-email",
                &font,
                cancelled,
            )?;
            let attachments: Vec<attachment::Attachment> = files
                .into_iter()
                .map(|(name, bytes)| attachment::Attachment {
                    name,
                    mime: "application/pdf",
                    bytes,
                })
                .collect();
            if attachments.is_empty() {
                return Err(conflict("未能生成任何单据附件。"));
            }
            let customer = if customer_id > 0 {
                store.connection()?.get("customers", customer_id)?
            } else {
                None
            };
            let customer_name = customer
                .as_ref()
                .map(|c| text(c, "customerNameEN"))
                .filter(|value| !value.is_empty())
                .or_else(|| customer.as_ref().map(|c| text(c, "customerNameCN")))
                .unwrap_or_default();
            let date_text = clock
                .now()
                .map_err(unavailable)?
                .today
                .format("%Y%m%d")
                .to_string();
            let (email, config) = config(&store, &protector)?;
            let subject = if subject.is_empty() {
                let template = text(&email, "documentEmailSubjectTemplate");
                apply_template(
                    if template.trim().is_empty() {
                        DEFAULT_DOCUMENT_EMAIL_SUBJECT
                    } else {
                        template.as_str()
                    },
                    &invoice_no,
                    &customer_name,
                    &date_text,
                )
            } else {
                subject.clone()
            };
            let body_html = if html.trim().is_empty() {
                let template = text(&email, "documentEmailBodyTemplate");
                apply_template(
                    if template.trim().is_empty() {
                        DEFAULT_DOCUMENT_EMAIL_BODY
                    } else {
                        template.as_str()
                    },
                    &invoice_no,
                    &customer_name,
                    &date_text,
                )
            } else {
                html.clone()
            };
            let recipient = if to.is_empty() {
                customer
                    .as_ref()
                    .map(|c| text(c, "email"))
                    .filter(|value| !value.trim().is_empty())
                    .ok_or_else(|| invalid("收件人地址不能为空，且当前发票客户档案没有邮箱。"))?
            } else {
                to.clone()
            };
            let mailbox = recipient::mailbox(&recipient).map_err(invalid)?;
            if !recipient::allowed(
                &mailbox,
                &text(&email, "recipientAllowList"),
                &text(&email, "recipientBlockList"),
            )
            .map_err(invalid)?
            {
                return Err(error(403, "收件人被邮件外发规则禁止。"));
            }
            let recipient = mailbox.email.to_string();
            let delivered = deliver(
                &store,
                &actor,
                &recipient,
                &subject,
                &body_html,
                attachments,
                "ReportDocumentEmail",
                &config,
                &[("Idempotency-Key", idempotency)],
            )?;
            let count = delivered["attachmentCount"].as_u64().unwrap_or_default();
            Ok(TaskOutput {
                file: None,
                destination: None,
                detail: format!(
                    "单据邮件已发送至 {}，共 {} 个附件。",
                    delivered["toAddress"].as_str().unwrap_or(&recipient),
                    count
                ),
                directory: None,
            })
        },
    )
}

fn apply_template(
    template: &str,
    invoice_no: &str,
    customer_name: &str,
    date_text: &str,
) -> String {
    let lowered = template.to_ascii_lowercase();
    let mut output = String::with_capacity(template.len());
    let mut cursor = 0;
    while let Some(open) = lowered[cursor..].find('{') {
        let start = cursor + open;
        output.push_str(&template[cursor..start]);
        match lowered[start..].find('}').map(|close| start + close) {
            Some(end) if end > start => {
                let value = match &lowered[start + 1..end] {
                    "invoiceno" => invoice_no,
                    "customer" => customer_name,
                    "date" => date_text,
                    _ => "",
                };
                if value.is_empty() {
                    output.push_str(&template[start..=end]);
                } else {
                    output.push_str(value);
                }
                cursor = end + 1;
            }
            _ => {
                output.push_str(&template[start..]);
                return output;
            }
        }
    }
    output.push_str(&template[cursor..]);
    output
}
pub fn recover(store: &Store) -> Result<()> {
    store.transaction(|tx| {
        for mut row in store::all(tx, KIND)?
            .into_iter()
            .filter(|r| r["status"] == "Attempting")
        {
            row["status"] = json!("Uncertain");
            row["errorMessage"] = json!("程序在投递完成前退出，结果未知，未自动重发。");
            let actor = Actor {
                id: 0,
                name: "系统".into(),
                company: String::new(),
                department: String::new(),
                admin: true,
                grants: vec![],
            };
            let identity = format!("{}:{}", row["ownerUserId"], text(&row, "deliveryId"));
            store::save(
                tx,
                KIND,
                row["id"].as_i64().unwrap_or(0),
                row,
                Some(identity),
                &actor,
                "uncertain",
            )?;
        }
        Ok(())
    })
}
fn suggestion(body: &Value) -> Result<Value> {
    let address = recipient::mailbox(&text(body, "emailAddress")).map_err(invalid)?;
    let domain = address.email.domain();
    let (host, port) = match domain {
        "qq.com" | "vip.qq.com" | "foxmail.com" => ("smtp.qq.com".into(), 465),
        "outlook.com" | "hotmail.com" | "live.com" => ("smtp.office365.com".into(), 587),
        "gmail.com" => ("smtp.gmail.com".into(), 587),
        "yahoo.com" => ("smtp.mail.yahoo.com".into(), 465),
        domain
            if domain == "263.net"
                || domain.ends_with(".263.net")
                || domain == "263.net.cn"
                || domain.ends_with(".263.net.cn") =>
        {
            ("smtp.263.net".into(), 465)
        }
        domain if domain == "263xmail.com" || domain.ends_with(".263xmail.com") => {
            ("smtpcom.263xmail.com".into(), 465)
        }
        _ => (format!("smtp.{domain}"), 465),
    };
    Ok(
        json!({"success":true,"message":"已推断 SMTP 配置，请核对服务商设置。","emailAddress":address.email.to_string(),"smtpHost":host,"smtpPort":port,"enableSsl":true,"storagePolicy":"仅在内存中提供建议，不保存配置。"}),
    )
}
