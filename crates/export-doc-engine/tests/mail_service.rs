#![cfg(feature = "mail")]
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
#[path = "support/smtp.rs"]
mod smtp;
use export_doc_engine::{generated_api::*, secrets::Protector};
use serde_json::{Value, json};

fn configure(fixture: &native_fixture::Fixture, port: u16) -> Value {
    let mut settings = fixture.request(GET_SETTINGS, None, None)["settings"].clone();
    settings["email"]["smtpHost"] = json!("127.0.0.1");
    settings["email"]["smtpPort"] = json!(port);
    settings["email"]["enableSsl"] = json!(false);
    settings["email"]["fromAddress"] = json!("sender@example.invalid");
    settings["email"]["userName"] = json!("sender@example.invalid");
    settings["email"]["password"] = json!("smtp-canary-credential");
    settings["email"]["recipientAllowList"] = json!("example.invalid");
    fixture.request(
        UPDATE_SETTINGS,
        None,
        Some(json!({"settings":settings,"updateSecrets":true})),
    )
}
#[test]
fn encrypted_credentials_are_masked_retained_and_bound_to_their_field() {
    let fixture = native_fixture::Fixture::new();
    let saved = configure(&fixture, 2525);
    assert_eq!(saved["secrets"]["emailPasswordSet"], true);
    assert_eq!(saved["settings"]["email"]["password"], "");
    assert!(!saved.to_string().contains("smtp-canary-credential"));
    let mut settings = saved["settings"].clone();
    settings["email"]["fromDisplayName"] = json!("测试发件人");
    let preserved = fixture.request(
        UPDATE_SETTINGS,
        None,
        Some(json!({"settings":settings,"updateSecrets":true})),
    );
    assert_eq!(preserved["secrets"]["emailPasswordSet"], true);
    let database = std::fs::read(fixture.root.join("exportdoc-native.db")).unwrap();
    assert!(
        !database
            .windows(b"smtp-canary-credential".len())
            .any(|b| b == b"smtp-canary-credential")
    );
    let protector = Protector::new(&fixture.root);
    let encrypted = protector.protect("email", "value-canary").unwrap();
    assert_eq!(
        protector.unprotect("email", &encrypted).unwrap().as_str(),
        "value-canary"
    );
    assert!(protector.unprotect("webdav", &encrypted).is_err());
    assert!(protector.unprotect("email", "plaintext-password").is_err());
    let mut tampered = encrypted;
    tampered.push('A');
    assert!(protector.unprotect("email", &tampered).is_err());
}
#[test]
fn settings_save_encrypts_credentials_without_plaintext_in_the_database() {
    let fixture = native_fixture::Fixture::new();
    let mut settings = fixture.request(GET_SETTINGS, None, None)["settings"].clone();
    settings["email"]["smtpHost"] = json!("127.0.0.1");
    settings["email"]["smtpPort"] = json!(2525);
    settings["email"]["enableSsl"] = json!(false);
    settings["email"]["fromAddress"] = json!("sender@example.invalid");
    settings["email"]["userName"] = json!("sender@example.invalid");
    settings["email"]["password"] = json!("native-settings-credential-canary");
    let saved = fixture.request(
        UPDATE_SETTINGS,
        None,
        Some(json!({"settings":settings,"updateSecrets":true})),
    );
    assert_eq!(saved["success"], true);
    assert_eq!(saved["secrets"]["emailPasswordSet"], true);
    assert_eq!(saved["settings"]["email"]["password"], "");
    assert!(
        !saved
            .to_string()
            .contains("native-settings-credential-canary")
    );
    let database = std::fs::read(fixture.root.join("exportdoc-native.db")).unwrap();
    assert!(
        !database
            .windows(b"native-settings-credential-canary".len())
            .any(|b| b == b"native-settings-credential-canary")
    );
}
#[test]
fn smtp_delivers_multipart_once_and_rejects_reused_keys_with_changed_content() {
    let fixture = native_fixture::Fixture::new();
    let peer = smtp::Smtp::start(true);
    configure(&fixture, peer.port);
    let body = json!({"toAddress":"recipient@example.invalid","subject":"测试邮件","body":"<p>Hello <b>世界</b></p>","attachmentPaths":[]});
    let path = [("Idempotency-Key", "test-send-once-0001".into())];
    let first: Value = fixture
        .client()
        .json(SEND_EMAIL, &path, &[], Some(body.clone()))
        .unwrap();
    export_doc_contracts::validation::response(SEND_EMAIL.id, &first).unwrap();
    assert_eq!(first["success"], true);
    let mime = peer
        .body
        .recv_timeout(std::time::Duration::from_secs(6))
        .unwrap();
    assert!(mime.contains("multipart/alternative"));
    assert!(mime.contains("text/plain"));
    assert!(mime.contains("text/html"));
    let duplicate: Value = fixture
        .client()
        .json(SEND_EMAIL, &path, &[], Some(body.clone()))
        .unwrap();
    assert_eq!(duplicate["success"], true);
    let mut changed = body.clone();
    changed["subject"] = json!("different");
    assert_eq!(
        fixture
            .client()
            .json::<Value>(SEND_EMAIL, &path, &[], Some(changed))
            .unwrap_err()
            .status,
        Some(409)
    );
    let mut blocked = body;
    blocked["toAddress"] = json!("blocked@external.invalid");
    assert_eq!(
        fixture
            .client()
            .json::<Value>(SEND_EMAIL, &[], &[], Some(blocked))
            .unwrap_err()
            .status,
        Some(403)
    );
    let deliveries = fixture.request(LIST_EMAIL_DELIVERIES, None, None);
    export_doc_contracts::validation::response(LIST_EMAIL_DELIVERIES.id, &deliveries).unwrap();
    assert_eq!(deliveries["totalCount"], 1);
    assert_eq!(deliveries["items"][0]["status"], "Sent");
    assert!(!deliveries.to_string().contains("Hello"));
}
#[test]
fn smtp_uncertainty_is_durable_and_never_automatically_replayed() {
    let fixture = native_fixture::Fixture::new();
    let peer = smtp::Smtp::start(false);
    configure(&fixture, peer.port);
    let body = json!({"toAddress":"recipient@example.invalid","subject":"lost acknowledgement","body":"<p>test</p>","attachmentPaths":[]});
    let path = [("Idempotency-Key", "test-uncertain-0001".into())];
    assert_eq!(
        fixture
            .client()
            .json::<Value>(SEND_EMAIL, &path, &[], Some(body.clone()))
            .unwrap_err()
            .status,
        Some(503)
    );
    peer.body
        .recv_timeout(std::time::Duration::from_secs(6))
        .unwrap();
    assert_eq!(
        fixture
            .client()
            .json::<Value>(SEND_EMAIL, &path, &[], Some(body))
            .unwrap_err()
            .status,
        Some(409)
    );
    let records = fixture.request(LIST_EMAIL_DELIVERIES, None, None);
    assert_eq!(records["items"][0]["status"], "Uncertain");
}
#[test]
fn email_template_versions_preview_and_publication_preserve_original_workflow() {
    let fixture = native_fixture::Fixture::new();
    let template=fixture.create(CREATE_EMAIL_TEMPLATE,json!({"name":"报价跟进","category":"报价","subject":"Hello {{CustomerName}}","bodyHtml":"<p>Dear <b>{{ContactName}}</b></p><script>bad()</script>","expectedVersion":0}));
    assert!(!template["bodyHtml"].as_str().unwrap().contains("script"));
    assert_eq!(template["status"], "Draft");
    let id = template["id"].as_i64().unwrap();
    let published = fixture.request(
        PUBLISH_EMAIL_TEMPLATE,
        Some(id),
        Some(json!({"expectedVersion":template["versionNumber"]})),
    );
    assert_eq!(published["status"], "Published");
    let shared = fixture.request(
        SHARE_EMAIL_TEMPLATE,
        Some(id),
        Some(json!({"expectedVersion":published["versionNumber"],"shareScope":"Company"})),
    );
    assert_eq!(shared["shareScope"], "Company");
    let draft=fixture.request(SAVE_EMAIL_TEMPLATE_DRAFT,Some(id),Some(json!({"expectedVersion":shared["versionNumber"],"name":"报价跟进","category":"报价","subject":"Updated","bodyHtml":"<p>New</p>"})));
    assert_eq!(draft["status"], "Draft");
    assert_eq!(draft["shareScope"], "Private");
    let versions = fixture.request(LIST_EMAIL_TEMPLATE_VERSIONS, Some(id), None);
    export_doc_contracts::validation::response(LIST_EMAIL_TEMPLATE_VERSIONS.id, &versions).unwrap();
    assert_eq!(versions.as_array().unwrap().len(), 4);
    let restored: Value = fixture
        .client()
        .json(
            RESTORE_EMAIL_TEMPLATE_VERSION,
            &[("id", id.to_string()), ("versionNumber", "1".into())],
            &[],
            Some(json!({"expectedVersion":draft["versionNumber"]})),
        )
        .unwrap();
    assert_eq!(restored["subject"], template["subject"]);
    assert_eq!(restored["shareScope"], "Private");
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                PUBLISH_EMAIL_TEMPLATE,
                &[("id", id.to_string())],
                &[],
                Some(json!({"expectedVersion":draft["versionNumber"]}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let preview=fixture.request(PREVIEW_EMAIL_TEMPLATE,None,Some(json!({"subject":restored["subject"],"bodyHtml":restored["bodyHtml"],"variables":{"CustomerName":"ACME","ContactName":"<img src=x>"}})));
    assert_eq!(preview["subject"], "Hello ACME");
    assert!(preview["bodyHtml"].as_str().unwrap().contains("&lt;img"));
}
