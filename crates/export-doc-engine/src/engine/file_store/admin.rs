//! Storage administration shares durable tasks and the application write gate.
use super::*;
use crate::{
    engine::{
        NativeService, auth,
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
    UPDATE_FILE_STORAGE_CREDENTIALS,
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
    if operation == UPDATE_FILE_STORAGE_CREDENTIALS {
        super::credentials::rotate(&service.store, &actor, body)?;
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
                store.read(|tx| {
                    tx.verify_file_accounting()?;
                    Ok(())
                })?;
                scan(&store, |object| {
                    store.files.verify_object(object)?;
                    count += 1;
                    bytes += object.size;
                    Ok(())
                })?;
            } else {
                (count, bytes) = super::migration::run(&store, &administrator, &body)?;
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
pub(super) fn maintenance_lock(files: &FileStore) -> Result<fs::File> {
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
    result["credentialVersion"] = json!(config.credential_revision);
    let health = store.files.health();
    let status = json!({"versionNumber":config.revision,"kind":kind,"directory":directory,"registeredRoots":store.files.roots.keys().collect::<Vec<_>>(),"s3":s3,"policy":config.policy,"migrationPending":store.files.configuration_path.with_file_name("file-migration.json").try_exists()?,"healthy":health.is_ok(),"healthMessage":health.err().map(|e|e.message).unwrap_or_default()});
    result
        .as_object_mut()
        .unwrap()
        .extend(status.as_object().unwrap().clone());
    Ok(result)
}
impl FileStore {
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
}
