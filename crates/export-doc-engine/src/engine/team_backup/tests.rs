//! 团队备份与灾备：受管路径辅助、密封包格式、一次性票据与状态接口的最小回归。
use super::sealed::{open_to, seal_to};
use super::*;
use std::time::Instant;
#[cfg(feature = "postgres")]
mod postgres_recovery;
mod recovery;
mod replica_protocol;
pub(super) use crate::engine::test_support::TestService;
pub(super) use crate::engine::test_support::{
    ScratchDirectory as Workspace, administrator as admin,
};
use crate::paths::nonce;
use std::fs;
#[cfg(feature = "postgres")]
use std::path::PathBuf;

pub(super) fn open_service(workspace: &Workspace) -> TestService {
    TestService::open(workspace.paths())
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
    let mut sealed = Vec::new();
    seal_to(
        b"EDM-TEST-2",
        "password-123",
        b"business data".as_slice(),
        &mut sealed,
    )
    .unwrap();
    assert_ne!(&sealed[..], b"business data");
    let mut output = Vec::new();
    open_to(
        b"EDM-TEST-2",
        "password-123",
        sealed.as_slice(),
        &mut output,
    )
    .unwrap();
    assert_eq!(output, b"business data");
}

#[test]
fn sealed_package_rejects_wrong_password_and_magic() {
    let mut sealed = Vec::new();
    seal_to(
        b"EDM-TEST-2",
        "password-123",
        b"business data".as_slice(),
        &mut sealed,
    )
    .unwrap();
    for (magic, password, input) in [
        (
            b"EDM-TEST-2".as_slice(),
            "wrong-password",
            sealed.as_slice(),
        ),
        (b"EDM-OTHER-2".as_slice(), "password-123", sealed.as_slice()),
        (
            b"EDM-TEST-2".as_slice(),
            "password-123",
            b"not a sealed package".as_slice(),
        ),
        (
            b"EDM-TEST-2".as_slice(),
            "password-123",
            b"EDM-TEST-1old-v1-payload".as_slice(),
        ),
    ] {
        let mut output = Vec::new();
        assert!(open_to(magic, password, input, &mut output).is_err());
        assert!(output.is_empty());
    }
}

#[test]
fn sensitive_ticket_binds_actor_and_action() {
    let tickets = Tickets::default();
    let actor = admin();
    let (token, _) = tickets
        .issue_sensitive(&actor, ACTION_RESTORE_DATABASE)
        .unwrap();
    let parameters: [(&str, String); 1] = [(TICKET_HEADER, token.clone())];
    assert!(
        Tickets::default()
            .authorize(&parameters, &actor, ACTION_RESTORE_DATABASE)
            .is_err()
    );
    assert!(
        tickets
            .authorize(&parameters, &actor, ACTION_RESTORE_DATABASE)
            .is_ok()
    );
    // 票据一次性：再次使用必须失败。
    assert!(
        tickets
            .authorize(&parameters, &actor, ACTION_RESTORE_DATABASE)
            .is_err()
    );

    let (other, _) = tickets
        .issue_sensitive(&actor, ACTION_RESTORE_SERVER)
        .unwrap();
    let other_parameters: [(&str, String); 1] = [(TICKET_HEADER, other)];
    assert!(
        tickets
            .authorize(&other_parameters, &actor, ACTION_RESTORE_DATABASE)
            .is_err()
    );

    let empty: [(&str, String); 0] = [];
    assert!(
        tickets
            .authorize(&empty, &actor, ACTION_RESTORE_DATABASE)
            .is_err()
    );
}

#[test]
fn download_ticket_is_single_use() {
    let tickets = Tickets::default();
    let ticket = tickets.issue_download("backup.dump".into()).unwrap();
    let token = ticket["token"].as_str().unwrap().to_string();
    assert!(Tickets::default().consume_download(&token).is_err());
    assert_eq!(tickets.consume_download(&token).unwrap(), "backup.dump");
    assert!(tickets.consume_download(&token).is_err());
    assert!(tickets.consume_download("unknown-token").is_err());
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
