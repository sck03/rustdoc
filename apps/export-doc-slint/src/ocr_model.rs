use std::sync::Arc;
#[derive(Default)]
pub struct OcrModel {
    pub bytes: Option<Arc<Vec<u8>>>,
    pub name: String,
    pub width: u32,
    pub height: u32,
    pub lines: Vec<export_doc_engine::generated_api::ApiOcrLineDto>,
}
