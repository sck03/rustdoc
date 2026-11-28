//! Self-contained recovery points over immutable encrypted objects. The local
//! database is only an optional index; discovery and verification use the target.
use super::{
    error::{Result, invalid, unavailable},
    sealed,
};
use crate::paths::{self, RuntimePaths};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    io::Write,
    path::{Path, PathBuf},
};
pub(super) mod capture;
mod catalog;
pub(super) mod names;
pub(in crate::engine) use catalog::Catalog;
mod garbage;
pub(super) mod io;
mod points;
mod portable;
mod portable_file;
pub(super) mod remote;
pub(in crate::engine::team_backup) mod restore;
mod retention;
#[cfg(test)]
mod tests;
use io::ObjectStore;
pub(super) use points::{PointWriter, Visitor};
const KEY_MAGIC: &[u8] = b"EDM-REPOSITORY-KEY-1\0";
const HEADER: &str = "repository.json";
const FORMAT: u32 = 2;
const PAGE_BYTES: usize = 64 * 1024;
#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Header {
    format: u32,
    id: String,
    wrapped_key: Vec<u8>,
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(in crate::engine::team_backup) struct Commit {
    pub id: String,
    pub repository: String,
    pub created_at: String,
    pub provider: String,
    pub database_schema: i64,
    pub pages: u64,
    pub manifest_digest: String,
    pub files: u64,
    pub size_bytes: u64,
    pub new_bytes: u64,
    pub source_storage: serde_json::Value,
}
pub(in crate::engine::team_backup) struct Repository {
    io: Box<dyn ObjectStore>,
    pub id: String,
    root: zeroize::Zeroizing<[u8; 32]>,
}
pub(in crate::engine::team_backup) struct PointPage {
    pub items: Vec<Commit>,
    pub next_cursor: String,
}
impl Repository {
    pub fn open(
        io: Box<dyn ObjectStore>,
        expected_id: &str,
        password: &str,
        create: bool,
    ) -> Result<Self> {
        valid_id(expected_id)?;
        let raw = io.read(HEADER, 64 * 1024)?;
        let header: Header = if let Some(raw) = raw {
            let (_, rotated) = current_header(io.as_ref())?;
            serde_json::from_slice(rotated.as_deref().unwrap_or(&raw))
                .map_err(|_| invalid("备份仓库身份文件损坏。"))?
        } else {
            if !create {
                return Err(invalid("目标没有可用的恢复仓库，请核对挂载和仓库位置。"));
            }
            if password.chars().count() < 12 {
                return Err(invalid("新仓库恢复凭证至少需要 12 个字符。"));
            }
            if io.list("", "", 4)?.iter().any(|name| {
                !matches!(
                    name.as_str(),
                    ".repository-lock" | "writer-lock" | "writer-session"
                )
            }) {
                return Err(invalid("目标不是空仓库，禁止覆盖未登记的文件。"));
            }
            let mut root = zeroize::Zeroizing::new([0u8; 32]);
            getrandom::fill(root.as_mut()).map_err(|_| unavailable("无法生成独立仓库根密钥。"))?;
            let mut wrapped = Vec::new();
            let mut writer = sealed::SealingWriter::new(KEY_MAGIC, password, &mut wrapped)?;
            writer.write_all(root.as_ref())?;
            writer.finish()?;
            let header = Header {
                format: FORMAT,
                id: expected_id.into(),
                wrapped_key: wrapped,
            };
            if !io.create(HEADER, &serde_json::to_vec(&header)?)? {
                return Err(invalid("仓库已被另一操作初始化，请重新核验。"));
            }
            header
        };
        if header.format != FORMAT || header.id != expected_id {
            return Err(invalid("备份仓库身份或格式不匹配，禁止接管另一个仓库。"));
        }
        let mut decoded = zeroize::Zeroizing::new(Vec::new());
        sealed::open_to(
            KEY_MAGIC,
            password,
            header.wrapped_key.as_slice(),
            &mut *decoded,
        )?;
        let key: [u8; 32] = decoded
            .as_slice()
            .try_into()
            .map_err(|_| invalid("恢复凭证未能解锁有效的仓库根密钥。"))?;
        let repository = Self {
            io,
            id: header.id,
            root: zeroize::Zeroizing::new(key),
        };
        repository.io.check()?;
        Ok(repository)
    }
    fn binding(&self, name: &str) -> Vec<u8> {
        format!("{}/{name}", self.id).into_bytes()
    }
    fn encrypted(&self, name: &str, plain: &[u8]) -> Result<Vec<u8>> {
        sealed::object::seal(&self.root, &self.binding(name), plain)
    }
    fn read(&self, name: &str) -> Result<zeroize::Zeroizing<Vec<u8>>> {
        self.read_object(name).map(|(_, plain)| plain)
    }
    fn read_object(&self, name: &str) -> Result<(Vec<u8>, zeroize::Zeroizing<Vec<u8>>)> {
        crate::operation::check()?;
        self.io.check()?;
        let bytes = self
            .io
            .read(name, sealed::object::MAX_PLAIN + sealed::object::OVERHEAD)?
            .ok_or_else(|| {
                unavailable(format!(
                    "恢复仓库缺少对象 {name}，请核对仓库完整性或选择其它恢复点。"
                ))
            })?;
        let plain = sealed::object::open(&self.root, &self.binding(name), &bytes)?;
        Ok((bytes, plain))
    }
    fn put(&self, name: &str, plain: &[u8]) -> Result<bool> {
        crate::operation::check()?;
        self.io.check()?;
        // Existing content is authenticated before deduplication. No unverified
        // ETag, last-modified date or local-only index can declare a blob sound.
        if let Some(bytes) = self
            .io
            .read(name, sealed::object::MAX_PLAIN + sealed::object::OVERHEAD)?
        {
            let existing = sealed::object::open(&self.root, &self.binding(name), &bytes)?;
            if existing.as_slice() != plain {
                return Err(invalid("恢复仓库不可变对象内容冲突。"));
            }
            return Ok(false);
        }
        let bytes = self.encrypted(name, plain)?;
        if !self.io.create(name, &bytes)? {
            return Err(invalid("恢复仓库被并发修改，请核对独占写入配置。"));
        }
        let verified = self.read(name)?;
        if verified.as_slice() != plain {
            return Err(unavailable("恢复对象回读校验失败，未发布恢复点。"));
        }
        Ok(true)
    }
    fn blob_name(&self, digest: &str) -> Result<String> {
        use hmac::{Hmac, Mac};
        let mut hmac = <Hmac<Sha256> as hmac::KeyInit>::new_from_slice(self.root.as_ref())
            .map_err(|_| invalid("仓库密钥无效。"))?;
        Mac::update(&mut hmac, b"ExportDocManager/recovery-object/1/");
        Mac::update(&mut hmac, digest.as_bytes());
        Ok(format!(
            "blob-{}",
            super::hex(&hmac.finalize().into_bytes())
        ))
    }
    pub fn commit(&self, id: &str) -> Result<Commit> {
        valid_id(id)?;
        let raw = self.read(&format!("point-{id}.commit"))?;
        self.decode_commit(id, &raw)
    }
    fn decode_commit(&self, id: &str, raw: &[u8]) -> Result<Commit> {
        let commit: Commit =
            serde_json::from_slice(raw).map_err(|_| invalid("恢复点提交记录无效。"))?;
        if commit.id != id
            || commit.repository != self.id
            || commit.database_schema != export_doc_storage::SCHEMA_VERSION
            || commit.pages == 0
            || commit.pages > 1_000_000
            || !matches!(commit.provider.as_str(), "SQLite" | "PostgreSQL")
            || chrono::DateTime::parse_from_rfc3339(&commit.created_at).is_err()
        {
            return Err(invalid("恢复点身份、数据库基线或清单范围无效。"));
        }
        Ok(commit)
    }
    pub fn list(&self, after: &str, limit: usize) -> Result<Vec<Commit>> {
        if limit == 0 || limit > 100 {
            return Err(invalid("恢复点分页大小须为 1–100。"));
        }
        let mut cursor = after.to_owned();
        let mut points = Vec::new();
        while points.len() < limit {
            let names = self.io.list("point-", &cursor, 100)?;
            if names.is_empty() {
                break;
            }
            for name in names {
                cursor = name.clone();
                if let Some(id) = name
                    .strip_prefix("point-")
                    .and_then(|s| s.strip_suffix(".commit"))
                {
                    points.push(self.commit(id)?);
                    if points.len() == limit {
                        break;
                    }
                }
            }
        }
        Ok(points)
    }
    pub fn page(&self, after: &str, limit: usize) -> Result<PointPage> {
        let items = self.list(after, limit)?;
        let mut next_cursor = String::new();
        if items.len() == limit
            && let Some(last) = items.last()
        {
            let after = format!("point-{}.commit", last.id);
            if !self.list(&after, 1)?.is_empty() {
                next_cursor = after;
            }
        }
        Ok(PointPage { items, next_cursor })
    }
    pub fn writer(
        &self,
        id: &str,
        provider: &str,
        source_storage: serde_json::Value,
    ) -> Result<PointWriter<'_>> {
        PointWriter::new(self, id, provider, source_storage)
    }
    pub fn verify(&self, id: &str, visitor: &mut impl Visitor) -> Result<Commit> {
        points::visit(self, id, visitor)
    }
    pub fn export(&self, id: &str, output: &mut impl std::io::Write) -> Result<()> {
        portable::export(self, id, output)
    }
    pub fn retain(&self, count: usize, days: u32) -> Result<u64> {
        retention::retain(self, count, days)
    }
}
fn current_header(io: &dyn ObjectStore) -> Result<(u64, Option<Vec<u8>>)> {
    let mut cursor = String::new();
    let mut generation = 0;
    let mut count = 0;
    loop {
        let rows = io.list("credential-", &cursor, 100)?;
        if rows.is_empty() {
            break;
        }
        for row in rows {
            count += 1;
            if count > 1000 {
                return Err(invalid("恢复凭证世代超过核验预算。"));
            }
            generation = row
                .strip_prefix("credential-")
                .and_then(|v| v.parse().ok())
                .ok_or_else(|| invalid("恢复凭证世代编号无效。"))?;
            cursor = row;
        }
    }
    Ok((
        generation,
        if cursor.is_empty() {
            None
        } else {
            Some(
                io.read(&cursor, 64 * 1024)?
                    .ok_or_else(|| invalid("恢复凭证世代在读取时消失。"))?,
            )
        },
    ))
}
pub(super) fn valid_id(id: &str) -> Result<()> {
    if id.len() != 32 || !id.bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(invalid("恢复点或仓库编号无效。"));
    }
    Ok(())
}
pub(super) fn directory(
    paths: &RuntimePaths,
    location: &str,
    repository: &str,
    expected_medium: Option<&str>,
    create: bool,
) -> Result<io::Directory> {
    valid_id(repository)?;
    let base = if location.is_empty() {
        paths.data_root.join("Backups/RecoveryRepositories")
    } else {
        paths
            .file_storage_roots
            .get(location)
            .cloned()
            .ok_or_else(|| invalid("备份位置尚未由部署管理员登记。"))?
    };
    paths::ensure_safe_absolute(&base).map_err(invalid)?;
    if !location.is_empty() && !base.is_dir() {
        return Err(unavailable("登记的备份位置未挂载，不能创建本地替代目录。"));
    }
    let root = base.join(format!("recovery-{repository}"));
    if let Some(expected) = expected_medium {
        if paths::volume_identity(&base).map_err(unavailable)? != expected {
            return Err(unavailable(
                "备份目标介质身份已改变，未创建任何替代仓库目录。",
            ));
        }
    }
    if create {
        super::package::private_directory(&root)?;
    }
    io::Directory::open(root, expected_medium)
}
