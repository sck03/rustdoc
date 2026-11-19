//! Charge real buffer growth before allocation, regardless of Content-Length or size hints.
use super::{
    ApiError, Arc, Body, Bytes, Duration, HttpBody, OwnedSemaphorePermit, Semaphore, error,
};
use export_doc_engine::bounded_bytes::BoundedBytes;
use std::{future::poll_fn, io::Write, pin::Pin};

pub(super) async fn bytes(body: Body, limit: usize, idle: Duration) -> Result<Bytes, ApiError> {
    receive(body, limit, idle, None)
        .await
        .map(|(bytes, _)| bytes)
}

pub(super) async fn receive(
    mut body: Body,
    limit: usize,
    idle: Duration,
    memory: Option<Arc<Semaphore>>,
) -> Result<(Bytes, Option<OwnedSemaphorePermit>), ApiError> {
    let busy = || error(429, "请求接收容量已满，请稍后重试。");
    let mut reservation = memory
        .as_ref()
        .map(|memory| memory.clone().try_acquire_many_owned(0).map_err(|_| busy()))
        .transpose()?;
    let mut content = BoundedBytes {
        bytes: Vec::new(),
        limit,
    };
    while let Some(frame) =
        tokio::time::timeout(idle, poll_fn(|cx| Pin::new(&mut body).poll_frame(cx)))
            .await
            .map_err(|_| error(504, "请求读取空闲超过时限。"))?
    {
        let frame = frame.map_err(|_| error(400, "请求内容读取失败。"))?;
        if let Ok(chunk) = frame.into_data() {
            let capacity = content
                .capacity_for(chunk.len())
                .map_err(|_| error(413, "请求超过该操作的容量上限。"))?;
            if let Some(reservation) = &mut reservation {
                let units = capacity.div_ceil(1024);
                if units > reservation.num_permits() {
                    let extra = memory
                        .as_ref()
                        .unwrap()
                        .clone()
                        .try_acquire_many_owned((units - reservation.num_permits()) as u32)
                        .map_err(|_| busy())?;
                    reservation.merge(extra);
                }
            }
            content
                .write_all(&chunk)
                .map_err(|_| error(503, "请求缓冲分配失败。"))?;
        }
    }
    Ok((Bytes::from(content.bytes), reservation))
}
