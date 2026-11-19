use super::*;
use std::pin::Pin;
struct PendingBody(Option<Bytes>);
impl HttpBody for PendingBody {
    type Data = Bytes;
    type Error = std::convert::Infallible;
    fn poll_frame(
        mut self: Pin<&mut Self>,
        _: &mut std::task::Context<'_>,
    ) -> std::task::Poll<Option<Result<hyper::body::Frame<Bytes>, Self::Error>>> {
        match self.0.take() {
            Some(bytes) => std::task::Poll::Ready(Some(Ok(hyper::body::Frame::data(bytes)))),
            None => std::task::Poll::Pending,
        }
    }
}

#[tokio::test]
async fn slow_receivers_do_not_reserve_parsers_and_idle_reads_expire() {
    let resources = Resources::default();
    let parameters = HashMap::new();
    let slow = parse(
        &resources,
        LOGIN,
        Request::new(Body::new(PendingBody(None))),
        &parameters,
        &[],
    );
    tokio::pin!(slow);
    tokio::select! {
        _ = &mut slow => panic!("pending body completed"),
        _ = tokio::time::sleep(Duration::from_millis(10)) => {}
    }
    assert_eq!(resources.parsers.available_permits(), 4);
    assert_eq!(resources.memory.available_permits(), 64 * 1024);
    assert!(
        parse(
            &resources,
            LOGIN,
            Request::new(Body::from("{}")),
            &parameters,
            &[]
        )
        .await
        .is_ok()
    );
    assert_eq!(
        bytes(
            Body::new(PendingBody(None)),
            1024,
            Duration::from_millis(10)
        )
        .await
        .unwrap_err()
        .status,
        Some(504)
    );
}

#[tokio::test]
async fn retained_json_is_bounded_but_bodyless_requests_and_other_hosts_remain_available() {
    let resources = Resources {
        memory: Arc::new(Semaphore::new(8)),
        ..Default::default()
    };
    let parameters = HashMap::new();
    let mut retained = Vec::new();
    for _ in 0..4 {
        retained.push(
            parse(
                &resources,
                CREATE_INVOICE,
                Request::new(Body::from(
                    serde_json::to_string(&"x".repeat(2046)).unwrap(),
                )),
                &parameters,
                &[],
            )
            .await
            .unwrap(),
        );
    }
    assert_eq!(resources.memory.available_permits(), 0);
    assert_eq!(
        parse(
            &resources,
            LOGIN,
            Request::new(Body::from("{}")),
            &parameters,
            &[]
        )
        .await
        .err()
        .unwrap()
        .status,
        Some(429)
    );
    assert!(
        parse(
            &resources,
            GET_HEALTH,
            Request::new(Body::new(PendingBody(None))),
            &parameters,
            &[]
        )
        .await
        .is_ok()
    );
    assert!(
        parse(
            &Resources::default(),
            LOGIN,
            Request::new(Body::from("{}")),
            &parameters,
            &[]
        )
        .await
        .is_ok()
    );
    drop(retained);
    assert_eq!(resources.memory.available_permits(), 8);
    assert!(
        parse(
            &resources,
            LOGIN,
            Request::new(Body::from("{")),
            &parameters,
            &[]
        )
        .await
        .is_err()
    );
    assert_eq!(resources.memory.available_permits(), 8);
}

#[tokio::test]
async fn small_parallel_requests_are_charged_for_actual_buffers() {
    let resources = Arc::new(Resources::default());
    let mut tasks = tokio::task::JoinSet::new();
    for _ in 0..20 {
        let resources = resources.clone();
        tasks.spawn(async move {
            parse(
                &resources,
                CREATE_INVOICE,
                Request::new(Body::from("{}")),
                &HashMap::new(),
                &[],
            )
            .await
            .unwrap()
        });
    }
    let mut retained = Vec::new();
    while let Some(result) = tasks.join_next().await {
        retained.push(result.unwrap());
    }
    assert_eq!(resources.memory.available_permits(), 64 * 1024 - 20);
    assert_eq!(resources.parsers.available_permits(), 4);
    drop(retained);
    assert_eq!(resources.memory.available_permits(), 64 * 1024);
}

#[tokio::test]
async fn cancelled_partial_receivers_return_their_actual_reservation() {
    let resources = Resources::default();
    let parameters = HashMap::new();
    let mut pending = Box::pin(parse(
        &resources,
        LOGIN,
        Request::new(Body::new(PendingBody(Some(Bytes::from_static(b"{"))))),
        &parameters,
        &[],
    ));
    tokio::select! {
        _ = &mut pending => panic!("partial body completed"),
        _ = tokio::time::sleep(Duration::from_millis(10)) => {}
    }
    assert_eq!(resources.memory.available_permits(), 64 * 1024 - 1);
    assert_eq!(resources.parsers.available_permits(), 4);
    drop(pending);
    assert_eq!(resources.memory.available_permits(), 64 * 1024);
}

#[test]
fn image_json_and_binary_uploads_share_the_declared_processing_lane() {
    for operation in [
        SAVE_SHIPPING_MARK_IMAGE,
        SAVE_INVOICE_SEAL_IMAGE,
        PREVIEW_SHIPPING_MARK_IMAGE,
        RECOGNIZE_OCR_IMAGE,
        UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
    ] {
        assert!(needs_processing_slot(operation), "{}", operation.id);
    }
    assert!(!needs_processing_slot(LOGIN));
}
#[tokio::test]
async fn login_body_limit_cannot_be_bypassed_by_multipart_and_other_json_keeps_its_capacity() {
    let parameters = HashMap::new();
    let request = |content: String| Request::new(Body::from(content));
    let oversized = parse(
        &Resources::default(),
        LOGIN,
        request(" ".repeat(4097)),
        &parameters,
        &[],
    )
    .await;
    assert_eq!(oversized.err().unwrap().status, Some(413));
    let invalid = parse(
        &Resources::default(),
        LOGIN,
        request("{".into()),
        &parameters,
        &[],
    )
    .await;
    assert_eq!(invalid.err().unwrap().status, Some(400));
    let mut form = request(String::new());
    form.headers_mut().insert(
        "content-type",
        "multipart/form-data; boundary=test".parse().unwrap(),
    );
    assert_eq!(
        parse(&Resources::default(), LOGIN, form, &parameters, &[])
            .await
            .err()
            .unwrap()
            .status,
        Some(400)
    );
    let large = serde_json::json!({"text": "中".repeat(4096)}).to_string();
    assert!(
        parse(
            &Resources::default(),
            CREATE_INVOICE,
            request(large),
            &parameters,
            &[]
        )
        .await
        .is_ok()
    );
    assert!(
        parse(
            &Resources::default(),
            LOGIN,
            request("{}".into()),
            &parameters,
            &[]
        )
        .await
        .is_ok()
    );
}
