use super::*;
use std::io::Write;

#[derive(Serialize, Deserialize)]
#[serde(tag = "record", rename_all = "camelCase", deny_unknown_fields)]
pub(super) enum Record {
    File {
        role: String,
        name: String,
        size: u64,
        digest: String,
    },
    Chunk {
        key: String,
        size: u64,
        digest: String,
    },
    EndFile,
    EndManifest {
        files: u64,
        bytes: u64,
    },
}
pub(in crate::engine::team_backup) trait Visitor {
    fn begin(&mut self, role: &str, name: &str, size: u64, digest: &str) -> Result<()>;
    fn data(&mut self, bytes: &[u8]) -> Result<()>;
    fn end(&mut self) -> Result<()>;
}
pub(in crate::engine::team_backup) struct PointWriter<'a> {
    repository: &'a Repository,
    commit: Commit,
    buffer: Vec<u8>,
    hash: Sha256,
}
impl<'a> PointWriter<'a> {
    pub(super) fn new(
        repository: &'a Repository,
        id: &str,
        provider: &str,
        source_storage: serde_json::Value,
    ) -> Result<Self> {
        valid_id(id)?;
        if repository
            .io
            .read(&format!("point-{id}.commit"), 64 * 1024)?
            .is_some()
        {
            return Err(invalid("此恢复点已经提交，不能再次写入。"));
        }
        if !matches!(provider, "SQLite" | "PostgreSQL") {
            return Err(invalid("恢复点数据库类型无效。"));
        }
        Ok(Self {
            repository,
            commit: Commit {
                id: id.into(),
                repository: repository.id.clone(),
                created_at: chrono::Utc::now().to_rfc3339(),
                provider: provider.into(),
                database_schema: export_doc_storage::SCHEMA_VERSION,
                pages: 0,
                manifest_digest: String::new(),
                files: 0,
                size_bytes: 0,
                new_bytes: 0,
                source_storage,
            },
            buffer: Vec::with_capacity(PAGE_BYTES),
            hash: Sha256::new(),
        })
    }
    fn record(&mut self, record: &Record) -> Result<()> {
        let mut line = serde_json::to_vec(record)?;
        line.push(b'\n');
        if line.len() > PAGE_BYTES {
            return Err(invalid("恢复点清单条目过大。"));
        }
        if self.buffer.len() + line.len() > PAGE_BYTES {
            self.page()?;
        }
        self.buffer.extend_from_slice(&line);
        Ok(())
    }
    fn page(&mut self) -> Result<()> {
        if self.buffer.is_empty() {
            return Ok(());
        }
        let name = page_name(&self.commit.id, self.commit.pages);
        self.repository.put(&name, &self.buffer)?;
        self.hash.update(&self.buffer);
        self.buffer.clear();
        self.commit.pages += 1;
        Ok(())
    }
    pub fn file(
        &mut self,
        role: &str,
        name: &str,
        size: u64,
        digest: &str,
        producer: impl FnOnce(&mut ChunkWriter<'_, 'a>) -> Result<()>,
    ) -> Result<()> {
        validate_file(role, name, size, digest)?;
        self.record(&Record::File {
            role: role.into(),
            name: name.into(),
            size,
            digest: digest.into(),
        })?;
        let mut writer = ChunkWriter {
            point: self,
            buffer: zeroize::Zeroizing::new(Vec::with_capacity(sealed::object::MAX_PLAIN)),
            hash: Sha256::new(),
            size: 0,
            expected: size,
        };
        producer(&mut writer)?;
        writer.chunk()?;
        if writer.size != size || super::super::hex(&writer.hash.finalize()) != digest {
            return Err(unavailable(format!(
                "恢复点捕获文件 {name} 的长度或摘要已改变，请重新备份。"
            )));
        }
        self.record(&Record::EndFile)?;
        self.commit.files += 1;
        self.commit.size_bytes = self
            .commit
            .size_bytes
            .checked_add(size)
            .ok_or_else(|| invalid("恢复点大小计数溢出。"))?;
        Ok(())
    }
    pub fn finish(mut self) -> Result<Commit> {
        self.record(&Record::EndManifest {
            files: self.commit.files,
            bytes: self.commit.size_bytes,
        })?;
        self.page()?;
        self.commit.manifest_digest = super::super::hex(&self.hash.finalize());
        self.repository.io.check()?;
        self.repository.put(
            &format!("point-{}.commit", self.commit.id),
            &serde_json::to_vec(&self.commit)?,
        )?;
        // Re-read the authenticated commit: a successful write alone is not a recovery point.
        self.repository.commit(&self.commit.id)
    }
}
pub(in crate::engine::team_backup) struct ChunkWriter<'b, 'a> {
    point: &'b mut PointWriter<'a>,
    buffer: zeroize::Zeroizing<Vec<u8>>,
    hash: Sha256,
    size: u64,
    expected: u64,
}
impl ChunkWriter<'_, '_> {
    fn chunk(&mut self) -> Result<()> {
        if self.buffer.is_empty() {
            return Ok(());
        }
        let digest = super::super::hex(&Sha256::digest(self.buffer.as_slice()));
        let key = self.point.repository.blob_name(&digest)?;
        if self.point.repository.put(&key, &self.buffer)? {
            self.point.commit.new_bytes += self.buffer.len() as u64;
        }
        self.point.record(&Record::Chunk {
            key,
            size: self.buffer.len() as u64,
            digest,
        })?;
        self.buffer.clear();
        Ok(())
    }
}
impl Write for ChunkWriter<'_, '_> {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        if self.size.saturating_add(bytes.len() as u64) > self.expected {
            return Err(std::io::Error::other("恢复文件超过预检长度"));
        }
        let size = bytes
            .len()
            .min(sealed::object::MAX_PLAIN - self.buffer.len());
        self.buffer.extend_from_slice(&bytes[..size]);
        self.hash.update(&bytes[..size]);
        self.size += size as u64;
        if self.buffer.len() == sealed::object::MAX_PLAIN {
            self.chunk().map_err(std::io::Error::other)?;
        }
        Ok(size)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}
pub(super) fn page_name(id: &str, page: u64) -> String {
    format!("manifest-{id}-{page:010}")
}
fn validate_file(role: &str, name: &str, size: u64, digest: &str) -> Result<()> {
    if !matches!(
        role,
        "Database" | "MasterKey" | "Template" | "Configuration" | "Attachment"
    ) || size > super::super::super::backup_io::MAX_FILE_BYTES
        || digest.len() != 64
        || !digest.bytes().all(|c| c.is_ascii_hexdigit())
        || name.is_empty()
        || name.len() > 1024
        || name.split('/').any(|part| !paths::valid_file_name(part))
    {
        return Err(invalid("恢复点文件类型、受管名称或摘要无效。"));
    }
    Ok(())
}
pub(super) fn visit(
    repository: &Repository,
    id: &str,
    visitor: &mut impl Visitor,
) -> Result<Commit> {
    let commit = repository.commit(id)?;
    visit_commit(repository, commit, visitor)
}
pub(super) fn visit_commit(
    repository: &Repository,
    commit: Commit,
    visitor: &mut impl Visitor,
) -> Result<Commit> {
    visit_with(repository, commit, visitor, &mut |name| {
        repository.read(name)
    })
}
pub(super) fn visit_with(
    repository: &Repository,
    commit: Commit,
    visitor: &mut impl Visitor,
    read: &mut impl FnMut(&str) -> Result<zeroize::Zeroizing<Vec<u8>>>,
) -> Result<Commit> {
    walk(repository, commit, &mut ReadMode::Content(visitor), read)
}
pub(super) fn references(
    repository: &Repository,
    commit: Commit,
    visitor: &mut dyn FnMut(&str, u64, &str) -> Result<()>,
) -> Result<Commit> {
    walk(
        repository,
        commit,
        &mut ReadMode::References(visitor),
        &mut |name| repository.read(name),
    )
}
enum ReadMode<'a> {
    Content(&'a mut dyn Visitor),
    References(&'a mut dyn FnMut(&str, u64, &str) -> Result<()>),
}
// Both modes authenticate the entire manifest, names, lengths and record order.
// References permits GC to verify each shared block once across all live points.
fn walk(
    repository: &Repository,
    commit: Commit,
    mode: &mut ReadMode<'_>,
    read: &mut impl FnMut(&str) -> Result<zeroize::Zeroizing<Vec<u8>>>,
) -> Result<Commit> {
    let id = &commit.id;
    let mut manifest_hash = Sha256::new();
    let mut active: Option<(u64, String, u64, Sha256)> = None;
    let mut files = 0;
    let mut total = 0u64;
    let mut ended = false;
    for page in 0..commit.pages {
        let bytes = read(&page_name(id, page))?;
        if bytes.len() > PAGE_BYTES || !bytes.ends_with(b"\n") {
            return Err(invalid("恢复点清单页长度或结束标记无效。"));
        }
        manifest_hash.update(bytes.as_slice());
        for line in bytes.split(|c| *c == b'\n').filter(|s| !s.is_empty()) {
            crate::operation::check()?;
            if ended {
                return Err(invalid("恢复点清单结束后仍有数据。"));
            }
            match serde_json::from_slice::<Record>(line)
                .map_err(|_| invalid("恢复点清单条目损坏。"))?
            {
                Record::File {
                    role,
                    name,
                    size,
                    digest,
                } => {
                    if active.is_some() {
                        return Err(invalid("恢复点文件缺少结束记录。"));
                    }
                    validate_file(&role, &name, size, &digest)?;
                    if let ReadMode::Content(visitor) = mode {
                        visitor.begin(&role, &name, size, &digest)?;
                    }
                    active = Some((size, digest, 0, Sha256::new()));
                }
                Record::Chunk { key, size, digest } => {
                    let state = active
                        .as_mut()
                        .ok_or_else(|| invalid("恢复点数据块缺少所属文件。"))?;
                    if repository.blob_name(&digest)? != key
                        || size == 0
                        || size > sealed::object::MAX_PLAIN as u64
                    {
                        return Err(invalid("恢复点数据块身份或长度无效。"));
                    }
                    state.2 = state
                        .2
                        .checked_add(size)
                        .ok_or_else(|| invalid("恢复点文件长度溢出。"))?;
                    if state.2 > state.0 {
                        return Err(invalid("恢复点文件超过声明长度。"));
                    }
                    match mode {
                        ReadMode::Content(visitor) => {
                            let data = read(&key)?;
                            verify_chunk(&data, size, &digest)?;
                            state.3.update(data.as_slice());
                            visitor.data(&data)?;
                        }
                        ReadMode::References(visitor) => visitor(&key, size, &digest)?,
                    }
                }
                Record::EndFile => {
                    let (expected, digest, size, hash) = active
                        .take()
                        .ok_or_else(|| invalid("恢复点文件结束记录无效。"))?;
                    if size != expected
                        || (matches!(mode, ReadMode::Content(_))
                            && super::super::hex(&hash.finalize()) != digest)
                    {
                        return Err(invalid("恢复点文件不完整或摘要不符。"));
                    }
                    if let ReadMode::Content(visitor) = mode {
                        visitor.end()?;
                    }
                    files += 1;
                    total = total
                        .checked_add(size)
                        .ok_or_else(|| invalid("恢复点统计溢出。"))?;
                }
                Record::EndManifest {
                    files: count,
                    bytes,
                } => {
                    if active.is_some() || count != files || bytes != total {
                        return Err(invalid("恢复点清单统计或文件结束记录不完整。"));
                    }
                    ended = true;
                }
            }
        }
    }
    if !ended
        || files != commit.files
        || total != commit.size_bytes
        || super::super::hex(&manifest_hash.finalize()) != commit.manifest_digest
    {
        return Err(invalid("恢复点清单未完整认证，不能启用业务。"));
    }
    repository.io.check()?;
    Ok(commit)
}
pub(super) fn verify_chunk(data: &[u8], size: u64, digest: &str) -> Result<()> {
    if data.len() as u64 != size || super::super::hex(&Sha256::digest(data)) != digest {
        return Err(invalid("恢复点数据块摘要不符。"));
    }
    Ok(())
}
