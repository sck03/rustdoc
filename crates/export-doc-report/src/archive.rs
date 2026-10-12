use crate::{Result, error::invalid};
use export_doc_domain::report_batches::MAX_ARCHIVE_BYTES;
use std::io::{Cursor, Write};

/// Compress one completed PDF at a time instead of retaining all source bytes.
pub struct DocumentArchive {
    writer: zip::ZipWriter<Cursor<Vec<u8>>>,
    names: std::collections::BTreeSet<String>,
    size: usize,
}

impl Default for DocumentArchive {
    fn default() -> Self {
        Self::new()
    }
}

impl DocumentArchive {
    pub fn new() -> Self {
        Self {
            writer: zip::ZipWriter::new(Cursor::new(Vec::new())),
            names: Default::default(),
            size: 0,
        }
    }
    pub fn add(&mut self, name: &str, bytes: &[u8]) -> Result<()> {
        if name.is_empty() || name.contains(['/', '\\']) || !self.names.insert(name.to_owned()) {
            return Err(invalid("压缩包文件名无效或重复。"));
        }
        self.size = self
            .size
            .checked_add(bytes.len())
            .ok_or_else(|| invalid("报表压缩包超过容量。"))?;
        if self.size > MAX_ARCHIVE_BYTES {
            return Err(invalid("报表压缩包超过 64 MiB。"));
        }
        self.writer
            .start_file(
                name,
                zip::write::SimpleFileOptions::default()
                    .compression_method(zip::CompressionMethod::Deflated),
            )
            .map_err(|e| invalid(format!("报表压缩失败：{e}")))?;
        self.writer
            .write_all(bytes)
            .map_err(|e| invalid(format!("报表压缩失败：{e}")))?;
        Ok(())
    }
    pub fn finish(self) -> Result<Vec<u8>> {
        let bytes = self
            .writer
            .finish()
            .map_err(|e| invalid(format!("报表压缩失败：{e}")))?
            .into_inner();
        if bytes.len() > MAX_ARCHIVE_BYTES {
            return Err(invalid("报表压缩包超过 64 MiB。"));
        }
        Ok(bytes)
    }
}

pub fn zip_documents(files: Vec<(String, Vec<u8>)>) -> Result<Vec<u8>> {
    let mut archive = DocumentArchive::new();
    for (name, bytes) in files {
        archive.add(&name, &bytes)?;
    }
    archive.finish()
}
