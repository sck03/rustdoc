use super::super::oa_contract::call;
use export_doc_engine::{contracts, engine::NativeService, generated_api::*, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(service: &NativeService, root: &str) {
    let suffix = &nonce().unwrap()[..8];
    let company = format!("HP-{suffix}");
    let department = format!("HPD-{suffix}");
    call(
        service,
        root,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"办理权限回归","isActive":true})),
    )
    .unwrap();
    call(
        service,
        root,
        CREATE_ORGANIZATION_DEPARTMENT,
        0,
        Some(json!({"code":department,"companyCode":company,"name":"办理部","isActive":true})),
    )
    .unwrap();
    let grants = json!([
        {"resourceKey":"office.general","action":"view","dataScope":"own"},
        {"resourceKey":"office.general","action":"complete","dataScope":"own"}
    ]);
    let scheme = call(service, root, CREATE_PERMISSION_TEMPLATE, 0, Some(json!({"code":format!("HP-{suffix}"),"name":"办理权限","isActive":true,"grants":grants}))).unwrap();
    let scheme = crate::oa_contract::workflow::publish_scheme(service, root, scheme);
    let create_user = |name: &str, role: &str, direct: bool, template: Value| {
        call(service, root, CREATE_USER_ACCOUNT, 0, Some(json!({"username":format!("hp-{name}-{suffix}"),"fullName":name,"role":role,
            "companyScope":company,"departmentId":department,"isActive":true,"resetPassword":"Handling-Permissions-2026",
            "permissionTemplateId":template,"permissionSchemeVersion":if direct||template.is_null(){Value::Null}else{scheme["publishedVersion"].clone()},"permissionGrants":if direct {grants.clone()} else {Value::Null}}))).unwrap()["user"].clone()
    };
    let admin = create_user("admin", "Admin", false, Value::Null);
    let inherited = create_user("inherited", "OfficeEmployee", false, scheme["id"].clone());
    let independent = create_user("independent", "OfficeEmployee", true, scheme["id"].clone());
    let login = |user: &Value| {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":user["username"],"password":"Handling-Permissions-2026"})),
        )
        .unwrap()["accessToken"]
            .as_str()
            .unwrap()
            .to_owned()
    };
    let admin_token = login(&admin);
    let inherited_token = login(&inherited);
    let independent_token = login(&independent);
    let mut settings = call(service, &admin_token, GET_OA_APPROVAL_SETTINGS, 0, None).unwrap();
    let save_settings = |mut settings: Value| {
        settings["expectedVersion"] = settings["versionNumber"].clone();
        call(
            service,
            &admin_token,
            SAVE_OA_APPROVAL_SETTINGS,
            0,
            Some(settings),
        )
        .unwrap()
    };
    settings["handlingServices"] = json!([{"key":"seal","name":"公章","category":"Seal","handlerUserIds":[inherited["id"]],"isActive":true}]);
    settings = save_settings(settings);
    let read_user = |id: &Value| {
        call(service, root, LIST_USERS, 0, None).unwrap()["users"]
            .as_array()
            .unwrap()
            .iter()
            .find(|user| user["id"] == *id)
            .unwrap()
            .clone()
    };
    let read_scheme = |id: &Value| {
        call(service, root, LIST_PERMISSION_TEMPLATES, 0, None).unwrap()["templates"]
            .as_array()
            .unwrap()
            .iter()
            .find(|row| row["id"] == *id)
            .unwrap()
            .clone()
    };
    for patch in [
        json!({"isActive":false}),
        json!({"permissionGrants":[]}),
        json!({"disabledModules":["office.general"]}),
    ] {
        let mut body = contracts::overlay(inherited.clone(), &patch);
        body["expectedVersion"] = inherited["versionNumber"].clone();
        assert_eq!(
            call(
                service,
                root,
                UPDATE_USER_ACCOUNT,
                inherited["id"].as_i64().unwrap(),
                Some(body)
            )
            .unwrap_err()
            .status,
            Some(409)
        );
        assert_eq!(
            read_user(&inherited["id"])["versionNumber"],
            inherited["versionNumber"]
        );
        call(service, &inherited_token, GET_CURRENT_USER, 0, None).unwrap();
    }
    let mut current_scheme = scheme.clone();
    for patch in [
        json!({"isActive":false}),
        json!({"grants":[]}),
        json!({"disabledModules":["office.general"]}),
    ] {
        let mut body = contracts::overlay(scheme.clone(), &patch);
        body["expectedVersion"] = current_scheme["versionNumber"].clone();
        let draft = call(
            service,
            root,
            UPDATE_PERMISSION_TEMPLATE,
            scheme["id"].as_i64().unwrap(),
            Some(body),
        )
        .unwrap();
        call(service, &inherited_token, GET_CURRENT_USER, 0, None).unwrap();
        current_scheme = crate::oa_contract::workflow::publish_scheme(service, root, draft);
        assert_eq!(
            crate::oa_contract::workflow::apply_scheme(
                service,
                root,
                &current_scheme,
                "Account",
                &inherited
            )
            .unwrap_err()
            .status,
            Some(409)
        );
        assert_eq!(
            read_user(&inherited["id"])["permissionSchemeVersion"],
            scheme["publishedVersion"]
        );
    }
    settings["handlingServices"][0]["handlerUserIds"] = json!([independent["id"]]);
    settings = save_settings(settings);
    let mut stopped = inherited.clone();
    stopped["isActive"] = json!(false);
    stopped["expectedVersion"] = inherited["versionNumber"].clone();
    call(
        service,
        root,
        UPDATE_USER_ACCOUNT,
        inherited["id"].as_i64().unwrap(),
        Some(stopped),
    )
    .unwrap();
    let mut reduced = scheme.clone();
    reduced["grants"] = json!([]);
    reduced["expectedVersion"] = current_scheme["versionNumber"].clone();
    call(
        service,
        root,
        UPDATE_PERMISSION_TEMPLATE,
        scheme["id"].as_i64().unwrap(),
        Some(reduced),
    )
    .unwrap();
    call(service, &independent_token, GET_CURRENT_USER, 0, None).unwrap();

    // Editing a preset draft never enlarges either an existing or a new binding.
    let original = call(service, root, LIST_PERMISSION_TEMPLATES, 0, None).unwrap()["templates"]
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["code"] == "OfficeEmployee")
        .unwrap()
        .clone();
    let mut enabled = original.clone();
    for grant in grants.as_array().unwrap() {
        if !enabled["grants"].as_array().unwrap().iter().any(|old| {
            old["resourceKey"] == grant["resourceKey"] && old["action"] == grant["action"]
        }) {
            enabled["grants"]
                .as_array_mut()
                .unwrap()
                .push(grant.clone());
        }
    }
    enabled["disabledModules"] = json!([]);
    enabled["expectedVersion"] = original["versionNumber"].clone();
    let enabled = call(
        service,
        root,
        UPDATE_PERMISSION_TEMPLATE,
        original["id"].as_i64().unwrap(),
        Some(enabled),
    )
    .unwrap();
    let default_user = create_user("default", "OfficeEmployee", false, Value::Null);
    assert_eq!(default_user["permissionSchemeVersion"], 1);
    let default_login = login(&default_user);
    let capabilities = call(service, &default_login, GET_CURRENT_USER, 0, None).unwrap();
    assert!(
        !capabilities["capabilities"]["permissions"]
            .as_array()
            .unwrap_or_else(|| capabilities["user"]["capabilities"]["permissions"]
                .as_array()
                .unwrap())
            .iter()
            .any(|g| g["resourceKey"] == "office.general" && g["action"] == "complete")
    );
    let enabled = crate::oa_contract::workflow::publish_scheme(service, root, enabled);
    let mut binding = default_user.clone();
    binding["permissionTemplateId"] = enabled["id"].clone();
    binding["permissionSchemeVersion"] = enabled["publishedVersion"].clone();
    binding["permissionGrants"] = Value::Null;
    binding["expectedVersion"] = default_user["versionNumber"].clone();
    let default_user = call(
        service,
        root,
        UPDATE_USER_ACCOUNT,
        default_user["id"].as_i64().unwrap(),
        Some(binding),
    )
    .unwrap()["user"]
        .clone();
    settings["handlingServices"][0]["handlerUserIds"] = json!([default_user["id"]]);
    settings = save_settings(settings);
    let mut invalid = enabled.clone();
    invalid["disabledModules"] = json!(["office.general"]);
    invalid["expectedVersion"] = enabled["versionNumber"].clone();
    let invalid = call(
        service,
        root,
        UPDATE_PERMISSION_TEMPLATE,
        enabled["id"].as_i64().unwrap(),
        Some(invalid),
    )
    .unwrap();
    let invalid = crate::oa_contract::workflow::publish_scheme(service, root, invalid);
    assert_eq!(
        crate::oa_contract::workflow::apply_scheme(
            service,
            root,
            &invalid,
            "Account",
            &default_user
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    settings["handlingServices"][0]["handlerUserIds"] = json!([independent["id"]]);
    save_settings(settings);
    let mut restored = original;
    restored["expectedVersion"] = read_scheme(&enabled["id"])["versionNumber"].clone();
    call(
        service,
        root,
        UPDATE_PERMISSION_TEMPLATE,
        restored["id"].as_i64().unwrap(),
        Some(restored.clone()),
    )
    .unwrap();
}
