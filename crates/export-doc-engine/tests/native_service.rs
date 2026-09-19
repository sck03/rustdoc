use export_doc_engine::{
    api::ApiClient, contracts, generated_api::*, invoice::InvoiceDraft, paths::nonce,
};
use serde_json::{Value, json};
#[path = "support/native_fixture.rs"]
mod native_fixture;
use native_fixture::Fixture;

#[test]
fn personnel_directory_images_and_employment_events_use_the_public_contract() {
    let fixture = Fixture::new();
    let mut person=fixture.create(CREATE_PERSONNEL,json!({"requestKey":nonce().unwrap(),"employeeNumber":"HR-001","departmentId":"GENERAL","jobTitle":"业务助理","employmentType":"FullTime","hireDate":"2026-09-01","onProbation":true,"profile":{"fullName":"档案测试人员","personalPhone":"private-phone-canary","workEmail":"native@example.invalid"}}));
    let id = person["employee"]["id"].as_i64().unwrap();
    let directory: Value = fixture
        .client()
        .json(LIST_PERSONNEL, &[], &[], None)
        .unwrap();
    export_doc_contracts::validation::response(LIST_PERSONNEL.id, &directory).unwrap();
    assert!(!directory.to_string().contains("private-phone-canary"));
    assert!(
        !directory["items"][0]["departmentName"]
            .as_str()
            .unwrap()
            .is_empty()
    );
    let private_search: Value = fixture
        .client()
        .json(
            LIST_PERSONNEL,
            &[],
            &[("keyword", "private-phone-canary".into())],
            None,
        )
        .unwrap();
    assert_eq!(private_search["totalCount"], 0);
    let managers: Value = fixture
        .client()
        .json(
            LIST_ORGANIZATION_MANAGERS,
            &[],
            &[
                ("companyCode", "DEFAULT".into()),
                ("keyword", "HR-001".into()),
            ],
            None,
        )
        .unwrap();
    export_doc_contracts::validation::response(LIST_ORGANIZATION_MANAGERS.id, &managers).unwrap();
    assert_eq!(managers["items"][0]["id"], id);
    let other: Value = fixture
        .client()
        .json(
            LIST_ORGANIZATION_MANAGERS,
            &[],
            &[("companyCode", "OTHER".into())],
            None,
        )
        .unwrap();
    assert_eq!(other["totalCount"], 0);
    let mut image_bytes = std::io::Cursor::new(Vec::new());
    image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(
        2,
        2,
        image::Rgba([50, 120, 80, 255]),
    ))
    .write_to(&mut image_bytes, image::ImageFormat::Png)
    .unwrap();
    person = fixture
        .client()
        .upload(
            UPLOAD_PERSONNEL_IMAGE,
            &[("id", id.to_string()), ("kind", "Avatar".into())],
            json!({"expectedVersion":person["versionNumber"]}),
            "avatar.png",
            image_bytes.get_ref(),
        )
        .unwrap();
    assert_eq!(person["images"][0]["kind"], "Avatar");
    person = fixture.request(GET_PERSONNEL, Some(id), None);
    assert_eq!(person["canCorrectRegistration"], true);
    assert_eq!(person["canLinkAccount"], false);
    let image = fixture
        .client()
        .bytes(
            GET_PERSONNEL_IMAGE,
            &[("id", id.to_string()), ("kind", "Avatar".into())],
            &[],
            None,
            1024 * 1024,
        )
        .unwrap();
    assert_eq!(image, image_bytes.into_inner());
    person=fixture.request(CONFIRM_PERSONNEL,Some(id),Some(json!({"expectedVersion":person["versionNumber"],"effectiveDate":"2026-09-17","note":"已完成试用考核"})));
    assert_eq!(person["employee"]["status"], "Active");
    assert_eq!(person["canCorrectRegistration"], false);
    let history = fixture
        .client()
        .json::<Value>(
            GET_PERSONNEL_HISTORY,
            &[("id", id.to_string())],
            &[("pageSize", "2".into())],
            None,
        )
        .unwrap();
    export_doc_contracts::validation::response(GET_PERSONNEL_HISTORY.id, &history).unwrap();
    assert_eq!(history["totalCount"], 3);
    assert_eq!(history["items"][0]["action"], "Confirm");
    assert_eq!(history["items"][0]["note"], "已完成试用考核");
    assert_eq!(history["hasNextPage"], true);
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                DELETE_PERSONNEL,
                &[("id", id.to_string())],
                &[],
                Some(json!({"expectedVersion":person["versionNumber"],"reason":"不应删除"}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
}

#[test]
fn dashboard_prefers_actual_data_and_keeps_previous_month_totals() {
    use chrono::Datelike;
    let fixture = Fixture::new();
    let today = export_doc_engine::clock::BusinessClock::default()
        .now()
        .unwrap()
        .today;
    let previous = today
        .with_day(1)
        .unwrap()
        .checked_sub_months(chrono::Months::new(1))
        .unwrap();
    let actual = InvoiceDraft::demo(&today.to_string(), "DASHBOARD-SAME-NUMBER")
        .build()
        .unwrap();
    let saved = fixture.create(CREATE_INVOICE, json!(actual));
    let mut customs = actual.clone();
    customs.r#type = "报关数据".into();
    for item in &mut customs.items {
        item.unit_price = rust_decimal::Decimal::ONE;
    }
    fixture.create(CREATE_INVOICE, json!(customs));
    let earlier = fixture.create(
        CREATE_INVOICE,
        json!(
            InvoiceDraft::demo(&previous.to_string(), "DASHBOARD-PREVIOUS")
                .build()
                .unwrap()
        ),
    );
    let duplicate = fixture
        .client()
        .json::<Value>(CREATE_INVOICE, &[], &[], Some(json!(actual)))
        .unwrap_err();
    assert_eq!(duplicate.status, Some(409));
    let dashboard = fixture.request(GET_DASHBOARD, None, None);
    export_doc_contracts::validation::response(GET_DASHBOARD.id, &dashboard).unwrap();
    assert_eq!(
        dashboard["monthlyExportAmount"],
        saved["invoice"]["totalAmount"]
    );
    assert_eq!(dashboard["monthlyProfit"], saved["invoice"]["totalProfit"]);
    assert_eq!(
        dashboard["previousMonthlyExportAmount"],
        earlier["invoice"]["totalAmount"]
    );
    assert_eq!(dashboard["monthlyInvoiceCount"], 1);
    assert_eq!(dashboard["totalActiveCount"], 2);
    assert_eq!(dashboard["pendingCount"], 2);
    assert_eq!(dashboard["recentInvoices"][1]["id"], saved["invoice"]["id"]);
}

#[test]
fn attachment_versions_require_confirmation_and_keep_auditable_reasons() {
    let fixture = Fixture::new();
    let invoice = fixture.create(
        CREATE_INVOICE,
        json!(
            InvoiceDraft::demo("2026-09-17", "ATTACHMENT-NATIVE")
                .build()
                .unwrap()
        ),
    );
    let invoice_id = invoice["invoice"]["id"].as_i64().unwrap();
    let category = fixture.create(
        CREATE_BUSINESS_ATTACHMENT_CATEGORY,
        json!({"companyScope":"DEFAULT","name":"客户确认资料"}),
    );
    let metadata = json!({"title":"装运确认","categoryId":category["id"],"poNumber":"PO-01","styleNo":"","uploadKey":nonce().unwrap()});
    let first = fixture
        .client()
        .upload(
            UPLOAD_BUSINESS_ATTACHMENT,
            &[("invoiceId", invoice_id.to_string())],
            metadata.clone(),
            "confirmation.txt",
            b"version one",
        )
        .unwrap();
    let id = first["id"].as_i64().unwrap();
    let confirmed=fixture.request(UPDATE_BUSINESS_ATTACHMENT,Some(id),Some(json!({"expectedVersion":first["versionNumber"],"currentRevision":1,"isArchived":false,"note":"客户邮件确认"})));
    let mut replacement = metadata.clone();
    replacement["attachmentId"] = json!(id);
    replacement["expectedVersion"] = confirmed["versionNumber"].clone();
    replacement["uploadKey"] = json!(nonce().unwrap());
    let second = fixture
        .client()
        .upload(
            UPLOAD_BUSINESS_ATTACHMENT,
            &[("invoiceId", invoice_id.to_string())],
            replacement.clone(),
            "confirmation.txt",
            b"version two",
        )
        .unwrap();
    assert_eq!(second["latestRevision"], 2);
    assert_eq!(second["currentRevision"], 1);
    let details = fixture.request(GET_BUSINESS_ATTACHMENT, Some(id), None);
    export_doc_contracts::validation::response(GET_BUSINESS_ATTACHMENT.id, &details).unwrap();
    assert_eq!(details["events"][0]["revision"], 1);
    assert_eq!(details["events"][0]["note"], "客户邮件确认");
    assert_eq!(fixture.client().json::<Value>(UPDATE_BUSINESS_ATTACHMENT,&[("id",id.to_string())],&[],Some(json!({"expectedVersion":first["versionNumber"],"currentRevision":2,"isArchived":false,"note":"过期版本"}))).unwrap_err().status,Some(409));
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                DELETE_BUSINESS_ATTACHMENT_CATEGORY,
                &[("id", category["id"].to_string())],
                &[],
                Some(json!({"expectedVersion":category["versionNumber"],"reason":"仍在使用"}))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let archived=fixture.request(UPDATE_BUSINESS_ATTACHMENT,Some(id),Some(json!({"expectedVersion":second["versionNumber"],"currentRevision":1,"isArchived":true,"note":"已被后续单据替代"})));
    replacement["expectedVersion"] = archived["versionNumber"].clone();
    replacement["uploadKey"] = json!(nonce().unwrap());
    assert_eq!(
        fixture
            .client()
            .upload(
                UPLOAD_BUSINESS_ATTACHMENT,
                &[("invoiceId", invoice_id.to_string())],
                replacement,
                "confirmation.txt",
                b"version three"
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let bytes = fixture
        .client()
        .bytes(
            DOWNLOAD_BUSINESS_ATTACHMENT,
            &[("id", id.to_string()), ("revision", "1".into())],
            &[],
            None,
            1024,
        )
        .unwrap();
    assert_eq!(bytes, b"version one");
    let list: Value = fixture
        .client()
        .json(
            LIST_BUSINESS_ATTACHMENTS,
            &[],
            &[
                ("invoiceId", invoice_id.to_string()),
                ("includeArchived", "true".into()),
            ],
            None,
        )
        .unwrap();
    export_doc_contracts::validation::response(LIST_BUSINESS_ATTACHMENTS.id, &list).unwrap();
    assert_eq!(list["usedBytes"], 22);
}

#[test]
fn implemented_parameterless_reads_match_the_generated_response_structure() {
    let fixture = Fixture::new();
    let mut failures = vec![];
    for operation in ALL_OPERATIONS
        .iter()
        .filter(|operation| operation.method == "GET" && fixture.client().supports(**operation))
        // Online providers have dedicated injected transport/parser tests; this
        // sweep checks deterministic database-backed response contracts.
        .filter(|operation| {
            ![
                LIST_EXCHANGE_RATES,
                LIST_AVAILABLE_EXCHANGE_RATE_CURRENCIES,
                GET_HS_CODE_REMOTE_HEALTH,
                SEARCH_REMOTE_HS_CODES,
                LIST_CLOUD_DATABASE_BACKUPS,
                LIST_POSTGRE_SQL_PHYSICAL_BACKUPS,
            ]
            .contains(operation)
        })
    {
        let parameters = &contracts::contract()["operations"][operation.id]["parameters"];
        if parameters
            .as_array()
            .into_iter()
            .flatten()
            .any(|parameter| parameter["required"] == true)
            || contracts::response(operation.id).is_null()
        {
            continue;
        }
        match fixture.client().json::<Value>(*operation, &[], &[], None) {
            Ok(value) => {
                if let Err(error) = export_doc_contracts::validation::response(operation.id, &value)
                {
                    failures.push(format!("{}: {error}", operation.id));
                }
            }
            Err(error) => failures.push(format!("{}: {error}", operation.id)),
        }
    }
    assert!(failures.is_empty(), "{}", failures.join("\n"));
}

#[test]
fn settings_keep_reference_defaults_and_reject_stale_or_invalid_updates() {
    let fixture = Fixture::new();
    let original = fixture.request(GET_SETTINGS, None, None);
    assert_eq!(original["settings"]["system"]["itemEntryBlankRowCount"], 20);
    assert_eq!(original["settings"]["system"]["databaseProvider"], "Sqlite");
    assert_eq!(original["settings"]["email"]["smtpPort"], 587);
    let mut body = json!({"settings":original["settings"],"updateSecrets":false});
    body["settings"]["system"]["itemEntrySpareColumnCount"] = json!(5);
    let validated = fixture.request(VALIDATE_SETTINGS, None, Some(body.clone()));
    export_doc_contracts::validation::response(VALIDATE_SETTINGS.id, &validated).unwrap();
    assert_eq!(validated["isValid"], true);
    let saved = fixture.request(UPDATE_SETTINGS, None, Some(body.clone()));
    export_doc_contracts::validation::response(UPDATE_SETTINGS.id, &saved).unwrap();
    assert_eq!(saved["settings"]["revision"], 1);
    assert_eq!(
        fixture
            .client()
            .json::<Value>(UPDATE_SETTINGS, &[], &[], Some(body.clone()))
            .unwrap_err()
            .status,
        Some(409)
    );
    body["settings"]["revision"] = json!(1);
    body["settings"]["system"]["itemEntrySpareColumnCount"] = json!(11);
    assert_eq!(
        fixture.request(VALIDATE_SETTINGS, None, Some(body.clone()))["isValid"],
        false
    );
    assert_eq!(
        fixture
            .client()
            .json::<Value>(UPDATE_SETTINGS, &[], &[], Some(body))
            .unwrap_err()
            .status,
        Some(400)
    );
    assert_eq!(
        fixture.request(GET_SETTINGS, None, None)["settings"]["system"]["itemEntrySpareColumnCount"],
        5
    );
}

#[test]
fn worklist_preserves_source_counts_filtering_and_the_react_page_contract() {
    let fixture = Fixture::new();
    for number in ["TODO-001", "TODO-002"] {
        let invoice = InvoiceDraft::demo("2026-09-16", number).build().unwrap();
        fixture.request(CREATE_INVOICE, None, Some(json!(invoice)));
    }
    let page: Value = fixture
        .client()
        .json(
            GET_WORKLIST,
            &[],
            &[("pageSize", "1".into()), ("pageNumber", "2".into())],
            None,
        )
        .unwrap();
    export_doc_contracts::validation::response(GET_WORKLIST.id, &page).unwrap();
    assert_eq!(page["page"]["totalCount"], 2);
    assert_eq!(page["page"]["items"].as_array().unwrap().len(), 1);
    assert_eq!(page["page"]["items"][0]["source"], "invoice-review");
    assert_eq!(page["sources"][0]["count"], 2);
    let overdue: Value = fixture
        .client()
        .json(GET_WORKLIST, &[], &[("due", "Overdue".into())], None)
        .unwrap();
    assert_eq!(overdue["page"]["totalCount"], 0);
    assert_eq!(
        fixture
            .client()
            .json::<Value>(GET_WORKLIST, &[], &[("source", "unknown".into())], None)
            .unwrap_err()
            .status,
        Some(400)
    );
}

#[test]
fn native_database_survives_restart_and_rejects_a_second_instance() {
    let mut fixture = Fixture::new();
    assert!(ApiClient::native(fixture.paths.clone()).is_err());
    let invoice = InvoiceDraft::demo("2026-09-16", "NATIVE-PERSIST")
        .build()
        .unwrap();
    let saved = fixture.client().save_invoice(&invoice).unwrap();
    fixture.client.take();
    let client = ApiClient::native(fixture.paths.clone())
        .unwrap()
        .login("admin".into(), String::new())
        .unwrap()
        .0;
    assert_eq!(client.get_invoice(saved.id).unwrap(), saved);
    fixture.client = Some(client);
}
#[test]
fn invoice_concurrency_and_state_are_enforced_by_the_rust_backend() {
    let fixture = Fixture::new();
    let draft = InvoiceDraft::demo("2026-09-16", "NATIVE-STATE");
    let saved = fixture
        .client()
        .save_invoice(&draft.build().unwrap())
        .unwrap();
    let updated = fixture.client().save_invoice(&saved).unwrap();
    assert_eq!(
        fixture.client().save_invoice(&saved).unwrap_err().status,
        Some(409)
    );
    let verified = fixture.request(
        TRANSITION_INVOICE_STATUS,
        Some(updated.id),
        Some(json!({"rowVersion":updated.row_version,"targetStatus":"Verified"})),
    );
    assert_eq!(verified["invoice"]["status"], "Verified");
    let detail = fixture.client().get_invoice(updated.id).unwrap();
    assert_eq!(
        fixture.client().save_invoice(&detail).unwrap_err().status,
        Some(409)
    );
    let reversed = fixture.request(
        UNVERIFY_INVOICE,
        Some(updated.id),
        Some(json!({"rowVersion":detail.row_version,"note":"测试重新核对"})),
    );
    assert_eq!(reversed["invoice"]["status"], "Draft");
}
#[test]
fn crm_supplier_and_payment_records_roundtrip_and_keep_optional_fields() {
    let fixture = Fixture::new();
    let crm = fixture.create(
        CREATE_CRM_CUSTOMER,
        json!({"name":"巴黎客户 é","countryRegion":"FR","notes":"原始备注"}),
    );
    let loaded = fixture.request(GET_CRM_CUSTOMER, crm["id"].as_i64(), None);
    assert_eq!(loaded["notes"], "原始备注");
    assert_eq!(loaded["status"], "潜在客户");
    let supplier = fixture.create(
        CREATE_SUPPLIER,
        json!({"name":"测试供应商","mainProducts":"棉衬衫"}),
    );
    assert_eq!(supplier["status"], "考察中");
    let admitted = fixture.request(
        ADMIT_SUPPLIER,
        supplier["id"].as_i64(),
        Some(json!({"expectedVersion":supplier["versionNumber"]})),
    );
    assert_eq!(admitted["status"], "合作中");
    let payment=fixture.create(CREATE_PAYMENT,json!({"invoiceNo":"NATIVE-PAY","cnyAmount":1234.56,"payeeName":"供应商","paymentDate":"2026-09-16","spare10":"保留备用十"}));
    assert_eq!(payment["payment"]["spare10"], "保留备用十");
    assert_eq!(payment["payment"]["cnyAmount"], json!(1234.56));
}
#[test]
fn meeting_bookings_reject_overlaps_but_allow_adjacent_times() {
    let fixture = Fixture::new();
    let person = fixture.employee();
    let room=fixture.create(CREATE_MEETING_ROOM,json!({"name":"会议室一","capacity":12,"maximumBookingHours":8,"advanceBookingDays":90,"isActive":true,"requiresKey":true}));
    let day = (chrono::Local::now().date_naive() + chrono::Duration::days(1)).to_string();
    let request = json!({"requestKey":nonce().unwrap(),"meetingRoomId":room["id"],"employeeId":person["id"],"title":"业务会议","attendeeCount":5,"startsAt":format!("{day}T10:00:00+08:00"),"endsAt":format!("{day}T11:00:00+08:00")});
    let booking = fixture.create(CREATE_MEETING_BOOKING, request.clone());
    assert_eq!(booking["status"], "Approved");
    let availability: Value = fixture
        .client()
        .json(
            GET_MEETING_ROOM_AVAILABILITY,
            &[("id", room["id"].to_string())],
            &[
                ("from", format!("{day}T00:00:00+08:00")),
                ("to", format!("{day}T23:59:00+08:00")),
            ],
            None,
        )
        .unwrap();
    export_doc_contracts::validation::response(GET_MEETING_ROOM_AVAILABILITY.id, &availability)
        .unwrap();
    assert_eq!(availability.as_array().unwrap().len(), 1);
    assert!(availability[0].get("applicantName").is_none());
    let clearance = fixture.request(GET_PERSONNEL_CLEARANCE, person["id"].as_i64(), None);
    export_doc_contracts::validation::response(GET_PERSONNEL_CLEARANCE.id, &clearance).unwrap();
    assert_eq!(clearance["meetingCount"], 1);
    assert_eq!(clearance["canDepart"], false);
    let mut overlapping = request;
    overlapping["requestKey"] = json!(nonce().unwrap());
    let failed = fixture
        .client()
        .json::<Value>(
            CREATE_MEETING_BOOKING,
            &[],
            &[],
            Some(contracts::overlay(
                contracts::object(CREATE_MEETING_BOOKING.id, true),
                &overlapping,
            )),
        )
        .unwrap_err();
    assert_eq!(failed.status, Some(409));
    overlapping["startsAt"] = json!(format!("{day}T11:00:00+08:00"));
    overlapping["endsAt"] = json!(format!("{day}T12:00:00+08:00"));
    assert_eq!(
        fixture.create(CREATE_MEETING_BOOKING, overlapping)["status"],
        "Approved"
    );
}
#[test]
fn stock_reservation_issue_partial_return_and_cancellation_are_atomic() {
    let fixture = Fixture::new();
    let person = fixture.employee();
    let supply = fixture.create(
        CREATE_OFFICE_SUPPLY,
        json!({"name":"测试电脑","unit":"台","isActive":true,"isReturnable":true,"minimumStock":1}),
    );
    fixture.request(RESTOCK_OFFICE_SUPPLY,supply["id"].as_i64(),Some(json!({"operationId":nonce().unwrap(),"quantity":10,"expectedVersion":supply["versionNumber"],"note":"期初登记"})));
    let due_date = export_doc_engine::clock::BusinessClock::default()
        .now()
        .unwrap()
        .today
        .to_string();
    let request=fixture.create(CREATE_OFFICE_SUPPLY_REQUEST,json!({"requestKey":nonce().unwrap(),"officeSupplyId":supply["id"],"employeeId":person["id"],"quantity":3,"purpose":"办公使用","returnDueDate":due_date}));
    let supplies = fixture.request(LIST_OFFICE_SUPPLIES, None, None);
    assert_eq!(supplies["items"][0]["availableQuantity"], 7);
    assert_eq!(supplies["items"][0]["stockQuantity"], 10);
    let issued = fixture.request(
        ISSUE_OFFICE_SUPPLY,
        request["id"].as_i64(),
        Some(json!({"expectedVersion":request["versionNumber"],"quantity":3})),
    );
    assert_eq!(issued["status"], "Issued");
    let returned = fixture.request(
        RETURN_OFFICE_SUPPLY,
        request["id"].as_i64(),
        Some(json!({"expectedVersion":issued["versionNumber"],"quantity":1})),
    );
    assert_eq!(returned["status"], "Issued");
    assert_eq!(returned["returnedQuantity"], 1);
    let returned = fixture.request(
        RETURN_OFFICE_SUPPLY,
        request["id"].as_i64(),
        Some(json!({"expectedVersion":returned["versionNumber"],"quantity":2})),
    );
    assert_eq!(returned["status"], "Returned");
    let supplies = fixture.request(LIST_OFFICE_SUPPLIES, None, None);
    assert_eq!(supplies["items"][0]["stockQuantity"], 10);
    assert_eq!(supplies["items"][0]["reservedQuantity"], 0);
    let excessive = json!({"requestKey":nonce().unwrap(),"officeSupplyId":supply["id"],"employeeId":person["id"],"quantity":11,"purpose":"测试不足","returnDueDate":due_date});
    assert_eq!(
        fixture
            .client()
            .json::<Value>(
                CREATE_OFFICE_SUPPLY_REQUEST,
                &[],
                &[],
                Some(contracts::overlay(
                    contracts::object(CREATE_OFFICE_SUPPLY_REQUEST.id, true),
                    &excessive
                ))
            )
            .unwrap_err()
            .status,
        Some(409)
    );
    let after = fixture.request(LIST_OFFICE_SUPPLIES, None, None);
    assert_eq!(after["items"][0]["stockQuantity"], 10);
    assert_eq!(after["items"][0]["reservedQuantity"], 0);
}
#[test]
fn organization_cycles_and_referenced_deletion_are_rejected() {
    let fixture = Fixture::new();
    let parent = fixture.create(
        CREATE_ORGANIZATION_DEPARTMENT,
        json!({"code":"SALES","name":"销售部","companyCode":"DEFAULT","isActive":true}),
    );
    let child=fixture.create(CREATE_ORGANIZATION_DEPARTMENT,json!({"code":"EXPORT","name":"出口组","companyCode":"DEFAULT","parentCode":"SALES","isActive":true}));
    let mut update = parent.clone();
    update["parentCode"] = child["code"].clone();
    update["expectedVersion"] = parent["versionNumber"].clone();
    let failure = fixture
        .client()
        .json::<Value>(
            UPDATE_ORGANIZATION_DEPARTMENT,
            &[("code", "SALES".into())],
            &[],
            Some(update),
        )
        .unwrap_err();
    assert_eq!(failure.status, Some(400));
    let failure = fixture
        .client()
        .json::<Value>(
            DELETE_ORGANIZATION_DEPARTMENT,
            &[("code", "SALES".into())],
            &[],
            Some(json!({"expectedVersion":parent["versionNumber"]})),
        )
        .unwrap_err();
    assert_eq!(failure.status, Some(409));
}
#[test]
fn an_assigned_empty_template_does_not_fall_back_to_role_defaults() {
    let fixture = Fixture::new();
    let template = fixture.create(
        CREATE_PERMISSION_TEMPLATE,
        json!({"code":"EMPTY","name":"未授权岗位","isActive":true,"grants":[]}),
    );
    let created=fixture.create(CREATE_USER_ACCOUNT,json!({"username":"reader","fullName":"只读测试","role":"User","permissionTemplateId":template["id"],"departmentId":"GENERAL","companyScope":"DEFAULT","isActive":true,"resetPassword":"Native-Test-2026"}));
    assert_eq!(created["success"], true);
    let client = fixture
        .client()
        .login("reader".into(), "Native-Test-2026".into())
        .unwrap()
        .0;
    assert_eq!(
        client
            .json::<Value>(LIST_INVOICES, &[], &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        client
            .json::<Value>(LIST_USERS, &[], &[], None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        fixture
            .client()
            .login("reader".into(), "wrong password".into())
            .err()
            .unwrap()
            .status,
        Some(401)
    );
}
