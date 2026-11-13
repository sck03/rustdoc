use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, token: &str) {
    let suffix = nonce().unwrap();
    let mut person: Value = serde_json::from_slice(
        &service
            .dispatch(
                CREATE_PERSONNEL,
                &[],
                &[],
                Some(json!({
                    "requestKey":suffix,"employeeNumber":format!("FILES-{}", &suffix[..8]),
                    "departmentId":"GENERAL","jobTitle":"资料管理员","employmentType":"FullTime",
                    "hireDate":"2026-09-01","onProbation":false,"profile":{"fullName":"分页验收"}
                })),
                token,
            )
            .unwrap(),
    )
    .unwrap();
    let path = [("id", person["employee"]["id"].to_string())];
    for index in 0..105 {
        let bytes =
            format!("%PDF-1.7\n% document {index}\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n");
        person = service
            .upload(
                UPLOAD_PERSONNEL_ATTACHMENT,
                &path,
                json!({"expectedVersion":person["versionNumber"]}),
                &format!("合同{index}.pdf"),
                bytes.as_bytes(),
                token,
            )
            .unwrap();
        assert_eq!(person["attachmentCount"], index + 1);
        assert!(person["attachments"].as_array().unwrap().len() <= 50);
    }
    let list = |after: &str| -> Value {
        serde_json::from_slice(
            &service
                .dispatch(
                    LIST_PERSONNEL_ATTACHMENTS,
                    &path,
                    &[("after", after.into())],
                    None,
                    token,
                )
                .unwrap(),
        )
        .unwrap()
    };
    let mut cursor = String::new();
    let mut seen = std::collections::BTreeSet::new();
    for expected in [50, 50, 5] {
        let page = list(&cursor);
        assert_eq!(page["totalCount"], 105);
        assert_eq!(page["items"].as_array().unwrap().len(), expected);
        for item in page["items"].as_array().unwrap() {
            assert!(
                seen.insert(item["id"].as_str().unwrap().to_owned()),
                "cursor pages must not overlap"
            );
        }
        cursor = page["nextCursor"].as_str().unwrap_or("").to_owned();
    }
    assert!(cursor.is_empty());
    assert_eq!(seen.len(), 105);
    assert_eq!(
        service
            .dispatch(LIST_PERSONNEL_ATTACHMENTS, &path, &[], None, "")
            .unwrap_err()
            .status,
        Some(401)
    );
    let removed = seen.first().unwrap().clone();
    let file_path = [("id", path[0].1.clone()), ("attachmentId", removed.clone())];
    let deleted: Value = serde_json::from_slice(
        &service
            .dispatch(
                DELETE_PERSONNEL_ATTACHMENT,
                &file_path,
                &[],
                Some(json!({"expectedVersion":person["versionNumber"],"note":"删除测试材料"})),
                token,
            )
            .unwrap(),
    )
    .unwrap();
    assert_eq!(deleted["attachmentCount"], 104);
    assert_eq!(list("")["totalCount"], 104);
    assert_eq!(
        service
            .download_file(DOWNLOAD_PERSONNEL_ATTACHMENT, &file_path, token)
            .err()
            .unwrap()
            .status,
        Some(404)
    );
}
