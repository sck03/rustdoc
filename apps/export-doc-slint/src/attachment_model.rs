use export_doc_engine::generated_api::*;
use serde_json::Value;
use std::path::PathBuf;

#[derive(Default)]
pub struct AttachmentModel {
    pub company: String,
    pub categories: Vec<BusinessAttachmentCategoryRecord>,
    pub invoices: Vec<Value>,
    pub invoice_id: Option<i64>,
    pub category_id: Option<i64>,
    pub include_archived: bool,
    pub details: Option<BusinessAttachmentDetails>,
    pub source: Option<PathBuf>,
    pub preview_kind: String,
}
pub fn file_size(bytes: i64) -> String {
    if bytes < 1024 {
        format!("{bytes} B")
    } else if bytes < 1024 * 1024 {
        format!("{} KiB", (bytes + 1023) / 1024)
    } else {
        format!(
            "{}.{:01} MiB",
            bytes / (1024 * 1024),
            (bytes % (1024 * 1024)) * 10 / (1024 * 1024)
        )
    }
}
