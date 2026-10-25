//! Persistent data-root maintenance fences shared by hosts and ordinary database opens.
use crate::{Error, Result};
use std::{fs, io::ErrorKind, path::Path};

pub const DATA_ROOT_MIGRATION_PENDING_FILE: &str = ".exportdoc-data-root-migration-pending.json";
pub const DATA_ROOT_MIGRATION_REDIRECT_FILE: &str = ".exportdoc-data-root-migration-redirect.json";

/// Call after acquiring the data root's instance lock, before opening or moving its database.
/// Marker contents are interpreted only by the maintenance owner; even a damaged marker
/// must prevent an ordinary open from initializing an empty or partially copied database.
pub fn ensure_data_root_available(data_root: &Path) -> Result<()> {
    for (name, message) in [
        (
            DATA_ROOT_MIGRATION_PENDING_FILE,
            "业务数据目录正在迁移，请使用安排迁移的原程序完成恢复后再打开。",
        ),
        (
            DATA_ROOT_MIGRATION_REDIRECT_FILE,
            "此业务数据目录已迁移并封存，请使用原程序保存的新数据目录，不能重新初始化旧目录。",
        ),
    ] {
        match fs::symlink_metadata(data_root.join(name)) {
            Ok(_) => return Err(Error::unavailable(message)),
            Err(cause) if cause.kind() == ErrorKind::NotFound => {}
            Err(cause) => {
                return Err(Error::unavailable(format!(
                    "无法检查业务数据目录的迁移状态：{cause}"
                )));
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn any_maintenance_marker_fails_closed_even_when_its_contents_are_damaged() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../.codex-runtime/maintenance-marker-tests")
            .join(format!(
                "{}-{}",
                std::process::id(),
                std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            ));
        fs::create_dir_all(&root).unwrap();
        for name in [
            DATA_ROOT_MIGRATION_PENDING_FILE,
            DATA_ROOT_MIGRATION_REDIRECT_FILE,
        ] {
            let marker = root.join(name);
            fs::write(&marker, b"damaged-json").unwrap();
            assert!(ensure_data_root_available(&root).is_err());
            fs::remove_file(&marker).unwrap();
            fs::create_dir(&marker).unwrap();
            assert!(ensure_data_root_available(&root).is_err());
            fs::remove_dir(&marker).unwrap();
        }
        assert!(ensure_data_root_available(&root).is_ok());
        fs::remove_dir_all(root).unwrap();
    }
}
