use super::*;
#[path = "../../../../../tests/support/approval_fixture.rs"]
mod fixture;

#[test]
fn bundles_keep_valid_long_names_and_refuse_to_consume_disk_headroom() {
    use std::{
        fs,
        io::{Cursor, Read},
        sync::Arc,
    };
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let input = directory.0.join("source.bin");
    fs::write(&input, b"content").unwrap();
    let names = [
        "approval.pdf".to_owned(),
        format!("{}.pdf", "a".repeat(176)),
        format!("{}.xml", "文".repeat(83)),
    ];
    let source = Source {
        files: names
            .iter()
            .enumerate()
            .map(|(index, name)| Entry {
                id: index as i64,
                kind: String::new(),
                info: export_doc_storage::BlobInfo {
                    file_name: name.clone(),
                    media_type: "application/octet-stream".into(),
                    size: 7,
                    digest: String::new(),
                },
            })
            .collect(),
        file_name: "bundle.zip".into(),
        manifest: b"{}".to_vec(),
    };
    let budget = Arc::new(crate::temporary_space::Budget::with_available(
        directory.0.clone(),
        512 * 1024 * 1024,
    ));
    let mut output = Cursor::new(Vec::new());
    write_bundle(budget.writer(&mut output).unwrap(), &source, |_| {
        Ok(fs::File::open(&input)?)
    })
    .unwrap();
    let mut zip = zip::ZipArchive::new(Cursor::new(output.into_inner())).unwrap();
    assert_eq!(zip.len(), 4);
    for (index, name) in names.iter().enumerate() {
        let entry_name = if index == 0 {
            name.clone()
        } else {
            format!("{index:02}/{name}")
        };
        let mut content = String::new();
        zip.by_name(&entry_name)
            .unwrap()
            .read_to_string(&mut content)
            .unwrap();
        assert_eq!(content, "content");
    }
    let low = Arc::new(crate::temporary_space::Budget::with_available(
        directory.0.clone(),
        256 * 1024 * 1024,
    ));
    let mut output = Cursor::new(Vec::new());
    assert!(
        write_bundle(low.writer(&mut output).unwrap(), &source, |_| Ok(
            fs::File::open(&input)?
        ))
        .is_err()
    );
    assert!(
        output.into_inner().is_empty(),
        "ZIP headers must obey the same disk budget as attachment bytes"
    );
}

#[test]
fn busy_document_generation_recovers_and_download_rechecks_request_access() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
    service.workflow_scheduler.stop().unwrap();
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
    let node = fixture::node("review", &[], "Named", json!([reviewer_id]));
    let mut handling = fixture::node("handle", &["review"], "Named", json!([reviewer_id]));
    handling["kind"] = json!("Handling");
    handling["phase"] = json!("Fulfillment");
    let mut document = fixture::node("document", &["review"], "Named", json!([]));
    document["kind"] = json!("System");
    document["phase"] = json!("Fulfillment");
    document["systemAction"] = json!("GenerateApprovalDocument");
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap()["stages"] = json!([node, handling, document]);
    settings["expectedVersion"] = settings["versionNumber"].clone();
    let saved = fixture::save_settings(&service, admin, settings).unwrap();
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
            json!({"requestKey":crate::paths::nonce().unwrap(),"employeeId":applicant["employee"]["id"],"title":"完整下载并发边界","reason":"下载准备之后撤销申请查看权限","category":"Other"}),
        ),
    );
    let id = draft["id"].as_i64().unwrap();
    let upload = crate::generated_api::ALL_OPERATIONS
        .iter()
        .copied()
        .find(|op| metadata(*op).is_some_and(|m| m["kind"] == "general" && m["action"] == "upload"))
        .unwrap();
    let draft = service
        .upload(
            upload,
            &[("id", id.to_string())],
            json!({"expectedVersion":draft["versionNumber"],"purpose":"Supporting"}),
            "supporting.xml",
            b"<?xml version=\"1.0\"?><Supporting>anonymous</Supporting>",
            admin,
        )
        .unwrap();
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
    let administrator =
        auth::current_actor(&service.store, login["user"]["id"].as_i64().unwrap()).unwrap();
    let mut occupied = vec![];
    for _ in 0..2 {
        let (release, held) = std::sync::mpsc::channel();
        let job = service
            .jobs
            .start(
                &administrator,
                crate::generated_api::GET_JOB,
                "test",
                "Occupied worker",
                move |_| {
                    held.recv_timeout(std::time::Duration::from_secs(30))
                        .unwrap();
                    Ok(TaskOutput::file(
                        "test.txt".into(),
                        "text/plain",
                        b"test".to_vec(),
                    ))
                },
            )
            .unwrap();
        occupied.push((release, job));
    }
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
    assert_eq!(
        row["status"], "Approved",
        "busy document preparation must not undo approval"
    );
    for _ in 0..2 {
        crate::engine::oa::document_schedule::tick(&service).unwrap();
        service
            .store
            .read(|tx| {
                let pending = store::get(tx, "oa-general", id)?;
                assert!(crate::engine::oa::fulfillment::active_system(
                    &pending,
                    export_doc_domain::approval_graph::SystemAction::GenerateApprovalDocument
                )?);
                let snapshot =
                    submissions::checked(tx, &pending, row["submissionId"].as_i64().unwrap())?;
                assert!(text(&snapshot, "documentError").is_empty());
                Ok(())
            })
            .unwrap();
    }
    for (release, job) in occupied {
        release.send(()).unwrap();
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
        while service
            .jobs
            .get(&administrator, job["jobId"].as_str().unwrap())
            .unwrap()["status"]
            != "Succeeded"
        {
            assert!(std::time::Instant::now() < deadline);
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
    }
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    loop {
        crate::engine::oa::document_schedule::tick(&service).unwrap();
        let started = service
            .store
            .read(|tx| {
                let snapshot =
                    submissions::checked(tx, &row, row["submissionId"].as_i64().unwrap())?;
                Ok(!text(&snapshot, "documentJobId").is_empty())
            })
            .unwrap();
        if started {
            break;
        }
        assert!(
            std::time::Instant::now() < deadline,
            "scheduler must start the deferred document without manual retry"
        );
        std::thread::sleep(std::time::Duration::from_millis(10));
    }
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
            assert!(super::super::super::materials::full(
                tx, &current, &row, &snapshot
            )?);
            Ok(())
        })
        .unwrap();
    service
        .store
        .transaction(|tx| {
            let mut user = store::get(tx, "users", reviewer_id)?;
            user["permissionGrants"]
                .as_array_mut()
                .unwrap()
                .retain(|g| !(g["resourceKey"] == "office.general" && g["action"] == "view"));
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
        .read(|tx| gather(tx, &actor, meta, &parameters));
    assert_eq!(
        result.err().unwrap().status,
        Some(403),
        "the second snapshot must recheck request access before returning the archive"
    );
    service.close().unwrap();
}
