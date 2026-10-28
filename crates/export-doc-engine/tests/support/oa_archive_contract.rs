//! The same immutable-version and stored-PDF contract runs on both databases.
use super::oa_contract::{call, operation};
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};
use std::time::{Duration, Instant};

pub fn exercise(service: &NativeService, admin: &str, applicant: &str, employee: Option<i64>) {
    let mut settings = call(service, admin, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    settings["expectedVersion"] = settings["versionNumber"].clone();
    for rule in settings["rules"].as_array_mut().unwrap() {
        *rule = json!({"kind":rule["kind"],"mode":"Single","approverUserIds":[]});
    }
    call(service, admin, SAVE_OA_APPROVAL_SETTINGS, 0, Some(settings)).unwrap();
    let mut body = json!({"requestKey":nonce().unwrap(),"title":"冻结版本验证","reason":"第一版正文","category":"IT","employeeId":employee});
    let mut row = call(
        service,
        applicant,
        operation("general", "create"),
        0,
        Some(body.clone()),
    )
    .unwrap();
    let id = row["id"].as_i64().unwrap();
    let path = [("id", id.to_string())];
    let original = b"<?xml version=\"1.0\"?><Voucher><Amount>100.00</Amount></Voucher>";
    row = service
        .upload(
            operation("general", "upload"),
            &path,
            json!({"expectedVersion":row["versionNumber"],"purpose":"Supporting"}),
            "original.xml",
            original,
            applicant,
        )
        .unwrap();
    let attachment = row["attachments"][0]["id"].as_i64().unwrap();
    let act = |token, action, row: &Value| {
        call(
            service,
            token,
            operation("general", action),
            id,
            Some(json!({"expectedVersion":row["versionNumber"],"note":"档案回归"})),
        )
        .unwrap()
    };
    row = act(applicant, "submit", &row);
    let first = row["submissionId"].as_i64().unwrap();
    row = act(applicant, "withdraw", &row);
    row = serde_json::from_slice(
        &service
            .dispatch(
                operation("general", "delete-attachment"),
                &[
                    ("id", id.to_string()),
                    ("attachmentId", attachment.to_string()),
                ],
                &[],
                Some(json!({"expectedVersion":row["versionNumber"],"note":"修改附件"})),
                applicant,
            )
            .unwrap(),
    )
    .unwrap();
    body["expectedVersion"] = row["versionNumber"].clone();
    body["reason"] = json!("第二版正文");
    row = call(
        service,
        applicant,
        operation("general", "update"),
        id,
        Some(body),
    )
    .unwrap();
    let get = |submission: i64| -> Value {
        serde_json::from_slice(
            &service
                .dispatch(
                    operation("general", "submission"),
                    &[
                        ("id", id.to_string()),
                        ("submissionId", submission.to_string()),
                    ],
                    &[],
                    None,
                    applicant,
                )
                .unwrap(),
        )
        .unwrap()
    };
    let snapshot = get(first);
    assert_eq!(snapshot["request"]["reason"], "第一版正文");
    assert_eq!(snapshot["request"]["attachments"][0]["id"], attachment);
    assert_eq!(snapshot["status"], "Withdrawn");
    assert_eq!(
        service
            .download_file(
                operation("general", "download"),
                &[
                    ("id", id.to_string()),
                    ("attachmentId", attachment.to_string())
                ],
                applicant
            )
            .unwrap()
            .content,
        original
    );
    row = act(applicant, "submit", &row);
    row = act(admin, "approve", &row);
    assert_eq!(row["status"], "Approved");
    let second = row["submissionId"].as_i64().unwrap();
    let output_path = [("id", id.to_string()), ("submissionId", second.to_string())];
    let deadline = Instant::now() + Duration::from_secs(60);
    loop {
        let snapshot = get(second);
        if snapshot["documentStatus"] == "Ready" {
            break;
        }
        assert_ne!(
            snapshot["documentStatus"], "Failed",
            "{}",
            snapshot["documentError"]
        );
        assert!(Instant::now() < deadline, "approval PDF timed out");
        std::thread::sleep(Duration::from_millis(50));
    }
    let download = || {
        service
            .download_file(
                operation("general", "document-download"),
                &output_path,
                applicant,
            )
            .unwrap()
            .content
    };
    let pdf = download();
    assert!(pdf.starts_with(b"%PDF-"));
    service
        .dispatch(
            operation("general", "document-generate"),
            &output_path,
            &[],
            None,
            applicant,
        )
        .unwrap();
    assert_eq!(
        pdf,
        download(),
        "stored PDF is byte-for-byte stable on repeat requests"
    );
    assert!(
        service
            .download_file(
                operation("general", "bundle-download"),
                &output_path,
                applicant
            )
            .unwrap()
            .content
            .starts_with(b"PK")
    );
    row = act(admin, "void", &row);
    assert_eq!(row["status"], "Cancelled");
    assert!(
        service
            .download_file(
                operation("general", "document-download"),
                &output_path,
                applicant
            )
            .is_err()
    );
    assert_eq!(get(first)["request"]["reason"], "第一版正文");
}
