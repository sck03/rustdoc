//! Bounded receivers for authorized uploads; storage reservations track disk writes.
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
    .map_err(|_| error(504, "上传写入空闲超过 60 秒。"))?
    .map_err(|_| error(503, "上传暂存空间不可用。"))?;
    reservation.committed(length);
    Ok(reservation)
}

fn temporary(root: &Path) -> Result<fs::File, ApiError> {
    export_doc_engine::paths::anonymous_file(&root.join("Uploads"))
        .map_err(|_| error(503, "无法创建受管上传暂存文件。"))
}

pub(super) async fn receive(
    mut body: Body,
    root: &Path,
    budget: Arc<crate::temporary_space::Budget>,
    limit: u64,
) -> Result<fs::File, ApiError> {
    let expected = body.size_hint().exact().unwrap_or(0);
    let mut sink = Sink::new(root, budget, expected, limit).await?;
    while let Some(frame) = tokio::time::timeout(
        std::time::Duration::from_secs(60),
        poll_fn(|context| Pin::new(&mut body).poll_frame(context)),
    )
    .await
    .map_err(|_| error(504, "上传读取空闲超过 60 秒。"))?
    {
        let frame = frame.map_err(|_| error(400, "上传内容读取失败。"))?;
        if let Ok(bytes) = frame.into_data() {
            sink.push(&bytes).await?;
        }
    }
    sink.finish().await
}

pub(super) async fn receive_field(
    mut field: axum::extract::multipart::Field<'_>,
    root: &Path,
    budget: Arc<crate::temporary_space::Budget>,
    limit: u64,
) -> Result<fs::File, ApiError> {
    let mut sink = Sink::new(root, budget, 0, limit).await?;
    while let Some(bytes) = tokio::time::timeout(std::time::Duration::from_secs(60), field.chunk())
        .await
        .map_err(|_| error(504, "上传读取空闲超过 60 秒。"))?
        .map_err(super::body::multipart_error)?
    {
        sink.push(&bytes).await?;
    }
    sink.finish().await
}

struct Sink {
    file: tokio::fs::File,
    reservation: Option<crate::temporary_space::Reservation>,
    batch: Vec<u8>,
    size: u64,
    limit: u64,
}
impl Sink {
    async fn new(
        root: &Path,
        budget: Arc<crate::temporary_space::Budget>,
        expected: u64,
        limit: u64,
    ) -> Result<Self, ApiError> {
        if expected > limit {
            return Err(error(413, "上传超过该操作的容量上限。"));
        }
        let root = root.to_owned();
        let (file, reservation) = tokio::task::spawn_blocking(move || {
            Ok::<_, ApiError>((temporary(&root)?, budget.reserve(expected)?))
        })
        .await
        .map_err(|_| error(503, "上传暂存任务中断。"))??;
        Ok(Self {
            file: tokio::fs::File::from_std(file),
            reservation: Some(reservation),
            batch: Vec::with_capacity(WRITE_BATCH),
            size: 0,
            limit,
        })
    }
    async fn push(&mut self, mut bytes: &[u8]) -> Result<(), ApiError> {
        self.size = self
            .size
            .checked_add(bytes.len() as u64)
            .filter(|n| *n <= self.limit)
            .ok_or_else(|| error(413, "上传超过该操作的容量上限。"))?;
        while !bytes.is_empty() {
            let count = bytes.len().min(WRITE_BATCH - self.batch.len());
            self.batch.extend_from_slice(&bytes[..count]);
            bytes = &bytes[count..];
            if self.batch.len() == WRITE_BATCH {
                self.flush().await?;
            }
        }
        Ok(())
    }
    async fn flush(&mut self) -> Result<(), ApiError> {
        if !self.batch.is_empty() {
            self.reservation = Some(
                write_batch(
                    &mut self.file,
                    self.reservation.take().expect("upload reservation"),
                    &self.batch,
                )
                .await?,
            );
            self.batch.clear();
        }
        Ok(())
    }
    async fn finish(mut self) -> Result<fs::File, ApiError> {
        if self.size == 0 {
            return Err(error(400, "请选择非空文件。"));
        }
        self.flush().await?;
        self.file
            .seek(std::io::SeekFrom::Start(0))
            .await
            .map_err(|_| error(503, "无法读取上传暂存文件。"))?;
        Ok(self.file.into_std().await)
    }
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
            let mut file = receive(body, &root, budget.clone(), bytes.len() as u64)
                .await
                .unwrap();
            let mut actual = vec![];
            file.read_to_end(&mut actual).unwrap();
            assert_eq!(actual, bytes);
            drop(file);
            assert_eq!(fs::read_dir(root.join("Uploads")).unwrap().count(), 0);
        }
        let body = Body::new(Frames(VecDeque::from([
            Ok(Frame::data(Bytes::from(bytes))),
            Err(std::io::Error::other("interrupted upload")),
        ])));
        assert!(
            receive(body, &root, budget.clone(), u64::MAX)
                .await
                .is_err()
        );
        assert!(
            receive(Body::from("too large"), &root, budget, 4)
                .await
                .is_err()
        );
        assert_eq!(fs::read_dir(root.join("Uploads")).unwrap().count(), 0);
        fs::remove_dir_all(root).unwrap();
    }
}
