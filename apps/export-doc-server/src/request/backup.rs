//! Authorized backup requests stream to an anonymous file in the managed cache.
use super::error;
use axum::body::{Body, HttpBody};
use export_doc_engine::api::ApiError;
use std::{fs, future::poll_fn, path::Path, pin::Pin, sync::Arc};
use tokio::io::{AsyncSeekExt, AsyncWriteExt};
const WRITE_BATCH: usize = 4 * 1024 * 1024;

async fn write_batch(
    file: &mut tokio::fs::File,
    mut reservation: crate::temporary_space::Reservation,
    bytes: &[u8],
) -> Result<crate::temporary_space::Reservation, ApiError> {
    let length = bytes.len() as u64;
    reservation = tokio::task::spawn_blocking(move || {
        reservation.ensure(length)?;
        Ok::<_, ApiError>(reservation)
    })
    .await
    .map_err(|_| error(503, "上传空间检查中断。"))??;
    tokio::time::timeout(std::time::Duration::from_secs(60), async {
        file.write_all(bytes).await?;
        file.flush().await
    })
    .await
    .map_err(|_| error(504, "备份上传写入空闲超过 60 秒。"))?
    .map_err(|_| error(503, "上传暂存空间不可用。"))?;
    reservation.committed(length);
    Ok(reservation)
}

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

pub(super) async fn receive(
    mut body: Body,
    root: &Path,
    budget: Arc<crate::temporary_space::Budget>,
) -> Result<fs::File, ApiError> {
    let root = root.to_owned();
    let expected = body.size_hint().exact().unwrap_or(0);
    let (file, mut reservation) = tokio::task::spawn_blocking(move || {
        let file = temporary(&root)?;
        Ok::<_, ApiError>((file, budget.reserve(expected)?))
    })
    .await
    .map_err(|_| error(503, "上传暂存任务中断。"))??;
    let mut file = tokio::fs::File::from_std(file);
    let mut size = 0_u64;
    let mut batch = Vec::with_capacity(expected.min(WRITE_BATCH as u64) as usize);
    while let Some(frame) = tokio::time::timeout(
        std::time::Duration::from_secs(60),
        poll_fn(|context| Pin::new(&mut body).poll_frame(context)),
    )
    .await
    .map_err(|_| error(504, "备份上传读取空闲超过 60 秒。"))?
    {
        let frame = frame.map_err(|_| error(400, "上传内容读取失败。"))?;
        if let Ok(bytes) = frame.into_data() {
            size = size
                .checked_add(bytes.len() as u64)
                .ok_or_else(|| error(413, "备份文件大小无效或超出可表示范围。"))?;
            let mut remaining = bytes.as_ref();
            while !remaining.is_empty() {
                let count = remaining.len().min(WRITE_BATCH - batch.len());
                batch.extend_from_slice(&remaining[..count]);
                remaining = &remaining[count..];
                if batch.len() == WRITE_BATCH {
                    reservation = write_batch(&mut file, reservation, &batch).await?;
                    batch.clear();
                }
            }
        }
    }
    if size == 0 {
        return Err(error(400, "请选择非空备份文件。"));
    }
    if !batch.is_empty() {
        write_batch(&mut file, reservation, &batch).await?;
    }
    file.seek(std::io::SeekFrom::Start(0))
        .await
        .map_err(|_| error(503, "无法读取上传暂存文件。"))?;
    Ok(file.into_std().await)
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::body::Bytes;
    use hyper::body::Frame;
    use std::{
        collections::VecDeque,
        io::Read,
        task::{Context, Poll},
    };

    struct Frames(VecDeque<Result<Frame<Bytes>, std::io::Error>>);
    impl HttpBody for Frames {
        type Data = Bytes;
        type Error = std::io::Error;
        fn poll_frame(
            mut self: Pin<&mut Self>,
            _: &mut Context<'_>,
        ) -> Poll<Option<Result<Frame<Bytes>, Self::Error>>> {
            Poll::Ready(self.0.pop_front())
        }
    }

    #[tokio::test]
    async fn known_and_chunked_uploads_preserve_bytes_and_cleanup_on_error() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../.codex-runtime/backup-upload-tests")
            .join(export_doc_engine::paths::nonce().unwrap());
        fs::create_dir_all(&root).unwrap();
        let root = root.canonicalize().unwrap();
        let budget = Arc::new(crate::temporary_space::Budget::new(root.clone()));
        let bytes: Vec<u8> = (0..WRITE_BATCH + 713).map(|n| (n % 251) as u8).collect();
        for known in [true, false] {
            let body = if known {
                Body::from(bytes.clone())
            } else {
                Body::new(Frames(
                    bytes
                        .chunks(17_003)
                        .map(|chunk| Ok(Frame::data(Bytes::copy_from_slice(chunk))))
                        .collect(),
                ))
            };
            let mut file = receive(body, &root, budget.clone()).await.unwrap();
            let mut actual = vec![];
            file.read_to_end(&mut actual).unwrap();
            assert_eq!(actual, bytes);
            drop(file);
            assert_eq!(fs::read_dir(root.join("BackupUploads")).unwrap().count(), 0);
        }
        let body = Body::new(Frames(VecDeque::from([
            Ok(Frame::data(Bytes::from(bytes))),
            Err(std::io::Error::other("interrupted upload")),
        ])));
        assert!(receive(body, &root, budget).await.is_err());
        assert_eq!(fs::read_dir(root.join("BackupUploads")).unwrap().count(), 0);
        fs::remove_dir_all(root).unwrap();
    }
}
