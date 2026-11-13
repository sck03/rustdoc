//! The same authorization and publication path accepts in-process bytes and
//! HTTP files. Validation seeks over the input; large originals are not buffered.
use super::*;
use std::io::{BufRead, BufReader, Seek, SeekFrom};
use unicode_normalization::UnicodeNormalization;

pub(super) fn prepare(
    store: &store::Store,
    name: &str,
    input: &mut (impl BufRead + Seek),
    size: u64,
    evidence: bool,
) -> error::Result<(String, &'static str, file_store::PreparedObject)> {
    store.files.configuration()?.policy.check_upload(size)?;
    input.seek(SeekFrom::Start(0))?;
    let (name, media) = if evidence {
        media::document_reader(name, input, size)?
    } else {
        let name = name.nfc().collect::<String>();
        if !crate::paths::valid_file_name(&name) {
            return Err(error::invalid("资料文件名无效。"));
        }
        let media = match std::path::Path::new(&name)
            .extension()
            .and_then(|v| v.to_str())
            .unwrap_or("")
            .to_ascii_lowercase()
            .as_str()
        {
            "pdf" => "application/pdf",
            "png" => "image/png",
            "jpg" | "jpeg" => "image/jpeg",
            "txt" => "text/plain",
            _ => "application/octet-stream",
        };
        (name, media)
    };
    input.seek(SeekFrom::Start(0))?;
    let prepared = store.files.prepare_reader(input, size)?;
    if prepared.object.size != size {
        return Err(error::invalid("上传文件长度不完整。"));
    }
    Ok((name, media, prepared))
}

impl NativeService {
    pub fn accepts_document_upload(operation: Operation) -> bool {
        matches!(
            operation,
            UPLOAD_BUSINESS_ATTACHMENT | UPLOAD_PERSONNEL_ATTACHMENT
        ) || oa::metadata(operation)
            .or_else(|| communication::metadata(operation))
            .is_some_and(|meta| meta["action"] == "upload")
    }

    pub fn upload_document_file(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        metadata: Value,
        name: &str,
        file: std::fs::File,
        token: &str,
    ) -> error::Result<Value> {
        let size = file.metadata()?.len();
        self.upload_document_reader(
            operation,
            parameters,
            metadata,
            name,
            &mut BufReader::new(file),
            size,
            token,
        )
    }

    pub(super) fn upload_document_reader(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        metadata: Value,
        name: &str,
        input: &mut (impl BufRead + Seek),
        size: u64,
        token: &str,
    ) -> error::Result<Value> {
        let _access = maintenance::acquire_read(&self.maintenance)?;
        let actor = self.sessions.actor(&self.store, token)?;
        self.authorize_operation(&actor, operation, &[])?;
        let value = match operation {
            UPLOAD_BUSINESS_ATTACHMENT => attachments::upload(
                &self.store,
                &actor,
                records::id(parameters)?,
                metadata,
                name,
                input,
                size,
            ),
            UPLOAD_PERSONNEL_ATTACHMENT => personnel_files::upload(
                &self.store,
                &actor,
                parameters,
                &metadata,
                name,
                input,
                size,
            ),
            _ if Self::accepts_document_upload(operation) => record_documents::upload(
                self, &actor, operation, parameters, &metadata, name, input, size,
            ),
            _ => Err(error::invalid("该操作不接受业务附件。")),
        }?;
        Ok(contracts::dto(contracts::response(operation.id), value))
    }
}
