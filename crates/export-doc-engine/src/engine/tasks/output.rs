//! Local publication keeps a private recovery receipt through database commit.
use super::{TaskOutput, persistence};
use crate::{
    engine::{
        backup_io,
        error::{Result, invalid, unavailable},
        media,
        store::Store,
    },
    paths,
};
use serde::{Deserialize, Serialize};
use std::{
    collections::BTreeSet,
    fs,
    path::{Path, PathBuf},
    time::Duration,
};

const OWNER: &str = ".exportdoc-publication-owner";
#[derive(Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Part {
    name: String,
    size: u64,
    digest: String,
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Prior {
    size: u64,
    digest: String,
}
#[derive(Clone, Serialize, Deserialize)]
enum Phase {
    Preparing,
    Ready,
    Committed,
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Receipt {
    schema: u8,
    job: String,
    target: PathBuf,
    stage: Option<PathBuf>,
    parts: Vec<Part>,
    previous: Option<Prior>,
    phase: Phase,
}
pub(super) struct Publication {
    root: PathBuf,
    receipt: Receipt,
}

fn journal_root(store: &Store) -> PathBuf {
    store.cache_root.join("TaskPublications")
}
fn job_id(value: &str) -> bool {
    value.len() == 32 && value.bytes().all(|byte| byte.is_ascii_hexdigit())
}
fn fingerprint(path: &Path) -> Result<(u64, String)> {
    let (size, digest) = backup_io::digest_file(path)?;
    Ok((size, digest.to_ascii_lowercase()))
}
fn matches(path: &Path, size: u64, digest: &str) -> Result<bool> {
    paths::ensure_safe_absolute(path).map_err(unavailable)?;
    match fs::metadata(path) {
        Ok(info) if info.is_file() && info.len() == size => {
            Ok(fingerprint(path)? == (size, digest.to_ascii_lowercase()))
        }
        Ok(_) => Ok(false),
        Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(cause) => Err(cause.into()),
    }
}
fn write_receipt(publication: &Publication) -> Result<()> {
    paths::atomic_write(
        &publication.root.join("manifest.json"),
        &serde_json::to_vec(&publication.receipt)?,
    )
    .map_err(unavailable)
}
fn copy_file(source: &Path, target: &Path, maximum: u64) -> Result<()> {
    crate::operation::OperationScope::new(Duration::from_secs(30)).run_result(|| {
        let mut input = fs::File::open(source)?;
        paths::atomic_write_with(target, |output| {
            backup_io::copy_and_hash(&mut input, output, maximum)
                .map(|_| ())
                .map_err(|cause| cause.to_string())
        })
        .map_err(unavailable)
    })
}
fn owned(directory: &Path, job: &str) -> Result<bool> {
    paths::ensure_safe_absolute(directory).map_err(unavailable)?;
    let owner = directory.join(OWNER);
    paths::ensure_safe_absolute(&owner).map_err(unavailable)?;
    match fs::metadata(&owner) {
        Ok(info) if info.is_file() && info.len() == job.len() as u64 => {
            Ok(fs::read(&owner)? == job.as_bytes())
        }
        Ok(_) => Ok(false),
        Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(cause) => Err(cause.into()),
    }
}
fn native_temporary(name: &str) -> bool {
    name.strip_prefix(".native-write-")
        .and_then(|name| name.strip_suffix(".tmp"))
        .is_some_and(job_id)
}

pub(super) fn publish(store: &Store, id: &str, output: &TaskOutput) -> Result<Option<Publication>> {
    let (target, stage, parts, previous) =
        if let (Some(target), Some(file)) = (&output.destination, &output.file) {
            if output.directory.is_some() {
                return Err(invalid("文件任务不能同时写入单文件与批次目录。"));
            }
            paths::ensure_safe_absolute(target).map_err(invalid)?;
            let name = target
                .file_name()
                .and_then(|name| name.to_str())
                .filter(|name| paths::valid_file_name(name))
                .ok_or_else(|| invalid("输出文件名无效。"))?;
            let previous = match fs::metadata(target) {
                Ok(info) if info.is_file() => {
                    if info.len() > 64 * 1024 * 1024 {
                        return Err(invalid(
                            "输出位置已有文件超过 64 MiB，请选择新的输出文件名。",
                        ));
                    }
                    let (size, digest) = fingerprint(target)?;
                    Some(Prior { size, digest })
                }
                Ok(_) => return Err(invalid("输出位置不是普通文件。")),
                Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => None,
                Err(cause) => return Err(cause.into()),
            };
            (
                target.clone(),
                None,
                vec![Part {
                    name: name.into(),
                    size: file.content.len() as u64,
                    digest: media::digest(&file.content),
                }],
                previous,
            )
        } else if let Some(group) = &output.directory {
            paths::ensure_safe_absolute(&group.path).map_err(invalid)?;
            if group.path.try_exists()? {
                return Err(invalid("本次导出目录已存在，请重新导出。"));
            }
            if group.files.is_empty() || group.files.len() > 21 {
                return Err(invalid("批次文件数量无效。"));
            }
            let mut names = BTreeSet::new();
            let mut total = 0usize;
            let mut parts = Vec::with_capacity(group.files.len());
            for file in &group.files {
                if !paths::valid_file_name(&file.file_name)
                    || file.file_name == OWNER
                    || !names.insert(file.file_name.to_lowercase())
                    || file.content.is_empty()
                {
                    return Err(invalid("批次文件名重复或内容无效。"));
                }
                total = total
                    .checked_add(file.content.len())
                    .filter(|total| *total <= 64 * 1024 * 1024)
                    .ok_or_else(|| invalid("批次输出超过 64 MiB。"))?;
                parts.push(Part {
                    name: file.file_name.clone(),
                    size: file.content.len() as u64,
                    digest: media::digest(&file.content),
                });
            }
            let parent = group
                .path
                .parent()
                .ok_or_else(|| invalid("输出目录缺少父目录。"))?;
            (
                group.path.clone(),
                Some(parent.join(format!(".exportdoc-batch-{id}"))),
                parts,
                None,
            )
        } else {
            return Ok(None);
        };
    if !job_id(id) || !target.parent().is_some_and(Path::is_dir) {
        return Err(invalid("输出事务标识或父目录无效。"));
    }
    let root = journal_root(store).join(id);
    paths::ensure_safe_absolute(&root).map_err(unavailable)?;
    if root.try_exists()? {
        return Err(unavailable("发现未完成的输出事务，请重启核对恢复状态。"));
    }
    crate::secrets::private_directory(&root).map_err(unavailable)?;
    let mut publication = Publication {
        root,
        receipt: Receipt {
            schema: 1,
            job: id.into(),
            target,
            stage,
            parts,
            previous,
            phase: Phase::Preparing,
        },
    };
    let result = (|| {
        write_receipt(&publication)?;
        if let Some(prior) = &publication.receipt.previous {
            copy_file(
                &publication.receipt.target,
                &publication.root.join("previous.bin"),
                prior.size,
            )?;
            if !matches(
                &publication.root.join("previous.bin"),
                prior.size,
                &prior.digest,
            )? || !matches(&publication.receipt.target, prior.size, &prior.digest)?
            {
                return Err(unavailable("输出目标在快照期间发生变化，未覆盖当前文件。"));
            }
        }
        if let Some(stage) = &publication.receipt.stage {
            paths::ensure_safe_absolute(stage).map_err(unavailable)?;
            fs::create_dir(stage)?;
            crate::secrets::private_directory(stage).map_err(unavailable)?;
            paths::atomic_write(&stage.join(OWNER), id.as_bytes()).map_err(unavailable)?;
            for file in &output.directory.as_ref().expect("directory output").files {
                paths::atomic_write(&stage.join(&file.file_name), &file.content)
                    .map_err(unavailable)?;
            }
        }
        publication.receipt.phase = Phase::Ready;
        write_receipt(&publication)?;
        if let Some(stage) = &publication.receipt.stage {
            if publication.receipt.target.try_exists()? {
                return Err(invalid("输出目录已出现，当前内容没有被覆盖。"));
            }
            fs::rename(stage, &publication.receipt.target)?;
        } else {
            paths::atomic_write(
                &publication.receipt.target,
                &output.file.as_ref().expect("file output").content,
            )
            .map_err(unavailable)?;
        }
        Ok(())
    })();
    if let Err(original) = result {
        return match publication.rollback() {
            Ok(()) => Err(original),
            Err(rollback) => Err(unavailable(format!(
                "输出准备失败且恢复未完成：{rollback}；原始错误：{original}"
            ))),
        };
    }
    Ok(Some(publication))
}

impl Publication {
    pub(super) fn commit(&mut self) -> Result<()> {
        crate::operation::OperationScope::new(Duration::from_secs(30))
            .run_result(|| self.commit_inner())
    }
    fn commit_inner(&mut self) -> Result<()> {
        self.receipt.phase = Phase::Committed;
        write_receipt(self)?;
        if self.receipt.stage.is_some() && owned(&self.receipt.target, &self.receipt.job)? {
            fs::remove_file(self.receipt.target.join(OWNER))?;
        }
        self.cleanup()
    }
    pub(super) fn rollback(&mut self) -> Result<()> {
        // Compensation must still run after the caller's request was canceled.
        crate::operation::OperationScope::new(Duration::from_secs(30))
            .run_result(|| self.rollback_inner())
    }
    fn rollback_inner(&mut self) -> Result<()> {
        if !matches!(self.receipt.phase, Phase::Preparing) {
            if self.receipt.stage.is_some() {
                if self.receipt.target.try_exists()? {
                    self.remove_directory(&self.receipt.target, true)?;
                }
            } else {
                let part = &self.receipt.parts[0];
                let already_restored = self
                    .receipt
                    .previous
                    .as_ref()
                    .map(|prior| matches(&self.receipt.target, prior.size, &prior.digest))
                    .transpose()?
                    .unwrap_or(false);
                if already_restored {
                    // Cleanup may have removed the snapshot before a restart.
                } else if matches(&self.receipt.target, part.size, &part.digest)? {
                    if let Some(prior) = &self.receipt.previous {
                        let backup = self.root.join("previous.bin");
                        if !matches(&backup, prior.size, &prior.digest)? {
                            return Err(unavailable("输出恢复快照损坏，已保留现场。"));
                        }
                        copy_file(&backup, &self.receipt.target, prior.size)?;
                    } else {
                        fs::remove_file(&self.receipt.target)?;
                    }
                } else if let Some(prior) = &self.receipt.previous {
                    if !matches(&self.receipt.target, prior.size, &prior.digest)? {
                        return Err(unavailable(
                            "输出文件已被其他操作更改，恢复快照保留，未覆盖当前内容。",
                        ));
                    }
                } else if self.receipt.target.try_exists()? {
                    return Err(unavailable("输出位置出现未知内容，已保留现场。"));
                }
            }
        }
        if let Some(stage) = &self.receipt.stage {
            if stage.try_exists()? {
                self.remove_directory(stage, false)?;
            }
        }
        self.cleanup()
    }
    fn remove_directory(&self, directory: &Path, published: bool) -> Result<()> {
        if !published && fs::read_dir(directory)?.next().is_none() {
            // The process may have exited after creating the private stage,
            // before its ownership marker was written.
            fs::remove_dir(directory)?;
            return Ok(());
        }
        if !owned(directory, &self.receipt.job)? {
            return Err(unavailable("输出目录缺少本次事务身份，已保留现场。"));
        }
        let entries = fs::read_dir(directory)?.collect::<std::result::Result<Vec<_>, _>>()?;
        for entry in &entries {
            let path = entry.path();
            paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            let name = entry
                .file_name()
                .into_string()
                .map_err(|_| unavailable("输出目录名称编码无效。"))?;
            if !entry.file_type()?.is_file() {
                return Err(unavailable("输出目录含未知目录，已保留现场。"));
            }
            if name == OWNER {
                continue;
            }
            if !published && native_temporary(&name) {
                continue;
            }
            let part = self
                .receipt
                .parts
                .iter()
                .find(|part| part.name == name)
                .ok_or_else(|| unavailable("输出目录含未知文件，已保留现场。"))?;
            if !matches(&path, part.size, &part.digest)? {
                return Err(unavailable("输出文件已变更，已保留现场。"));
            }
        }
        for entry in entries {
            if entry.file_name() != OWNER {
                fs::remove_file(entry.path())?;
            }
        }
        fs::remove_file(directory.join(OWNER))?;
        fs::remove_dir(directory)?;
        Ok(())
    }
    fn cleanup(&self) -> Result<()> {
        paths::ensure_safe_absolute(&self.root).map_err(unavailable)?;
        let entries = fs::read_dir(&self.root)?.collect::<std::result::Result<Vec<_>, _>>()?;
        if !matches!(self.receipt.phase, Phase::Preparing) {
            if let Some(prior) = &self.receipt.previous {
                let backup = self.root.join("previous.bin");
                if backup.try_exists()? && !matches(&backup, prior.size, &prior.digest)? {
                    return Err(unavailable("恢复快照已变更，已保留现场。"));
                }
            }
        }
        for entry in &entries {
            paths::ensure_safe_absolute(&entry.path()).map_err(unavailable)?;
            let name = entry
                .file_name()
                .into_string()
                .map_err(|_| unavailable("输出快照名称无效。"))?;
            if !entry.file_type()?.is_file()
                || !matches!(name.as_str(), "manifest.json" | "previous.bin")
                    && !native_temporary(&name)
            {
                return Err(unavailable("输出恢复目录含未知内容，已保留现场。"));
            }
        }
        for entry in entries {
            if entry.file_name() != "manifest.json" {
                fs::remove_file(entry.path())?;
            }
        }
        let manifest = self.root.join("manifest.json");
        if manifest.try_exists()? {
            fs::remove_file(manifest)?;
        }
        fs::remove_dir(&self.root)?;
        Ok(())
    }
}

pub(super) fn recover(store: &Store) -> Result<()> {
    let root = journal_root(store);
    paths::ensure_safe_absolute(&root).map_err(unavailable)?;
    let entries = match fs::read_dir(&root) {
        Ok(entries) => entries,
        Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(cause) => return Err(cause.into()),
    };
    for entry in entries {
        let entry = entry?;
        let directory = entry.path();
        paths::ensure_safe_absolute(&directory).map_err(unavailable)?;
        let id = entry
            .file_name()
            .into_string()
            .map_err(|_| unavailable("输出恢复标识无效。"))?;
        if !entry.file_type()?.is_dir() || !job_id(&id) {
            return Err(unavailable("输出恢复目录含未知内容。"));
        }
        let path = directory.join("manifest.json");
        paths::ensure_safe_absolute(&path).map_err(unavailable)?;
        if !path.try_exists()? && fs::read_dir(&directory)?.next().is_none() {
            // The manifest is removed last; an empty receipt directory is safe
            // after either initial creation or completed cleanup.
            fs::remove_dir(directory)?;
            continue;
        }
        if fs::metadata(&path)?.len() > 64 * 1024 {
            return Err(unavailable("输出恢复清单超限。"));
        }
        let receipt: Receipt = serde_json::from_slice(&fs::read(path)?)?;
        let hex =
            |value: &str| value.len() == 64 && value.bytes().all(|byte| byte.is_ascii_hexdigit());
        let mut names = BTreeSet::new();
        if receipt.schema != 1
            || receipt.job != id
            || receipt.parts.is_empty()
            || receipt.parts.len() > 21
            || receipt.parts.iter().any(|part| {
                !paths::valid_file_name(&part.name)
                    || part.name == OWNER
                    || !names.insert(part.name.to_lowercase())
                    || part.size == 0
                    || part.size > 64 * 1024 * 1024
                    || !hex(&part.digest)
            })
            || receipt
                .previous
                .as_ref()
                .is_some_and(|prior| prior.size > 64 * 1024 * 1024 || !hex(&prior.digest))
        {
            return Err(unavailable("输出恢复清单无效，已保留现场。"));
        }
        paths::ensure_safe_absolute(&receipt.target).map_err(unavailable)?;
        if let Some(stage) = &receipt.stage {
            let expected = receipt
                .target
                .parent()
                .ok_or_else(|| unavailable("输出恢复父目录无效。"))?
                .join(format!(".exportdoc-batch-{id}"));
            if stage != &expected || receipt.previous.is_some() {
                return Err(unavailable("输出恢复暂存路径不匹配。"));
            }
        } else if receipt.parts.len() != 1
            || receipt.target.file_name().and_then(|name| name.to_str())
                != Some(receipt.parts[0].name.as_str())
        {
            return Err(unavailable("输出恢复文件不匹配。"));
        }
        let job = store.connection()?.find_identity(persistence::KIND, &id)?;
        let committed = matches!(receipt.phase, Phase::Committed)
            || job.as_ref().is_some_and(|job| job["status"] == "Succeeded");
        let mut publication = Publication {
            root: directory,
            receipt,
        };
        if committed {
            publication.commit()?;
        } else if job.is_some() {
            publication.rollback()?;
        } else {
            return Err(unavailable(
                "输出提交记录无法确认，恢复快照保留，请管理员核对。",
            ));
        }
    }
    Ok(())
}
