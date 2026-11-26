//! Company-owned defaults are resolved by the application, not the Excel parser.
use crate::engine::{
    error::{Result, error},
    records::text,
    store::{self, Actor, Store},
};
use crate::generated_api::ApiExcelImportPreviewResponse;

pub(super) fn exporter_name(store: &Store, actor: &Actor) -> Result<String> {
    if actor.company.is_empty() {
        return Ok(String::new());
    }
    store.read(|tx| {
        let company = tx
            .find_identity("companies", &store::normalize(&actor.company))?
            .ok_or_else(|| error(404, "当前单位不存在，请重新选择单位身份。"))?;
        Ok(text(&company, "name"))
    })
}

pub(super) fn apply_to_preview(
    store: &Store,
    actor: &Actor,
    preview: &mut ApiExcelImportPreviewResponse,
) -> Result<()> {
    if let Some(invoice) = &mut preview.invoice {
        if invoice.exporter_name_cn.trim().is_empty() {
            invoice.exporter_name_cn = exporter_name(store, actor)?;
        }
        if let Some(exporter) = &mut preview.exporter {
            if exporter.exporter_name_cn.trim().is_empty() {
                exporter
                    .exporter_name_cn
                    .clone_from(&invoice.exporter_name_cn);
            }
        }
    }
    Ok(())
}
