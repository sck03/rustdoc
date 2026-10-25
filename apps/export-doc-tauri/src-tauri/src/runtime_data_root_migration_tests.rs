use super::*;
use crate::runtime_paths::ensure_runtime_data_directories;
use crate::runtime_paths_config::{
    read_data_root_from_config, runtime_paths_config_backup_path, runtime_paths_config_path,
};
use std::sync::atomic::{AtomicU64, Ordering};

struct Workspace {
    root: PathBuf,
    paths: RuntimePaths,
    target: PathBuf,
}

impl Workspace {
    fn new() -> Self {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        let root = std::env::current_dir()
            .unwrap()
            .join("target/tauri-migration-tests")
            .join(format!(
                "{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::Relaxed)
            ));
        let app_root = root.join("app");
        let data_root = root.join("source");
        let config = root.join("config");
        fs::create_dir_all(&app_root).unwrap();
        ensure_runtime_data_directories(&data_root).unwrap();
        fs::create_dir_all(data_root.join("Security")).unwrap();
        fs::write(
            data_root.join("Database/exportdoc-native.db"),
            b"database-snapshot",
        )
        .unwrap();
        fs::write(data_root.join("Security/master.key"), b"isolated-test-key").unwrap();
        persist_runtime_data_root(&config, &data_root).unwrap();
        let workspace = Self {
            target: root.join("target"),
            root,
            paths: RuntimePaths {
                app_root,
                log_root: data_root.join("Logs"),
                data_root,
                runtime_config_root: config,
                portable: false,
            },
        };
        schedule_data_root_migration(&workspace.paths, &workspace.target).unwrap();
        workspace
    }

    fn apply(&self) -> Result<(), Box<dyn Error>> {
        apply_pending_data_root_migration(&self.paths.runtime_config_root, &self.paths.app_root)
    }

    fn prepare_journal(&self, copy: bool, seal_source: bool) -> MigrationJournal {
        let migration = read_marker(&pending_data_root_migration_path(
            &self.paths.runtime_config_root,
        ))
        .unwrap();
        let _source = lock_root(&self.paths.data_root, false).unwrap();
        let _target = lock_root(&self.target, true).unwrap();
        let journal = MigrationJournal {
            schema_version: 1,
            migration,
            source_manifest: collect_tree_manifest(&self.paths.data_root).unwrap(),
        };
        write_marker(
            &self.target.join(DATA_ROOT_MIGRATION_PENDING_FILE),
            &journal,
        )
        .unwrap();
        if copy {
            copy_verified_tree(
                &journal.source_manifest,
                &self.paths.data_root,
                &self.target,
            )
            .unwrap();
        }
        if seal_source {
            write_marker(
                &self.paths.data_root.join(DATA_ROOT_MIGRATION_REDIRECT_FILE),
                &journal.migration,
            )
            .unwrap();
        }
        journal
    }
}

impl Drop for Workspace {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.root);
    }
}

#[test]
fn source_and_target_business_instance_locks_prevent_all_migration_writes() {
    for source in [true, false] {
        let workspace = Workspace::new();
        let lock = lock_root(
            if source {
                &workspace.paths.data_root
            } else {
                &workspace.target
            },
            !source,
        )
        .unwrap();
        assert!(workspace.apply().unwrap_err().to_string().contains("占用"));
        assert_eq!(
            fs::read(
                workspace
                    .paths
                    .data_root
                    .join("Database/exportdoc-native.db")
            )
            .unwrap(),
            b"database-snapshot"
        );
        assert!(
            !workspace
                .target
                .join(DATA_ROOT_MIGRATION_PENDING_FILE)
                .exists()
        );
        assert!(
            !workspace
                .paths
                .data_root
                .join(DATA_ROOT_MIGRATION_REDIRECT_FILE)
                .exists()
        );
        drop(lock);
        workspace.apply().unwrap();
    }
}

#[test]
fn legacy_source_instance_lock_is_also_respected() {
    let workspace = Workspace::new();
    let path = workspace.paths.data_root.join("native-instance.lock");
    let lock = File::create(&path).unwrap();
    lock.lock_exclusive().unwrap();
    assert!(workspace.apply().is_err());
    assert!(
        workspace
            .paths
            .data_root
            .join("Database/exportdoc-native.db")
            .exists()
    );
    drop(lock);
    workspace.apply().unwrap();
}

#[test]
fn interrupted_copy_reuses_verified_files_and_releases_only_its_temporary_file() {
    let workspace = Workspace::new();
    workspace.prepare_journal(false, false);
    fs::create_dir(workspace.target.join("Database")).unwrap();
    fs::write(
        workspace.target.join("Database/exportdoc-native.db"),
        b"database-snapshot",
    )
    .unwrap();
    fs::write(
        workspace
            .target
            .join(crate::runtime_tree_manifest::COPY_TEMP_FILE),
        b"partial",
    )
    .unwrap();
    assert!(ensure_data_root_available(&workspace.target).is_err());
    workspace.apply().unwrap();
    assert_eq!(
        fs::read(workspace.target.join("Security/master.key")).unwrap(),
        b"isolated-test-key"
    );
    assert!(
        !workspace
            .target
            .join(crate::runtime_tree_manifest::COPY_TEMP_FILE)
            .exists()
    );
    assert!(ensure_data_root_available(&workspace.target).is_ok());
    assert!(ensure_data_root_available(&workspace.paths.data_root).is_err());
}

#[test]
fn conflicting_target_content_is_preserved_and_source_is_not_sealed_or_removed() {
    let workspace = Workspace::new();
    workspace.prepare_journal(false, false);
    fs::create_dir(workspace.target.join("Database")).unwrap();
    let target = workspace.target.join("Database/exportdoc-native.db");
    fs::write(&target, b"user-content").unwrap();
    assert!(workspace.apply().is_err());
    assert_eq!(fs::read(&target).unwrap(), b"user-content");
    assert!(
        workspace
            .paths
            .data_root
            .join("Database/exportdoc-native.db")
            .exists()
    );
    assert!(
        !workspace
            .paths
            .data_root
            .join(DATA_ROOT_MIGRATION_REDIRECT_FILE)
            .exists()
    );
}

#[test]
fn interrupted_source_cleanup_resumes_without_recopying_missing_source_files() {
    let workspace = Workspace::new();
    workspace.prepare_journal(true, true);
    fs::remove_file(
        workspace
            .paths
            .data_root
            .join("Database/exportdoc-native.db"),
    )
    .unwrap();
    fs::remove_dir(workspace.paths.data_root.join("Database")).unwrap();
    persist_runtime_data_root(&workspace.paths.runtime_config_root, &workspace.target).unwrap();
    workspace.apply().unwrap();
    assert_eq!(
        fs::read(workspace.target.join("Database/exportdoc-native.db")).unwrap(),
        b"database-snapshot"
    );
    assert!(
        !workspace
            .paths
            .data_root
            .join("Security/master.key")
            .exists()
    );
    assert!(workspace.paths.data_root.join(INSTANCE_LOCK).is_file());
    assert!(ensure_data_root_available(&workspace.paths.data_root).is_err());
    for config in [
        runtime_paths_config_path(&workspace.paths.runtime_config_root),
        runtime_paths_config_backup_path(&workspace.paths.runtime_config_root),
    ] {
        assert!(same_path(
            &read_data_root_from_config(&config, &workspace.paths.app_root).unwrap(),
            &fs::canonicalize(&workspace.target).unwrap()
        ));
    }
    fs::write(
        runtime_paths_config_path(&workspace.paths.runtime_config_root),
        b"broken-json",
    )
    .unwrap();
    assert!(same_path(
        &read_persisted_data_root(
            &workspace.paths.runtime_config_root,
            &workspace.paths.app_root
        )
        .unwrap()
        .unwrap(),
        &fs::canonicalize(&workspace.target).unwrap()
    ));
}

#[test]
fn source_changes_after_sealing_are_preserved_until_the_conflict_is_resolved() {
    let workspace = Workspace::new();
    workspace.prepare_journal(true, true);
    let source = workspace
        .paths
        .data_root
        .join("Database/exportdoc-native.db");
    fs::write(&source, b"changed-external-data").unwrap();
    assert!(workspace.apply().is_err());
    assert_eq!(fs::read(&source).unwrap(), b"changed-external-data");
    assert!(ensure_data_root_available(&workspace.target).is_err());
    fs::write(&source, b"database-snapshot").unwrap();
    workspace.apply().unwrap();
}

#[test]
fn diagnostic_added_after_source_sealing_is_preserved_without_blocking_the_target() {
    let workspace = Workspace::new();
    workspace.prepare_journal(true, true);
    let diagnostic = workspace.paths.data_root.join("Logs/rejected-open.log");
    fs::write(&diagnostic, b"old-root-open-was-rejected").unwrap();
    workspace.apply().unwrap();
    assert_eq!(
        fs::read(&diagnostic).unwrap(),
        b"old-root-open-was-rejected"
    );
    assert!(!workspace.target.join("Logs/rejected-open.log").exists());
    assert!(ensure_data_root_available(&workspace.paths.data_root).is_err());
    assert!(ensure_data_root_available(&workspace.target).is_ok());
}

#[test]
fn committed_recovery_preserves_changes_made_in_the_already_published_target() {
    let workspace = Workspace::new();
    let journal = workspace.prepare_journal(true, true);
    journal
        .source_manifest
        .remove_verified_source(&workspace.paths.data_root)
        .unwrap();
    fs::rename(
        workspace.target.join(DATA_ROOT_MIGRATION_PENDING_FILE),
        workspace.target.join(COMPLETION_FILE),
    )
    .unwrap();
    persist_runtime_data_root(&workspace.paths.runtime_config_root, &workspace.target).unwrap();
    fs::write(
        workspace.target.join("Database/exportdoc-native.db"),
        b"new-business-data",
    )
    .unwrap();
    workspace.apply().unwrap();
    assert_eq!(
        fs::read(workspace.target.join("Database/exportdoc-native.db")).unwrap(),
        b"new-business-data"
    );
    assert!(!pending_data_root_migration_path(&workspace.paths.runtime_config_root).exists());
    assert!(!workspace.target.join(COMPLETION_FILE).exists());
}

#[test]
fn forged_migration_paths_fail_before_copying_or_deleting_any_source_file() {
    let workspace = Workspace::new();
    let mut journal = workspace.prepare_journal(false, false);
    journal.source_manifest.entries[0].path = "../outside".into();
    let path = workspace.target.join(DATA_ROOT_MIGRATION_PENDING_FILE);
    fs::write(&path, serde_json::to_vec(&journal).unwrap()).unwrap();
    assert!(workspace.apply().is_err());
    assert!(
        workspace
            .paths
            .data_root
            .join("Database/exportdoc-native.db")
            .exists()
    );
}

#[test]
fn capacity_calculation_keeps_a_margin_and_saturates() {
    assert_eq!(
        required_migration_capacity(1),
        1 + MINIMUM_MIGRATION_FREE_SPACE_MARGIN_BYTES
    );
    assert_eq!(
        required_migration_capacity(20 * 1024 * 1024 * 1024),
        21 * 1024 * 1024 * 1024
    );
    assert_eq!(required_migration_capacity(u64::MAX), u64::MAX);
}
