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
    let body = body.map(|value| contracts::overlay(contracts::object(op.id, true), &value));
    let result =
        serde_json::from_slice(&service.dispatch(op, &parameters, &[], body, token)?).unwrap();
    export_doc_contracts::validation::response(op.id, &result).unwrap();
    Ok(result)
}

fn unlink_roundtrip(service: &NativeService, admin: &str, person: Value) -> Value {
    let id = person["employee"]["id"].as_i64().unwrap();
    let account = &person["account"];
    let before = call(service, admin, LIST_USERS, 0, None).unwrap();
    let before = before["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|user| user["id"] == account["id"])
        .unwrap();
    let login = call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":account["username"],"password":"Personnel-Test-2026"})),
    )
    .unwrap();
    let body = json!({"expectedVersion":person["versionNumber"],"userId":account["id"],"expectedAccountVersion":account["versionNumber"],"reason":"纠正账号关联"});
    for (field, value, status) in [
        ("expectedVersion", json!(0), 409),
        ("expectedAccountVersion", json!(0), 409),
        ("userId", json!(99999999), 409),
        ("reason", json!("  "), 400),
    ] {
        let mut invalid = body.clone();
        invalid[field] = value;
        assert_eq!(
            call(service, admin, UNLINK_PERSONNEL_ACCOUNT, id, Some(invalid))
                .unwrap_err()
                .status,
            Some(status)
        );
        assert_eq!(
            call(service, admin, GET_PERSONNEL, id, None).unwrap()["account"],
            *account
        );
    }
    let unlinked = call(
        service,
        admin,
        UNLINK_PERSONNEL_ACCOUNT,
        id,
        Some(body.clone()),
    )
    .unwrap();
    assert!(unlinked["account"].is_null());
    assert!(!unlinked["deleteRestriction"].as_str().unwrap().is_empty());
    assert_eq!(unlinked["employee"]["id"], id);
    assert_eq!(unlinked["profile"], person["profile"]);
    assert_eq!(
        call(service, admin, UNLINK_PERSONNEL_ACCOUNT, id, Some(body))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        call(
            service,
            login["accessToken"].as_str().unwrap(),
            GET_CURRENT_USER,
            0,
            None
        )
        .unwrap_err()
        .status,
        Some(401)
    );
    let users = call(service, admin, LIST_USERS, 0, None).unwrap();
    let user = users["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|user| user["id"] == account["id"])
        .unwrap();
    assert!(user["employee"].is_null());
    for field in [
        "username",
        "fullName",
        "isActive",
        "departmentId",
        "companyScope",
        "role",
        "permissionTemplateId",
    ] {
        assert_eq!(user[field], before[field], "unlink must preserve {field}");
    }
    let history = call(service, admin, GET_PERSONNEL_HISTORY, id, None).unwrap();
    assert!(
        history["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|event| event["action"] == "UnlinkAccount"
                && event["note"].as_str().unwrap().contains("纠正账号关联"))
    );
    call(service, admin, LINK_PERSONNEL_ACCOUNT, id, Some(json!({"expectedVersion":unlinked["versionNumber"],"userId":user["id"],"expectedAccountVersion":user["versionNumber"]}))).unwrap()
}

pub fn exercise(service: &NativeService, admin: &str) {
    let suffix = nonce().unwrap();
    let body = json!({"requestKey":suffix,"employeeNumber":format!("AUDIT-{}", &suffix[..8]),"departmentId":"GENERAL","jobTitle":"业务员","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":false,"profile":{"fullName":"资料校验"},
        "account":{"id":1,"role":"Admin"},"attachments":[{"id":"forged"}],"images":[{"kind":"Avatar","contentHash":"forged"}]});
    let mut person = call(service, admin, CREATE_PERSONNEL, 0, Some(body.clone())).unwrap();
    let id = person["employee"]["id"].as_i64().unwrap();
    assert!(
        person["account"].is_null(),
        "request cannot inject account linkage"
    );
    assert_eq!(person["images"], json!([]));
    assert_eq!(person["attachments"], json!([]));
    assert_eq!(person["canDelete"], true);
    assert_eq!(person["deleteRestriction"], "");
    assert_eq!(
        call(service, admin, CREATE_PERSONNEL, 0, Some(body.clone())).unwrap()["employee"]["id"],
        id
    );
    let mut changed = body.clone();
    changed["profile"]["fullName"] = json!("不同资料");
    assert_eq!(
        call(service, admin, CREATE_PERSONNEL, 0, Some(changed))
            .err()
            .expect("request must be denied")
            .status,
        Some(409)
    );
    let path = [("id", id.to_string())];
    let image_path = [("id", id.to_string()), ("kind", "Avatar".into())];
    let mut image = std::io::Cursor::new(vec![]);
    image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(
        2,
        2,
        image::Rgba([20, 80, 30, 255]),
    ))
    .write_to(&mut image, image::ImageFormat::Png)
    .unwrap();
    person = service
        .upload(
            UPLOAD_PERSONNEL_IMAGE,
            &image_path,
            json!({"expectedVersion":person["versionNumber"]}),
            "avatar.png",
            image.get_ref(),
            admin,
        )
        .unwrap();
    let avatar = person["employee"]["avatarHash"].clone();
    person = call(service, admin, UPDATE_PERSONNEL, id, Some(json!({"expectedVersion":person["versionNumber"],"profile":{"fullName":"已改名"},"employmentType":"FullTime","account":{"id":1},"images":[]}))).unwrap();
    assert_eq!(person["employee"]["avatarHash"], avatar);
    assert_eq!(person["images"].as_array().unwrap().len(), 1);
    assert!(person["account"].is_null());
    let version = person["versionNumber"].clone();
    let pdf = b"%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n";
    person = service
        .upload(
            UPLOAD_PERSONNEL_ATTACHMENT,
            &path,
            json!({"expectedVersion":version}),
            "劳动合同.pdf",
            pdf,
            admin,
        )
        .unwrap();
    let retry = service
        .upload(
            UPLOAD_PERSONNEL_ATTACHMENT,
            &path,
            json!({"expectedVersion":version}),
            "劳动合同.pdf",
            pdf,
            admin,
        )
        .unwrap();
    assert_eq!(retry["versionNumber"], person["versionNumber"]);
    assert_eq!(retry["attachments"].as_array().unwrap().len(), 1);
    let attachment_path = [
        ("id", id.to_string()),
        (
            "attachmentId",
            person["attachments"][0]["id"].as_str().unwrap().into(),
        ),
    ];
    assert_eq!(
        service
            .download_file(DOWNLOAD_PERSONNEL_ATTACHMENT, &attachment_path, admin)
            .unwrap()
            .content,
        pdf
    );
    assert_eq!(
        service
            .upload(
                UPLOAD_PERSONNEL_ATTACHMENT,
                &path,
                json!({"expectedVersion":person["versionNumber"]}),
                "伪装.png",
                pdf,
                admin
            )
            .err()
            .expect("request must be denied")
            .status,
        Some(400)
    );
    let account = call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":format!("person-{suffix}"),"fullName":"普通员工","role":"OfficeEmployee","departmentId":"GENERAL","companyScope":"DEFAULT","isActive":true,"resetPassword":"Personnel-Test-2026"}))).unwrap()["user"].clone();
    let login = || {
        call(
            service,
            "",
            LOGIN,
            0,
            Some(json!({"username":account["username"],"password":"Personnel-Test-2026"})),
        )
        .unwrap()
    };
    let staff = login();
    let staff_token = staff["accessToken"].as_str().unwrap();
    assert_eq!(
        service
            .download_file(DOWNLOAD_PERSONNEL_ATTACHMENT, &attachment_path, staff_token)
            .err()
            .expect("request must be denied")
            .status,
        Some(403)
    );
    assert_eq!(
        service
            .upload(
                UPLOAD_PERSONNEL_ATTACHMENT,
                &path,
                json!({"expectedVersion":person["versionNumber"]}),
                "越权.pdf",
                pdf,
                staff_token
            )
            .err()
            .expect("request must be denied")
            .status,
        Some(403)
    );
    let company = format!("PRIVATE-{}", &suffix[..8]);
    let department = format!("PRIVATE-HR-{}", &suffix[..8]);
    call(
        service,
        admin,
        CREATE_ORGANIZATION_COMPANY,
        0,
        Some(json!({"code":company,"name":"其他公司","isActive":true})),
    )
    .unwrap();
    call(
        service,
        admin,
        CREATE_ORGANIZATION_DEPARTMENT,
        0,
        Some(json!({"code":department,"companyCode":company,"name":"人事部","isActive":true})),
    )
    .unwrap();
    let other_name = format!("private-{suffix}");
    call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":other_name,"fullName":"其他公司人事","role":"PersonnelManager","departmentId":department,"companyScope":company,"isActive":true,"resetPassword":"Personnel-Test-2026"}))).unwrap();
    let other = call(
        service,
        "",
        LOGIN,
        0,
        Some(json!({"username":other_name,"password":"Personnel-Test-2026"})),
    )
    .unwrap();
    assert_eq!(
        service
            .download_file(
                DOWNLOAD_PERSONNEL_ATTACHMENT,
                &attachment_path,
                other["accessToken"].as_str().unwrap()
            )
            .err()
            .expect("request must be denied")
            .status,
        Some(403)
    );
    let wrong_parent = [
        ("id", "99999999".into()),
        ("attachmentId", attachment_path[1].1.clone()),
    ];
    assert_eq!(
        service
            .download_file(DOWNLOAD_PERSONNEL_ATTACHMENT, &wrong_parent, admin)
            .err()
            .expect("request must be denied")
            .status,
        Some(404)
    );
    assert_eq!(
        person["deleteRestriction"], "",
        "edits and attachments do not prevent correction"
    );
    person = call(service, admin, LINK_PERSONNEL_ACCOUNT, id, Some(json!({"expectedVersion":person["versionNumber"],"userId":account["id"],"expectedAccountVersion":account["versionNumber"]}))).unwrap();
    assert_eq!(
        person["canDelete"], true,
        "administrator retains permission"
    );
    assert!(
        person["deleteRestriction"]
            .as_str()
            .unwrap()
            .contains("已关联登录账号")
    );
    let users = call(service, admin, LIST_USERS, 0, None).unwrap();
    let linked = users["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|u| u["id"] == account["id"])
        .unwrap();
    assert_eq!(linked["employee"]["id"], id);
    assert_eq!(linked["employee"]["jobTitle"], "业务员");
    assert_eq!(
        linked["employee"].as_object().unwrap().len(),
        5,
        "Account summary excludes private HR data"
    );
    for active in [false, true] {
        let saved = call(service, admin, UPDATE_USER_ACCOUNT, account["id"].as_i64().unwrap(), Some(json!({
            "username":account["username"], "fullName":person["profile"]["fullName"], "role":"OfficeEmployee",
            "departmentId":"GENERAL", "companyScope":"DEFAULT", "isActive":active, "resetPassword":"",
            "expectedVersion":person["account"]["versionNumber"], "employee":{"id":999999}
        }))).unwrap()["user"].clone();
        assert_eq!(
            saved["employee"]["id"], id,
            "Account writes cannot change the personnel link"
        );
        person = call(service, admin, GET_PERSONNEL, id, None).unwrap();
        assert_eq!(
            person["account"]["isActive"], active,
            "Personnel detail reads current login status"
        );
        assert_eq!(person["account"]["versionNumber"], saved["versionNumber"]);
    }
    let other_person = call(service, admin, CREATE_PERSONNEL, 0, Some(json!({
        "requestKey":nonce().unwrap(),"employeeNumber":format!("SECOND-{}", &suffix[..8]),
        "departmentId":"GENERAL","jobTitle":"助理","employmentType":"FullTime","hireDate":"2026-09-01",
        "onProbation":false,"profile":{"fullName":"另一员工"}
    }))).unwrap();
    let other_id = other_person["employee"]["id"].as_i64().unwrap();
    assert_eq!(
        call(
            service,
            admin,
            LINK_PERSONNEL_ACCOUNT,
            other_id,
            Some(json!({
                "expectedVersion":other_person["versionNumber"],"userId":account["id"],
                "expectedAccountVersion":person["account"]["versionNumber"]
            }))
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    let options = call(
        service,
        admin,
        LIST_PERSONNEL_ACCOUNT_OPTIONS,
        other_id,
        None,
    )
    .unwrap();
    assert!(
        options["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|u| u["id"] != account["id"])
    );
    let staff = login();
    let staff_token = staff["accessToken"].as_str().unwrap();
    let spare = call(service, admin, CREATE_USER_ACCOUNT, 0, Some(json!({"username":format!("spare-{suffix}"),"fullName":"候选账号","role":"OfficeEmployee","departmentId":"GENERAL","companyScope":"DEFAULT","isActive":true,"resetPassword":"Personnel-Test-2026"}))).unwrap()["user"].clone();
    let draft = call(service, staff_token, CREATE_GENERAL_REQUEST, 0, Some(json!({"requestKey":nonce().unwrap(),"employeeId":if service.provider().unwrap() == "SQLite" {Some(id)} else {None},"title":"更换账号前的未结申请","reason":"保护申请人关联","category":"IT"}))).unwrap();
    let link = json!({"expectedVersion":person["versionNumber"],"userId":spare["id"],"expectedAccountVersion":spare["versionNumber"]});
    let unlink = json!({"expectedVersion":person["versionNumber"],"userId":account["id"],"expectedAccountVersion":person["account"]["versionNumber"],"reason":"有未结申请时不得解绑"});
    for (token, status) in [
        (admin, 409),
        (staff_token, 403),
        (other["accessToken"].as_str().unwrap(), 403),
    ] {
        assert_eq!(
            call(
                service,
                token,
                UNLINK_PERSONNEL_ACCOUNT,
                id,
                Some(unlink.clone())
            )
            .unwrap_err()
            .status,
            Some(status)
        );
    }
    assert_eq!(
        call(
            service,
            admin,
            LINK_PERSONNEL_ACCOUNT,
            id,
            Some(link.clone())
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(
        call(service, admin, GET_PERSONNEL, id, None).unwrap()["account"]["id"],
        account["id"]
    );
    call(
        service,
        staff_token,
        CANCEL_GENERAL_REQUEST,
        draft["id"].as_i64().unwrap(),
        Some(json!({"expectedVersion":draft["versionNumber"],"note":"完成交接后再关联"})),
    )
    .unwrap();
    person = unlink_roundtrip(service, admin, person);
    let mut link = link;
    link["expectedVersion"] = person["versionNumber"].clone();
    let denied = call(
        service,
        admin,
        LINK_PERSONNEL_ACCOUNT,
        id,
        Some(link.clone()),
    )
    .unwrap_err();
    assert_eq!(
        denied.status,
        Some(409),
        "Even after clearance, changing accounts requires an explicit unlink"
    );
    assert_eq!(
        call(service, admin, GET_PERSONNEL, id, None).unwrap(),
        person
    );
    let unlink = |person: &Value| {
        call(service, admin, UNLINK_PERSONNEL_ACCOUNT, id, Some(json!({"expectedVersion":person["versionNumber"],"userId":person["account"]["id"],"expectedAccountVersion":person["account"]["versionNumber"],"reason":"更换关联账号"}))).unwrap()
    };
    let original_account = person["account"].clone();
    person = unlink(&person);
    link["expectedVersion"] = person["versionNumber"].clone();
    person = call(service, admin, LINK_PERSONNEL_ACCOUNT, id, Some(link)).unwrap();
    assert_eq!(person["account"]["id"], spare["id"]);
    person = unlink(&person);
    let users = call(service, admin, LIST_USERS, 0, None).unwrap();
    let original_account = users["users"]
        .as_array()
        .unwrap()
        .iter()
        .find(|user| user["id"] == original_account["id"])
        .unwrap();
    person = call(service, admin, LINK_PERSONNEL_ACCOUNT, id, Some(json!({"expectedVersion":person["versionNumber"],"userId":account["id"],"expectedAccountVersion":original_account["versionNumber"]}))).unwrap();
    let today = login()["user"]["businessDate"].as_str().unwrap().to_owned();
    let future =
        chrono::NaiveDate::parse_from_str(&today, "%Y-%m-%d").unwrap() + chrono::Duration::days(1);
    assert_eq!(call(service, admin, TRANSFER_PERSONNEL, id, Some(json!({"expectedVersion":person["versionNumber"],"departmentId":"GENERAL","jobTitle":"新岗位","effectiveDate":future.to_string(),"note":"不允许未来日期立即调岗"}))).unwrap_err().status, Some(400));
    if service.provider().unwrap() == "PostgreSQL" {
        let staff = login();
        person = call(service, admin, UPDATE_PERSONNEL, id, Some(json!({"expectedVersion":person["versionNumber"],"profile":{"fullName":"账号同步姓名"},"employmentType":"FullTime"}))).unwrap();
        assert_eq!(person["account"]["fullName"], "账号同步姓名");
        assert_eq!(
            call(
                service,
                staff["accessToken"].as_str().unwrap(),
                GET_CURRENT_USER,
                0,
                None
            )
            .err()
            .expect("request must be denied")
            .status,
            Some(401)
        );
        let department = format!("TRANSFER-{}", &suffix[..8]);
        call(service, admin, CREATE_ORGANIZATION_DEPARTMENT, 0, Some(json!({"code":department,"companyCode":"DEFAULT","name":"调岗部门","isActive":true}))).unwrap();
        person = call(service, admin, TRANSFER_PERSONNEL, id, Some(json!({"expectedVersion":person["versionNumber"],"departmentId":department,"jobTitle":"新岗位","effectiveDate":"2026-09-02","note":"部门调整"}))).unwrap();
        assert_eq!(login()["user"]["departmentId"], department);
        assert_eq!(
            login()["user"]["role"],
            "OfficeEmployee",
            "Job changes do not assign permissions"
        );
        let staff = login();
        person = call(service, admin, DEPART_PERSONNEL, id, Some(json!({"expectedVersion":person["versionNumber"],"effectiveDate":"2026-09-03","note":"完成交接离职"}))).unwrap();
        assert_eq!(person["account"]["isActive"], false);
        assert_eq!(
            call(
                service,
                staff["accessToken"].as_str().unwrap(),
                GET_CURRENT_USER,
                0,
                None
            )
            .err()
            .expect("request must be denied")
            .status,
            Some(401)
        );
        assert_eq!(
            call(
                service,
                "",
                LOGIN,
                0,
                Some(json!({"username":account["username"],"password":"Personnel-Test-2026"}))
            )
            .err()
            .expect("request must be denied")
            .status,
            Some(401)
        );
        let corrected = service
            .upload(
                UPLOAD_PERSONNEL_ATTACHMENT,
                &path,
                json!({"expectedVersion":person["versionNumber"]}),
                "离职后.pdf",
                b"%PDF-1.7\n% Separation document\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n",
                admin,
            )
            .unwrap();
        assert_eq!(corrected["attachments"].as_array().unwrap().len(), 2);
        assert_eq!(corrected["employee"]["status"], "Departed");
        assert_eq!(corrected["account"]["isActive"], false);
    } else {
        let removed: Value = serde_json::from_slice(
            &service
                .dispatch(
                    DELETE_PERSONNEL_ATTACHMENT,
                    &attachment_path,
                    &[],
                    Some(json!({"expectedVersion":person["versionNumber"],"note":"资料有误"})),
                    admin,
                )
                .unwrap(),
        )
        .unwrap();
        assert_eq!(removed["attachments"], json!([]));
        assert_eq!(
            service
                .download_file(DOWNLOAD_PERSONNEL_ATTACHMENT, &attachment_path, admin)
                .err()
                .expect("request must be denied")
                .status,
            Some(404)
        );
    }
}
