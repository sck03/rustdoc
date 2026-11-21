//! Bounded PDF cache belongs to one service; every page still rechecks source access.
use super::super::media;
use super::*;
use sha2::{Digest, Sha256};
mod cache;
pub(in crate::engine) use cache::Cache;
struct Pdf {
    bytes: Vec<u8>,
    page_count: u32,
}

pub(super) fn response(
    service: &NativeService,
    actor: &Actor,
    data: ReportData,
    template: Template,
    body: &Value,
    mut metadata: Value,
) -> Result<Value> {
    let format = body["previewFormat"].as_str().unwrap_or("Svg");
    if format == "Svg" {
        metadata["html"] = json!(
            template
                .render(
                    &data,
                    &service.report_fonts,
                    &crate::operation::cancellation_flag()
                )?
                .html_with_limit(
                    &service.report_fonts,
                    &crate::operation::cancellation_flag(),
                    resources::PREVIEW_BYTES,
                )?
        );
        validate_current_access(service, actor, &data, &template, &metadata)?;
        return Ok(metadata);
    }
    if format != "PdfPage" {
        return Err(invalid("未知预览格式。"));
    }
    let page = body
        .get("pageIndex")
        .map_or(Some(0), Value::as_u64)
        .filter(|page| *page < 500)
        .ok_or_else(|| invalid("PDF 预览页码须为 0–499。"))? as u32;
    let mut hash = Sha256::new();
    hash.update(actor.id.to_le_bytes());
    hash.update(template.content());
    hash.update(serde_json::to_vec(&data.root)?);
    for (name, image) in &data.images {
        hash.update(name.as_bytes());
        hash.update(media::digest(&image.bytes).as_bytes());
    }
    let revision = crate::paths::hex(&hash.finalize());
    if body["previewRevision"]
        .as_str()
        .is_some_and(|expected| expected != revision)
    {
        return Err(conflict("模板或单据已变化，请重新生成预览。"));
    }
    let pdf = service.report_previews.pdf(&revision, || {
        let document = template.render(
            &data,
            &service.report_fonts,
            &crate::operation::cancellation_flag(),
        )?;
        let bytes = render::pdf_document(
            &document,
            &service.report_fonts,
            &crate::operation::cancellation_flag(),
        )?;
        Ok(Pdf {
            bytes,
            page_count: document.pages.len() as u32,
        })
    })?;
    if page >= pdf.page_count {
        return Err(invalid("PDF 预览页码超过实际页数。"));
    }
    let image = crate::pdf::render_page(&pdf.bytes, page, &service.paths.pdfium_path())?;
    use base64::{Engine, engine::general_purpose::STANDARD};
    metadata["html"] = json!(format!(
        "<!doctype html><html lang=\"zh-CN\"><head><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head><body style=\"margin:0;background:#e8ebed\"><img alt=\"PDF 第 {} 页\" style=\"display:block;max-width:100%;height:auto;margin:auto\" width=\"{}\" height=\"{}\" src=\"data:image/png;base64,{}\"></body></html>",
        page + 1,
        image.width,
        image.height,
        STANDARD.encode(image.png)
    ));
    metadata["pageIndex"] = json!(image.page_index);
    metadata["pageCount"] = json!(image.page_count);
    metadata["previewRevision"] = json!(revision);
    validate_current_access(service, actor, &data, &template, &metadata)?;
    Ok(metadata)
}

fn validate_current_access(
    service: &NativeService,
    actor: &Actor,
    data: &ReportData,
    template: &Template,
    metadata: &Value,
) -> Result<()> {
    crate::operation::check()?;
    let (operation, resource, id) = if let Some(id) = metadata["invoiceId"].as_i64() {
        (PREVIEW_INVOICE_REPORT_HTML, "invoices", id)
    } else if let Some(id) = metadata["paymentId"].as_i64() {
        (PREVIEW_PAYMENT_VOUCHER_HTML, "payments", id)
    } else {
        (PREVIEW_REPORT_TEMPLATE_CONTENT, "", 0)
    };
    super::super::licensing::check_operation(service, operation)?;
    let _lock = super::super::report_template_files::storage_lock(&service.paths)?;
    service.store.read(|tx| {
        let current = auth::refresh_actor_in(tx, actor)?;
        auth::authorize_operation(&current, operation, &[])?;
        if id > 0 {
            subject_in(tx, &current, resource, id, "preview")?;
        }
        if !template.path().is_empty() {
            template_in(
                tx,
                &service.paths,
                &current,
                template.path(),
                data.report_type,
                true,
            )?;
        }
        Ok(())
    })
}
