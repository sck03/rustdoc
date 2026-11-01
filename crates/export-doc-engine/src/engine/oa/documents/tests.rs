use super::*;
#[path = "../../../../tests/support/approval_fixture.rs"]
mod fixture;

#[test]
fn complete_download_rechecks_private_material_after_the_preparation_snapshot() {
    let root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/oa-download-tests")
        .join(crate::paths::nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let _cleanup = crate::test_support::ScratchDirectory(root.clone());
    let paths = crate::paths::RuntimePaths::server(&root, &root.join("Data")).unwrap();
    let service = NativeService::open(paths).unwrap();
    let login = fixture::request(
        &service,
        "",
        crate::generated_api::LOGIN,
        Some(json!({"username":"admin","password":""})),
    );
    let admin = login["accessToken"].as_str().unwrap();
    let (reviewer_id, token) = fixture::reviewer(&service, admin);
    let applicant = fixture::request(
        &service,
        admin,
        crate::generated_api::CREATE_PERSONNEL,
        Some(
            json!({"requestKey":crate::paths::nonce().unwrap(),"employeeNumber":crate::paths::nonce().unwrap(),"departmentId":"GENERAL","jobTitle":"下载材料申请人","employmentType":"FullTime","hireDate":"2026-01-01","profile":{"fullName":"匿名申请人"}}),
        ),
    );
    let mut settings = fixture::request(
        &service,
        admin,
        crate::generated_api::GET_OA_APPROVAL_SETTINGS,
        None,
    );
    let mut node = fixture::node("review", &[], "Named", json!([reviewer_id]));
    node["materialPolicy"] = json!({"allowRestrictedAttachments":true});
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap()["stages"] = json!([node]);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let saved = fixture::request(
        &service,
        admin,
        crate::generated_api::SAVE_OA_APPROVAL_SETTINGS,
        Some(settings),
    );
    fixture::request(
        &service,
        admin,
        crate::generated_api::PUBLISH_OA_DEFINITION,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"general"})),
    );
    let draft = fixture::request(
        &service,
        admin,
        crate::generated_api::CREATE_GENERAL_REQUEST,
        Some(
            json!({"requestKey":crate::paths::nonce().unwrap(),"employeeId":applicant["employee"]["id"],"title":"完整下载并发边界","reason":"下载准备之后撤销私密材料权限","category":"Other"}),
        ),
    );
    let id = draft["id"].as_i64().unwrap();
    let upload = crate::generated_api::ALL_OPERATIONS
        .iter()
        .copied()
        .find(|op| metadata(*op).is_some_and(|m| m["kind"] == "general" && m["action"] == "upload"))
        .unwrap();
    let draft=service.upload(upload,&[("id",id.to_string())],json!({"expectedVersion":draft["versionNumber"],"purpose":"Supporting","classification":"Restricted"}),"private.xml",b"<?xml version=\"1.0\"?><Private>anonymous</Private>",admin).unwrap();
    let pending = fixture::call(
        &service,
        admin,
        crate::generated_api::SUBMIT_GENERAL_REQUEST,
        id,
        fixture::decision_body(
            &service,
            admin,
            crate::generated_api::SUBMIT_GENERAL_REQUEST,
            id,
            Some(json!({"expectedVersion":draft["versionNumber"],"note":"提交"})),
        ),
    )
    .unwrap();
    let row = fixture::call(
        &service,
        &token,
        crate::generated_api::APPROVE_GENERAL_REQUEST,
        id,
        fixture::decision_body(
            &service,
            &token,
            crate::generated_api::APPROVE_GENERAL_REQUEST,
            id,
            Some(json!({"expectedVersion":pending["versionNumber"],"note":"批准"})),
        ),
    )
    .unwrap();
    let actor = auth::current_actor(&service.store, reviewer_id).unwrap();
    let op = crate::generated_api::ALL_OPERATIONS
        .iter()
        .copied()
        .find(|op| {
            metadata(*op)
                .is_some_and(|m| m["kind"] == "general" && m["action"] == "document-download")
        })
        .unwrap();
    let meta = metadata(op).unwrap();
    let parameters = [
        ("id", id.to_string()),
        (
            "submissionId",
            row["submissionId"].as_i64().unwrap().to_string(),
        ),
    ];
    service
        .store
        .read(|tx| {
            let (current, row) = current(tx, &actor, meta, id)?;
            let snapshot = submissions::checked(tx, &row, submissions::id(&parameters)?)?;
            assert!(super::super::materials::full(
                tx, &current, &row, &snapshot
            )?);
            Ok(())
        })
        .unwrap();
    let administrator =
        auth::current_actor(&service.store, login["user"]["id"].as_i64().unwrap()).unwrap();
    service
        .store
        .transaction(|tx| {
            let mut user = store::get(tx, "users", reviewer_id)?;
            user["permissionGrants"]
                .as_array_mut()
                .unwrap()
                .retain(|g| {
                    !(g["resourceKey"] == "office.people" && g["action"] == "view-details")
                });
            let username = text(&user, "username");
            store::save(
                tx,
                "users",
                reviewer_id,
                user,
                Some(username),
                &administrator,
                "permission-change",
            )?;
            Ok(())
        })
        .unwrap();
    let result = service
        .store
        .read(|tx| full_download(tx, &actor, meta, &parameters));
    assert_eq!(
        result.err().unwrap().status,
        Some(403),
        "the second snapshot must not return a complete private file"
    );
    service.close().unwrap();
}
