//! Explicit ownership of plaintext scratch data. Recovery runs only under the
//! package lock, validates the complete tree before deleting, and keeps pending restores.
use super::error::{Result, invalid, unavailable};
use crate::paths::{self, RuntimePaths};
use serde_json::{Value, json};
use std::{
    collections::HashSet,
    fs,
    path::{Path, PathBuf},
};

const OWNER: &str = ".exportdoc-scratch.json";

pub(super) fn create(parent: &Path, prefix: &str) -> Result<PathBuf> {
    paths::ensure_safe_absolute(parent).map_err(invalid)?;
    let name = format!("{prefix}{}", paths::nonce().map_err(unavailable)?);
    let directory = parent.join(&name);
    super::package::private_directory(&directory)?;
    super::sealed::atomic_write(
        &directory.join(OWNER),
        &serde_json::to_vec(&json!({
            "purpose":"backup-scratch", "version":1, "directory":name
        }))?,
    )?;
    Ok(directory)
}

pub(super) fn recover(paths: &RuntimePaths) -> Result<()> {
    for root in [
        paths.data_root.join("DisasterRecovery"),
        paths.data_root.join("Backups/ServerMigration"),
        paths.data_root.join("Backups/ScheduledRecovery"),
    ] {
        recover_root(&root)?;
    }
    Ok(())
}

fn read_json(path: &Path) -> Result<Value> {
    use std::io::Read;
    paths::ensure_safe_absolute(path).map_err(invalid)?;
    let mut bytes = Vec::new();
    fs::File::open(path)?
        .take(4 * 1024 * 1024 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > 4 * 1024 * 1024 {
        return Err(unavailable("备份暂存清单过大，已保留现场。"));
    }
    serde_json::from_slice(&bytes).map_err(|_| unavailable("备份暂存清单无效，已保留现场。"))
}

fn recover_root(root: &Path) -> Result<()> {
    paths::ensure_safe_absolute(root).map_err(invalid)?;
    if !root.try_exists()? {
        return Ok(());
    }
    let marker = root.join(".pending-restore.json");
    let pending = if marker.try_exists()? {
        Some(
            read_json(&marker)?["stagingDirectoryName"]
                .as_str()
                .filter(|name| paths::valid_file_name(name))
                .ok_or_else(|| unavailable("恢复标记无效，已保留全部暂存内容。"))?
                .to_owned(),
        )
    } else {
        None
    };
    for entry in fs::read_dir(root)? {
        let entry = entry?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if pending.as_deref() == Some(&name)
            || !(name.starts_with(".work-") || name.starts_with("pending-"))
        {
            continue;
        }
        let directory = entry.path();
        paths::ensure_safe_absolute(&directory).map_err(invalid)?;
        if !entry.file_type()?.is_dir() || !directory.join(OWNER).try_exists()? {
            continue;
        }
        // Installation journals retain the pre-restore database/key/templates for
        // rollback and operator recovery, even after the pending marker is removed.
        if directory.join("install-journal.json").try_exists()? {
            continue;
        }
        let owner = read_json(&directory.join(OWNER))?;
        if owner != json!({"purpose":"backup-scratch", "version":1, "directory":name}) {
            return Err(unavailable("备份暂存归属无效，已保留现场。"));
        }
        let mut known: HashSet<String> = [
            OWNER,
            "snapshot.sqlite3",
            "database.dump",
            "payload.zip",
            ".payload.zip",
            "sealed-package.bin",
            "manifest.json",
            super::package::SQLITE,
            super::package::POSTGRES,
            super::package::KEY,
        ]
        .into_iter()
        .map(str::to_owned)
        .collect();
        if directory.join("manifest.json").try_exists()? {
            let manifest = read_json(&directory.join("manifest.json"))?;
            for item in manifest["files"].as_array().into_iter().flatten() {
                if let Some(name) = item["relativePath"].as_str().filter(|name| {
                    name.starts_with("Templates/") && name.split('/').all(paths::valid_file_name)
                }) {
                    known.insert(name.into());
                }
            }
        }
        validate_tree(&directory, &directory, &known)?;
        fs::remove_dir_all(&directory)?;
    }
    Ok(())
}

fn validate_tree(root: &Path, directory: &Path, known: &HashSet<String>) -> Result<()> {
    for entry in fs::read_dir(directory)? {
        let entry = entry?;
        let path = entry.path();
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        let name = path
            .strip_prefix(root)
            .map_err(|_| invalid("暂存路径越界。"))?
            .components()
            .map(|v| v.as_os_str().to_string_lossy())
            .collect::<Vec<_>>()
            .join("/");
        let kind = entry.file_type()?;
        if kind.is_dir()
            && known
                .iter()
                .any(|file| file.starts_with(&format!("{name}/")))
        {
            validate_tree(root, &path, known)?;
        } else if !kind.is_file() || !known.contains(&name) {
            return Err(unavailable("备份暂存包含未知内容，已保留现场。"));
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn only_owned_orphans_are_removed_and_pending_unknown_data_survive() {
        let workspace = crate::engine::test_support::ScratchDirectory::new();
        let root = workspace.paths().data_root.join("scratch-test");
        fs::create_dir_all(&root).unwrap();
        let orphan = create(&root, ".work-").unwrap();
        fs::write(orphan.join("snapshot.sqlite3"), b"plaintext").unwrap();
        fs::create_dir(orphan.join("Templates")).unwrap();
        fs::write(orphan.join("Templates/invoice.dtpl"), b"template").unwrap();
        fs::write(
            orphan.join("manifest.json"),
            br#"{"files":[{"relativePath":"Templates/invoice.dtpl"}]}"#,
        )
        .unwrap();
        let pending = create(&root, "pending-").unwrap();
        fs::write(
            root.join(".pending-restore.json"),
            serde_json::to_vec(
                &json!({"stagingDirectoryName":pending.file_name().unwrap().to_str().unwrap()}),
            )
            .unwrap(),
        )
        .unwrap();
        let unknown = root.join(".work-unregistered");
        fs::create_dir(&unknown).unwrap();
        recover_root(&root).unwrap();
        assert!(!orphan.exists());
        assert!(pending.exists());
        assert!(unknown.exists());
        let installed = create(&root, "pending-").unwrap();
        fs::write(installed.join("install-journal.json"), b"[]").unwrap();
        fs::create_dir(installed.join("previous")).unwrap();
        fs::write(installed.join("previous/user-data"), b"retained").unwrap();
        recover_root(&root).unwrap();
        assert!(installed.join("previous/user-data").exists());
        let unexpected = create(&root, ".work-").unwrap();
        fs::write(unexpected.join("user.txt"), b"preserved").unwrap();
        assert!(recover_root(&root).is_err());
        assert!(unexpected.join("user.txt").exists());
        fs::remove_dir_all(root).unwrap();
    }
}
