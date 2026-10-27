use super::call;
use export_doc_engine::{engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, owner: &str, other: &str) {
    let mut template = call(
        service,
        owner,
        CREATE_USER_REPORT_TEMPLATE,
        0,
        Some(json!({"reportType":"ExportDocument","name":format!("历史容量-{}",nonce().unwrap())})),
    )
    .unwrap();
    let id = template["id"].as_i64().unwrap();
    for number in 2..=26 {
        template = call(service, owner, SAVE_USER_REPORT_TEMPLATE_DRAFT, id, Some(json!({"reportType":"ExportDocument",
            "name":format!("历史容量第{number}版"),"contentHtml":template["contentHtml"],"expectedVersion":template["versionNumber"]}))).unwrap();
    }
    assert_eq!(template["canManageHistory"], true);
    let stats = || {
        call(
            service,
            owner,
            GET_USER_REPORT_TEMPLATE_HISTORY_RETENTION,
            id,
            None,
        )
        .unwrap()
    };
    assert_eq!(stats()["totalCount"], 26);
    assert_eq!(stats()["eligibleCount"], 6);
    assert!(
        stats()["totalBytes"].as_u64().unwrap()
            >= template["contentHtml"].as_str().unwrap().len() as u64 * 26
    );
    let preview = || {
        call(
            service,
            owner,
            PREVIEW_USER_REPORT_TEMPLATE_HISTORY_CLEANUP,
            id,
            Some(json!({"retainLatest":20})),
        )
        .unwrap()
    };
    assert_eq!(
        call(
            service,
            other,
            PREVIEW_USER_REPORT_TEMPLATE_HISTORY_CLEANUP,
            id,
            Some(json!({"retainLatest":20}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(
            service,
            owner,
            PREVIEW_USER_REPORT_TEMPLATE_HISTORY_CLEANUP,
            id,
            Some(json!({"retainLatest":4}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let stale = preview();
    assert_eq!(stale["items"].as_array().unwrap().len(), 6);
    template = call(service, owner, SAVE_USER_REPORT_TEMPLATE_DRAFT, id, Some(json!({"reportType":"ExportDocument",
        "name":"预检后继续编辑","contentHtml":template["contentHtml"],"expectedVersion":template["versionNumber"]}))).unwrap();
    let commit = |token: &Value, reason: &str| {
        call(
            service,
            owner,
            COMMIT_USER_REPORT_TEMPLATE_HISTORY_CLEANUP,
            id,
            Some(json!({"token":token,"reason":reason})),
        )
    };
    assert_eq!(
        commit(&stale["token"], "过期预检").unwrap_err().status,
        Some(409)
    );
    assert_eq!(stats()["totalCount"], 27);
    let fresh = preview();
    assert_eq!(commit(&fresh["token"], "").unwrap_err().status, Some(400));
    commit(&fresh["token"], "保留最近20版，清除旧编辑快照").unwrap();
    assert_eq!(stats()["totalCount"], 20);
    assert_eq!(stats()["eligibleCount"], 0);
    assert_eq!(
        call(service, owner, GET_USER_REPORT_TEMPLATE, id, None).unwrap(),
        template
    );
    assert_eq!(
        commit(&fresh["token"], "重复提交").unwrap_err().status,
        Some(409)
    );
    let restore = |number: i64| {
        let result = service.dispatch(
            RESTORE_USER_REPORT_TEMPLATE_VERSION,
            &[
                ("id", id.to_string()),
                ("versionNumber", number.to_string()),
            ],
            &[],
            Some(json!({"expectedVersion":template["versionNumber"]})),
            owner,
        );
        result.map(|bytes| serde_json::from_slice::<Value>(&bytes).unwrap())
    };
    assert_eq!(restore(1).unwrap_err().status, Some(404));
    assert_eq!(restore(26).unwrap()["name"], "历史容量第26版");
    assert_eq!(stats()["totalCount"], 21);
}
