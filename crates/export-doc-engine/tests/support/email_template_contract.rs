use export_doc_engine::{api::ApiError, generated_api::*};
use serde_json::{Value, json};

pub fn exercise(
    call: &impl Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let mut body = json!({"name":"分页邮件 École","category":"并发回归","subject":"subject","bodyHtml":"<p>".to_owned()+&"body ".repeat(1000)+"</p>","expectedVersion":0});
    let first = call(CREATE_EMAIL_TEMPLATE, &[], &[], Some(body.clone())).unwrap();
    let path = [("id", first["id"].as_i64().unwrap().to_string())];
    body["name"] = json!("分页邮件 E\u{301}cole");
    assert_eq!(
        call(CREATE_EMAIL_TEMPLATE, &[], &[], Some(body.clone()))
            .unwrap_err()
            .status,
        Some(409)
    );
    for number in 1..33 {
        body["expectedVersion"] = json!(number);
        body["subject"] = json!(format!("subject-{number}"));
        call(SAVE_EMAIL_TEMPLATE_DRAFT, &path, &[], Some(body.clone())).unwrap();
    }
    let page = call(
        LIST_EMAIL_TEMPLATE_VERSIONS,
        &path,
        &[("pageNumber", "2".into()), ("pageSize", "30".into())],
        None,
    )
    .unwrap();
    export_doc_contracts::validation::response(LIST_EMAIL_TEMPLATE_VERSIONS.id, &page).unwrap();
    assert_eq!(page["totalCount"], 33);
    assert_eq!(page["items"].as_array().unwrap().len(), 3);
    assert_eq!(page["items"][0]["versionNumber"], 3);
    assert!(serde_json::to_vec(&page).unwrap().len() < 4096);
    assert!(
        page["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|r| r.get("bodyHtml").is_none())
    );
    let detail = call(
        GET_EMAIL_TEMPLATE_VERSION,
        &[("id", path[0].1.clone()), ("versionNumber", "1".into())],
        &[],
        None,
    )
    .unwrap();
    assert_eq!(detail["bodyHtml"], first["bodyHtml"]);
    let directory = call(
        LIST_EMAIL_TEMPLATES,
        &[],
        &[
            ("keyword", "分页邮件".into()),
            ("pageSize", "1".into()),
            ("scope", "editable".into()),
        ],
        None,
    )
    .unwrap();
    assert_eq!(directory["totalCount"], 1);
    assert_eq!(directory["items"][0]["id"], first["id"]);
}
