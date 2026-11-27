//! Explicit portable export; scheduled backups never create this full-size file.
use super::*;
use crate::engine::records::text;
pub(in crate::engine) fn save_to_path(
    service: &NativeService,
    actor: &Actor,
    id: &str,
    body: &Value,
) -> Result<Value> {
    administrator(actor)?;
    super::super::repository::valid_id(id)?;
    let destination = text(body, "destinationPath");
    let target = if destination.is_empty() {
        None
    } else {
        if service.provider()? != "SQLite" {
            return Err(error(403, "网页或容器不能写客户端本机目录。"));
        }
        let target = PathBuf::from(destination);
        crate::paths::ensure_safe_absolute(&target).map_err(invalid)?;
        if !target
            .extension()
            .is_some_and(|ext| ext.eq_ignore_ascii_case("edmpoint"))
        {
            return Err(invalid("请选择 .edmpoint 便携恢复点文件。"));
        }
        Some(target)
    };
    let (store, paths, protector, owner, id) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
        id.to_owned(),
    );
    let saved = load(&store)?;
    service.jobs.start_backup(
        actor,
        START_RECOVERY_ARCHIVE_SAVE_TO_PATH_JOB,
        "RecoveryPointExport",
        "导出便携恢复点",
        move |_| {
            administrator(&auth::current_actor(&store, owner)?)?;
            let _gate = package::lock(&paths)?;
            let password = protector
                .unprotect(PASSWORD, &saved.password)
                .map_err(unavailable)?;
            let repo = points::open(&store, &paths, &protector, &saved, &password, false)?;
            let directory = paths
                .data_root
                .join("Backups/PortableRecovery")
                .join(format!(
                    "export-{}",
                    crate::paths::nonce().map_err(unavailable)?
                ));
            package::private_directory(&directory)?;
            let path = directory.join(format!("{id}.edmpoint"));
            let result = (|| {
                let mut file = fs::OpenOptions::new()
                    .write(true)
                    .create_new(true)
                    .open(&path)?;
                repo.export(
                    &id,
                    &mut paths.disk_space.at(directory.clone()).writer(&mut file)?,
                )?;
                file.sync_all()?;
                let mut output = tasks::TaskOutput::managed_file(
                    path,
                    "便携恢复点已导出，请在任务结果中下载并保管恢复凭证。".into(),
                )?;
                output.destination = target;
                Ok(output)
            })();
            if result.is_err() {
                let _ = fs::remove_dir_all(&directory);
            }
            result
        },
    )
}
