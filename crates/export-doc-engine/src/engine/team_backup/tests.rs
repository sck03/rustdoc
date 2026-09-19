//! 团队备份与灾备：受管路径辅助、密封包格式、一次性票据与状态接口的最小回归。
use super::sealed::{Entry, ManifestFile, open, seal, unpack, verify_manifest, zip_payload};
use super::*;
use crate::paths::nonce;
use std::{fs, path::PathBuf, sync::Arc};

struct Workspace(PathBuf);
impl Workspace {
    fn new() -> Self {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf();
        let root = workspace
            .join(".codex-runtime")
            .join("team-backup-tests")
            .join(nonce().unwrap());
        fs::create_dir_all(root.join("Cache")).unwrap();
        fs::create_dir_all(root.join("Logs")).unwrap();
        Self(root)
    }
    fn paths(&self) -> RuntimePaths {
        RuntimePaths {
            app_root: self.0.clone(),
            data_root: self.0.clone(),
            cache_root: self.0.join("Cache"),
            log_root: self.0.join("Logs"),
            font_path: self.0.join("font.otf"),
        }
    }
}
impl Drop for Workspace {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}
fn open_service(workspace: &Workspace) -> Arc<NativeService> {
    NativeService::open(workspace.paths()).unwrap()
}
fn admin() -> Actor {
    Actor {
        id: 1,
        name: "系统管理员".into(),
        company: "DEFAULT".into(),
        department: "GENERAL".into(),
        admin: true,
        grants: vec![],
    }
}

#[test]
fn managed_file_rejects_traversal_and_extension_mismatch() {
    let workspace = Workspace::new();
    let root = disaster_root(&workspace.paths()).unwrap();
    assert!(managed_file(&root, "edm-disaster-recovery-1.edmrecovery", "edmrecovery").is_ok());
    assert!(managed_file(&root, "../escape.edmrecovery", "edmrecovery").is_err());
    assert!(managed_file(&root, "sub/package.edmrecovery", "edmrecovery").is_err());
    assert!(managed_file(&root, "package.zip", "edmrecovery").is_err());
    assert!(managed_file(&root, "", "edmrecovery").is_err());
}

#[test]
fn validate_package_password_enforces_length_bounds() {
    assert!(validate_package_password("1234567").is_err());
    assert!(validate_package_password("12345678").is_ok());
    assert!(validate_package_password(&"a".repeat(1024)).is_ok());
    assert!(validate_package_password(&"a".repeat(1025)).is_err());
}

#[test]
fn sha256_hex_matches_known_vector() {
    assert_eq!(
        sha256_hex(b"abc"),
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    );
}

#[test]
fn sealed_package_roundtrip_keeps_payload_secret() {
    let sealed = seal(b"EDM-TEST", "password-123", b"business data").unwrap();
    assert_ne!(&sealed[..], b"business data");
    assert_eq!(
        open(b"EDM-TEST", "password-123", &sealed).unwrap(),
        b"business data"
    );
}

#[test]
fn sealed_package_rejects_wrong_password_and_magic() {
    let sealed = seal(b"EDM-TEST", "password-123", b"business data").unwrap();
    assert!(open(b"EDM-TEST", "wrong-password", &sealed).is_err());
    assert!(open(b"EDM-OTHER", "password-123", &sealed).is_err());
    assert!(open(b"EDM-TEST", "password-123", b"not a sealed package").is_err());
}

#[test]
fn zip_payload_unpack_and_manifest_agree() {
    let workspace = Workspace::new();
    let working = sealed::working_directory(&workspace.paths().data_root).unwrap();
    let first = working.join("a.bin");
    let second = working.join("b.bin");
    fs::write(&first, b"first").unwrap();
    fs::write(&second, b"second").unwrap();
    let entries = [
        Entry {
            source: &first,
            name: "Database/a.bin",
        },
        Entry {
            source: &second,
            name: "Security/b.bin",
        },
    ];
    let payload = zip_payload(&entries).unwrap();
    let extracted = working.join("extracted");
    let unpacked = unpack(&payload, &extracted).unwrap();
    assert_eq!(unpacked.len(), 2);
    let manifest = [
        ManifestFile::from_path("Database/a.bin", &first).unwrap(),
        ManifestFile::from_path("Security/b.bin", &second).unwrap(),
    ];
    verify_manifest(&unpacked, &manifest).unwrap();

    // 摘要与清单不一致必须被拒绝。
    let mut wrong_digest = unpacked.clone();
    wrong_digest[0].2 = "0".repeat(64);
    assert!(verify_manifest(&wrong_digest, &manifest).is_err());

    let mut wrong_name = unpacked.clone();
    wrong_name[0].0 = "Database/other.bin".into();
    assert!(verify_manifest(&wrong_name, &manifest).is_err());

    let short = [ManifestFile::from_path("Database/a.bin", &first).unwrap()];
    assert!(verify_manifest(&unpacked, &short).is_err());
    fs::remove_dir_all(&working).unwrap();
}

#[test]
fn sensitive_ticket_binds_actor_and_action() {
    let actor = admin();
    let (token, _) = issue_sensitive_ticket(&actor, ACTION_RESTORE_DATABASE).unwrap();
    let parameters: [(&str, String); 1] = [(TICKET_HEADER, token.clone())];
    assert!(sensitive_ticket(&parameters, &actor, ACTION_RESTORE_DATABASE).is_ok());
    // 票据一次性：再次使用必须失败。
    assert!(sensitive_ticket(&parameters, &actor, ACTION_RESTORE_DATABASE).is_err());

    let (other, _) = issue_sensitive_ticket(&actor, ACTION_RESTORE_SERVER).unwrap();
    let other_parameters: [(&str, String); 1] = [(TICKET_HEADER, other)];
    assert!(sensitive_ticket(&other_parameters, &actor, ACTION_RESTORE_DATABASE).is_err());

    let empty: [(&str, String); 0] = [];
    assert!(sensitive_ticket(&empty, &actor, ACTION_RESTORE_DATABASE).is_err());
}

#[test]
fn download_ticket_is_single_use() {
    let ticket = issue_download_ticket("backup.dump".into()).unwrap();
    let token = ticket["token"].as_str().unwrap().to_string();
    assert_eq!(consume_download_ticket(&token).unwrap(), "backup.dump");
    assert!(consume_download_ticket(&token).is_err());
    assert!(consume_download_ticket("unknown-token").is_err());
}

#[test]
fn cloud_status_reports_unconfigured_state() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    let value = cloud::status(&service, &admin()).unwrap();
    assert_eq!(value["enabled"], false);
    assert_eq!(value["isConfigured"], false);
    assert!(value["backupRoot"].as_str().unwrap().ends_with("Backups"));
}

#[test]
fn disaster_status_reports_clean_workspace() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    let value = disaster::status(&service, &admin()).unwrap();
    assert_eq!(value["supported"], true);
    assert_eq!(value["usesSqlite"], true);
    assert_eq!(value["pendingRestore"], false);
    assert!(
        value["recoveryRoot"]
            .as_str()
            .unwrap()
            .ends_with("DisasterRecovery")
    );
}

#[test]
fn postgres_status_reports_unsupported_on_sqlite() {
    let workspace = Workspace::new();
    let service = open_service(&workspace);
    let result = postgres::list(&service, &admin());
    assert!(result.is_err());
    assert_eq!(result.unwrap_err().status, Some(501));
}
