//! Bounded PDF cache belongs to one service; every page still rechecks source access.
use super::super::media;
use super::*;
use sha2::{Digest, Sha256};
use std::{
    collections::VecDeque,
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

struct Entry {
    key: String,
    pdf: Arc<Pdf>,
    inserted: Instant,
}
struct Pdf {
    bytes: Vec<u8>,
    page_count: u32,
}
#[derive(Default)]
pub(in crate::engine) struct Cache {
    entries: Mutex<VecDeque<Entry>>,
    rendering: Mutex<()>,
}

impl Cache {
    fn pdf(&self, key: &str, render: impl FnOnce() -> Result<Pdf>) -> Result<Arc<Pdf>> {
        {
            let mut entries = self
                .entries
                .lock()
                .map_err(|_| unavailable("PDF 预览缓存异常。"))?;
            entries.retain(|entry| entry.inserted.elapsed() < Duration::from_secs(300));
            if let Some(entry) = entries.iter().find(|entry| entry.key == key) {
                return Ok(entry.pdf.clone());
            }
        }
        let pdf = Arc::new(render()?);
        let mut entries = self
            .entries
            .lock()
            .map_err(|_| unavailable("PDF 预览缓存异常。"))?;
        while entries.len() >= 4
            || entries
                .iter()
                .map(|entry| entry.pdf.bytes.len())
                .sum::<usize>()
                + pdf.bytes.len()
                > 64 * 1024 * 1024
        {
            if entries.pop_front().is_none() {
                break;
            }
        }
        entries.push_back(Entry {
            key: key.into(),
            pdf: pdf.clone(),
            inserted: Instant::now(),
        });
        Ok(pdf)
    }
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
                .html(
                    &service.report_fonts,
                    &crate::operation::cancellation_flag()
                )?
        );
        validate_current_access(service, actor, &data, &template, &metadata)?;
        return Ok(metadata);
    }
    if format != "PdfPage" {
        return Err(invalid("未知预览格式。"));
    }
    let waiting = Instant::now();
    let _rendering = loop {
        crate::operation::check()?;
        match service.report_previews.rendering.try_lock() {
            Ok(guard) => break guard,
            Err(std::sync::TryLockError::Poisoned(_)) => {
                return Err(unavailable("PDF 预览状态异常。"));
            }
            Err(std::sync::TryLockError::WouldBlock)
                if waiting.elapsed() < Duration::from_secs(5) =>
            {
                std::thread::sleep(Duration::from_millis(10))
            }
            Err(_) => return Err(error(429, "PDF 页图预览繁忙，请稍后重试。")),
        }
    };
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
    let revision = hash
        .finalize()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect::<String>();
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

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preview_cache_reuses_a_revision_and_evicts_old_entries() {
        let cache = Cache::default();
        let make = || {
            Ok(Pdf {
                bytes: vec![0; 1024],
                page_count: 2,
            })
        };
        let first = cache.pdf("one", make).unwrap();
        let same = cache
            .pdf("one", || panic!("same revision must reuse encoded PDF"))
            .unwrap();
        assert!(Arc::ptr_eq(&first, &same));
        for key in ["two", "three", "four", "five"] {
            cache.pdf(key, make).unwrap();
        }
        let entries = cache.entries.lock().unwrap();
        assert_eq!(entries.len(), 4);
        assert!(entries.iter().all(|entry| entry.key != "one"));
    }
}
