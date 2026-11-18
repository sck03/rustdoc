//! Materialize downloads only in assertions; production HTTP uses the bounded stream.
use export_doc_engine::{
    api::ApiError,
    engine::{NativeService, tasks::FileOutput},
    generated_api::Operation,
};

pub trait DownloadFile {
    fn download_file(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<FileOutput, ApiError>;
}
impl DownloadFile for NativeService {
    fn download_file(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<FileOutput, ApiError> {
        self.prepare_file_download(operation, parameters, token)?
            .into_memory()
    }
}
