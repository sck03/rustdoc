use super::*;

fn create(service: &NativeService, name: &str) -> Value {
    handle(
        service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":name}),
    )
    .unwrap()
}

#[test]
fn concurrent_display_changes_have_one_winner_and_keep_other_catalog_rows() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = create(&service, "并发源");
    let other = create(&service, "另一模板");
    let barrier = Arc::new(std::sync::Barrier::new(2));
    let results = std::thread::scope(|scope| {
        let handles: Vec<_> = ["名称甲", "名称乙"]
            .into_iter()
            .map(|name| {
                let service = &service;
                let barrier = &barrier;
                let created = &created;
                scope.spawn(move || {
                barrier.wait();
                handle(service, &admin(), UPDATE_REPORT_TEMPLATE_DISPLAY_NAME, &[], &[],
                    &json!({"reportType":"ExportDocument","templatePath":created["templatePath"],
                        "expectedRevision":created["revision"],"displayName":name}))
            })
            })
            .collect();
        handles
            .into_iter()
            .map(|handle| handle.join().unwrap())
            .collect::<Vec<_>>()
    });
    assert_eq!(results.iter().filter(|result| result.is_ok()).count(), 1);
    assert_eq!(
        results.into_iter().find_map(Result::err).unwrap().status,
        Some(409)
    );
    let rows = catalog_rows(&service.paths).unwrap();
    assert_eq!(rows.len(), 2);
    assert!(
        rows.iter()
            .any(|row| row["fileName"] == other["templatePath"] && row["name"] == "另一模板")
    );
}

#[test]
fn package_roundtrip_restores_the_exported_default_in_another_data_root() {
    let source = Workspace::new();
    let source_service = open(&source);
    let created = create(&source_service, "默认模板");
    handle(
        &source_service,
        &admin(),
        SET_DEFAULT_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","templatePath":created["templatePath"]}),
    )
    .unwrap();
    let (bytes, _) = package_bytes(&source_service, &admin()).unwrap();
    let entries = extract_package(&bytes).unwrap();
    let manifest: Value = serde_json::from_slice(&entries["config.json"]).unwrap();
    assert_eq!(
        manifest["TemplateDefaults"]["ExportDocumentTemplatePath"],
        created["templatePath"]
    );
    let target = Workspace::new();
    let target_service = open(&target);
    import_template_package(&target_service, &admin(), &bytes, "Overwrite").unwrap();
    let settings = settings_of(&target_service.store).unwrap();
    assert_eq!(
        settings["reportTemplateDefaults"]["exportDocumentTemplatePath"],
        created["templatePath"]
    );
}

#[test]
fn failed_rollback_keeps_the_only_recovery_copy() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let target = user_root(&paths).join("Export/recover.dtpl");
    fs::create_dir_all(target.parent().unwrap()).unwrap();
    fs::write(&target, b"original").unwrap();
    let mut transaction = FileTransaction::new(&paths).unwrap();
    let result = transaction.execute(|files| {
        files.capture(&target)?;
        fs::remove_file(&target)?;
        fs::create_dir(&target)?;
        Err::<(), _>(unavailable("injected failure"))
    });
    assert!(result.unwrap_err().message.contains("恢复快照保留"));
    let directory = fs::read_dir(paths.cache_root.join("TemplateTransactions"))
        .unwrap()
        .next()
        .unwrap()
        .unwrap()
        .path();
    assert_eq!(fs::read(directory.join("0.bak")).unwrap(), b"original");
    let manifest: Value =
        serde_json::from_slice(&fs::read(directory.join("manifest.json")).unwrap()).unwrap();
    assert_eq!(manifest["files"][0]["target"], "Export/recover.dtpl");
    drop(transaction);
    assert!(FileTransaction::new(&paths).is_err());
}

#[test]
fn resource_listing_ignores_email_template_history() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    service.store.transaction(|tx| {
        store::save(tx, "template-versions", 0,
            json!({"templateKind":"email-templates","content":{"contentHtml":"<p>邮件正文</p>"}}),
            None, &admin(), "version")
    }).unwrap();
    let result = report_assets::handle(
        &service,
        &admin(),
        QUERY_REPORT_TEMPLATE_V3_IMAGE_RESOURCES,
        &[],
        &[],
        &json!({}),
    )
    .unwrap();
    assert_eq!(result["totalCount"], 0);
}
