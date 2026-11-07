//! Durable, per-file migration identities. Never delete a source by directory name alone.
use std::{
    collections::HashSet,
    error::Error,
    fs,
    io::Read,
    path::{Component, Path},
};

use export_doc_engine::maintenance::{
    DATA_ROOT_MIGRATION_PENDING_FILE, DATA_ROOT_MIGRATION_REDIRECT_FILE,
};
use sha2::{Digest, Sha256};

use crate::migration_progress::MigrationProgress;
use crate::runtime_data_root_storage::reject_link_like_path;

pub(crate) const COPY_TEMP_FILE: &str = ".exportdoc-data-root-migration-copy.tmp";
pub(crate) const COMPLETION_FILE: &str = ".exportdoc-data-root-migration-complete";
pub(crate) const INSTANCE_LOCK: &str = "Locks/native-instance.lock";
const LEGACY_INSTANCE_LOCK: &str = "native-instance.lock";

#[derive(Clone, Debug, PartialEq, Eq, serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TreeEntry {
    pub(crate) path: String,
    pub(crate) bytes: u64,
    pub(crate) sha256: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Eq, serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TreeManifest {
    pub(crate) entries: Vec<TreeEntry>,
}

pub(crate) fn managed_marker(relative: &str) -> bool {
    [
        INSTANCE_LOCK,
        LEGACY_INSTANCE_LOCK,
        DATA_ROOT_MIGRATION_PENDING_FILE,
        DATA_ROOT_MIGRATION_REDIRECT_FILE,
        COPY_TEMP_FILE,
        COMPLETION_FILE,
    ]
    .iter()
    .any(|name| {
        if cfg!(windows) {
            relative.eq_ignore_ascii_case(name)
        } else {
            relative == *name
        }
    })
}

pub(crate) fn collect_tree_manifest(
    root: &Path,
    progress: &MigrationProgress,
) -> Result<TreeManifest, Box<dyn Error>> {
    reject_link_like_path(root)?;
    let mut entries = Vec::new();
    collect_directory(root, root, &mut entries, progress)?;
    Ok(TreeManifest { entries })
}

fn collect_directory(
    root: &Path,
    directory: &Path,
    entries: &mut Vec<TreeEntry>,
    progress: &MigrationProgress,
) -> Result<(), Box<dyn Error>> {
    let mut children = fs::read_dir(directory)?.collect::<Result<Vec<_>, _>>()?;
    children.sort_by_key(|entry| entry.file_name());
    for child in children {
        progress.check()?;
        let path = child.path();
        let relative = path
            .strip_prefix(root)?
            .components()
            .map(|component| {
                component
                    .as_os_str()
                    .to_str()
                    .ok_or("迁移路径必须是有效 Unicode。")
            })
            .collect::<Result<Vec<_>, _>>()?
            .join("/");
        reject_link_like_path(&path)?;
        if managed_marker(&relative) {
            continue;
        }
        let metadata = fs::symlink_metadata(&path)?;
        let digest = if metadata.is_dir() {
            None
        } else if metadata.is_file() {
            Some(hash_file(&path, progress)?)
        } else {
            return Err(format!("迁移不支持的文件对象：{}", path.display()).into());
        };
        entries.push(TreeEntry {
            path: relative,
            bytes: if digest.is_some() { metadata.len() } else { 0 },
            sha256: digest,
        });
        if metadata.is_dir() {
            collect_directory(root, &path, entries, progress)?;
        }
    }
    Ok(())
}

impl TreeManifest {
    pub(crate) fn validate(&self, root: &Path) -> Result<(), Box<dyn Error>> {
        let mut identities = HashSet::new();
        for entry in &self.entries {
            let path = Path::new(&entry.path);
            if entry.path.is_empty()
                || entry.path.contains('\\')
                || managed_marker(&entry.path)
                || !path
                    .components()
                    .all(|part| matches!(part, Component::Normal(_)))
                || entry.sha256.as_ref().is_some_and(|digest| {
                    digest.len() != 64 || !digest.bytes().all(|value| value.is_ascii_hexdigit())
                })
                || (entry.sha256.is_none() && entry.bytes != 0)
            {
                return Err("数据目录迁移清单包含无效路径或摘要。".into());
            }
            let identity = crate::runtime_path_identity::identity_components(&root.join(path));
            if !identities.insert(identity) {
                return Err("数据目录迁移清单包含重复路径。".into());
            }
            reject_link_like_path(&root.join(path))?;
        }
        Ok(())
    }

    pub(crate) fn verify(
        &self,
        root: &Path,
        progress: &MigrationProgress,
    ) -> Result<(), Box<dyn Error>> {
        self.validate(root)?;
        let found = collect_tree_manifest(root, progress)?;
        let identities = |manifest: &Self| {
            manifest
                .entries
                .iter()
                .map(|entry| {
                    (
                        crate::runtime_path_identity::identity_components(&root.join(&entry.path)),
                        (entry.bytes, entry.sha256.clone()),
                    )
                })
                .collect::<std::collections::BTreeMap<_, _>>()
        };
        if identities(&found) != identities(self) {
            return Err(format!("数据目录迁移内容与校验清单不一致：{}", root.display()).into());
        }
        Ok(())
    }

    pub(crate) fn remove_verified_source(
        &self,
        root: &Path,
        progress: &MigrationProgress,
    ) -> Result<(), Box<dyn Error>> {
        self.validate(root)?;
        for entry in self.entries.iter().filter(|entry| entry.sha256.is_some()) {
            progress.check()?;
            let path = root.join(&entry.path);
            if path.try_exists()? {
                entry.verify_file(&path, progress)?;
                fs::remove_file(&path)?;
            }
        }
        // Keep the instance-lock directory and redirect fence at their original paths.
        // Windows never needs to remove or rename a directory containing our open lock.
        for entry in self
            .entries
            .iter()
            .rev()
            .filter(|entry| entry.sha256.is_none())
        {
            if entry.path == "Locks" || (cfg!(windows) && entry.path.eq_ignore_ascii_case("Locks"))
            {
                continue;
            }
            let path = root.join(&entry.path);
            if path.try_exists()? {
                match fs::remove_dir(&path) {
                    Ok(()) => {}
                    // A rejected attempt to open the sealed source can still write
                    // a bootstrap diagnostic. Preserve all unplanned files in the
                    // fenced old root instead of blocking the published target.
                    Err(cause) if cause.kind() == std::io::ErrorKind::DirectoryNotEmpty => {}
                    Err(cause) => return Err(cause.into()),
                }
            }
        }
        Ok(())
    }
}

impl TreeEntry {
    pub(crate) fn verify_file(
        &self,
        path: &Path,
        progress: &MigrationProgress,
    ) -> Result<(), Box<dyn Error>> {
        self.verify_size(path)?;
        self.verify_digest(path, &hash_file(path, progress)?)
    }
    fn verify_digest(&self, path: &Path, digest: &str) -> Result<(), Box<dyn Error>> {
        if self.sha256.as_deref() != Some(digest) {
            return Err(format!("迁移文件已变化，已保留原内容：{}", path.display()).into());
        }
        Ok(())
    }
    fn verify_size(&self, path: &Path) -> Result<(), Box<dyn Error>> {
        reject_link_like_path(path)?;
        let metadata = fs::symlink_metadata(path)?;
        if !metadata.is_file() || metadata.len() != self.bytes {
            return Err(format!("迁移文件已变化，已保留原内容：{}", path.display()).into());
        }
        Ok(())
    }
}

fn hash_file(path: &Path, progress: &MigrationProgress) -> Result<String, Box<dyn Error>> {
    let mut file = fs::File::open(path)?;
    hash_to(&mut file, &mut std::io::sink(), progress, false)
}
fn hash_to(
    input: &mut impl Read,
    output: &mut impl std::io::Write,
    progress: &MigrationProgress,
    report_bytes: bool,
) -> Result<String, Box<dyn Error>> {
    let mut hasher = Sha256::new();
    let mut buffer = vec![0_u8; 1024 * 1024];
    loop {
        progress.check()?;
        let read = input.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
        output.write_all(&buffer[..read])?;
        if report_bytes {
            progress.advance(read as u64)?;
        }
    }
    Ok(hasher
        .finalize()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect())
}

pub(crate) fn copy_verified_tree(
    manifest: &TreeManifest,
    source: &Path,
    target: &Path,
    progress: &MigrationProgress,
) -> Result<(), Box<dyn Error>> {
    manifest.validate(source)?;
    manifest.validate(target)?;
    let temporary = target.join(COPY_TEMP_FILE);
    reject_link_like_path(&temporary)?;
    if temporary.try_exists()? {
        if !fs::symlink_metadata(&temporary)?.is_file() {
            return Err("数据目录迁移暂存文件类型无效。".into());
        }
        fs::remove_file(&temporary)?;
    }
    let total = manifest
        .entries
        .iter()
        .try_fold(0_u64, |total, entry| total.checked_add(entry.bytes))
        .ok_or("迁移数据总量超出支持范围。")?;
    progress.stage("copy", Some(total));
    for entry in &manifest.entries {
        progress.check()?;
        let destination = target.join(&entry.path);
        reject_link_like_path(&destination)?;
        if entry.sha256.is_none() {
            fs::create_dir_all(&destination)?;
            continue;
        }
        if destination.try_exists()? {
            entry.verify_file(&destination, progress)?;
            progress.advance(entry.bytes)?;
            continue;
        }
        let original = source.join(&entry.path);
        entry.verify_size(&original)?;
        let mut input = fs::File::open(&original)?;
        let mut output = fs::OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temporary)?;
        let digest = hash_to(
            &mut input.by_ref().take(entry.bytes.saturating_add(1)),
            &mut output,
            progress,
            true,
        )?;
        entry.verify_digest(&original, &digest)?;
        output.sync_all()?;
        drop(output);
        entry.verify_file(&temporary, progress)?;
        fs::rename(&temporary, &destination)?;
        crate::runtime_data_root_storage::sync_directory(destination.parent().unwrap_or(target));
    }
    progress.stage("verify", None);
    manifest.verify(target, progress)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn copy_reports_written_bytes_and_checks_cancellation_between_chunks() {
        struct CancelWriter<'a>(&'a MigrationProgress, Vec<u8>);
        impl std::io::Write for CancelWriter<'_> {
            fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
                self.1.extend_from_slice(bytes);
                self.0.cancel();
                Ok(bytes.len())
            }
            fn flush(&mut self) -> std::io::Result<()> {
                Ok(())
            }
        }
        let progress = MigrationProgress::default();
        let data = vec![17_u8; 2 * 1024 * 1024];
        progress.stage("copy", Some(data.len() as u64));
        let mut output = Vec::new();
        hash_to(&mut data.as_slice(), &mut output, &progress, true).unwrap();
        assert_eq!(output, data);
        let snapshot = progress.snapshot();
        assert_eq!(snapshot.completed_bytes, data.len() as u64);
        assert_eq!(snapshot.total_bytes, Some(data.len() as u64));
        let mut cancelled_output = CancelWriter(&progress, Vec::new());
        assert!(hash_to(&mut data.as_slice(), &mut cancelled_output, &progress, true).is_err());
        assert_eq!(cancelled_output.1.len(), 1024 * 1024);
    }

    #[test]
    fn manifest_detects_same_length_changes_and_cleanup_preserves_changed_source() {
        let progress = MigrationProgress::default();
        let root = std::env::current_dir()
            .unwrap()
            .join("target/runtime-tree-manifest-tests")
            .join(format!("verified-cleanup-{}", std::process::id()));
        fs::create_dir_all(root.join("Database")).unwrap();
        let file = root.join("Database/data.db");
        fs::write(&file, b"first").unwrap();
        let first = collect_tree_manifest(&root, &progress).unwrap();
        fs::write(&file, b"other").unwrap();
        assert_ne!(first, collect_tree_manifest(&root, &progress).unwrap());
        let target = root.with_extension("copy");
        assert!(copy_verified_tree(&first, &root, &target, &progress).is_err());
        assert!(!target.join("Database/data.db").exists());
        let current = collect_tree_manifest(&root, &progress).unwrap();
        copy_verified_tree(&current, &root, &target, &progress).unwrap();
        assert_eq!(fs::read(target.join("Database/data.db")).unwrap(), b"other");
        fs::remove_dir_all(target).unwrap();
        assert!(first.remove_verified_source(&root, &progress).is_err());
        assert_eq!(fs::read(&file).unwrap(), b"other");
        fs::remove_dir_all(root).unwrap();
    }
}
