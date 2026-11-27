//! Archive transfers reuse immutable objects, the location catalog and the migration lock.
use super::*;
use crate::engine::{
    auth,
    store::{Actor, Store},
};
use serde_json::{Value, json};
impl FileStore {
    pub(super) fn restore_archived(
        &self,
        object: &FileObject,
        source: &mut fs::File,
    ) -> Result<()> {
        let config = self.object_configuration(object)?;
        self.check_identity(&config)?;
        match config.backend {
            Backend::Local { .. } => {
                let path = self.object_path(&config, object)?;
                crate::secrets::private_directory(path.parent().unwrap()).map_err(unavailable)?;
                paths::atomic_write_with(&path, |output| {
                    let result: Result<()> = (|| {
                        let actual = crate::engine::backup_io::copy_and_hash(
                            source,
                            &mut self
                                .space
                                .at(path.parent().unwrap().into())
                                .writer(output)?,
                            object.size,
                        )?;
                        if actual != (object.size, object.digest.clone()) {
                            return Err(unavailable("归档文件恢复摘要不符。"));
                        }
                        Ok(())
                    })();
                    result.map_err(|e| e.to_string())
                })
                .map_err(unavailable)
            }
            Backend::S3 { .. } => self.target(&config)?.upload_file(
                source,
                &object.digest,
                object.size,
                &object.digest,
            ),
        }
    }

    pub(in crate::engine) fn reject_repository_overlap(&self, target: &Path) -> Result<()> {
        let config = self.configuration()?;
        if matches!(config.backend, Backend::Local { .. })
            && paths::overlaps(&self.local_root(&config)?, target).map_err(invalid)?
        {
            return Err(invalid(
                "恢复仓库不能与生产文件库重叠，请选择独立的登记位置。",
            ));
        }
        for (id, location) in &config.archive_locations {
            if location.is_none() {
                continue;
            }
            let location = self.object_configuration(&FileObject {
                digest: "0".repeat(64),
                size: 1,
                archive_identity: id.clone(),
            })?;
            if paths::overlaps(&self.local_root(&location)?, target).map_err(invalid)? {
                return Err(invalid("恢复仓库不能与归档文件库重叠。"));
            }
        }
        Ok(())
    }

    pub(super) fn object_configuration(&self, object: &FileObject) -> Result<Configuration> {
        let mut config = self.configuration()?;
        if object.archive_identity.is_empty() {
            return Ok(config);
        }
        let location = config
            .archive_locations
            .get(&object.archive_identity)
            .ok_or_else(|| {
                unavailable("档案位置未登记，请恢复匹配的文件库配置；未回退到其它目录。")
            })?
            .clone();
        if let Some(location) = location {
            config.identity = object.archive_identity.clone();
            config.medium_identity = location.medium_identity;
            config.backend = Backend::Local {
                directory: location.directory,
            };
        }
        Ok(config)
    }
}
pub(super) fn run(store: &Store, actor: &Actor, body: &Value) -> Result<(u64, u64)> {
    let ids = body["ids"]
        .as_array()
        .ok_or_else(|| invalid("请选择需要归档的资料。"))?;
    if ids.is_empty() || ids.len() > 100 {
        return Err(invalid("一次选择 1–100 条归档资料。"));
    }
    let location = body["location"].as_str().unwrap_or("");
    let base = store
        .files
        .roots
        .get(location)
        .ok_or_else(|| invalid("请先在部署配置登记归档位置，再选择该位置编号。"))?;
    paths::ensure_safe_absolute(base).map_err(invalid)?;
    if !base.is_dir() {
        return Err(unavailable("归档位置未挂载。请连接原设备后再执行。"));
    }
    let mut config = store.files.configuration()?;
    if body["expectedVersion"] != config.revision {
        return Err(conflict("文件库配置已变化，请刷新后重新归档。"));
    }
    let directory = format!("@{location}/archive-{}", config.identity);
    let existing = config
        .archive_locations
        .iter()
        .find(|(_, v)| v.as_ref().is_some_and(|v| v.directory == directory))
        .map(|(k, _)| k.clone());
    let identity = existing.unwrap_or(paths::nonce().map_err(unavailable)?);
    if !config.archive_locations.contains_key(&identity) {
        if config.archive_locations.len() >= 32 {
            return Err(invalid("归档位置数量已达 32 个，请使用已登记的位置。"));
        }
        config.archive_locations.insert(
            identity.clone(),
            Some(ArchiveLocation {
                directory: directory.clone(),
                medium_identity: paths::volume_identity(base).map_err(unavailable)?,
            }),
        );
    }
    let archive = config.archive_locations[&identity].as_ref().unwrap();
    let mut target = config.clone();
    target.identity = identity.clone();
    target.backend = Backend::Local {
        directory: directory.clone(),
    };
    target.medium_identity = archive.medium_identity.clone();
    if let Backend::Local { .. } = config.backend {
        let source = store.files.local_root(&config)?;
        if crate::paths::overlaps(&source, base).map_err(invalid)? {
            return Err(invalid("归档位置不能与生产文件库重叠。"));
        }
    }
    store.files.initialize_target(&target)?;
    // Persist the registered destination before publishing any database route. Unused registrations are harmless.
    store.with_writes_paused(|| {
        let current = store.read(|tx| auth::refresh_actor_in(tx, actor))?;
        if !current.admin {
            return Err(crate::engine::error::error(403, "管理员权限已撤销。"));
        }
        config.revision += 1;
        store.files.activate(config.clone())
    })?;
    let mut seen = std::collections::BTreeSet::new();
    let mut count = 0;
    let mut bytes = 0;
    let result: Result<()> = (|| {
        for id in ids {
            crate::operation::check()?;
            let id = id.as_str().ok_or_else(|| invalid("归档编号无效。"))?;
            let row = store
                .read(|tx| tx.retained_file(id).map_err(Into::into))?
                .ok_or_else(|| conflict("档案引用已变化，请刷新。"))?;
            if row["activeReferences"].as_u64().unwrap_or(0) != 0 {
                return Err(invalid(
                    "该文件仍被业务记录使用，请保留在线位置；结束业务引用后再迁移档案。",
                ));
            }
            if row["basisDate"].as_str().unwrap_or("").is_empty() {
                return Err(invalid("请先确认所选资料的档案类别和归档日期。"));
            }
            let object = FileObject {
                digest: row["digest"].as_str().unwrap_or("").into(),
                size: row["size"].as_u64().unwrap_or(0),
                archive_identity: row["archiveIdentity"].as_str().unwrap_or("").into(),
            };
            if !seen.insert(object.digest.clone()) {
                continue;
            }
            let source = store.files.object_configuration(&object)?;
            store.files.copy_object(&target, &object)?;
            store.files.check_identity(&target)?;
            let _archive_write = store
                .files
                .archive_access
                .write(std::time::Duration::from_secs(30))?;
            let cleanup = if source.identity != target.identity {
                Some(store.files.begin_archive_cleanup(&source, &object)?)
            } else {
                None
            };
            store.transaction_as(actor, |tx, actor| {
                if !actor.admin {
                    return Err(crate::engine::error::error(403, "管理员权限已撤销。"));
                }
                if tx.retained_file(id)?.as_ref() != Some(&row) {
                    return Err(conflict("复制期间档案记录已变化，源文件保留，请重新归档。"));
                }
                tx.archive_file(&identity, &object.digest)?;
                tx.append_audit_details(
                    &export_doc_storage::AuditWrite {
                        kind: "file-archive",
                        record_id: row["recordId"].as_i64().unwrap_or(0),
                        version: config.revision as i64,
                        action: "archive",
                        actor_id: actor.id,
                        occurred_at: &crate::engine::store::timestamp(),
                        note: "",
                    },
                    &json!({"digest":object.digest,"location":location,"reason":body["reason"]}),
                )?;
                Ok(())
            })?;
            count += 1;
            bytes += object.size;
            // The journal predates the SQL switch and survives failed/unknown commits.
            if let Some(cleanup) = cleanup {
                store.files.finish_archive_cleanup(store, &cleanup.path)?;
            }
        }
        Ok(())
    })();
    if let Err(mut cause) = result {
        // Registration also persists a configuration revision even if no file moved.
        cause.partially_committed = true;
        let public = cause.public_body("");
        cause.message = format!(
            "归档已切换 {count} 个对象、{bytes} 字节；文件库配置已登记。后续阶段失败：{} 请刷新档案和文件库配置核对结果；待清理旧副本已保留记录，可在文件存储中执行回收完成清理。",
            public["message"].as_str().unwrap_or("请查询任务回执。")
        );
        cause.code = Some(crate::api::ErrorCode::PartialCommit);
        return Err(cause);
    }
    Ok((count, bytes))
}
