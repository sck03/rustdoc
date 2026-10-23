//! A bounded pipe keeps slow clients out of the request executor and bounds the
//! lifetime of the original file, buffer and download permit independently.
use axum::body::Body;
use std::{
    pin::Pin,
    sync::Arc,
    task::{Context, Poll},
    time::Duration,
};
use tokio::{
    io::{AsyncRead, AsyncReadExt, AsyncWriteExt},
    sync::{OwnedSemaphorePermit, oneshot},
};
struct Reader {
    inner: tokio::io::DuplexStream,
    remaining: u64,
    finished: Option<oneshot::Sender<()>>,
}
impl AsyncRead for Reader {
    fn poll_read(
        mut self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buffer: &mut tokio::io::ReadBuf<'_>,
    ) -> Poll<std::io::Result<()>> {
        let before = buffer.filled().len();
        let result = Pin::new(&mut self.inner).poll_read(cx, buffer);
        if matches!(result, Poll::Ready(Ok(()))) {
            let read = buffer.filled().len() - before;
            if read == 0 && self.remaining > 0 && buffer.remaining() > 0 {
                return Poll::Ready(Err(std::io::Error::new(
                    std::io::ErrorKind::UnexpectedEof,
                    "download interrupted",
                )));
            }
            self.remaining = self.remaining.saturating_sub(read as u64);
            if self.remaining == 0 {
                if let Some(sender) = self.finished.take() {
                    let _ = sender.send(());
                }
            }
        }
        result
    }
}

pub(crate) fn body(
    source: Box<dyn AsyncRead + Unpin + Send>,
    size: u64,
    permit: OwnedSemaphorePermit,
    telemetry: Arc<crate::telemetry::Telemetry>,
    request_id: String,
) -> Body {
    transfer(
        source,
        size,
        permit,
        telemetry,
        Duration::from_secs(60),
        Duration::from_secs(3600),
        request_id,
    )
}
fn transfer(
    mut source: Box<dyn AsyncRead + Unpin + Send>,
    size: u64,
    permit: OwnedSemaphorePermit,
    telemetry: Arc<crate::telemetry::Telemetry>,
    idle: Duration,
    total: Duration,
    request_id: String,
) -> Body {
    let (inner, mut writer) = tokio::io::duplex(64 * 1024);
    let (finished, completion) = oneshot::channel();
    let reader = Reader {
        inner,
        remaining: size,
        finished: Some(finished),
    };
    tokio::spawn(async move {
        let _permit = permit;
        let started = std::time::Instant::now();
        let observation = telemetry.download_start();
        let copy = async {
            let mut remaining = size;
            let mut buffer = vec![0; 64 * 1024];
            while remaining > 0 {
                let length = buffer.len().min(remaining as usize);
                let read = tokio::time::timeout(idle, source.read(&mut buffer[..length]))
                    .await
                    .map_err(|_| "idle-timeout")?
                    .map_err(|_| "source-read")?;
                if read == 0 {
                    return Err("source-truncated");
                }
                tokio::time::timeout(idle, writer.write_all(&buffer[..read]))
                    .await
                    .map_err(|_| "idle-timeout")?
                    .map_err(|_| "client-disconnected")?;
                remaining -= read as u64;
            }
            drop(source);
            writer.shutdown().await.map_err(|_| "client-disconnected")?;
            if size == 0 {
                return Ok(());
            }
            tokio::time::timeout(idle, completion)
                .await
                .map_err(|_| "idle-timeout")?
                .map_err(|_| "client-disconnected")
        };
        let result = tokio::time::timeout(total, copy)
            .await
            .unwrap_or(Err("transfer-timeout"));
        if let Err(reason) = result {
            telemetry.download_failure(reason);
        }
        observation.finish(result.is_ok());
        telemetry.download_finished(&request_id, result.err(), started.elapsed());
    });
    Body::from_stream(tokio_util::io::ReaderStream::with_capacity(
        reader,
        64 * 1024,
    ))
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::sync::Semaphore;
    #[tokio::test]
    async fn slow_or_dropped_bodies_release_the_download_slot_and_count_failures() {
        let telemetry = Arc::new(crate::telemetry::Telemetry::new(
            std::env::current_dir()
                .unwrap()
                .join(".codex-runtime/download-body-tests"),
        ));
        let slots = Arc::new(Semaphore::new(1));
        let start = |bytes: Vec<u8>, size| {
            transfer(
                Box::new(std::io::Cursor::new(bytes)),
                size,
                slots.clone().try_acquire_owned().unwrap(),
                telemetry.clone(),
                Duration::from_millis(20),
                Duration::from_secs(1),
                "download-test".into(),
            )
        };
        let unread = start(vec![1; 128 * 1024], 128 * 1024);
        tokio::time::sleep(Duration::from_millis(80)).await;
        assert_eq!(slots.available_permits(), 1);
        drop(unread);
        let disconnected = start(vec![1; 128 * 1024], 128 * 1024);
        drop(disconnected);
        tokio::time::sleep(Duration::from_millis(30)).await;
        assert_eq!(slots.available_permits(), 1);
        let good = start(vec![2; 1024], 1024);
        assert_eq!(axum::body::to_bytes(good, 2048).await.unwrap().len(), 1024);
        tokio::task::yield_now().await;
        let short = start(vec![1; 2], 10);
        let _ = axum::body::to_bytes(short, 32).await;
        tokio::time::sleep(Duration::from_millis(30)).await;
        let metrics = telemetry.snapshot(16, 2, 0);
        assert_eq!(metrics["downloads"]["completed"], 4);
        assert_eq!(metrics["downloads"]["failures"], 3);
        assert_eq!(metrics["downloadTimeouts"], 1);
        assert_eq!(metrics["downloadDisconnects"], 1);
        assert_eq!(metrics["downloadReadErrors"], 1);
    }
}
