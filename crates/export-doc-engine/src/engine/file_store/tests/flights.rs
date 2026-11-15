use super::*;
use std::time::Duration;
#[test]
fn waiters_cancel_without_poisoning_or_leaking_the_key() {
    let flights = Flights::default();
    let first = flights.get("object").unwrap();
    let second = flights.get("object").unwrap();
    assert!(Arc::ptr_eq(&first, &second));
    let held = first.write(Duration::from_secs(1)).unwrap();
    let scope = crate::operation::OperationScope::new(Duration::from_millis(10));
    assert_eq!(
        scope
            .run(|| second.write(Duration::from_secs(1)))
            .unwrap_err()
            .status,
        Some(504)
    );
    let other = flights.get("other").unwrap();
    drop(other.write(Duration::from_secs(1)).unwrap());
    drop(held);
    drop(second.write(Duration::from_secs(1)).unwrap());
    drop((first, second, other));
    let _next = flights.get("next").unwrap();
    assert_eq!(flights.0.lock().unwrap().len(), 1);
}
