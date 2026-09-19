use crate::{Check, MAX_INPUT, Result};
use std::{
    collections::BTreeMap,
    io::{Cursor, Read, Write},
};
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};

pub struct Package(pub BTreeMap<String, Vec<u8>>);
impl Package {
    pub fn open(bytes: &[u8], check: Check<'_>) -> Result<Self> {
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
            let name = entry.name().to_owned();
            if entry.enclosed_name().is_none()
                || name.contains('\\')
                || name.split('/').any(|p| p == "..")
                || entry
                    .unix_mode()
                    .is_some_and(|mode| mode & 0o170000 == 0o120000)
            {
                return Err("Excel 包含不安全的内部路径。".into());
            }
            if entry.is_dir() {
                continue;
            }
            let size = entry.size();
            let mut bytes = Vec::with_capacity(size as usize);
            entry
                .by_ref()
                .take(size + 1)
                .read_to_end(&mut bytes)
                .map_err(|e| format!("Excel 解压失败：{e}"))?;
            if bytes.len() as u64 != entry.size() || entries.insert(name, bytes).is_some() {
                return Err("Excel 内部文件重复或容量与目录不一致。".into());
            }
        }
        Ok(Self(entries))
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
