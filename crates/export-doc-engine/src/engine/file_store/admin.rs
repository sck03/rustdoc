//! Storage administration shares durable tasks and the application write gate.
use super::*;
use crate::{
    engine::{
        NativeService, auth,
        records::text,
        store::{Actor, Store},
        tasks::TaskOutput,
    },
    generated_api::*,
};
use serde_json::{Value, json};

pub(in crate::engine) const OPERATIONS: &[Operation] = &[
    GET_FILE_STORAGE,
    GET_FILE_STORAGE_POLICY,
    RECONCILE_FILE_STORAGE,
    UPDATE_FILE_STORAGE_POLICY,
    START_FILE_STORAGE_MIGRATION,
    VERIFY_FILE_STORAGE,
];

pub(in crate::engine) fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    let actor = service.store.read(|tx| auth::refresh_actor_in(tx, actor))?;
    auth::authorize_operation(&actor, operation, &[])?;
    if operation == GET_FILE_STORAGE_POLICY {
        return serde_json::to_value(service.store.files.configuration()?.policy)
            .map_err(Into::into);
    }
    if !actor.admin {
        return Err(super::super::error::error(403, "文件存储仅限管理员配置。"));
    }
    if operation == GET_FILE_STORAGE {
        return status(&service.store);
    }
    if operation == UPDATE_FILE_STORAGE_POLICY {
        let _maintenance = maintenance_lock(&service.store.files)?;
        if service
            .store
            .files
            .configuration_path
            .with_file_name("file-migration.json")
            .try_exists()?
        {
            return Err(conflict("请先完成或撤销未结束的文件迁移，再修改容量策略。"));
        }
        let policy: Policy = serde_json::from_value(body["policy"].clone())
            .map_err(|_| invalid("文件策略无效。"))?;
        policy.validate()?;
        service.store.with_writes_paused(|| {
            let current = service
                .store
                .read(|tx| auth::refresh_actor_in(tx, &actor))?;
            if !current.admin {
                return Err(super::super::error::error(403, "管理员权限已撤销。"));
            }
            let mut config = service.store.files.configuration()?;
            if body["expectedVersion"] != config.revision {
                return Err(conflict("存储设置已变化，请刷新。"));
            }
            config.policy = policy;
            config.revision += 1;
            service.store.files.activate(config)
        })?;
        return status(&service.store);
    }
    let store = service.store.clone();
    let body = body.clone();
    let administrator = actor.clone();
    let paths = service.paths.clone();
    service.jobs.start_backup(
        &actor,
        operation,
        if operation == RECONCILE_FILE_STORAGE {
            "FileReconcile"
        } else if operation == VERIFY_FILE_STORAGE {
            "FileIntegrity"
        } else {
            "FileMigration"
        },
        if operation == RECONCILE_FILE_STORAGE {
            "回收无引用文件"
        } else if operation == VERIFY_FILE_STORAGE {
            "检查文件完整性"
        } else {
            "迁移文件存储"
        },
        move |context| {
            let _lock = maintenance_lock(&store.files)?;
            let _backup = super::super::team_backup::package::lock(&paths)?;
            let mut count = 0u64;
            let mut bytes = 0u64;
            if operation == RECONCILE_FILE_STORAGE {
                bytes = store.with_writes_paused(|| {
                    let actor = store.read(|tx| auth::refresh_actor_in(tx, &administrator))?;
                    if !actor.admin {
                        return Err(super::super::error::error(403, "管理员权限已撤销。"));
                    }
                    if store
                        .files
                        .configuration_path
                        .with_file_name("file-migration.json")
                        .try_exists()?
                    {
                        return Err(conflict("请先完成未结束的文件迁移。"));
                    }
                    store.files.reconcile(&store)
                })?;
            } else if operation == VERIFY_FILE_STORAGE {
                scan(&store, |object| {
                    store.files.open_object(object)?;
                    count += 1;
                    bytes += object.size;
                    Ok(())
                })?;
            } else if body["cancel"] == true {
                store.with_writes_paused(|| {
                    let current = store.read(|tx| auth::refresh_actor_in(tx, &administrator))?;
                    if !current.admin {
                        return Err(super::super::error::error(403, "管理员权限已撤销。"));
                    }
                    let pending = store
                        .files
                        .configuration_path
                        .with_file_name("file-migration.json");
                    if pending.try_exists()? {
                        fs::remove_file(pending)?;
                    }
                    Ok(())
                })?;
            } else {
                let source = store.files.configuration()?;
                let pending = store
                    .files
                    .configuration_path
                    .with_file_name("file-migration.json");
                let target: Configuration = if body["resume"] == true {
                    let file =
                        fs::File::open(&pending).map_err(|_| invalid("没有可继续的文件迁移。"))?;
                    serde_json::from_reader(file.take(64 * 1024))
                        .map_err(|_| invalid("文件迁移记录损坏。"))?
                } else {
                    if pending.try_exists()? {
                        return Err(conflict("已有未完成迁移，请继续该任务。"));
                    }
                    if body["expectedVersion"] != source.revision {
                        return Err(conflict("存储设置已变化，请刷新。"));
                    }
                    let identity = paths::nonce().map_err(unavailable)?;
                    let backend = if body["kind"] == "s3" {
                        let settings = serde_json::from_value(body["s3"].clone())
                            .map_err(|_| invalid("S3 参数无效。"))?;
                        Backend::S3 {
                            settings,
                            secret: store
                                .files
                                .protector
                                .protect("file-storage-secret", &text(&body, "secret"))
                                .map_err(unavailable)?,
                            token: if text(&body, "token").is_empty() {
                                String::new()
                            } else {
                                store
                                    .files
                                    .protector
                                    .protect("file-storage-token", &text(&body, "token"))
                                    .map_err(unavailable)?
                            },
                        }
                    } else if body["kind"] == "local" {
                        Backend::Local {
                            directory: text(&body, "directory"),
                        }
                    } else {
                        return Err(invalid("请选择本地或 S3 存储。"));
                    };
                    let config = Configuration {
                        version: 1,
                        revision: source.revision + 1,
                        identity,
                        backend,
                        policy: source.policy.clone(),
                    };
                    match &config.backend {
                        Backend::Local { .. } => {
                            store.files.local_root(&config)?;
                        }
                        Backend::S3 { .. } => {
                            store.files.target(&config)?;
                        }
                    }
                    paths::atomic_write(&pending, &serde_json::to_vec(&config)?)
                        .map_err(unavailable)?;
                    config
                };
                if source.identity == target.identity {
                    // Activation is durable. A crash before removing the journal is
                    // completed here without copying from or overwriting the source.
                    if source.revision != target.revision {
                        return Err(conflict("迁移完成后配置已变化，请核对迁移记录。"));
                    }
                } else {
                    if target.revision != source.revision + 1 {
                        return Err(conflict("未完成迁移与当前存储版本不匹配。"));
                    }
                    store.files.initialize_target(&target)?;
                    copy_pending(&store, &target, false, &mut count, &mut bytes)?;
                    store.with_writes_paused(|| {
                        let current = store.files.configuration()?;
                        if current.identity != source.identity
                            || current.revision != source.revision
                        {
                            return Err(conflict("迁移期间存储配置已改变。"));
                        }
                        let refreshed =
                            store.read(|tx| auth::refresh_actor_in(tx, &administrator))?;
                        if !refreshed.admin {
                            return Err(super::super::error::error(403, "管理员权限已撤销。"));
                        }
                        copy_pending(&store, &target, true, &mut count, &mut bytes)?;
                        store.files.check_identity(&target)?;
                        scan(&store, |object| store.files.verify_at(&target, object))?;
                        store.files.activate(target)
                    })?;
                }
                fs::remove_file(pending)?;
            }
            let _ = context;
            Ok(TaskOutput {
                detail: if body["cancel"] == true {
                    "未完成迁移已撤销，当前存储与已复制文件保留。".into()
                } else {
                    format!("已核验 {count} 个文件对象，共 {bytes} 字节。")
                },
                file: None,
                destination: None,
                directory: None,
                managed_file: None,
                publication: None,
                archive: None,
            })
        },
    )
}
fn maintenance_lock(files: &FileStore) -> Result<fs::File> {
    let path = files
        .configuration_path
        .with_file_name("file-migration.lock");
    paths::ensure_safe_absolute(&path).map_err(unavailable)?;
    let lock = fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)?;
    fs2::FileExt::try_lock_exclusive(&lock)
        .map_err(|_| super::super::error::error(429, "文件维护正在进行。"))?;
    Ok(lock)
}
pub(super) fn copy_pending(
    store: &Store,
    target: &Configuration,
    paused: bool,
    count: &mut u64,
    bytes: &mut u64,
) -> Result<()> {
    loop {
        let batch = store.connection()?.pending_file_copies(&target.identity)?;
        if batch.is_empty() {
            return Ok(());
        }
        for object in batch {
            crate::operation::check()?;
            store.files.copy_object(target, &object)?;
            if paused {
                // The application write gate is already held; this independent
                // metadata write never holds a transaction during file I/O.
                store
                    .connection()?
                    .locate_file(&target.identity, &object.digest)?;
            } else {
                store.transaction(|tx| {
                    tx.locate_file(&target.identity, &object.digest)?;
                    Ok(())
                })?;
            }
            *count += 1;
            *bytes += object.size;
        }
    }
}
fn scan(store: &Store, mut visit: impl FnMut(&FileObject) -> Result<()>) -> Result<()> {
    let mut cursor = String::new();
    loop {
        let objects = store.connection()?.file_objects(&cursor, 200, false)?;
        if objects.is_empty() {
            break;
        }
        for object in objects {
            crate::operation::check()?;
            visit(&object)?;
            cursor = object.digest;
        }
    }
    Ok(())
}
fn status(store: &Store) -> Result<Value> {
    let config = store.files.configuration()?;
    let (kind, directory, s3) = match &config.backend {
        Backend::Local { directory } => (
            "local",
            directory.clone(),
            json!(export_doc_domain::backup_plan::S3Settings::default()),
        ),
        Backend::S3 { settings, .. } => ("s3", String::new(), json!(settings)),
    };
    let mut result = store.connection()?.file_statistics()?;
    let health = store.files.health();
    let status = json!({"versionNumber":config.revision,"kind":kind,"directory":directory,"registeredRoots":store.files.roots.keys().collect::<Vec<_>>(),"s3":s3,"policy":config.policy,"migrationPending":store.files.configuration_path.with_file_name("file-migration.json").try_exists()?,"healthy":health.is_ok(),"healthMessage":health.err().map(|e|e.message).unwrap_or_default()});
    result
        .as_object_mut()
        .unwrap()
        .extend(status.as_object().unwrap().clone());
    Ok(result)
}
impl FileStore {
    fn verify_at(&self, config: &Configuration, object: &FileObject) -> Result<()> {
        match &config.backend {
            Backend::Local { .. } => verify(
                &mut fs::File::open(self.object_path(config, object)?)
                    .map_err(|_| unavailable("迁移目标文件缺失。"))?,
                object,
            ),
            Backend::S3 { .. } => {
                let mut file =
                    paths::anonymous_file(&self.cache.join("FileStaging")).map_err(unavailable)?;
                self.target(config)?
                    .download(&object.digest, &mut file, object.size)?;
                file.seek(SeekFrom::Start(0))?;
                verify(&mut file, object)
            }
        }
    }
    pub(super) fn activate(&self, config: Configuration) -> Result<()> {
        config.policy.validate()?;
        let mut state = self
            .state
            .write()
            .map_err(|_| unavailable("文件存储状态异常。"))?;
        paths::atomic_write(&self.configuration_path, &serde_json::to_vec(&config)?)
            .map_err(unavailable)?;
        *state = config;
        Ok(())
    }
    pub(super) fn initialize_target(&self, config: &Configuration) -> Result<()> {
        match &config.backend {
            Backend::Local { .. } => {
                let root = self.local_root(config)?;
                if root.try_exists()? && fs::read_dir(&root)?.next().is_some() {
                    return self.local_identity(config);
                }
                crate::secrets::private_directory(&root).map_err(unavailable)?;
                paths::atomic_write(&root.join(".store-id"), config.identity.as_bytes())
                    .map_err(unavailable)?;
            }
            Backend::S3 { .. } => {
                let mut file =
                    paths::anonymous_file(&self.cache.join("FileStaging")).map_err(unavailable)?;
                file.write_all(config.identity.as_bytes())?;
                file.seek(SeekFrom::Start(0))?;
                self.target(config)?.upload_file(
                    &mut file,
                    "store-id",
                    config.identity.len() as u64,
                    &crate::engine::media::digest(config.identity.as_bytes()),
                )?;
            }
        }
        Ok(())
    }
    fn copy_object(&self, target: &Configuration, object: &FileObject) -> Result<()> {
        match &target.backend {
            Backend::Local { .. } => {
                self.local_identity(target)?;
                let path = self.object_path(target, object)?;
                if path.try_exists()? {
                    return verify(&mut fs::File::open(path)?, object);
                }
                let mut source = self.open_object(object)?;
                crate::secrets::private_directory(path.parent().unwrap()).map_err(unavailable)?;
                paths::atomic_write_with(&path, |output| {
                    super::super::backup_io::copy_and_hash(&mut source, output, object.size)
                        .map_err(|e| e.to_string())
                        .and_then(|actual| {
                            if actual == (object.size, object.digest.clone()) {
                                Ok(())
                            } else {
                                Err("迁移时源文件摘要改变。".into())
                            }
                        })
                })
                .map_err(unavailable)
            }
            Backend::S3 { .. } => {
                let mut source = self.open_object(object)?;
                let transport = self.target(target)?;
                transport.upload_file(&mut source, &object.digest, object.size, &object.digest)?;
                let mut verified =
                    paths::anonymous_file(&self.cache.join("FileStaging")).map_err(unavailable)?;
                transport.download(&object.digest, &mut verified, object.size)?;
                verified.seek(SeekFrom::Start(0))?;
                verify(&mut verified, object)
            }
        }
    }
}
