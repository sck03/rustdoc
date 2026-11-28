//! Read a portable point in place. Only a bounded object index and a single
//! encrypted frame occupy memory; attachments are never unpacked onto A first.
use super::*;
use std::{
    collections::BTreeMap,
    fs,
    io::{Read, Seek, SeekFrom},
    sync::Mutex,
};
struct PortableFile {
    file: Mutex<fs::File>,
    entries: BTreeMap<String, (u64, usize, [u8; 32])>,
    length: u64,
    modified: std::time::SystemTime,
}
impl Repository {
    pub fn from_portable(mut file: fs::File, password: &str) -> Result<(Self, String)> {
        crate::operation::check()?;
        fs2::FileExt::try_lock_shared(&file)
            .map_err(|_| invalid("便携文件正在写入，请等待导出完成。"))?;
        let length = file.metadata()?.len();
        let modified = file.metadata()?.modified()?;
        let mut magic = vec![0; portable::MAGIC.len()];
        read_exact(&mut file, &mut magic)?;
        if magic != portable::MAGIC {
            return Err(invalid(
                "仅支持本版本 .edmpoint 格式；旧包请使用原版本工具处理。",
            ));
        }
        let mut entries = BTreeMap::new();
        let mut point = None;
        let mut header = None;
        let mut frames = 0usize;
        let mut bytes = Vec::new();
        loop {
            let mut size = [0; 2];
            read_exact(&mut file, &mut size)?;
            let size = u16::from_le_bytes(size) as usize;
            if size == 0 {
                break;
            }
            if size > 128 || point.is_some() {
                return Err(invalid("便携恢复点名称或结束顺序无效。"));
            }
            let mut name = vec![0; size];
            read_exact(&mut file, &mut name)?;
            let name = String::from_utf8(name).map_err(|_| invalid("便携对象名称无效。"))?;
            io::valid_name(&name)?;
            let mut size = [0; 4];
            read_exact(&mut file, &mut size)?;
            let size = u32::from_le_bytes(size) as usize;
            if size > sealed::object::MAX_PLAIN + sealed::object::OVERHEAD {
                return Err(invalid("便携对象超过解密窗口。"));
            }
            let offset = file.stream_position()?;
            bytes.resize(size, 0);
            read_exact(&mut file, &mut bytes)?;
            let digest: [u8; 32] = Sha256::digest(&bytes).into();
            if entries.is_empty() {
                if name != HEADER || size > 64 * 1024 {
                    return Err(invalid("便携恢复点必须以仓库头开始。"));
                }
                header = Some(serde_json::from_slice::<Header>(&bytes)?);
            } else if name.starts_with("point-") && name.ends_with(".commit") {
                point = Some(
                    name.trim_start_matches("point-")
                        .trim_end_matches(".commit")
                        .to_owned(),
                );
            } else if !(name.starts_with("blob-") || name.starts_with("manifest-")) {
                return Err(invalid("便携文件包含非恢复对象。"));
            }
            if let Some((_, _, existing)) = entries.get(&name) {
                if existing != &digest {
                    return Err(invalid("便携文件的重复对象内容冲突。"));
                }
            } else {
                entries.insert(name, (offset, size, digest));
            }
            frames += 1;
            if entries.len() > 1_000_000 || frames > 10_000_000 {
                return Err(invalid("便携文件索引超过单次恢复预算。"));
            }
        }
        if file.stream_position()? != length {
            return Err(invalid("便携恢复点结束后含多余数据。"));
        }
        let point = point.ok_or_else(|| invalid("便携恢复点缺少最终 COMMIT。"))?;
        let header = header.ok_or_else(|| invalid("便携仓库头缺失。"))?;
        let repo = Self::open(
            Box::new(PortableFile {
                file: Mutex::new(file),
                entries,
                length,
                modified,
            }),
            &header.id,
            password,
            false,
        )?;
        repo.commit(&point)?;
        Ok((repo, point))
    }
}
impl ObjectStore for PortableFile {
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        self.check()?;
        let Some((offset, size, digest)) = self.entries.get(name) else {
            return Ok(None);
        };
        if *size > limit {
            return Err(invalid("便携对象长度超限。"));
        }
        let mut file = self
            .file
            .lock()
            .map_err(|_| unavailable("便携文件读取锁不可用。"))?;
        file.seek(SeekFrom::Start(*offset))?;
        let mut bytes = vec![0; *size];
        read_exact(&mut *file, &mut bytes)?;
        if <[u8; 32]>::from(Sha256::digest(&bytes)) != *digest {
            return Err(invalid("便携文件在恢复过程中发生变化。"));
        }
        Ok(Some(bytes))
    }
    fn create(&self, _: &str, _: &[u8]) -> Result<bool> {
        Err(invalid("便携恢复源只读。"))
    }
    fn delete(&self, _: &str) -> Result<()> {
        Err(invalid("便携恢复源不能被回收。"))
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        self.check()?;
        Ok(self
            .entries
            .keys()
            .filter(|name| name.starts_with(prefix) && name.as_str() > after)
            .take(limit)
            .cloned()
            .collect())
    }
    fn check(&self) -> Result<()> {
        crate::operation::check()?;
        let file = self
            .file
            .lock()
            .map_err(|_| unavailable("便携文件读取锁不可用。"))?;
        let meta = file.metadata()?;
        if meta.len() != self.length || meta.modified()? != self.modified {
            return Err(invalid(
                "便携恢复文件已被修改，请重新选择已完整导出的文件。",
            ));
        }
        Ok(())
    }
}

// Bound cooperative cancellation latency even while indexing a large frame.
fn read_exact(reader: &mut impl Read, bytes: &mut [u8]) -> Result<()> {
    for chunk in bytes.chunks_mut(64 * 1024) {
        crate::operation::check()?;
        reader.read_exact(chunk)?;
    }
    crate::operation::check()
}

#[cfg(test)]
#[path = "tests/portable_read.rs"]
mod tests;
