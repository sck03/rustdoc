use super::*;
use crate::paths::{RuntimePaths, nonce};
use std::{fs, path::PathBuf, sync::Arc};
mod storage_safety;

struct Workspace(PathBuf);
impl Workspace {
    fn new() -> Self {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf();
        let root = workspace
            .join(".codex-runtime")
            .join("template-file-tests")
            .join(nonce().unwrap());
        fs::create_dir_all(root.join("Cache")).unwrap();
        fs::create_dir_all(root.join("Logs")).unwrap();
        Self(root)
    }
    fn paths(&self) -> RuntimePaths {
        RuntimePaths {
            app_root: self.0.join("App"),
            data_root: self.0.clone(),
            cache_root: self.0.join("Cache"),
            log_root: self.0.join("Logs"),
            font_path: self.0.join("font.otf"),
        }
    }
}
impl Drop for Workspace {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}
fn open(workspace: &Workspace) -> Arc<NativeService> {
    NativeService::open(workspace.paths()).unwrap()
}
fn admin() -> Actor {
    Actor {
        id: 1,
        name: "系统管理员".into(),
        company: "DEFAULT".into(),
        department: "GENERAL".into(),
        admin: true,
        grants: vec![],
    }
}

#[test]
fn storage_check_reports_a_writable_user_root() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let value = storage_check(&service, &admin()).unwrap();
    assert_eq!(value["success"], true);
    assert!(
        value["templateRoot"]
            .as_str()
            .unwrap()
            .contains("Templates")
    );
    assert_eq!(value["writable"], true);
}

#[test]
fn ensure_managed_rejects_traversal_and_foreign_roots() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let user = user_root(&paths);
    assert!(ensure_managed(&user.join("ExportDocument/a.json"), &user).is_ok());
    assert!(ensure_managed(&user.join("../../evil.json"), &user).is_err());
    assert!(ensure_managed(&PathBuf::from("/etc/evil.json"), &user).is_err());
}

#[test]
fn created_template_roundtrips_and_blocks_stale_revisions() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"测试发票模板"}),
    )
    .unwrap();
    assert_eq!(created["success"], true);
    assert_eq!(created["contentEncoding"], "v3-json");
    assert!(
        created["content"]
            .as_str()
            .unwrap()
            .trim_start()
            .starts_with('{'),
        "文件模板 API 必须返回可编辑 V3 JSON"
    );
    let created_path = to_absolute(
        &workspace.paths(),
        created["templatePath"].as_str().unwrap(),
    )
    .unwrap();
    assert!(
        fs::read(&created_path).unwrap().starts_with(b"EXPORTDOCDT"),
        "磁盘上的 .dtpl 必须保持二进制容器"
    );
    let stored = created["templatePath"].as_str().unwrap().to_string();
    let revision = created["revision"].as_str().unwrap().to_string();

    let saved = handle(
        &service,
        &admin(),
        SAVE_REPORT_TEMPLATE_CONTENT,
        &[],
        &[],
        &json!({
            "reportType":"ExportDocument",
            "templatePath":stored,
            "expectedRevision":revision,
            "content":report_templates::starter::create("ExportDocument","测试发票模板-修订").unwrap()
        }),
    )
    .unwrap();
    assert_eq!(saved["success"], true);
    assert_ne!(saved["revision"].as_str().unwrap(), revision);

    let stale = handle(
        &service,
        &admin(),
        SAVE_REPORT_TEMPLATE_CONTENT,
        &[],
        &[],
        &json!({
            "reportType":"ExportDocument",
            "templatePath":stored,
            "expectedRevision":revision,
            "content":report_templates::starter::create("ExportDocument","测试发票模板").unwrap()
        }),
    );
    assert!(stale.is_err());
    assert_eq!(stale.unwrap_err().status, Some(409));
}

#[test]
fn uploaded_file_is_listed_and_downloaded_unchanged() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"占位模板"}),
    )
    .unwrap();
    let target = created["templatePath"].as_str().unwrap();
    let content = report_templates::starter::create("ExportDocument", "上传模板").unwrap();
    let uploaded = upload(
        &service,
        &admin(),
        UPLOAD_REPORT_TEMPLATE_FILE,
        &[],
        &json!({
            "reportType":"ExportDocument",
            "templatePath":target,
            "expectedRevision":created["revision"],
            "displayName":"上传模板"
        }),
        "uploaded.dtpl",
        &report_templates::stored_content("ExportDocument", &content).unwrap(),
    )
    .unwrap();
    assert_eq!(uploaded["success"], true);
    let stored = uploaded["templatePath"].as_str().unwrap();
    assert!(stored.starts_with("user:Export/"));

    let _files = handle(
        &service,
        &admin(),
        CHECK_REPORT_TEMPLATE_STORAGE,
        &[],
        &[],
        &json!({}),
    )
    .unwrap();
    assert!(
        catalog_rows(&service.paths)
            .unwrap()
            .iter()
            .any(|row| row["fileName"].as_str().unwrap_or("") == stored)
    );

    let output = download(
        &service,
        &admin(),
        DOWNLOAD_REPORT_TEMPLATE_FILE,
        &[
            ("reportType", "ExportDocument".into()),
            ("templatePath", stored.into()),
        ],
    )
    .unwrap();
    assert_eq!(
        output.content,
        report_templates::stored_content("ExportDocument", &content).unwrap()
    );
}

#[test]
fn save_to_path_writes_the_chosen_local_file() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"PaymentVoucher","displayName":"测试付款模板"}),
    )
    .unwrap();
    let target = workspace.paths().data_root.join("exported.dtpl");
    let saved = handle(
        &service,
        &admin(),
        SAVE_REPORT_TEMPLATE_FILE_TO_PATH,
        &[],
        &[],
        &json!({
            "reportType":"PaymentVoucher",
            "templatePath":created["templatePath"],
            "filePath":target.to_string_lossy()
        }),
    )
    .unwrap();
    assert_eq!(saved["success"], true);
    assert!(target.is_file());
    assert!(fs::read(&target).unwrap().starts_with(b"EXPORTDOCDT"));
}

#[test]
fn builtin_dtpl_opens_as_editable_v3_json() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    for (kind, path) in [
        ("ExportDocument", "Templates/Export/invoice_template.dtpl"),
        (
            "ExportDocument",
            "Templates/Export/packing_list_template.dtpl",
        ),
        ("ExportDocument", "Templates/Export/contract_template.dtpl"),
        (
            "ExportDocument",
            "Templates/Export/customs_declaration_template.dtpl",
        ),
        (
            "PaymentVoucher",
            "Templates/Internal/payment_voucher_template.dtpl",
        ),
        (
            "PaymentVoucher",
            "Templates/Internal/expense_reimbursement_template.dtpl",
        ),
    ] {
        let builtin_path = workspace.paths().app_root.join(path);
        fs::create_dir_all(builtin_path.parent().unwrap()).unwrap();
        let builtin = export_doc_report::Builtin::find(path).unwrap();
        fs::write(&builtin_path, builtin.source()).unwrap();
        let response = crate::engine::reports::handle(
            &service,
            &admin(),
            GET_REPORT_TEMPLATE_CONTENT,
            &[],
            &[("reportType", kind.into()), ("templatePath", path.into())],
            &json!({}),
        )
        .unwrap();
        assert_eq!(response["contentEncoding"], "v3-json", "{path}");
        let content = response["content"].as_str().unwrap();
        assert!(content.trim_start().starts_with('{'), "{path}");
        let design = crate::designer::Design::from_source(content).unwrap();
        assert_eq!(design.report_type, kind, "{path}");
        assert!(!design.layers.is_empty(), "{path}");
        let mut edited: Value = serde_json::from_str(content).unwrap();
        edited["layers"][0]["name"] = json!("编辑后的页眉");
        let saved = save_template_content(
            &service,
            &admin(),
            kind,
            path,
            response["revision"].as_str().unwrap(),
            &edited.to_string(),
        )
        .unwrap();
        let stored = saved["templatePath"].as_str().unwrap();
        assert!(stored.starts_with("user:"));
        let downloaded = download(
            &service,
            &admin(),
            DOWNLOAD_REPORT_TEMPLATE_FILE,
            &[("reportType", kind.into()), ("templatePath", stored.into())],
        )
        .unwrap();
        let reopened = report_templates::validate_bytes(kind, &downloaded.content).unwrap();
        assert_eq!(reopened.layers[0].name, "编辑后的页眉");
        assert_eq!(fs::read(&builtin_path).unwrap(), builtin.source());
        let reread = crate::engine::reports::handle(
            &service,
            &admin(),
            GET_REPORT_TEMPLATE_CONTENT,
            &[],
            &[("reportType", kind.into()), ("templatePath", stored.into())],
            &json!({}),
        )
        .unwrap();
        assert_eq!(saved["revision"], reread["revision"], "{path}");
    }
}

#[test]
fn html_extensions_and_disguised_payloads_are_rejected_without_overwrite() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = create_template(
        &service,
        &admin(),
        "ExportDocument",
        &json!({"displayName":"单一格式"}),
    )
    .unwrap();
    let path = created["templatePath"].as_str().unwrap();
    let absolute = to_absolute(&service.paths, path).unwrap();
    let before = fs::read(&absolute).unwrap();
    let mut corrupt = before.clone();
    *corrupt.last_mut().unwrap() ^= 1;
    for (name, bytes) in [
        ("old.html", before.as_slice()),
        ("fake.dtpl", b"<html>old template</html>".as_slice()),
        ("json.dtpl", created["content"].as_str().unwrap().as_bytes()),
        ("corrupt.dtpl", corrupt.as_slice()),
    ] {
        let result = upload(
            &service,
            &admin(),
            UPLOAD_REPORT_TEMPLATE_FILE,
            &[],
            &json!({"reportType":"ExportDocument","templatePath":path,"expectedRevision":created["revision"]}),
            name,
            bytes,
        );
        assert_eq!(result.unwrap_err().status, Some(400), "{name}");
        assert_eq!(fs::read(&absolute).unwrap(), before);
    }
    assert!(normalize_new(Path::new("invalid.html")).is_err());
    assert!(
        report_templates::validate_content("ExportDocument", "<html>old template</html>").is_err()
    );
    let mut cross_domain: Value =
        serde_json::from_str(created["content"].as_str().unwrap()).unwrap();
    cross_domain["layers"][0]["elements"][0]["type"] = json!("Field");
    cross_domain["layers"][0]["elements"][0]["fieldPath"] = json!("Payment.CNYAmount");
    assert!(
        report_templates::validate_content("ExportDocument", &cross_domain.to_string()).is_err()
    );
    assert!(
        report_templates::validate_content("PaymentVoucher", created["content"].as_str().unwrap())
            .is_err()
    );
}

#[test]
fn rename_and_display_name_keep_the_catalog_consistent() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"改名前"}),
    )
    .unwrap();
    let stored = created["templatePath"].as_str().unwrap();
    let renamed = handle(
        &service,
        &admin(),
        UPDATE_REPORT_TEMPLATE_DISPLAY_NAME,
        &[],
        &[],
        &json!({
            "reportType":"ExportDocument",
            "templatePath":stored,
            "expectedRevision":created["revision"],
            "displayName":"改名后"
        }),
    )
    .unwrap();
    assert_eq!(renamed["displayName"], "改名后");
    let rows = catalog_rows(&service.paths).unwrap();
    assert!(rows.iter().any(|row| row["name"] == "改名后"));
    assert!(!rows.iter().any(|row| row["name"] == "改名前"));
}

#[test]
fn managed_file_template_can_be_cloned_into_an_editable_user_draft() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"可复制源模板"}),
    )
    .unwrap();
    let source_path = created["templatePath"].as_str().unwrap().to_string();
    let source_content =
        report_templates::starter::create("ExportDocument", "可复制源模板").unwrap();
    let _uploaded = upload(
        &service,
        &admin(),
        UPLOAD_REPORT_TEMPLATE_FILE,
        &[],
        &json!({
            "reportType":"ExportDocument",
            "templatePath":source_path,
            "expectedRevision":created["revision"],
            "displayName":"可复制源模板"
        }),
        "clone-source.dtpl",
        &report_templates::stored_content("ExportDocument", &source_content).unwrap(),
    )
    .unwrap();
    let cloned = crate::engine::report_templates::handle(
        &service,
        &admin(),
        CLONE_USER_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({
            "reportType":"ExportDocument",
            "name":"复制后的个人草稿",
            "sourceTemplatePath":created["templatePath"]
        }),
    )
    .unwrap();
    assert_eq!(cloned["name"], "复制后的个人草稿");
    assert_eq!(cloned["status"], "Draft");
    assert!(
        cloned["contentHtml"]
            .as_str()
            .unwrap()
            .contains("ReportDocument")
    );
}

#[test]
fn managed_file_template_is_published_to_the_report_catalog_and_preview() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"目录贯通模板"}),
    )
    .unwrap();
    let stored = created["templatePath"].as_str().unwrap().to_string();
    let catalog = crate::engine::reports::handle(
        &service,
        &admin(),
        LIST_REPORT_TEMPLATES,
        &[],
        &[("reportType", "ExportDocument".into())],
        &json!({}),
    )
    .unwrap();
    assert!(catalog.as_array().unwrap().iter().any(|row| {
        row["templatePath"] == stored && row["displayName"] == "目录贯通模板"
    }));
    let content = crate::engine::reports::handle(
        &service,
        &admin(),
        PREVIEW_REPORT_TEMPLATE_CONTENT,
        &[],
        &[("reportType", "ExportDocument".into())],
        &json!({
            "content": report_templates::starter::create("ExportDocument", "目录贯通预览").unwrap(),
            "withSeal": false
        }),
    )
    .unwrap();
    assert!(
        content["html"]
            .as_str()
            .unwrap()
            .contains("<!doctype html>")
    );
}

#[test]
fn revision_read_failure_is_reported_as_unavailable_not_a_conflict() {
    let workspace = Workspace::new();
    let directory = workspace.paths().data_root.join("not-a-template-file");
    fs::create_dir_all(&directory).unwrap();
    let result = validate_revision(&directory, "模板", "expected");
    assert_ne!(result.unwrap_err().status, Some(409));
}

#[test]
fn file_transaction_restores_modified_and_created_templates_on_failure() {
    let workspace = Workspace::new();
    let paths = workspace.paths();
    let root = user_root(&paths);
    fs::create_dir_all(root.join(EXPORT_CATEGORY)).unwrap();
    let modified = root.join(EXPORT_CATEGORY).join("modified.dtpl");
    let created = root.join(EXPORT_CATEGORY).join("created.dtpl");
    fs::write(&modified, b"before").unwrap();

    let mut transaction = FileTransaction::new(&paths).unwrap();
    let result = transaction.execute(|transaction| {
        transaction.capture(&modified)?;
        fs::write(&modified, b"after")?;
        transaction.capture(&created)?;
        fs::write(&created, b"created")?;
        Err::<(), _>(unavailable("forced failure"))
    });

    assert!(result.is_err());
    assert_eq!(fs::read(&modified).unwrap(), b"before");
    assert!(!created.exists());
}

#[test]
fn non_v3_managed_template_clone_is_rejected_without_creating_a_draft() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    let created = handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"旧格式源模板"}),
    )
    .unwrap();
    let source_path = created["templatePath"].as_str().unwrap().to_string();
    let uploaded = upload(
        &service,
        &admin(),
        UPLOAD_REPORT_TEMPLATE_FILE,
        &[],
        &json!({
            "reportType":"ExportDocument",
            "templatePath":source_path,
            "expectedRevision":created["revision"],
            "displayName":"旧格式源模板"
        }),
        "legacy-source.dtpl",
        &report_templates::stored_content(
            "ExportDocument",
            &report_templates::starter::create("ExportDocument", "旧格式源模板").unwrap(),
        )
        .unwrap(),
    )
    .unwrap();
    let stored_path = uploaded["templatePath"].as_str().unwrap();
    let absolute = to_absolute(&workspace.paths(), stored_path).unwrap();
    fs::write(&absolute, b"<html><body>legacy template</body></html>").unwrap();
    let before = service.store.all("report-templates").unwrap().len();
    let result = crate::engine::report_templates::handle(
        &service,
        &admin(),
        CLONE_USER_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({
            "reportType":"ExportDocument",
            "name":"不应创建的草稿",
            "sourceTemplatePath":created["templatePath"]
        }),
    );
    assert!(result.is_err());
    assert_eq!(service.store.all("report-templates").unwrap().len(), before);
}
#[test]
fn package_roundtrip_imports_uploaded_entries() {
    let workspace = Workspace::new();
    let service = open(&workspace);
    handle(
        &service,
        &admin(),
        CREATE_REPORT_TEMPLATE,
        &[],
        &[],
        &json!({"reportType":"ExportDocument","displayName":"打包源"}),
    )
    .unwrap();
    let package_target = workspace.paths().data_root.join("package.edtpl");
    handle(
        &service,
        &admin(),
        SAVE_REPORT_TEMPLATE_PACKAGE_TO_PATH,
        &[],
        &[],
        &json!({"packagePath":package_target.to_string_lossy()}),
    )
    .unwrap();
    assert!(package_target.is_file());
    let imported = import_template_package(
        &service,
        &admin(),
        &media::read_local(&package_target, MAX_PACKAGE_BYTES).unwrap(),
        "merge",
    )
    .unwrap();
    assert_eq!(imported["success"], true);
}
