//! 持卡机灾备包：当前 SQLite 数据库快照与本机主密钥，使用包密码派生的
//! AES-256-GCM 密封。恢复采用暂存＋重启模式：校验解密后把快照与主密钥写入
//! 受管暂存区并登记待恢复标记，下一次启动时在建立数据库连接之前应用。
//! 所有写入都落在受管数据根目录内。
use super::{
    DISASTER_STORAGE_POLICY, NativeService, auth,
    error::{Result, error, invalid, unavailable},
    sealed,
    store::Actor,
    tasks::{FileOutput, TaskOutput},
};
use crate::{contracts, generated_api::*, paths::RuntimePaths};
use serde_json::{Value, json};
use std::{
    fs,
    path::{Path, PathBuf},
};

const MAGIC: &[u8] = b"EDM-DISASTER-RECOVERY-1";
const PACKAGE_SUFFIX: &str = ".edmrecovery";
const DATABASE_FILE_NAME: &str = "exportdoc-native.db";
const MASTER_KEY_FILE_NAME: &str = "native-master-key.bin";
const MANIFEST_ENTRY: &str = "manifest.json";
const DATABASE_ENTRY: &str = "Database/exportdoc-native.db";
const MASTER_KEY_ENTRY: &str = "Security/native-master-key.bin";
const CONFIRM_TEXT: &str = "RECOVER";
const SCHEMA_VERSION: u32 = 1;
const MASTER_KEY_ENVIRONMENT: &str = "EXPORTDOCMANAGER_MASTER_KEY";
const MASTER_KEY_BYTES: usize = 32;
const MAX_PACKAGE_BYTES: usize = 8 * 1024 * 1024 * 1024;

pub(super) fn root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::directory(paths.data_root.join("DisasterRecovery"))
}
fn marker_path(paths: &RuntimePaths) -> Result<PathBuf> {
    Ok(root(paths)?.join(".pending-restore.json"))
}
fn control_root(paths: &RuntimePaths) -> Result<PathBuf> {
    super::directory(root(paths)?.join(".restore-control"))
}
fn package_name() -> Result<String> {
    Ok(format!(
        "edm-disaster-recovery-{}{PACKAGE_SUFFIX}",
        chrono::Utc::now().format("%Y%m%d-%H%M%S")
    ))
}

struct Manifest {
    package_id: String,
    package_file_name: String,
    database_file_name: String,
    files: Vec<sealed::ManifestFile>,
}
impl Manifest {
    fn to_json(&self, created_at: &str) -> Value {
        json!({
            "schemaVersion": SCHEMA_VERSION,
            "packageId": self.package_id,
            "packageCreatedAtUtc": created_at,
            "packageFileName": self.package_file_name,
            "databaseFileName": self.database_file_name,
            "files": self.files.iter().map(sealed::ManifestFile::to_json).collect::<Vec<_>>()
        })
    }
    fn from_json(value: &Value) -> Result<Self> {
        let package_id = value["packageId"]
            .as_str()
            .map(str::to_string)
            .ok_or_else(|| invalid("恢复包清单缺少包标识。"))?;
        let package_file_name = value["packageFileName"]
            .as_str()
            .map(str::to_string)
            .ok_or_else(|| invalid("恢复包清单缺少包文件名。"))?;
        let database_file_name = value["databaseFileName"]
            .as_str()
            .map(str::to_string)
            .ok_or_else(|| invalid("恢复包清单缺少数据库文件名。"))?;
        let files = value["files"]
            .as_array()
            .ok_or_else(|| invalid("恢复包清单缺少文件列表。"))?
            .iter()
            .map(|item| {
                let name = item["relativePath"]
                    .as_str()
                    .ok_or_else(|| invalid("清单条目缺少相对路径。"))?;
                let size_bytes = item["sizeBytes"]
                    .as_u64()
                    .ok_or_else(|| invalid("清单条目缺少字节数。"))?;
                let sha256 = item["sha256"]
                    .as_str()
                    .ok_or_else(|| invalid("清单条目缺少摘要。"))?;
                if name.is_empty() || sha256.is_empty() {
                    return Err(invalid("清单条目不完整。"));
                }
                Ok(sealed::ManifestFile {
                    name: name.into(),
                    size_bytes,
                    sha256: sha256.into(),
                })
            })
            .collect::<Result<Vec<_>>>()?;
        Ok(Self {
            package_id,
            package_file_name,
            database_file_name,
            files,
        })
    }
}

pub(super) fn status(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    let uses_sqlite = service.provider()? == "SQLite";
    let pending = marker_path(&service.paths)?.is_file();
    let mut response = contracts::initial(contracts::schema("ApiDisasterRecoveryStatusResponse"));
    response["supported"] = json!(uses_sqlite);
    response["usesSqlite"] = json!(uses_sqlite);
    response["pendingRestore"] = json!(pending);
    response["recoveryRoot"] = json!(root(&service.paths)?);
    response["message"] = json!(if !uses_sqlite {
        "持卡机灾难恢复只适用于 SQLite 单机版；PostgreSQL 团队库请使用服务器迁移包。"
    } else if pending {
        "灾难恢复已排队，请立即重启桌面程序。"
    } else {
        "可创建独立加密恢复包，供本机损坏或更换持卡机时恢复。"
    });
    response["storagePolicy"] = json!(DISASTER_STORAGE_POLICY);
    Ok(response)
}

pub(super) fn create_package(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    if service.provider()? != "SQLite" {
        return Err(unavailable("持卡机灾难恢复包只在 SQLite 单机版可用。"));
    }
    let password = super::records_text(body, "password");
    super::validate_package_password(&password)?;
    if std::env::var_os(MASTER_KEY_ENVIRONMENT).is_some() {
        return Err(invalid(
            "当前通过环境变量提供本地主密钥，无法写入独立恢复包。请由部署管理员单独备份该环境密钥。",
        ));
    }
    if marker_path(&service.paths)?.is_file() {
        return Err(error(
            409,
            "灾难恢复任务已排队，完成重启恢复前不能创建新的恢复包。",
        ));
    }
    let database_path = service.paths.data_root.join(DATABASE_FILE_NAME);
    if !database_path.is_file() {
        return Err(error(
            404,
            "当前 SQLite 数据库不存在，无法创建持卡机灾难恢复包。",
        ));
    }
    let master_key_path = service
        .paths
        .data_root
        .join("Security")
        .join(MASTER_KEY_FILE_NAME);
    if !master_key_path.is_file() {
        return Err(error(
            404,
            "本地主密钥缺失，请先在桌面程序中正常使用一次凭证后重试。",
        ));
    }
    let store = service.store.clone();
    let paths = service.paths.clone();
    let actor_id = actor.id;
    service.jobs.start(
        actor,
        "DisasterRecoveryPackage",
        "创建持卡机灾难恢复包",
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, CREATE_DISASTER_RECOVERY_PACKAGE, &[])?;
            let master_key_path = paths
                .data_root
                .join("Security")
                .join(MASTER_KEY_FILE_NAME);
            let master_key = fs::read(&master_key_path)?;
            if master_key.len() != MASTER_KEY_BYTES {
                return Err(invalid("本地主密钥文件长度无效，无法创建恢复包。"));
            }
            let working = sealed::working_directory(&root(&paths)?)?;
            let result = (|| {
                if marker_path(&paths)?.is_file() {
                    return Err(error(409, "灾难恢复任务已排队，完成重启恢复前不能创建新的恢复包。"));
                }
                let snapshot = working.join("snapshot.sqlite3");
                crate::paths::ensure_safe_absolute(&snapshot).map_err(invalid)?;
                store.connection()?.backup(&snapshot)?;
                let created_at = chrono::Utc::now().to_rfc3339();
                let package_file_name = package_name()?;
                let manifest = Manifest {
                    package_id: crate::paths::nonce().map_err(unavailable)?,
                    package_file_name: package_file_name.clone(),
                    database_file_name: DATABASE_FILE_NAME.into(),
                    files: vec![
                        sealed::ManifestFile::from_path(DATABASE_ENTRY, &snapshot)?,
                        sealed::ManifestFile::from_path(MASTER_KEY_ENTRY, &master_key_path)?,
                    ],
                };
                let manifest_bytes = serde_json::to_vec_pretty(&manifest.to_json(&created_at))?;
                let manifest_path = working.join("manifest.json");
                crate::paths::ensure_safe_absolute(&manifest_path).map_err(invalid)?;
                fs::write(&manifest_path, &manifest_bytes)?;
                let entries = vec![
                    sealed::Entry { source: &manifest_path, name: MANIFEST_ENTRY },
                    sealed::Entry { source: &snapshot, name: DATABASE_ENTRY },
                    sealed::Entry { source: &master_key_path, name: MASTER_KEY_ENTRY },
                ];
                let payload = sealed::zip_payload(&entries)?;
                if payload.len() > MAX_PACKAGE_BYTES {
                    return Err(invalid("恢复包内容超过容量上限。"));
                }
                let sealed_bytes = sealed::seal(MAGIC, &password, &payload)?;
                let target = root(&paths)?.join(&package_file_name);
                sealed::atomic_write(&target, &sealed_bytes)?;
                super::audit(
                    &store,
                    &actor,
                    "create-disaster-recovery-package",
                    &format!("{package_file_name} 已写入受管灾备目录"),
                )?;
                Ok((package_file_name, target, sealed_bytes.len()))
            })();
            let _ = fs::remove_dir_all(&working);
            let (package_file_name, target, size) = result?;
            Ok(TaskOutput {
                file: Some(FileOutput {
                    file_name: package_file_name,
                    media_type: "application/octet-stream".into(),
                    content: Vec::new(),
                }),
                detail: format!(
                    "持卡机加密灾难恢复包已创建（{size} 字节）。请将包和密码分开保管，并至少复制一份到脱机介质。"
                ),
                destination: Some(target),
                directory: None,
            })
        },
    )
}

pub(super) fn restore_package(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.disaster-recovery", "manage")?;
    if service.provider()? != "SQLite" {
        return Err(unavailable("持卡机灾难恢复只在 SQLite 单机版可用。"));
    }
    let password = super::records_text(body, "password");
    let package_file_name = super::records_text(body, "packagePath");
    super::validate_package_password(&password)?;
    if super::records_text(body, "confirmationText") != CONFIRM_TEXT {
        return Err(invalid(format!(
            "安排灾难恢复前需要输入确认文本 {CONFIRM_TEXT}。"
        )));
    }
    if package_file_name.is_empty() {
        return Err(invalid("恢复包文件名不能为空。"));
    }
    if marker_path(&service.paths)?.is_file() {
        return Err(error(409, "已有持卡机灾难恢复任务等待重启执行。"));
    }
    // 提前校验包文件存在且在受管目录内，避免启动后台任务后才发现输入无效。
    let package_root = root(&service.paths)?;
    let package_path = super::managed_file(&package_root, &package_file_name, "edmrecovery")?;
    if !package_path.is_file() {
        return Err(error(
            404,
            "指定的恢复包不存在，请选择 DisasterRecovery 目录中的包。",
        ));
    }
    let store = service.store.clone();
    let paths = service.paths.clone();
    let actor_id = actor.id;
    service.jobs.start(
        actor,
        "DisasterRecoveryRestore",
        "校验并安排持卡机灾难恢复",
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, RESTORE_DISASTER_RECOVERY_PACKAGE, &[])?;
            let root = root(&paths)?;
            if marker_path(&paths)?.is_file() {
                return Err(error(409, "已有持卡机灾难恢复任务等待重启执行。"));
            }
            let working = sealed::working_directory(&root)?;
            let result = (|| {
                let sealed_bytes = fs::read(super::managed_file(&root, &package_file_name, "edmrecovery")?)?;
                let payload = sealed::open(MAGIC, &password, &sealed_bytes)?;
                let extracted = working.join("extracted");
                let entries = sealed::unpack(&payload, &extracted)?;
                let manifest = read_manifest(&entries)?;
                let staging = control_root(&paths)?.join(format!("pending-{}", manifest.package_id));
                if staging.exists() {
                    return Err(error(409, "同一恢复包已存在暂存数据，请先处理上一次恢复任务。"));
                }
                stage_package(&entries, &manifest, &staging)?;
                let marker = json!({
                    "schemaVersion": SCHEMA_VERSION,
                    "packageId": manifest.package_id,
                    "packageFileName": manifest.package_file_name,
                    "scheduledAtUtc": chrono::Utc::now().to_rfc3339(),
                    "stagingDirectoryName": format!("pending-{}", manifest.package_id),
                    "databaseFileName": manifest.database_file_name,
                });
                sealed::atomic_write(&marker_path(&paths)?, serde_json::to_vec(&marker)?.as_slice())?;
                super::audit(
                    &store,
                    &actor,
                    "restore-disaster-recovery",
                    &format!("{} 已校验并暂存，等待重启恢复", manifest.package_file_name),
                )?;
                Ok(staging)
            })();
            let _ = fs::remove_dir_all(&working);
            let staging = result?;
            Ok(TaskOutput {
                file: None,
                detail: "灾难恢复已安全排队。请立即退出并重新打开桌面程序；恢复会在建立数据库连接之前执行，完成后必须重新登录。".into(),
                destination: Some(staging),
                directory: None,
            })
        },
    )
}

fn read_manifest(entries: &[(String, Vec<u8>, String)]) -> Result<Manifest> {
    let manifest_bytes = entries
        .iter()
        .find(|(name, _, _)| name == MANIFEST_ENTRY)
        .map(|(_, bytes, _)| bytes.as_slice())
        .ok_or_else(|| invalid("恢复包缺少清单文件。"))?;
    let value: Value = serde_json::from_slice(manifest_bytes)?;
    Manifest::from_json(&value)
}

/// 把解密后的数据库快照与主密钥原子写入暂存区，并按清单校验。
fn stage_package(
    entries: &[(String, Vec<u8>, String)],
    manifest: &Manifest,
    staging: &Path,
) -> Result<()> {
    crate::paths::ensure_safe_absolute(staging).map_err(invalid)?;
    fs::create_dir_all(staging)?;
    sealed::verify_manifest(entries, &manifest.files)?;
    for (name, bytes, _) in entries {
        let target = match name.as_str() {
            DATABASE_ENTRY => staging.join(DATABASE_ENTRY),
            MASTER_KEY_ENTRY => staging.join(MASTER_KEY_ENTRY),
            MANIFEST_ENTRY => staging.join(MANIFEST_ENTRY),
            _ => continue,
        };
        if bytes.is_empty() {
            return Err(invalid("恢复包内含空文件，不能暂存。"));
        }
        sealed::atomic_write(&target, bytes)?;
    }
    if !staging.join(DATABASE_ENTRY).is_file() || !staging.join(MASTER_KEY_ENTRY).is_file() {
        return Err(invalid("恢复包缺少数据库快照或本机主密钥。"));
    }
    Ok(())
}

/// 启动时应用已排队的灾备恢复。必须在建立数据库连接之前调用：
/// 用暂存快照替换当前 SQLite 文件与本机主密钥，成功后才删除标记与暂存区。
pub fn apply_pending(paths: &RuntimePaths) -> Result<()> {
    let marker = marker_path(paths)?;
    if !marker.is_file() {
        return Ok(());
    }
    let control = control_root(paths)?;
    let value: Value = serde_json::from_str(&fs::read_to_string(&marker)?)
        .map_err(|cause| invalid(format!("灾备恢复标记无效：{cause}")))?;
    let staging_name = value["stagingDirectoryName"]
        .as_str()
        .ok_or_else(|| invalid("灾备恢复标记缺少暂存目录。"))?;
    let staging = control.join(staging_name);
    crate::paths::ensure_safe_absolute(&staging).map_err(invalid)?;
    if !staging.is_dir() {
        return Err(unavailable("灾备暂存目录缺失，不能执行恢复。"));
    }
    let database_target = paths.data_root.join(DATABASE_FILE_NAME);
    let master_key_target = paths.data_root.join("Security").join(MASTER_KEY_FILE_NAME);
    let database_source = staging.join(DATABASE_ENTRY);
    let master_key_source = staging.join(MASTER_KEY_ENTRY);
    if !database_source.is_file() || !master_key_source.is_file() {
        return Err(invalid("灾备暂存内容不完整，不能执行恢复。"));
    }
    staged_copy(&master_key_source, &master_key_target)?;
    staged_copy(&database_source, &database_target)?;
    let _ = fs::remove_dir_all(&staging);
    fs::remove_file(&marker)?;
    Ok(())
}

fn staged_copy(source: &Path, target: &Path) -> Result<()> {
    let bytes = fs::read(source)?;
    if bytes.is_empty() {
        return Err(invalid("灾备暂存文件为空，不能覆盖当前文件。"));
    }
    crate::paths::atomic_write(target, &bytes).map_err(unavailable)
}
