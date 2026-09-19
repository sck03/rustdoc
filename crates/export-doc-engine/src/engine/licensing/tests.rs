#![allow(unused_imports, dead_code)]

use super::p256::*;
use super::*;
use crate::paths::{RuntimePaths, nonce};
use std::sync::Arc;
use zip::ZipArchive;

const TEST_MACHINE: &str = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const TEST_PUBLIC_KEY: &str = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExmev74ezFtXdBMnhoFcZomuei6hvAkfOTiiQ/NkGo3VVMTizAky8BlPlJKi/GGG7jDQKhGCW3rPpmOJcmMuLeQ==";
const EXPIRING_KEY: &str = "EDM2-eyJ2IjoyLCJtaWQiOiJhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwiZXhwIjoxOTAwMDAwMDAwfQ.MEUCIQDrRocmME_GTmL7YWYgexFWJAY69hx3Qj7ip0lEJkb-9QIgcRl9Baf4KJww7M9tdoIGvpDub7C7X5zo5b5o4lFhfJs";
const LIFETIME_KEY: &str = "EDM2-eyJ2IjoyLCJtaWQiOiJhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwiZXhwIjotMX0.MEUCIH4T0GLIyWQFDCXfkHLYyBHaBt6IVRe01Qckr0EaIhvaAiEAtzKoD_FPeRgnt3Nfrdgd4qXOvaOB8Zp1-X2bva-3h3w";

struct Workspace(PathBuf);
impl Workspace {
    fn new() -> Self {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .join(".codex-runtime")
            .join("licensing-tests")
            .join(nonce().unwrap());
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
fn open(workspace: &Workspace) -> Arc<NativeService> {
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
struct TestVerifier;
impl SignatureVerifier for TestVerifier {
    fn validate(
        &self,
        _machine_id: &str,
        license_key: &str,
    ) -> std::result::Result<Option<i64>, String> {
        let key = normalize_license_key(license_key);
        let rest = key.strip_prefix("TEST-").ok_or("注册码无效。")?;
        if rest == "LIFETIME" {
            return Ok(None);
        }
        let date = NaiveDate::parse_from_str(rest, "%Y-%m-%d").map_err(|_| "注册码有效期无效。")?;
        Ok(Some(
            date.and_hms_opt(12, 0, 0).unwrap().and_utc().timestamp(),
        ))
    }
}

#[test]
fn generator_is_on_curve_and_the_group_order_reaches_infinity() {
    assert!(on_curve(&GENERATOR));
    let tripled = jac_to_affine(scalar_mult(THREE, &GENERATOR)).unwrap();
    let summed = jac_to_affine(jac_add_mixed(
        jac_double(Jacobian::from_affine(GENERATOR)),
        &GENERATOR,
    ))
    .unwrap();
    assert_eq!((summed.x, summed.y), (tripled.x, tripled.y));
    assert!(jac_to_affine(scalar_mult(N, &GENERATOR)).is_none());
}
#[test]
fn signed_license_keys_validate_only_for_the_bound_machine() {
    let verifier = EcdsaVerifier::new(TEST_PUBLIC_KEY).unwrap();
    assert_eq!(
        verifier.validate(TEST_MACHINE, EXPIRING_KEY).unwrap(),
        Some(1_900_000_000)
    );
    assert!(
        verifier
            .validate(TEST_MACHINE, LIFETIME_KEY)
            .unwrap()
            .is_none()
    );
    assert!(EcdsaVerifier::new(VENDOR_PUBLIC_KEY).is_ok());
    assert!(verifier.validate(&"b".repeat(64), EXPIRING_KEY).is_err());
    assert!(
        verifier
            .validate(TEST_MACHINE, &format!("{EXPIRING_KEY}x"))
            .is_err()
    );
    assert!(verifier.validate(TEST_MACHINE, "EDM2-bad.bad").is_err());
    assert!(verifier.validate(TEST_MACHINE, "not-a-key").is_err());
}
#[test]
fn fresh_installation_reports_an_active_trial() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let status = handle(&service, &admin(), GET_LICENSE_STATUS, &[], &[], &json!({})).unwrap();
    assert_eq!(status["isRegistered"], false);
    assert_eq!(status["trialDays"], TRIAL_DAYS);
    assert!(status["daysRemaining"].as_i64().unwrap() > 0);
    assert_eq!(status["machineId"].as_str().unwrap().len(), 64);
    assert!(status["message"].as_str().unwrap().contains("试用期"));
}
#[test]
fn registration_persists_and_survives_reopening_the_data_root() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let service = open(&workspace);
    let response = register(
        &service.store,
        &service.protector,
        &service.clock,
        &TestVerifier,
        &admin(),
        "TEST-2099-12-31",
    )
    .unwrap();
    assert_eq!(response["success"], true);
    // handle() 固定使用生产 ECDSA 验证器；注册结果以同一测试验证器读取为准。
    let status = super::status(
        &service.store,
        &service.protector,
        &service.clock,
        &TestVerifier,
    )
    .unwrap();
    assert_eq!(status["isRegistered"], true);
    assert_eq!(status["expireDate"], "2099-12-31");
    // the anchor is sealed: neither the key nor the plaintext anchor may appear on disk
    let database = fs::read(paths.data_root.join("exportdoc-native.db")).unwrap();
    assert!(!String::from_utf8_lossy(&database).contains("TEST-2099-12-31"));
    assert!(!String::from_utf8_lossy(&database).contains("local_binding_secret"));
    drop(service);
    // handle() 固定使用生产 ECDSA 验证器；注册码持久化以同一测试验证器重开读取为准。
    let reopened = open(&workspace);
    let again = super::status(
        &reopened.store,
        &reopened.protector,
        &reopened.clock,
        &TestVerifier,
    )
    .unwrap();
    assert_eq!(again["isRegistered"], true);
    assert_eq!(again["expireDate"], "2099-12-31");
}
#[test]
fn invalid_license_keys_are_rejected_and_the_trial_stays_active() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    for key in ["garbage", "", "TEST-not-a-date"] {
        assert_eq!(
            register(
                &service.store,
                &service.protector,
                &service.clock,
                &TestVerifier,
                &admin(),
                key
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    let status = handle(&service, &admin(), GET_LICENSE_STATUS, &[], &[], &json!({})).unwrap();
    assert_eq!(status["isRegistered"], false);
}
#[test]
fn license_registration_requires_an_administrator() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let user = Actor {
        id: 2,
        name: "用户".into(),
        company: "DEFAULT".into(),
        department: "GENERAL".into(),
        admin: false,
        grants: vec![],
    };
    assert_eq!(
        handle(
            &service,
            &user,
            REGISTER_LICENSE,
            &[],
            &[],
            &json!({"licenseKey":"TEST-2099-12-31"})
        )
        .unwrap_err()
        .status,
        Some(403)
    );
}
#[test]
fn support_package_carries_diagnostics_without_secrets() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    register(
        &service.store,
        &service.protector,
        &service.clock,
        &TestVerifier,
        &admin(),
        "TEST-2099-12-31",
    )
    .unwrap();
    let saved = handle(
        &service,
        &admin(),
        SAVE_SUPPORT_PACKAGE_TO_RUNTIME,
        &[],
        &[],
        &json!({}),
    )
    .unwrap();
    assert_eq!(saved["success"], true);
    let path = PathBuf::from(saved["fullPath"].as_str().unwrap());
    assert!(path.is_file());
    assert!(!String::from_utf8_lossy(&fs::read(&path).unwrap()).contains("TEST-2099-12-31"));
    let output = download(&service, &admin(), DOWNLOAD_SUPPORT_PACKAGE, &[]).unwrap();
    assert_eq!(output.media_type, "application/zip");
    assert!(output.file_name.ends_with("_support_package.zip"));
    let mut archive = ZipArchive::new(std::io::Cursor::new(output.content)).unwrap();
    let mut names = vec![];
    for index in 0..archive.len() {
        names.push(archive.by_index(index).unwrap().name().to_string());
    }
    assert_eq!(
        names
            .iter()
            .filter(|name| name.starts_with("diagnostics/"))
            .count(),
        5
    );
    assert!(names.contains(&"diagnostics/database.json".to_string()));
}
#[test]
fn system_log_cleanup_honors_the_configured_retention() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let service = open(&workspace);
    service
        .store
        .transaction(|tx| {
            tx.append_audit_details(
                &AuditWrite {
                    kind: "license",
                    record_id: 0,
                    version: 1,
                    action: "register",
                    actor_id: 1,
                    occurred_at: "2020-01-01T00:00:00Z",
                    note: "",
                },
                &json!({}),
            )?;
            Ok(tx.set_settings(
                "settings",
                1,
                &json!({"system":{"auditLogRetentionDays":30,"logRetentionDays":30}}),
            )?)
        })
        .unwrap();
    let old_log = paths.log_root.join("2020-01-01.log");
    fs::write(&old_log, b"old log line").unwrap();
    std::fs::File::options()
        .write(true)
        .open(&old_log)
        .unwrap()
        .set_times(
            std::fs::FileTimes::new().set_modified(
                SystemTime::UNIX_EPOCH + std::time::Duration::from_secs(1_577_836_800),
            ),
        )
        .unwrap();
    let response = handle(
        &service,
        &admin(),
        CLEANUP_SYSTEM_LOGS,
        &[],
        &[],
        &json!({}),
    )
    .unwrap();
    assert_eq!(response["success"], true);
    assert!(response["deletedAuditLogs"].as_i64().unwrap() >= 1);
    assert!(response["deletedTextLogsByAge"].as_i64().unwrap() >= 1);
    assert!(!old_log.exists());
}
