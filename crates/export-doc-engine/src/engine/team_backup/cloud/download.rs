//! A streamed, verified backup is published once; only its own scratch files are reclaimed.
use super::*;
use crate::paths::{self, RuntimePaths};
use std::{io::Write, path::PathBuf};

pub(super) fn download_backup(
    config: &Config,
    paths: &RuntimePaths,
    remote_name: &str,
) -> Result<u64> {
    let target =
        super::super::managed_file(&super::super::backup_root(paths)?, remote_name, "sqlite3")?;
    let temporary = super::super::cloud_staging_root(paths)?.join(format!(
        ".download-{}.tmp",
        paths::nonce().map_err(unavailable)?
    ));
    paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
    let writer = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)?;
    let result = (|| {
        let size = receive_file(config, remote_name, writer)?;
        let mut header = Vec::with_capacity(16);
        fs::File::open(&temporary)?
            .take(16)
            .read_to_end(&mut header)?;
        if header != b"SQLite format 3\0" {
            return Err(invalid("下载到的文件不是有效的 SQLite 备份。"));
        }
        export_doc_storage::verify_sqlite_backup(&temporary)?;
        paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
        paths::ensure_safe_absolute(&target).map_err(invalid)?;
        crate::operation::check()?;
        fs::rename(&temporary, &target)?;
        Ok(size)
    })();
    // Read-only SQLite validation can create WAL/SHM files for a WAL-mode snapshot.
    let mut cleanup_errors = Vec::new();
    for suffix in ["", "-wal", "-shm"] {
        let mut path = temporary.as_os_str().to_os_string();
        path.push(suffix);
        if let Err(cause) = fs::remove_file(PathBuf::from(path)) {
            if cause.kind() != std::io::ErrorKind::NotFound {
                cleanup_errors.push(cause.to_string());
            }
        }
    }
    if !cleanup_errors.is_empty() {
        return Err(unavailable(format!(
            "{}；下载暂存文件清理失败：{}",
            result
                .as_ref()
                .err()
                .map_or("下载已完成", |cause| cause.message.as_str()),
            cleanup_errors.join("；")
        )));
    }
    result
}

fn receive_file(config: &Config, file_name: &str, mut writer: fs::File) -> Result<u64> {
    let endpoint = parse_url(&config.url)?;
    let (name, value) = authorization(config);
    let remote = remote_path(&endpoint, file_name)?;
    let mut response = agent(TRANSFER_TIMEOUT)
        .get(&remote)
        .header(name.as_str(), value)
        .call()
        .map_err(|cause| match cause {
            ureq::Error::Timeout(_) => error(504, "WebDAV 下载超时。"),
            _ => unavailable(format!("WebDAV 下载失败:{cause}")),
        })?;
    let status = response.status().as_u16();
    let mut reader = response.body_mut().as_reader();
    if !(200..300).contains(&status) {
        let head = read_limited(&mut reader, ERROR_LIMIT, "WebDAV 错误响应超过容量上限。")?;
        return Err(failure(status, &head));
    }
    let mut buffer = [0u8; TRANSFER_BUFFER];
    let mut total = 0u64;
    loop {
        crate::operation::check()?;
        let length = reader
            .read(&mut buffer)
            .map_err(|_| unavailable("读取 WebDAV 下载流失败。"))?;
        if length == 0 {
            break;
        }
        total = total
            .checked_add(length as u64)
            .filter(|total| *total <= MAX_TRANSFER_BYTES)
            .ok_or_else(|| invalid("WebDAV 备份超过 4 GiB 下载上限。"))?;
        writer.write_all(&buffer[..length])?;
    }
    if total == 0 {
        return Err(unavailable("WebDAV 返回了空备份。"));
    }
    writer.sync_all()?;
    Ok(total)
}
