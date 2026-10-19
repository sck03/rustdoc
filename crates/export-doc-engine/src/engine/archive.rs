//! Bounded, cancellable ZIP entry reads shared by business imports.
use super::error::{Result, invalid};
use std::io::{Read, Write};

pub(super) fn read_entry<R: Read>(
    entry: &mut zip::read::ZipFile<'_, R>,
    limit: u64,
) -> Result<Vec<u8>> {
    let mut bytes = Vec::new();
    copy_entry(entry, limit, &mut bytes)?;
    Ok(bytes)
}

pub(super) fn verify_entry<R: Read>(
    entry: &mut zip::read::ZipFile<'_, R>,
    limit: u64,
) -> Result<()> {
    copy_entry(entry, limit, &mut std::io::sink())
}

fn copy_entry<R: Read>(
    entry: &mut zip::read::ZipFile<'_, R>,
    limit: u64,
    output: &mut impl Write,
) -> Result<()> {
    let expected = entry.size();
    if expected > limit {
        return Err(invalid("压缩包文件超过解压容量上限。"));
    }
    let mut buffer = [0u8; 64 * 1024];
    let mut total = 0u64;
    loop {
        crate::operation::check()?;
        // Read at most one byte beyond the declared length, including at EOF,
        // so forged lengths cannot defer the bound until after decompression.
        let next = (expected - total).min(buffer.len() as u64 - 1) as usize + 1;
        let length = entry
            .read(&mut buffer[..next])
            .map_err(|_| invalid("压缩包文件内容或校验码损坏。"))?;
        total += length as u64;
        if total > expected || (length == 0 && total != expected) {
            return Err(invalid("压缩包文件实际长度与目录不一致。"));
        }
        if length == 0 {
            return Ok(());
        }
        output.write_all(&buffer[..length])?;
    }
}

#[cfg(test)]
#[path = "tests/archive.rs"]
mod tests;
