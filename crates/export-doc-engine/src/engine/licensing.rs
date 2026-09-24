//! License status/registration, diagnostic support packages and system log cleanup.
//!
//! License keys use the EDM2-<payload>.<signature> format: payload is the exact
//! JSON {"v":2,"mid":<machine id>,"exp":<unix seconds, -1 for lifetime>} and the
//! signature is a DER-encoded ECDSA P-256 signature with SHA-256 over the raw
//! payload bytes. The machine id binds a key to the installation anchor (stable
//! random seed plus sealed local binding secret), kept as an AES-256-GCM
//! protected settings record. Keys and anchors are never written to logs.
use super::{
    NativeService, auth,
    error::{Result, invalid, unavailable, unsupported},
    settings,
    store::{self, Actor, Store},
    tasks,
};
use crate::{
    clock::BusinessClock,
    contracts,
    generated_api::*,
    paths::{RuntimePaths, atomic_write, ensure_safe_absolute, nonce},
};
use base64::{Engine, prelude::BASE64_STANDARD, prelude::BASE64_URL_SAFE_NO_PAD};
use chrono::{DateTime, NaiveDate, Utc};
use export_doc_storage::AuditWrite;
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    time::SystemTime,
};
use zip::{CompressionMethod, ZipWriter, write::SimpleFileOptions};

#[allow(dead_code)]
pub const OPERATIONS: &[Operation] = &[
    GET_LICENSE_STATUS,
    REGISTER_LICENSE,
    DOWNLOAD_SUPPORT_PACKAGE,
    SAVE_SUPPORT_PACKAGE_TO_RUNTIME,
    CLEANUP_SYSTEM_LOGS,
];
/// Writing a support package into the managed data root is a desktop-local operation.
#[allow(dead_code)]
pub const LOCAL: &[Operation] = &[SAVE_SUPPORT_PACKAGE_TO_RUNTIME];
const TRIAL_DAYS: i64 = 7;
const BINDING_VERSION: u8 = 3;
const SIGNED_PREFIX: &str = "EDM2-";
const LIFETIME: i64 = -1;
const VENDOR_PUBLIC_KEY: &str = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHfZ6xFQTzZbClzRPjqoF9VHiIjN8eDyXQuDZ2gG6oT0yF8qNZ0MzGA1n4m7Kl1Sd6DOuf32TMyGLxbqoNGcJAg==";
const ANCHOR_NAME: &str = "license-anchor";
const ANCHOR_STORAGE: &str = "当前数据库 settings/license-anchor 受密封保护的授权锚点";
const STORAGE_POLICY: &str = "授权状态、试用开始日期、稳定机器码种子、本机密封随机量与已验证注册码以 AES-256-GCM 密封保存到受管设置记录；本地主密钥位于运行数据根 Security 目录。删除程序目录后重新安装不会重置试用，也不会丢失已注册授权；业务数据库、模板和普通运行数据不写系统盘默认用户目录。";
const SUPPORT_PACKAGE_POLICY: &str = "支持包只包含诊断摘要（版本、数据库类型、受管目录、脱敏配置摘要与审计统计）；不收集密码、令牌、业务数据或数据库内容。";
const SCAN_LIMIT: i64 = 50_000;
const DELETE_LIMIT: usize = 100_000;

/// Verifies an issued license key against the current machine identity. The
/// built-in implementation checks the EDM2- signed format with ECDSA P-256;
/// tests and key tooling inject deterministic verifiers.
pub trait SignatureVerifier: Send + Sync {
    /// Returns the Unix expiry second; None marks a lifetime license.
    fn validate(
        &self,
        machine_id: &str,
        license_key: &str,
    ) -> std::result::Result<Option<i64>, String>;
}
/// ECDSA P-256 with SHA-256 over the raw EDM2- payload bytes.
fn verifier() -> Result<EcdsaVerifier> {
    EcdsaVerifier::new(VENDOR_PUBLIC_KEY).map_err(unavailable)
}

mod p256;
mod tests;
use self::p256::EcdsaVerifier;

// --- Machine identity and the sealed installation anchor. ---
#[derive(Serialize, Deserialize)]
struct Anchor {
    schema_version: i64,
    machine_seed: String,
    local_binding_secret: String,
    install_date: String,
    last_run_date: String,
    license_key: String,
    fingerprint_hash: String,
    binding_hash: String,
}
impl Anchor {
    fn new(today: NaiveDate) -> Result<Self> {
        Ok(Self {
            schema_version: 1,
            machine_seed: hex(&random_bytes(16)?),
            local_binding_secret: BASE64_STANDARD.encode(random_bytes(32)?),
            install_date: today.to_string(),
            last_run_date: today.to_string(),
            license_key: String::new(),
            fingerprint_hash: String::new(),
            binding_hash: String::new(),
        })
    }
    fn install(&self) -> Result<NaiveDate> {
        parse_date(&self.install_date)
    }
    fn effective(&self, today: NaiveDate) -> Result<NaiveDate> {
        Ok(today.max(parse_date(&self.last_run_date)?))
    }
    /// Seeds first-use identity fields and advances the trial clock.
    fn refresh(&mut self, identity: &Identity, today: NaiveDate) -> Result<bool> {
        let mut changed = false;
        if self.fingerprint_hash.is_empty() {
            self.fingerprint_hash = identity.fingerprint_hash.clone();
            changed = true;
        }
        if self.binding_hash.is_empty() {
            self.binding_hash = identity.binding_hash.clone();
            changed = true;
        }
        if today > parse_date(&self.last_run_date)? {
            self.last_run_date = today.to_string();
            changed = true;
        }
        Ok(changed)
    }
}
struct Identity {
    machine_id: String,
    fingerprint_hash: String,
    binding_hash: String,
}
fn machine_identity(anchor: &Anchor, provider: &str) -> Identity {
    let fingerprint_hash = sha256_hex(&format!(
        "device-v{BINDING_VERSION}|{}",
        if provider == "PostgreSQL" {
            "managed-server-installation".into()
        } else {
            device_fingerprint()
        }
    ));
    let binding_hash = sha256_hex(&format!(
        "local-binding-v{BINDING_VERSION}|{}",
        anchor.local_binding_secret
    ));
    let machine_id = sha256_hex(&format!(
        "license-v{BINDING_VERSION}|{}|{fingerprint_hash}|{binding_hash}",
        anchor.machine_seed
    ));
    Identity {
        machine_id,
        fingerprint_hash,
        binding_hash,
    }
}
fn device_fingerprint() -> String {
    let host = std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .unwrap_or_default();
    format!(
        "{}|{}|{}",
        std::env::consts::OS,
        std::env::consts::ARCH,
        host.trim()
    )
}
fn random_bytes(count: usize) -> Result<Vec<u8>> {
    let mut bytes = vec![0u8; count];
    getrandom::fill(&mut bytes).map_err(|_| unavailable("安全随机数不可用。"))?;
    Ok(bytes)
}
fn sha256_hex(value: &str) -> String {
    hex(&Sha256::digest(value))
}
fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|byte| format!("{byte:02x}")).collect()
}
pub(super) fn normalize_machine_id(machine_id: &str) -> String {
    machine_id.trim().to_lowercase()
}
pub(super) fn normalize_license_key(license_key: &str) -> String {
    license_key.trim().trim_matches('"').to_string()
}
pub(super) fn url_decode(value: &str) -> std::result::Result<Vec<u8>, String> {
    BASE64_URL_SAFE_NO_PAD
        .decode(value.trim())
        .map_err(|_| "注册码的载荷或签名不是有效的 Base64URL 编码。".to_string())
}
fn parse_date(value: &str) -> Result<NaiveDate> {
    NaiveDate::parse_from_str(value, "%Y-%m-%d").map_err(|_| unavailable("授权锚点日期损坏。"))
}
fn ensure_anchor(
    store: &Store,
    protector: &crate::secrets::Protector,
    today: NaiveDate,
) -> Result<Anchor> {
    match store.settings(ANCHOR_NAME)? {
        Some(Value::String(ciphertext)) => {
            let plain = protector
                .unprotect(ANCHOR_NAME, &ciphertext)
                .map_err(unavailable)?;
            serde_json::from_str(&plain).map_err(|error| unavailable(error.to_string()))
        }
        Some(_) => Err(unavailable("授权锚点记录损坏。")),
        None => {
            let anchor = Anchor::new(today)?;
            write_anchor(store, protector, &anchor)?;
            Ok(anchor)
        }
    }
}
fn write_anchor(
    store: &Store,
    protector: &crate::secrets::Protector,
    anchor: &Anchor,
) -> Result<()> {
    let protected = protector
        .protect(ANCHOR_NAME, &serde_json::to_string(anchor)?)
        .map_err(unavailable)?;
    store.transaction(|tx| Ok(tx.set_settings(ANCHOR_NAME, 1, &Value::String(protected))?))
}

// --- Registration state as reported through the generated contract. ---
fn status(
    store: &Store,
    protector: &crate::secrets::Protector,
    clock: &BusinessClock,
    verifier: &dyn SignatureVerifier,
) -> Result<Value> {
    let today = clock.now().map_err(unavailable)?.today;
    let mut anchor = ensure_anchor(store, protector, today)?;
    let identity = machine_identity(&anchor, store.provider()?);
    let changed = anchor.refresh(&identity, today)?;
    let mut response = contracts::initial(contracts::schema("ApiLicenseStatusResponse"));
    response["machineId"] = json!(identity.machine_id);
    response["trialDays"] = json!(TRIAL_DAYS);
    response["licenseStoragePath"] = json!(ANCHOR_STORAGE);
    response["storagePolicy"] = json!(STORAGE_POLICY);
    let effective = anchor.effective(today)?;
    let terminal = if !anchor.fingerprint_hash.is_empty()
        && (anchor.fingerprint_hash != identity.fingerprint_hash
            || anchor.binding_hash != identity.binding_hash)
    {
        Some("设备指纹或本机密封信息变更，请重新注册。")
    } else if !anchor.license_key.is_empty() {
        match verifier.validate(&identity.machine_id, &anchor.license_key) {
            Ok(None) => {
                response["isRegistered"] = json!(true);
                response["daysRemaining"] = json!(i64::MAX);
                response["expireDate"] = json!(NaiveDate::MAX.to_string());
                response["message"] = json!("已注册 (终身授权)");
                None
            }
            Ok(Some(unix)) => {
                let expire = expire_date(clock, unix)?;
                if effective > expire {
                    response["isTrialExpired"] = json!(true);
                    response["expireDate"] = json!(expire.to_string());
                    Some("授权已过期，请重新注册。")
                } else {
                    response["isRegistered"] = json!(true);
                    response["daysRemaining"] =
                        json!(expire.signed_duration_since(effective).num_days().max(0) + 1);
                    response["expireDate"] = json!(expire.to_string());
                    response["message"] = json!(format!("已注册 (有效期至: {expire})"));
                    None
                }
            }
            Err(_) => Some("注册码无效或机器码已变更。"),
        }
    } else {
        None
    };
    if let Some(message) = terminal {
        response["isTrialExpired"] = json!(true);
        response["message"] = json!(message);
    } else if !response["isRegistered"].as_bool().unwrap_or(false) {
        let used = effective
            .signed_duration_since(anchor.install()?)
            .num_days();
        let remaining = TRIAL_DAYS - used;
        response["daysRemaining"] = json!(remaining.max(0));
        if used >= TRIAL_DAYS {
            response["isTrialExpired"] = json!(true);
            response["message"] = json!("试用期已过，请注册。");
        } else {
            response["message"] = json!(format!("试用期剩余 {} 天。", remaining.max(0)));
        }
    }
    if changed {
        write_anchor(store, protector, &anchor)?;
    }
    Ok(response)
}
fn expire_date(clock: &BusinessClock, unix: i64) -> Result<NaiveDate> {
    let instant = DateTime::from_timestamp(unix, 0).ok_or_else(|| invalid("注册码有效期无效。"))?;
    Ok(clock.at(instant).map_err(unavailable)?.today)
}
fn register(
    store: &Store,
    protector: &crate::secrets::Protector,
    clock: &BusinessClock,
    verifier: &dyn SignatureVerifier,
    actor: &Actor,
    key: &str,
) -> Result<Value> {
    let key = normalize_license_key(key);
    if key.is_empty() {
        return Err(invalid("注册码不能为空。"));
    }
    let today = clock.now().map_err(unavailable)?.today;
    let mut anchor = ensure_anchor(store, protector, today)?;
    let identity = machine_identity(&anchor, store.provider()?);
    let expire = verifier
        .validate(&identity.machine_id, &key)
        .map_err(invalid)?;
    let registered = match expire {
        None => NaiveDate::MAX,
        Some(unix) => expire_date(clock, unix)?,
    };
    let effective = anchor.effective(today)?;
    if registered < effective {
        return Err(invalid("注册码已过期，当前授权未更改。"));
    }
    anchor.license_key = key;
    anchor.fingerprint_hash = identity.fingerprint_hash.clone();
    anchor.binding_hash = identity.binding_hash.clone();
    anchor.last_run_date = effective.to_string();
    write_anchor(store, protector, &anchor)?;
    store.transaction(|tx| {
        tx.append_audit_details(
            &AuditWrite {
                kind: "license",
                record_id: 0,
                version: 1,
                action: "register",
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: "",
            },
            &json!({"newValues":{"isRegistered":true,"expireDate":registered.to_string()}}),
        )?;
        Ok(())
    })?;
    let mut response = contracts::initial(contracts::schema("ApiLicenseRegisterResponse"));
    response["success"] = json!(true);
    response["message"] = json!("注册成功。");
    response["status"] = status(store, protector, clock, verifier)?;
    Ok(response)
}

// --- Diagnostic support packages. ---
fn support_root(paths: &RuntimePaths) -> Result<PathBuf> {
    let root = paths.data_root.join("SupportPackages");
    ensure_safe_absolute(&root).map_err(invalid)?;
    fs::create_dir_all(&root)?;
    Ok(root)
}
fn entry(
    writer: &mut ZipWriter<std::io::Cursor<Vec<u8>>>,
    name: &str,
    value: &Value,
) -> Result<()> {
    writer
        .start_file(
            name,
            SimpleFileOptions::default().compression_method(CompressionMethod::Deflated),
        )
        .map_err(|error| unavailable(error.to_string()))?;
    writer
        .write_all(&serde_json::to_vec(value)?)
        .map_err(|error| unavailable(error.to_string()))?;
    Ok(())
}
fn support_package(
    store: &Store,
    paths: &RuntimePaths,
    clock: &BusinessClock,
) -> Result<(String, Vec<u8>)> {
    let mut writer = ZipWriter::new(std::io::Cursor::new(Vec::new()));
    entry(
        &mut writer,
        "diagnostics/runtime.json",
        &runtime_diagnostics(store)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/database.json",
        &audit_statistics(store)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/paths.json",
        &paths_diagnostics(paths)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/settings-redacted.json",
        &settings_summary(store)?,
    )?;
    entry(
        &mut writer,
        "diagnostics/logs-inventory.json",
        &log_inventory(paths)?,
    )?;
    let bytes = writer
        .finish()
        .map_err(|error| unavailable(error.to_string()))?
        .into_inner();
    let name = format!(
        "{}_{}_support_package.zip",
        Utc::now().format("%Y%m%d_%H%M%S"),
        &nonce().map_err(unavailable)?[..8]
    );
    let _ = clock;
    Ok((name, bytes))
}
fn runtime_diagnostics(store: &Store) -> Result<Value> {
    let provider = store.provider()?;
    Ok(json!({
        "productVersion": env!("CARGO_PKG_VERSION"),
        "informationalVersion": concat!(env!("CARGO_PKG_VERSION"), " Rust"),
        "databaseProvider": provider,
        "databaseProviderKey": if provider == "SQLite" { "sqlite" } else { "postgresql" },
        "operatingSystem": std::env::consts::OS,
        "architecture": std::env::consts::ARCH,
        "generatedAt": Utc::now().to_rfc3339(),
        "storagePolicy": SUPPORT_PACKAGE_POLICY
    }))
}
fn paths_diagnostics(paths: &RuntimePaths) -> Result<Value> {
    let item = |key: &str, label: &str, path: &Path| json!({"key": key, "label": label, "path": path, "exists": path.exists()});
    Ok(json!({"roots": [
        item("appRoot", "程序", &paths.app_root),
        item("dataRoot", "业务数据", &paths.data_root),
        item("logRoot", "运行日志", &paths.log_root),
        item("cacheRoot", "缓存", &paths.cache_root),
        item("fontPath", "报表字体", &paths.font_path)
    ], "storagePolicy": SUPPORT_PACKAGE_POLICY}))
}
fn settings_summary(store: &Store) -> Result<Value> {
    let settings = settings::current(store)?;
    let item = |path: &str| settings.pointer(path).cloned().unwrap_or(Value::Null);
    Ok(json!({
        "auditLogRetentionDays": item("/system/auditLogRetentionDays"),
        "logRetentionDays": item("/system/logRetentionDays"),
        "logRetainedFileCount": item("/system/logRetainedFileCount"),
        "logFileSizeLimitMB": item("/system/logFileSizeLimitMB"),
        "backupRetentionDays": item("/system/backupRetentionDays"),
        "databaseProvider": item("/system/databaseProvider"),
        "storagePolicy": SUPPORT_PACKAGE_POLICY
    }))
}
fn audit_statistics(store: &Store) -> Result<Value> {
    store.transaction(|tx| {
        let mut before = i64::MAX;
        let mut total = 0i64;
        let mut days: std::collections::BTreeMap<String, i64> = std::collections::BTreeMap::new();
        let mut actions: std::collections::BTreeMap<String, i64> =
            std::collections::BTreeMap::new();
        let mut oldest = String::new();
        for _ in 0..50 {
            crate::operation::check()?;
            let batch = tx.audit_events(before, 1000)?;
            if batch.is_empty() {
                break;
            }
            before = batch
                .iter()
                .filter_map(|event| event["id"].as_i64())
                .min()
                .unwrap_or(before);
            for event in &batch {
                total += 1;
                let timestamp = event["timestamp"].as_str().unwrap_or("");
                if timestamp.len() >= 10 {
                    days.entry(timestamp[..10].to_string())
                        .and_modify(|count| *count += 1)
                        .or_insert(1);
                }
                if oldest.as_str() < timestamp {
                    oldest = timestamp.to_string();
                }
                actions
                    .entry(event["action"].as_str().unwrap_or("").to_string())
                    .and_modify(|count| *count += 1)
                    .or_insert(1);
            }
        }
        let mut top: Vec<_> = actions
            .into_iter()
            .map(|(action, count)| json!({"action": action, "count": count}))
            .collect();
        top.sort_by(|left, right| {
            right["count"]
                .as_i64()
                .unwrap_or(0)
                .cmp(&left["count"].as_i64().unwrap_or(0))
        });
        top.truncate(5);
        Ok(json!({"scannedEvents": total, "oldestEvent": oldest, "byDay": days, "topActions": top}))
    })
}
fn log_inventory(paths: &RuntimePaths) -> Result<Value> {
    let mut files = vec![];
    for item in fs::read_dir(&paths.log_root)? {
        let item = item?;
        let metadata = item.metadata()?;
        if !metadata.is_file() {
            continue;
        }
        let modified = metadata
            .modified()
            .ok()
            .and_then(|time| time.duration_since(SystemTime::UNIX_EPOCH).ok())
            .map(|duration| duration.as_secs())
            .unwrap_or(0);
        files.push(json!({"fileName": item.file_name().to_string_lossy(), "sizeBytes": metadata.len(), "modifiedUnixSeconds": modified}));
    }
    files.sort_by(|left, right| {
        right["modifiedUnixSeconds"]
            .as_i64()
            .unwrap_or(0)
            .cmp(&left["modifiedUnixSeconds"].as_i64().unwrap_or(0))
    });
    Ok(json!({"logRoot": paths.log_root, "count": files.len(), "files": files}))
}
fn save_support_package_to_runtime(
    store: &Store,
    paths: &RuntimePaths,
    clock: &BusinessClock,
    actor: &Actor,
) -> Result<Value> {
    auth::authorize(actor, "system.settings", "manage")?;
    let (name, bytes) = support_package(store, paths, clock)?;
    let root = support_root(paths)?;
    let path = root.join(&name);
    ensure_safe_absolute(&path).map_err(invalid)?;
    atomic_write(&path, &bytes).map_err(unavailable)?;
    let mut response = contracts::initial(contracts::schema("ApiSupportPackageResponse"));
    response["success"] = json!(true);
    response["message"] = json!("支持包已保存到运行数据根 SupportPackages 目录。");
    response["fileName"] = json!(name);
    response["fullPath"] = json!(path);
    response["sizeBytes"] = json!(bytes.len());
    response["supportPackageRoot"] = json!(root);
    response["storagePolicy"] = json!(SUPPORT_PACKAGE_POLICY);
    Ok(response)
}

// --- System log cleanup. ---
fn cleanup_system_logs(store: &Store, paths: &RuntimePaths, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.audit", "manage")?;
    let settings = settings::current(store)?;
    let audit_days = settings
        .pointer("/system/auditLogRetentionDays")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let log_days = settings
        .pointer("/system/logRetentionDays")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let log_count = settings
        .pointer("/system/logRetainedFileCount")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let deleted_audit = if audit_days > 0 {
        store.transaction(|tx| {
            let cutoff = Utc::now() - chrono::Duration::days(audit_days);
            let mut before = i64::MAX;
            let mut ids = Vec::new();
            let mut scanned = 0i64;
            loop {
                crate::operation::check()?;
                let batch = tx.audit_events(before, 1000)?;
                if batch.is_empty() || scanned >= SCAN_LIMIT || ids.len() >= DELETE_LIMIT {
                    break;
                }
                before = batch
                    .iter()
                    .filter_map(|event| event["id"].as_i64())
                    .min()
                    .unwrap_or(before);
                scanned += batch.len() as i64;
                ids.extend(
                    batch
                        .iter()
                        .filter(|event| {
                            event_time(event).map(|time| time < cutoff).unwrap_or(false)
                        })
                        .filter_map(|event| event["id"].as_i64()),
                );
            }
            let deleted = tx.delete_audits(&ids)?;
            tx.append_audit_details(
                &AuditWrite {
                    kind: "system-maintenance",
                    record_id: 0,
                    version: 1,
                    action: "CleanupSystemLogs",
                    actor_id: actor.id,
                    occurred_at: &store::timestamp(),
                    note: "",
                },
                &json!({"newValues":{"deletedAuditLogs":deleted}}),
            )?;
            Ok(deleted)
        })?
    } else {
        0
    };
    let (by_age, by_count) = cleanup_text_logs(paths, log_days, log_count)?;
    let mut response = contracts::initial(contracts::schema("ApiSystemLogCleanupResponse"));
    response["success"] = json!(true);
    response["message"] = json!(format!(
        "日志清理已完成：审计日志 {deleted_audit} 条，文本日志 {} 个。",
        by_age + by_count
    ));
    response["deletedAuditLogs"] = json!(deleted_audit);
    response["deletedTextLogs"] = json!(by_age + by_count);
    response["deletedTextLogsByAge"] = json!(by_age);
    response["deletedTextLogsByCount"] = json!(by_count);
    response["logRoot"] = json!(paths.log_root);
    response["storagePolicy"] = json!(
        "手动日志清理只读取已保存的日志保留设置；审计日志清理仅操作数据库审计表，文本日志清理仅操作运行数据根 Logs 目录，不接收任意路径。"
    );
    Ok(response)
}
fn event_time(event: &Value) -> Result<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(event["timestamp"].as_str().unwrap_or(""))
        .map(|time| time.with_timezone(&Utc))
        .map_err(|_| unavailable("审计记录时间损坏，已停止处理。"))
}
/// Age-based deletion first; the retained-file-count cap then keeps the newest logs.
fn cleanup_text_logs(paths: &RuntimePaths, days: i64, retained: i64) -> Result<(i64, i64)> {
    if days <= 0 && retained <= 0 {
        return Ok((0, 0));
    }
    let cutoff: SystemTime = (Utc::now() - chrono::Duration::days(days.max(0))).into();
    let mut files: Vec<(PathBuf, SystemTime)> = fs::read_dir(&paths.log_root)?
        .filter_map(|item| item.ok())
        .filter_map(|item| {
            let metadata = item.metadata().ok()?;
            if !metadata.is_file() {
                return None;
            }
            Some((item.path(), metadata.modified().ok()?))
        })
        .collect();
    files.sort_by(|left, right| right.1.cmp(&left.1));
    let (mut by_age, mut by_count) = (0, 0);
    for (index, (path, modified)) in files.iter().enumerate() {
        let aged = days > 0 && *modified < cutoff;
        let surplus = retained > 0 && index as i64 >= retained;
        if aged || surplus {
            ensure_safe_absolute(path).map_err(invalid)?;
            fs::remove_file(path)?;
            if aged { by_age += 1 } else { by_count += 1 }
        }
    }
    Ok((by_age, by_count))
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    _parameters: &[(&str, String)],
    _query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let _gate = service
        .license_gate
        .lock()
        .map_err(|_| unavailable("授权状态异常。"))?;
    match operation {
        GET_LICENSE_STATUS => status(
            &service.store,
            &service.protector,
            &service.clock,
            &verifier()?,
        ),
        REGISTER_LICENSE => {
            auth::authorize(actor, "system.license", "manage")?;
            register(
                &service.store,
                &service.protector,
                &service.clock,
                &verifier()?,
                actor,
                &super::records::text(body, "licenseKey"),
            )
        }
        SAVE_SUPPORT_PACKAGE_TO_RUNTIME => {
            save_support_package_to_runtime(&service.store, &service.paths, &service.clock, actor)
        }
        CLEANUP_SYSTEM_LOGS => cleanup_system_logs(&service.store, &service.paths, actor),
        _ => Err(unsupported("此项原生操作尚未接入。")),
    }
}
pub(super) fn check_operation(service: &NativeService, operation: Operation) -> Result<()> {
    let required = contracts::contract()["operations"][operation.id]["policy"]["requiresLicense"]
        .as_bool()
        .ok_or_else(|| unavailable("端点缺少许可证策略。"))?;
    if !required {
        return Ok(());
    }
    let _gate = service
        .license_gate
        .lock()
        .map_err(|_| unavailable("授权状态异常。"))?;
    let state = status(
        &service.store,
        &service.protector,
        &service.clock,
        &verifier()?,
    )?;
    if state["isRegistered"] == true
        || (state["isTrialExpired"] == false
            && state["daysRemaining"].as_i64().is_some_and(|days| days > 0))
    {
        return Ok(());
    }
    Err(super::error::error(
        403,
        state["message"].as_str().unwrap_or("授权无效，请注册。"),
    ))
}
pub fn download(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    _parameters: &[(&str, String)],
) -> Result<tasks::FileOutput> {
    match operation {
        DOWNLOAD_SUPPORT_PACKAGE => {
            auth::authorize(actor, "system.settings", "manage")?;
            let (file_name, content) =
                support_package(&service.store, &service.paths, &service.clock)?;
            Ok(tasks::FileOutput {
                file_name,
                media_type: "application/zip".into(),
                content,
            })
        }
        _ => Err(unsupported("此项原生操作尚未接入。")),
    }
}
