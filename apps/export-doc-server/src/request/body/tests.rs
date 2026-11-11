use super::*;
struct PendingBody;
impl HttpBody for PendingBody {
    type Data = Bytes;
    type Error = std::convert::Infallible;
    fn poll_frame(
        self: Pin<&mut Self>,
        _: &mut std::task::Context<'_>,
    ) -> std::task::Poll<Option<Result<hyper::body::Frame<Bytes>, Self::Error>>> {
        std::task::Poll::Pending
    }
}

#[tokio::test]
async fn slow_receivers_do_not_reserve_parsers_and_idle_reads_expire() {
    let resources = Resources::default();
    let parameters = HashMap::new();
    let slow = parse(
        &resources,
        LOGIN,
        Request::new(Body::new(PendingBody)),
        &parameters,
        &[],
    );
    tokio::pin!(slow);
    tokio::select! {
        _ = &mut slow => panic!("pending body completed"),
        _ = tokio::time::sleep(Duration::from_millis(10)) => {}
    }
    assert_eq!(resources.parsers.available_permits(), 4);
    assert_eq!(resources.memory.available_permits(), 64 * 1024 - 4);
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
        bytes(Body::new(PendingBody), 1024, Duration::from_millis(10))
            .await
            .unwrap_err()
            .status,
        Some(504)
    );
}

#[tokio::test]
async fn retained_json_is_bounded_but_bodyless_requests_and_other_hosts_remain_available() {
    let resources = Resources::default();
    let parameters = HashMap::new();
    let mut retained = Vec::new();
    for _ in 0..4 {
        retained.push(
            parse(
                &resources,
                CREATE_INVOICE,
                Request::new(Body::from("{}")),
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
            Request::new(Body::new(PendingBody)),
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
    assert_eq!(resources.memory.available_permits(), 64 * 1024);
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
