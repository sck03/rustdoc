//! Explicit disposal of unpublished restore targets. Never touches an activated library.
use super::*;
use crate::engine::file_store::{Backend, Configuration};
use crate::paths;
use serde_json::{Value, json};
use std::io::Read;
fn record(paths: &RuntimePaths, id: &str) -> Result<(PathBuf, Value, Configuration)> {
    super::repository::valid_id(id)?;
    let path = paths.data_root.join("Recovery").join(format!("{id}.json"));
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    let value: Value = serde_json::from_reader(std::fs::File::open(&path)?.take(128 * 1024))?;
    let config: Configuration = serde_json::from_value(value["storage"].clone())?;
    if config.identity != id
        || !matches!(
            value["state"].as_str(),
            Some("ApplyingFiles" | "Ready" | "Cleaning" | "Completed")
        )
    {
        return Err(invalid("未发布恢复工件记录无效。"));
    }
    Ok((path, value, config))
}
fn active(paths: &RuntimePaths) -> Result<String> {
    let path = paths.data_root.join("Configuration/file-storage.json");
    if !path.try_exists()? {
        return Ok(String::new());
    }
    let config: Configuration =
        serde_json::from_reader(std::fs::File::open(path)?.take(128 * 1024))?;
    Ok(config.identity)
}
pub(super) fn complete_active(paths: &RuntimePaths) -> Result<()> {
    let id = active(paths)?;
    if id.is_empty() {
        return Ok(());
    }
    repository::valid_id(&id)?;
    let intent = paths.data_root.join("Recovery").join(format!("{id}.json"));
    paths::ensure_safe_absolute(&intent).map_err(invalid)?;
    if !intent.try_exists()? {
        return Ok(());
    }
    let (path, mut value, _) = record(paths, &id)?;
    value["state"] = json!("Completed");
    paths::atomic_write(&path, &serde_json::to_vec(&value)?).map_err(unavailable)
}
pub(super) fn list(paths: &RuntimePaths) -> Result<Value> {
    let active = active(paths)?;
    let mut rows = vec![];
    for entry in std::fs::read_dir(paths.data_root.join("Recovery"))? {
        let path = entry?.path();
        let Some(id) = path.file_stem().and_then(|v| v.to_str()) else {
            continue;
        };
        if repository::valid_id(id).is_err() || id == active {
            continue;
        }
        let (_, value, config) = record(paths, id)?;
        if value["reconnect"] == true || value["state"] == "Completed" {
            continue;
        }
        let location = match config.backend {
            Backend::Local { directory } => directory,
            Backend::S3 { settings, .. } => format!("S3:{}/{}", settings.bucket, config.identity),
        };
        rows.push(json!({"id":id,"location":location,"state":value["state"]}));
        if rows.len() == 100 {
            break;
        }
    }
    Ok(json!(rows))
}
pub(super) fn discard(paths: &RuntimePaths, body: &Value) -> Result<()> {
    let id = body["id"].as_str().unwrap_or("");
    if active(paths)? == id {
        return Err(invalid("该文件库已经启用，禁止按未发布工件清理。"));
    }
    let (path, mut value, config) = record(paths, id)?;
    if value["reconnect"] == true || value["state"] == "Completed" {
        return Err(invalid("原文件库或已启用的恢复库不是临时工件，禁止清理。"));
    }
    value["state"] = json!("Cleaning");
    paths::atomic_write(&path, &serde_json::to_vec(&value)?).map_err(unavailable)?;
    match config.backend {
        Backend::Local { directory } if directory.starts_with('@') => {
            let (location, relative) = directory[1..]
                .split_once('/')
                .ok_or_else(|| invalid("恢复目标位置无效。"))?;
            if relative != format!("recovered-{id}") {
                return Err(invalid("只能清理本次恢复建立的独立目标目录。"));
            }
            let base = paths
                .file_storage_roots
                .get(location)
                .ok_or_else(|| invalid("请先连接并登记原恢复目标。"))?;
            if paths::volume_identity(base).map_err(unavailable)? != config.medium_identity {
                return Err(invalid("恢复目标介质已改变，不能清理。"));
            }
            let root = base.join(relative);
            paths::ensure_safe_absolute(&root).map_err(invalid)?;
            if root.try_exists()? {
                let marker = root.join(".store-id");
                if !marker.try_exists()? {
                    if std::fs::read_dir(&root)?.next().is_some() {
                        return Err(invalid("恢复目标缺少身份标记，不能清理。"));
                    }
                } else {
                    if std::fs::read_to_string(&marker)? != id {
                        return Err(invalid("恢复目标身份不符，不能清理。"));
                    }
                    // Each deletion is limited to the known object namespace, with link checks at every level.
                    for entry in std::fs::read_dir(&root)? {
                        let p = entry?.path();
                        paths::ensure_safe_absolute(&p).map_err(invalid)?;
                        if p != marker && p != root.join("objects") {
                            return Err(invalid("目标含非恢复文件，已停止清理。"));
                        }
                    }
                    let objects = root.join("objects");
                    if objects.try_exists()? {
                        for shard in std::fs::read_dir(&objects)? {
                            crate::operation::check()?;
                            let shard = shard?.path();
                            paths::ensure_safe_absolute(&shard).map_err(invalid)?;
                            let prefix = shard
                                .file_name()
                                .and_then(|v| v.to_str())
                                .ok_or_else(|| invalid("恢复对象分区无效。"))?;
                            if prefix.len() != 2 || !prefix.bytes().all(|b| b.is_ascii_hexdigit()) {
                                return Err(invalid("恢复对象分区无效。"));
                            }
                            for entry in std::fs::read_dir(&shard)? {
                                crate::operation::check()?;
                                let p = entry?.path();
                                paths::ensure_safe_absolute(&p).map_err(invalid)?;
                                let name = p.file_name().and_then(|v| v.to_str()).unwrap_or("");
                                if name.len() != 64
                                    || !name.starts_with(prefix)
                                    || !name.bytes().all(|b| b.is_ascii_hexdigit())
                                    || !p.is_file()
                                {
                                    return Err(invalid("目标存在非恢复对象，已停止清理。"));
                                }
                                std::fs::remove_file(p)?;
                            }
                            std::fs::remove_dir(shard)?;
                        }
                        std::fs::remove_dir(objects)?;
                    }
                    std::fs::remove_file(marker)?;
                }
                std::fs::remove_dir(root)?;
            }
        }
        Backend::Local { directory } if directory == "Files" => {} // Local staging is owned and removed by PreparedRestore.
        Backend::Local { .. } => return Err(invalid("恢复工件目录不属于受控目标。")),
        Backend::S3 { settings, .. } => {
            #[cfg(feature = "s3-storage")]
            {
                let target = crate::engine::s3_transport::Target::new(
                    settings,
                    id,
                    body["secretAccessKey"].as_str().unwrap_or(""),
                    body["sessionToken"].as_str().unwrap_or(""),
                )?
                .with_journal(paths.data_root.join("Recovery/Uploads").join(id));
                let marker = target.read_small("store-id", 128)?;
                if marker.as_deref() != Some(id.as_bytes())
                    && !target.list_names("", "", 1)?.is_empty()
                {
                    return Err(invalid("S3 恢复目标身份不符，不能清理。"));
                }
                target.recover_uploads(|_| Ok(false))?;
                loop {
                    let names = target.list_names("", "", 100)?;
                    let mut removed = 0;
                    for name in names {
                        if name == "store-id" {
                            continue;
                        }
                        if name.len() != 64 || !name.bytes().all(|b| b.is_ascii_hexdigit()) {
                            return Err(invalid("S3 目标包含非恢复对象，已停止清理。"));
                        }
                        target.delete(&name)?;
                        removed += 1;
                    }
                    if removed == 0 {
                        break;
                    }
                }
                target.delete("store-id")?;
            }
            #[cfg(not(feature = "s3-storage"))]
            {
                let _ = settings;
                return Err(unsupported("此构建没有 S3 清理能力。"));
            }
        }
    }
    std::fs::remove_file(path)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::paths;
    #[test]
    fn disposal_requires_unpublished_identity_and_refuses_original_library() {
        let root = crate::engine::test_support::ScratchDirectory::new();
        let target = crate::engine::test_support::ScratchDirectory::new();
        let mut paths = root.paths();
        paths
            .register_file_storage_root("target", target.0.clone())
            .unwrap();
        let store = crate::engine::store::Store::open(&paths).unwrap();
        let mut config = store.files.configuration().unwrap();
        let id = crate::paths::nonce().unwrap();
        config.identity = id.clone();
        config.medium_identity = paths::volume_identity(&target.0).unwrap();
        config.backend = Backend::Local {
            directory: format!("@target/recovered-{id}"),
        };
        let folder = target.0.join(format!("recovered-{id}"));
        std::fs::create_dir_all(folder.join("objects/aa")).unwrap();
        std::fs::write(folder.join(".store-id"), &id).unwrap();
        std::fs::write(folder.join("objects/aa").join("a".repeat(64)), b"partial").unwrap();
        std::fs::create_dir_all(root.0.join("Recovery")).unwrap();
        let intent = root.0.join("Recovery").join(format!("{id}.json"));
        paths::atomic_write(
            &intent,
            &serde_json::to_vec(
                &json!({"state":"ApplyingFiles","reconnect":true,"storage":config}),
            )
            .unwrap(),
        )
        .unwrap();
        assert!(discard(&paths, &json!({"id":id})).is_err());
        assert!(folder.exists());
        paths::atomic_write(
            &intent,
            &serde_json::to_vec(
                &json!({"state":"ApplyingFiles","reconnect":false,"storage":config}),
            )
            .unwrap(),
        )
        .unwrap();
        std::fs::write(folder.join(".store-id"), "wrong").unwrap();
        assert!(discard(&paths, &json!({"id":id})).is_err());
        assert!(folder.exists());
        std::fs::write(folder.join(".store-id"), &id).unwrap();
        discard(&paths, &json!({"id":id})).unwrap();
        assert!(!folder.exists());
        assert!(!intent.exists());
        assert!(store.files.health().is_ok());
        // A formerly activated library remains protected after a later configuration change.
        let live_path = root.0.join("Configuration/file-storage.json");
        let previous = std::fs::read(&live_path).unwrap();
        paths::atomic_write(&live_path, &serde_json::to_vec(&config).unwrap()).unwrap();
        paths::atomic_write(
            &intent,
            &serde_json::to_vec(&json!({"state":"Ready","reconnect":false,"storage":config}))
                .unwrap(),
        )
        .unwrap();
        complete_active(&paths).unwrap();
        paths::atomic_write(&live_path, &previous).unwrap();
        assert_eq!(list(&paths).unwrap(), json!([]));
        assert!(discard(&paths, &json!({"id":id})).is_err());
        assert!(intent.exists());
    }
}
