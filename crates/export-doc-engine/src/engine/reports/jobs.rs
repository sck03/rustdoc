use super::super::{
    records,
    tasks::{TaskOutput, retry::Replay},
};
use super::*;
use export_doc_domain::report_batches::{MAX_REPORTS, MAX_TEMPLATES};

fn requests(body: &Value, zip: bool, invoice_count: usize) -> Result<Vec<Value>> {
    let Some(paths) = body
        .get("templatePaths")
        .filter(|value| zip && !value.is_null())
    else {
        return Ok(vec![body.clone()]);
    };
    let paths: Vec<String> =
        serde_json::from_value(paths.clone()).map_err(|_| invalid("报表模板清单无效。"))?;
    let paths: Vec<_> = paths.iter().map(|path| path.trim()).collect();
    if paths.is_empty()
        || paths.len() > MAX_TEMPLATES
        || paths.iter().any(|path| path.is_empty())
        || paths
            .iter()
            .collect::<std::collections::BTreeSet<_>>()
            .len()
            != paths.len()
    {
        return Err(invalid(format!(
            "请选择 1–{MAX_TEMPLATES} 个不同的报表模板。"
        )));
    }
    if invoice_count.saturating_mul(paths.len()) > MAX_REPORTS {
        return Err(invalid(format!(
            "每次最多生成 {MAX_REPORTS} 份 PDF，请减少发票或模板数量。"
        )));
    }
    Ok(paths
        .into_iter()
        .map(|path| {
            json!({
                "reportType":body["reportType"],"templatePath":path,"withSeal":body["withSeal"]
            })
        })
        .collect())
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
pub(crate) fn invoice_document(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    fonts: &render::Fonts,
    actor: &Actor,
    id: i64,
    item: &Value,
    action: &str,
    cancelled: &AtomicBool,
) -> Result<(Document, String)> {
    let (data, template) = prepare(store, paths, actor, id, item, false, action)?;
    Ok((
        template.render(&data, fonts, cancelled)?,
        output_name(&data, &template, id),
    ))
}
pub(super) fn start(
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
            || ids.len() > export_doc_domain::report_batches::MAX_INVOICES
            || ids.iter().any(|v| *v <= 0)
            || ids.iter().collect::<std::collections::BTreeSet<_>>().len() != ids.len()
        {
            return Err(invalid("每次请选择 1–200 张不同的发票。"));
        }
        ids
    } else {
        vec![records::id(parameters)?]
    };
    let requests = requests(body, zip, ids.len())?;
    let target = destination(body, LOCAL_OPERATIONS.contains(&operation), zip)?;
    if target.is_some() && service.provider()? != "SQLite" {
        return Err(error(403, "服务器不能保存到客户端本机路径。"));
    }
    for id in &ids {
        for request in &requests {
            prepare(
                &service.store,
                &service.paths,
                actor,
                *id,
                request,
                payment,
                "export-pdf",
            )?;
        }
    }
    let store = service.store.clone();
    let paths = service.paths.clone();
    let fonts = service.report_fonts.clone();
    let actor_id = actor.id;
    let replay = Replay::new(operation, parameters, body);
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
            let mut archive = zip.then(render::DocumentArchive::new);
            let mut single_file = None;
            let mut output_index = 0;
            for id in &ids {
                for request in &requests {
                    crate::operation::check()?;
                    let actor = auth::current_actor(&store, actor_id)?;
                    auth::authorize_operation(&actor, operation, &[])?;
                    let (data, template) =
                        prepare(&store, &paths, &actor, *id, request, payment, "export-pdf")?;
                    let document = template.render(&data, &fonts, cancelled)?;
                    let pdf = render::pdf_document(&document, &fonts, cancelled)?;
                    let name = output_name(&data, &template, *id);
                    output_index += 1;
                    if let Some(archive) = &mut archive {
                        let name = if requests.len() > 1 {
                            paths::suggested_pdf_name(&format!(
                                "{output_index:03}-{}",
                                name.trim_end_matches(".pdf")
                            ))
                        } else {
                            name
                        };
                        archive.add(&name, &pdf)?;
                    } else {
                        single_file = Some((name, pdf));
                    }
                }
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
            for request in &requests {
                let kind = if payment {
                    "PaymentVoucher"
                } else {
                    report_type(&text(request, "reportType"))?
                };
                template(
                    &store,
                    &paths,
                    &actor,
                    &text(request, "templatePath"),
                    kind,
                    true,
                )?;
            }
            let (name, media, bytes) = if zip {
                (
                    "单据报表.zip".into(),
                    "application/zip",
                    archive
                        .ok_or_else(|| unavailable("报表压缩包输出缺失。"))?
                        .finish()?,
                )
            } else {
                let (name, bytes) = single_file.ok_or_else(|| unavailable("PDF 输出缺失。"))?;
                (name, "application/pdf", bytes)
            };
            let mut output = TaskOutput::file(name, media, bytes);
            output.destination = target;
            Ok(output)
        },
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn expands_all_templates_and_bounds_the_cartesian_product() {
        let body = json!({"reportType":"ExportDocument","templatePaths":["invoice.dtpl","packing.dtpl"],"withSeal":true});
        let expanded = requests(&body, true, 100).unwrap();
        assert_eq!(expanded.len(), 2);
        assert_eq!(expanded[1]["templatePath"], "packing.dtpl");
        assert_eq!(expanded[1]["withSeal"], true);
        assert!(requests(&body, true, 101).is_err());
        for invalid in [json!([]), json!([""]), json!(["x", " x "]), json!("x")] {
            assert!(requests(&json!({"templatePaths":invalid}), true, 1).is_err());
        }
        let original = json!({"templatePath":"invoice.dtpl"});
        assert_eq!(requests(&original, true, 1).unwrap(), vec![original]);
    }
}
