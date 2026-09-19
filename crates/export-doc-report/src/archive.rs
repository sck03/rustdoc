use crate::{Result, error::invalid};
use std::io::{Cursor, Write};
pub fn zip_documents(files: Vec<(String, Vec<u8>)>) -> Result<Vec<u8>> {
    let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let mut names = std::collections::BTreeSet::new();
    let mut size = 0usize;
    for (name, bytes) in files {
        if name.is_empty() || name.contains(['/', '\\']) || !names.insert(name.clone()) {
            return Err(invalid("压缩包文件名无效或重复。"));
        }
        size = size
            .checked_add(bytes.len())
            .ok_or_else(|| invalid("报表压缩包超过容量。"))?;
        if size > 64 * 1024 * 1024 {
            return Err(invalid("报表压缩包超过 64 MiB。"));
        }
        writer
            .start_file(
                name,
                zip::write::SimpleFileOptions::default()
                    .compression_method(zip::CompressionMethod::Deflated),
            )
            .map_err(|e| invalid(format!("报表压缩失败：{e}")))?;
        writer
            .write_all(&bytes)
            .map_err(|e| invalid(format!("报表压缩失败：{e}")))?;
    }
    let bytes = writer
        .finish()
        .map_err(|e| invalid(format!("报表压缩失败：{e}")))?
        .into_inner();
    if bytes.len() > 64 * 1024 * 1024 {
        return Err(invalid("报表压缩包超过 64 MiB。"));
    }
    Ok(bytes)
}
