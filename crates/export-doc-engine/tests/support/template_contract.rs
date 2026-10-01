use export_doc_engine::{
    api::ApiError, contracts, engine::NativeService, generated_api::*, paths::nonce,
};
use serde_json::{Value, json};

fn call(
    service: &NativeService,
    token: &str,
    op: Operation,
    id: i64,
    body: Option<Value>,
) -> Result<Value, ApiError> {
    let parameters = if id > 0 {
        vec![("id", id.to_string())]
    } else {
        vec![]
    };
    let body = body.map(|body| contracts::overlay(contracts::object(op.id, true), &body));
    let bytes = service.dispatch(op, &parameters, &[], body, token)?;
    Ok(serde_json::from_slice(&bytes).unwrap())
}

/// Run identical ownership, default and conflict checks on SQLite and PostgreSQL.
pub fn exercise(service: &NativeService, admin: &str) {
    let suffix = nonce().unwrap();
    let grants = ["view","design","clone","publish","share","deactivate","restore","archive","import","export"].iter().map(|action| json!({"resourceKey":"document.report-templates","action":action,"dataScope":"all"})).chain(std::iter::once(json!({"resourceKey":"document.invoices","action":"view","dataScope":"all"}))).collect::<Vec<_>>();
    let role = call(service, admin, CREATE_PERMISSION_TEMPLATE, 0, Some(json!({"code":format!("TPL-{suffix}"),"name":"模板权限回归","isActive":true,"grants":grants}))).unwrap();
    let mut users = vec![];
    for index in 0..2 {
        let username = format!("tpl-{index}-{}", &suffix[..8]);
        let user = call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":username,"fullName":username,"role":"User","companyScope":"DEFAULT","departmentId":"GENERAL","permissionTemplateId":role["id"],"isActive":true,"resetPassword":"Template-Test-2026"}))).unwrap();
        let login = call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":username,"password":"Template-Test-2026"})),
        )
        .unwrap();
        users.push((
            login["accessToken"].as_str().unwrap().to_owned(),
            user["user"]["id"].clone(),
        ));
    }
    let file = call(
        service,
        admin,
        CREATE_REPORT_TEMPLATE,
        0,
        Some(json!({"reportType":"ExportDocument","displayName":"公共模板"})),
    )
    .unwrap();
    let default = json!({"reportType":"ExportDocument","templatePath":file["templatePath"]});
    call(
        service,
        admin,
        SET_DEFAULT_REPORT_TEMPLATE,
        0,
        Some(default.clone()),
    )
    .unwrap();
    for op in [
        SET_DEFAULT_REPORT_TEMPLATE,
        CREATE_REPORT_TEMPLATE,
        SAVE_REPORT_TEMPLATE_CONTENT,
        UPDATE_REPORT_TEMPLATE_DISPLAY_NAME,
        RENAME_REPORT_TEMPLATE,
        IMPORT_REPORT_TEMPLATE_FILE,
        IMPORT_REPORT_TEMPLATE_PACKAGE,
    ] {
        assert_eq!(
            call(service, &users[0].0, op, 0, Some(default.clone()))
                .unwrap_err()
                .status,
            Some(403),
            "{}",
            op.id
        );
    }
    let mut copy = call(service, &users[0].0, CLONE_USER_REPORT_TEMPLATE, 0, Some(json!({"reportType":"ExportDocument","sourceTemplatePath":file["templatePath"],"name":"我的修改版"}))).unwrap();
    assert_eq!(copy["ownerUserId"], users[0].1);
    assert_eq!(copy["shareScope"], "Private");
    assert_eq!(copy["contentHtml"], file["content"]);
    let id = copy["id"].as_i64().unwrap();
    assert_eq!(
        call(service, &users[1].0, GET_USER_REPORT_TEMPLATE, id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    let save = json!({"reportType":"ExportDocument","name":"我的版式","contentHtml":copy["contentHtml"],"expectedVersion":copy["versionNumber"]});
    copy = call(
        service,
        &users[0].0,
        SAVE_USER_REPORT_TEMPLATE_DRAFT,
        id,
        Some(save.clone()),
    )
    .unwrap();
    assert_eq!(
        call(
            service,
            &users[0].0,
            SAVE_USER_REPORT_TEMPLATE_DRAFT,
            id,
            Some(save)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    copy = call(
        service,
        &users[0].0,
        PUBLISH_USER_REPORT_TEMPLATE,
        id,
        Some(json!({"expectedVersion":copy["versionNumber"]})),
    )
    .unwrap();
    let select =
        json!({"reportType":"ExportDocument","templatePath":format!("user-template:{id}")});
    let settings = call(service, admin, GET_SETTINGS, 0, None).unwrap()["settings"].clone();
    call(
        service,
        admin,
        UPDATE_SETTINGS,
        0,
        Some(json!({"settings":settings,"updateSecrets":false})),
    )
    .unwrap();
    let mut private_default =
        call(service, admin, GET_SETTINGS, 0, None).unwrap()["settings"].clone();
    private_default["reportTemplateDefaults"]["exportDocumentTemplatePath"] =
        select["templatePath"].clone();
    assert_eq!(
        call(
            service,
            admin,
            UPDATE_SETTINGS,
            0,
            Some(json!({"settings":private_default,"updateSecrets":false}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    assert_eq!(
        call(
            service,
            admin,
            SET_DEFAULT_REPORT_TEMPLATE,
            0,
            Some(select.clone())
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    copy = call(
        service,
        &users[0].0,
        SHARE_USER_REPORT_TEMPLATE,
        id,
        Some(json!({"expectedVersion":copy["versionNumber"],"shareScope":"All"})),
    )
    .unwrap();
    let viewed = call(service, &users[1].0, GET_USER_REPORT_TEMPLATE, id, None).unwrap();
    for field in [
        "canEdit",
        "canPublish",
        "canShare",
        "canDisable",
        "canRestore",
        "canArchive",
    ] {
        assert_eq!(viewed[field], false, "{field}");
    }
    for op in [
        DISABLE_USER_REPORT_TEMPLATE,
        SHARE_USER_REPORT_TEMPLATE,
        RESTORE_USER_REPORT_TEMPLATE,
    ] {
        assert_eq!(
            call(
                service,
                &users[1].0,
                op,
                id,
                Some(json!({"expectedVersion":copy["versionNumber"],"shareScope":"Private"}))
            )
            .unwrap_err()
            .status,
            Some(403)
        );
    }
    call(service, admin, SET_DEFAULT_REPORT_TEMPLATE, 0, Some(select)).unwrap();
    assert_eq!(
        call(
            service,
            &users[0].0,
            DISABLE_USER_REPORT_TEMPLATE,
            id,
            Some(json!({"expectedVersion":copy["versionNumber"]}))
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        call(
            service,
            admin,
            DISABLE_USER_REPORT_TEMPLATE,
            id,
            Some(json!({"expectedVersion":copy["versionNumber"]}))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let fork = call(service, &users[1].0, CLONE_USER_REPORT_TEMPLATE, 0, Some(json!({"reportType":"ExportDocument","sourceTemplatePath":format!("user-template:{id}"),"name":"我的独立副本"}))).unwrap();
    assert_eq!(fork["shareScope"], "Private");
    assert_eq!(fork["ownerUserId"], users[1].1);
    assert_eq!(
        call(service, admin, GET_USER_REPORT_TEMPLATE, id, None).unwrap()["versionNumber"],
        copy["versionNumber"]
    );
    call(
        service,
        admin,
        SET_DEFAULT_REPORT_TEMPLATE,
        0,
        Some(default),
    )
    .unwrap();
    call(
        service,
        &users[0].0,
        DISABLE_USER_REPORT_TEMPLATE,
        id,
        Some(json!({"expectedVersion":copy["versionNumber"]})),
    )
    .unwrap();
}
