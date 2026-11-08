//! Desktop export shares the durable, streaming file-publication boundary.
use super::*;
use crate::engine::records::text;

pub(in crate::engine) fn save_to_path(
    service: &NativeService,
    actor: &Actor,
    id: &str,
    body: &Value,
) -> Result<Value> {
    administrator(actor)?;
    if service.provider()? != "SQLite" {
        return Err(error(403, "网页或容器不能写客户端本机目录。"));
    }
    let target = PathBuf::from(text(body, "destinationPath"));
    crate::paths::ensure_safe_absolute(&target).map_err(invalid)?;
    if !target
        .file_name()
        .and_then(|name| name.to_str())
        .is_some_and(crate::paths::valid_file_name)
        || !target
            .extension()
            .is_some_and(|extension| extension.eq_ignore_ascii_case("edmrecovery"))
    {
        return Err(invalid("请选择有效的 .edmrecovery 保存文件。"));
    }
    let archive = repository::read(&service.paths, id)?;
    if archive.deleting {
        return Err(error(404, "此备份已清理。"));
    }
    let source = repository::root(&service.paths).join(&archive.file_name);
    let paths = service.paths.clone();
    let archive_id = archive.id.clone();
    service.jobs.start_backup(
        actor,
        START_RECOVERY_ARCHIVE_SAVE_TO_PATH_JOB,
        "RecoveryArchiveSave",
        "保存完整灾备",
        move |_| {
            let mut output =
                tasks::TaskOutput::managed_file(source, "完整灾备已保存到所选文件。".into())?;
            let file = output.managed_file.as_ref().expect("managed archive");
            if file.size != archive.size_bytes || file.digest != archive.sha256 {
                return Err(unavailable("备份文件已变化，未保存。"));
            }
            output.destination = Some(target);
            Ok(
                output.check_on_publish(tasks::PublicationCheck::new(move |_, actor| {
                    administrator(actor)?;
                    if repository::read(&paths, &archive_id)?.deleting {
                        return Err(error(404, "此备份已清理。"));
                    }
                    Ok(())
                })),
            )
        },
    )
}
