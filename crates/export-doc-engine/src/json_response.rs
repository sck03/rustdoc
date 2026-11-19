//! JSON encoding reserves buffer capacity before growth. Host admission survives the response body.
use crate::{
    api::ApiError,
    bounded_bytes::BoundedBytes,
    engine::{
        backup_io::{Budget, Reservation},
        error::{Result, error},
    },
};
use std::{
    cell::RefCell,
    io::{self, Write},
    sync::Arc,
};

struct Context {
    budget: Arc<Budget>,
    reservation: Option<Reservation>,
    error: Option<ApiError>,
}
thread_local! { static CURRENT: RefCell<Option<Context>> = const { RefCell::new(None) }; }

pub fn with_budget<T>(
    budget: Arc<Budget>,
    reservation: Option<Reservation>,
    operation: impl FnOnce() -> Result<T>,
) -> Result<(T, Option<Reservation>)> {
    struct Restore(Option<Context>);
    impl Drop for Restore {
        fn drop(&mut self) {
            CURRENT.with(|current| *current.borrow_mut() = self.0.take());
        }
    }
    let previous = CURRENT.with(|current| {
        current.replace(Some(Context {
            budget,
            reservation,
            error: None,
        }))
    });
    let _restore = Restore(previous);
    let result = operation()?;
    let reservation =
        CURRENT.with(|current| current.borrow_mut().as_mut().unwrap().reservation.take());
    Ok((result, reservation))
}
struct Writer(BoundedBytes);
impl Write for Writer {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        let capacity = self.0.capacity_for(bytes.len())?;
        CURRENT.with(|current| {
            if let Some(context) = current.borrow_mut().as_mut() {
                let admitted = match &mut context.reservation {
                    Some(reservation) => reservation.grow_to(capacity.max(1)),
                    None => context
                        .budget
                        .reserve_working(capacity.max(1))
                        .map(|reservation| context.reservation = Some(reservation)),
                };
                if let Err(cause) = admitted {
                    context.error = Some(cause);
                    return Err(io::Error::other("响应内存预算不足。"));
                }
            }
            Ok(())
        })?;
        self.0.write(bytes)
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
pub fn encode<T: serde::Serialize + ?Sized>(
    operation: crate::generated_api::Operation,
    value: &T,
) -> Result<Vec<u8>> {
    encode_with_limit(
        value,
        export_doc_contracts::request_limits::json_response_bytes(operation.id),
    )
}
fn encode_with_limit<T: serde::Serialize + ?Sized>(value: &T, limit: usize) -> Result<Vec<u8>> {
    let mut output = Writer(BoundedBytes {
        bytes: Vec::new(),
        limit,
    });
    serde_json::to_writer(&mut output, value).map_err(|_| {
        CURRENT
            .with(|current| {
                current
                    .borrow_mut()
                    .as_mut()
                    .and_then(|context| context.error.take())
            })
            .unwrap_or_else(|| error(503, "响应编码失败或超过容量上限，请核对操作结果。"))
    })?;
    Ok(output.0.bytes)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn json_escaping_is_bounded_before_allocation_and_exact_bytes_are_preserved() {
        let text = "\u{0000}".repeat(100);
        assert!(encode_with_limit(&text, 601).is_err());
        assert_eq!(
            encode_with_limit(&text, 602).unwrap(),
            serde_json::to_vec(&text).unwrap()
        );
        let mut sink = BoundedBytes {
            bytes: Vec::new(),
            limit: 1030,
        };
        sink.write_all(&[0; 1025]).unwrap();
        assert!(sink.bytes.capacity() <= 1030);
        assert!(sink.write_all(&[0; 6]).is_err());
        assert_eq!(sink.bytes.len(), 1025);
    }
    #[test]
    fn growth_admission_releases_on_success_failure_and_nested_scope() {
        let budget = Arc::new(Budget::default());
        let held = budget.reserve_working(128 * 1024 * 1024 - 1024).unwrap();
        let cause = with_budget(budget.clone(), None, || {
            encode_with_limit(&"x".repeat(2048), 4096)
        })
        .err()
        .unwrap();
        assert_eq!(cause.status, Some(429));
        assert_eq!(budget.snapshot()["reservedBytes"], 128 * 1024 * 1024 - 1024);
        drop(held);
        let (bytes, reservation) = with_budget(budget.clone(), None, || {
            encode_with_limit(&"x".repeat(3000), 4096)
        })
        .unwrap();
        assert_eq!(budget.snapshot()["reservedBytes"], bytes.capacity());
        drop((bytes, reservation));
        assert_eq!(budget.snapshot()["reservedBytes"], 0);
        let before = Arc::strong_count(&budget);
        let mut reservation = budget.reserve_working(1).unwrap();
        reservation.grow_to(1024).unwrap();
        reservation.grow_to(2048).unwrap();
        drop(reservation);
        assert_eq!(Arc::strong_count(&budget), before);
    }
}
