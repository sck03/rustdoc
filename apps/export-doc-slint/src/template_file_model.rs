use export_doc_engine::generated_api::Operation;
use serde_json::Value;
use std::path::PathBuf;

/// A file mutation that needs a fresh content revision before it can run.
#[derive(Clone)]
pub enum PendingTemplate {
    Request {
        operation: Operation,
        body: Value,
        reply: String,
    },
    Upload {
        metadata: Value,
        source: PathBuf,
    },
}

#[derive(Default)]
pub struct TemplateFileState {
    /// (template path, revision) taken from the last content response.
    pub revision: Option<(String, String)>,
    pub pending: Option<PendingTemplate>,
}
