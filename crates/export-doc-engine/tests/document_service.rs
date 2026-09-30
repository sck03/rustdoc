#[path = "support/document_samples.rs"]
mod document_samples;
#[path = "support/native_fixture.rs"]
#[allow(dead_code)]
mod native_fixture;
use export_doc_engine::generated_api::*;
use serde_json::{Value, json};

#[test]
fn office_documents_round_trip_with_type_and_state_validation() {
    let mut fixture = native_fixture::Fixture::new();
    let person = fixture.employee();
    let mut row=fixture.create(CREATE_GENERAL_REQUEST,json!({"requestKey":"office-files","title":"办公文档附件","reason":"按原始字节交接","category":"IT","employeeId":person["employee"]["id"]}));
    let path = [("id", row["id"].to_string())];
    fixture.client.take();
    let service = export_doc_engine::engine::NativeService::open(fixture.paths.clone()).unwrap();
    let login: Value = serde_json::from_slice(
        &service
            .dispatch(
                LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    let token = login["accessToken"].as_str().unwrap();
    for (extension, bytes) in document_samples::office_files() {
        row = service
            .upload(
                UPLOAD_ATTACHMENT_TO_GENERAL_REQUEST,
                &path,
                json!({"expectedVersion":row["versionNumber"]}),
                &format!("材料.{extension}"),
                &bytes,
                token,
            )
            .unwrap();
        let attachment = row["attachments"]
            .as_array()
            .unwrap()
            .iter()
            .find(|a| a["fileName"] == format!("材料.{extension}"))
            .unwrap();
        let download = service.download_file(
            DOWNLOAD_ATTACHMENT_OF_GENERAL_REQUEST,
            &[
                ("id", row["id"].to_string()),
                ("attachmentId", attachment["id"].to_string()),
            ],
            token,
        );
        // The native client exposes authenticated original-file output.
        assert_eq!(download.unwrap().content, bytes);
        assert_eq!(
            service
                .upload(
                    UPLOAD_ATTACHMENT_TO_GENERAL_REQUEST,
                    &path,
                    json!({"expectedVersion":row["versionNumber"]}),
                    if extension == "docx" {
                        "伪装.xlsx"
                    } else {
                        "伪装.pdf"
                    },
                    &bytes,
                    token
                )
                .unwrap_err()
                .status,
            Some(400)
        );
    }
    for (name, bytes) in [
        ("损坏.docx", b"PKbad".as_slice()),
        ("脚本.exe", b"MZbad".as_slice()),
        ("宏.docm", b"PKbad".as_slice()),
    ] {
        assert_eq!(
            service
                .upload(
                    UPLOAD_ATTACHMENT_TO_GENERAL_REQUEST,
                    &path,
                    json!({"expectedVersion":row["versionNumber"]}),
                    name,
                    bytes,
                    token
                )
                .unwrap_err()
                .status,
            Some(400)
        );
    }
    let pending: Value = serde_json::from_slice(
        &service
            .dispatch(
                SUBMIT_GENERAL_REQUEST,
                &path,
                &[],
                Some(json!({"expectedVersion":row["versionNumber"],"note":"提交"})),
                token,
            )
            .unwrap(),
    )
    .unwrap();
    let (_, bytes) = document_samples::office_files().remove(0);
    assert_eq!(
        service
            .upload(
                UPLOAD_ATTACHMENT_TO_GENERAL_REQUEST,
                &path,
                json!({"expectedVersion":pending["versionNumber"]}),
                "冻结.docx",
                &bytes,
                token
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    service.close().unwrap();
}
