//! Durable migration planning, verified copies and bounded final activation.
use super::*;
use crate::engine::{
    auth,
    records::text,
    store::{Actor, Store},
};
use serde_json::Value;

pub(super) fn run(store: &Store, administrator: &Actor, body: &Value) -> Result<(u64, u64)> {
    let (mut count, mut bytes) = (0, 0);
    if body["cancel"] == true {
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
                let target: Configuration =
                    serde_json::from_reader(fs::File::open(&pending)?.take(64 * 1024))
                        .map_err(|_| invalid("文件迁移记录损坏。"))?;
                store.connection()?.end_file_migration(&target.identity)?;
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
            let file = fs::File::open(&pending).map_err(|_| invalid("没有可继续的文件迁移。"))?;
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
                credential_revision: 1,
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
            paths::atomic_write(&pending, &serde_json::to_vec(&config)?).map_err(unavailable)?;
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
            store.transaction(|tx| {
                tx.begin_file_migration(&target.identity)?;
                Ok(())
            })?;
            loop {
                copy_pending(&store, &target, &mut count, &mut bytes)?;
                store.files.check_identity(&target)?;
                let activated = store.with_writes_paused(|| {
                    let current = store.files.configuration()?;
                    if current.identity != source.identity || current.revision != source.revision {
                        return Err(conflict("迁移期间存储配置已改变。"));
                    }
                    let refreshed = store.read(|tx| auth::refresh_actor_in(tx, &administrator))?;
                    if !refreshed.admin {
                        return Err(super::super::error::error(403, "管理员权限已撤销。"));
                    }
                    if !store
                        .connection()?
                        .pending_file_copies(&target.identity)?
                        .is_empty()
                    {
                        return Ok(false);
                    }
                    store.files.activate(target.clone())?;
                    Ok(true)
                })?;
                if activated {
                    break;
                }
            }
        }
        store.transaction(|tx| {
            tx.end_file_migration(&target.identity)?;
            Ok(())
        })?;
        fs::remove_file(pending)?;
    }
    Ok((count, bytes))
}
pub(super) fn copy_pending(
    store: &Store,
    target: &Configuration,
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
            store.transaction(|tx| {
                tx.locate_file(&target.identity, &object.digest)?;
                Ok(())
            })?;
            *count += 1;
            *bytes += object.size;
        }
    }
}
impl FileStore {
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
                    let mut output = self
                        .space
                        .at(path.parent().unwrap().to_path_buf())
                        .writer(output)
                        .map_err(|e| e.to_string())?;
                    super::super::backup_io::copy_and_hash(&mut source, &mut output, object.size)
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
                transport.upload_file(&mut source, &object.digest, object.size, &object.digest)
            }
        }
    }
}
