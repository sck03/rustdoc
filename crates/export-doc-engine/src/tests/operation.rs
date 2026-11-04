use super::*;

#[test]
fn parent_cancel_reaches_external_flags_and_does_not_cancel_compensation() {
    let parent = OperationScope::new(Duration::from_secs(1));
    let child = OperationScope::new(Duration::from_secs(1));
    parent.run(|| {
        child.run(|| {
            let worker_flag = cancellation_flag();
            parent.cancel();
            assert!(worker_flag.load(Ordering::Acquire));
            OperationScope::detached(Duration::from_secs(1)).run(|| {
                assert!(!cancellation_flag().load(Ordering::Acquire));
            });
        })
    });
}

#[test]
fn nested_work_observes_parent_cancel_and_deadline_but_compensation_can_finish() {
    let parent = OperationScope::new(Duration::from_secs(30));
    let child = OperationScope::new(Duration::from_secs(60));
    parent.run(|| {
        child.run(|| {
            parent.cancel();
            assert_eq!(check().unwrap_err().status, Some(504));
        });
        OperationScope::detached(Duration::from_secs(1))
            .run_result(|| Ok(()))
            .unwrap();
        assert_eq!(check().unwrap_err().status, Some(504));
    });
    OperationScope::new(Duration::ZERO).run(|| {
        assert_eq!(child.run_result(|| Ok(())).unwrap_err().status, Some(504));
    });
    check().unwrap();
}

#[test]
fn result_boundary_preserves_cancellation_after_an_adapter_erases_the_error() {
    let scope = OperationScope::new(Duration::from_secs(30));
    let result: Result<(), ApiError> = scope.run_result(|| {
        scope.cancel();
        Err(ApiError {
            status: Some(400),
            message: "stringified cancellation".into(),
            diagnostic: None,
        })
    });
    assert_eq!(result.unwrap_err().status, Some(504));
    check().unwrap(); // The caller's scope is restored on error too.
}

#[test]
fn expired_work_does_not_start_and_genuine_validation_errors_keep_their_category() {
    let mut started = false;
    let result = OperationScope::new(Duration::ZERO).run_result(|| {
        started = true;
        Ok(())
    });
    assert!(!started);
    assert_eq!(result.unwrap_err().status, Some(504));
    let result: Result<(), ApiError> =
        OperationScope::new(Duration::from_secs(30)).run_result(|| {
            Err(ApiError {
                status: Some(400),
                message: "invalid format".into(),
                diagnostic: None,
            })
        });
    assert_eq!(result.unwrap_err().status, Some(400));
}
