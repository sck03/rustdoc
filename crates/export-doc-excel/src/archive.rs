use crate::{Check, MAX_INPUT, Result};
use std::{
    collections::{BTreeMap, BTreeSet},
    io::{Cursor, Read, Write},
};
use unicode_normalization::UnicodeNormalization;
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};

pub struct Package(pub BTreeMap<String, Vec<u8>>);
pub fn validate(bytes: &[u8], check: Check<'_>) -> Result<()> {
    read(bytes, check, false).map(|_| ())
}

fn read(bytes: &[u8], check: Check<'_>, retain: bool) -> Result<BTreeMap<String, Vec<u8>>> {
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err("Excel 文件为空或超过 25 MiB。".into());
    }
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|e| format!("Excel 压缩包无效：{e}"))?;
    if archive.len() > 4096 {
        return Err("Excel 内部文件数量超过上限。".into());
    }
    let mut total = 0_u64;
    let mut entries = BTreeMap::new();
    let mut names = BTreeSet::new();
    let mut buffer = [0_u8; 64 * 1024];
    for index in 0..archive.len() {
        check()?;
        let mut entry = archive
            .by_index(index)
            .map_err(|e| format!("Excel 内容不可读：{e}"))?;
        total = total
            .checked_add(entry.size())
            .ok_or("Excel 解压容量无效。")?;
        if total > 128 * 1024 * 1024 {
            return Err("Excel 解压容量超过 128 MiB。".into());
        }
        let name = entry
            .name()
            .map_err(|_| "Excel 文件条目名称编码无效。")?
            .into_owned();
        if entry.enclosed_name().is_none()
            || name.contains('\\')
            || name.split('/').any(|p| p == "..")
            || entry
                .unix_mode()
                .is_some_and(|mode| mode & 0o170000 == 0o120000)
        {
            return Err("Excel 包含不安全的内部路径。".into());
        }
        if !names.insert(name.nfc().collect::<String>().to_ascii_lowercase()) {
            return Err("Excel 内部文件重复。".into());
        }
        if entry.is_dir() {
            continue;
        }
        let size = entry.size();
        let mut bytes = Vec::new();
        let mut actual = 0_u64;
        loop {
            check()?;
            let count = entry
                .read(&mut buffer)
                .map_err(|e| format!("Excel 解压失败：{e}"))?;
            if count == 0 {
                break;
            }
            actual = actual
                .checked_add(count as u64)
                .ok_or("Excel 解压容量无效。")?;
            if actual > size {
                return Err("Excel 容量与目录不一致。".into());
            }
            if retain {
                bytes.extend_from_slice(&buffer[..count]);
            }
        }
        if actual != size {
            return Err("Excel 内部文件重复或容量与目录不一致。".into());
        }
        if retain {
            entries.insert(name, bytes);
        }
    }
    Ok(entries)
}

impl Package {
    pub fn open(bytes: &[u8], check: Check<'_>) -> Result<Self> {
        read(bytes, check, true).map(Self)
    }
    pub fn get(&self, name: &str) -> Result<&[u8]> {
        self.0
            .get(name)
            .map(Vec::as_slice)
            .ok_or_else(|| format!("Excel 缺少必要内容：{name}"))
    }
    pub fn finish(self, check: Check<'_>) -> Result<Vec<u8>> {
        let mut writer = ZipWriter::new(Cursor::new(Vec::new()));
        let options =
            SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
        for (name, bytes) in self.0 {
            check()?;
            writer
                .start_file(name, options)
                .map_err(|e| e.to_string())?;
            writer.write_all(&bytes).map_err(|e| e.to_string())?;
        }
        Ok(writer.finish().map_err(|e| e.to_string())?.into_inner())
    }
}
