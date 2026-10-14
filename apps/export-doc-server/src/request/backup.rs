//! Authorized backup requests stream to an anonymous file in the managed cache.
use super::error;
use axum::body::{Body, HttpBody};
use export_doc_engine::{api::ApiError, engine::backup_io::MAX_FILE_BYTES};
use std::{fs, future::poll_fn, path::Path, pin::Pin};
use tokio::io::{AsyncSeekExt, AsyncWriteExt};

fn temporary(root: &Path) -> Result<fs::File, ApiError> {
    let directory = root.join("BackupUploads");
    export_doc_engine::secrets::private_directory(&directory)
        .map_err(|_| error(503, "无法准备受管上传目录。"))?;
    let path = directory.join(format!(
        "{}.upload",
        export_doc_engine::paths::nonce().map_err(|_| error(503, "无法生成上传标识。"))?
    ));
    let mut options = fs::OpenOptions::new();
    options.create_new(true).read(true).write(true);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        // Last-handle-close cleanup also covers canceled outstanding async writes.
        const DELETE_ON_CLOSE: u32 = 0x04000000;
        options.custom_flags(DELETE_ON_CLOSE);
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let file = options
        .open(&path)
        .map_err(|_| error(503, "无法创建上传暂存文件。"))?;
    #[cfg(unix)]
    fs::remove_file(path).map_err(|_| error(503, "无法保护上传暂存文件。"))?;
    Ok(file)
}

pub(super) async fn receive(mut body: Body, root: &Path) -> Result<fs::File, ApiError> {
    let root = root.to_owned();
    let file = tokio::task::spawn_blocking(move || temporary(&root))
        .await
        .map_err(|_| error(503, "上传暂存任务中断。"))??;
    let mut file = tokio::fs::File::from_std(file);
    let mut size = 0_u64;
    while let Some(frame) = poll_fn(|context| Pin::new(&mut body).poll_frame(context)).await {
        let frame = frame.map_err(|_| error(400, "上传内容读取失败。"))?;
        if let Ok(bytes) = frame.into_data() {
            size = size
                .checked_add(bytes.len() as u64)
                .filter(|size| *size <= MAX_FILE_BYTES)
                .ok_or_else(|| error(413, "备份文件大小无效或超出可表示范围。"))?;
            file.write_all(&bytes)
                .await
                .map_err(|_| error(503, "上传暂存空间不可用。"))?;
        }
    }
    if size == 0 {
        return Err(error(400, "请选择非空备份文件。"));
    }
    file.flush()
        .await
        .map_err(|_| error(503, "上传暂存写入失败。"))?;
    file.seek(std::io::SeekFrom::Start(0))
        .await
        .map_err(|_| error(503, "无法读取上传暂存文件。"))?;
    Ok(file.into_std().await)
}
