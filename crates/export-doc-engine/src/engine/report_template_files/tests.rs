use super::*;
use crate::paths::{RuntimePaths, nonce};
use std::{fs, path::PathBuf, sync::Arc};

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
        "uploaded.html",
        content.as_bytes(),
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
    assert_eq!(output.content, content.as_bytes());
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
    let target = workspace.paths().data_root.join("exported.html");
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
    assert!(
        fs::read_to_string(&target)
            .unwrap()
            .contains("ReportDocument")
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
        "clone-source.html",
        source_content.as_bytes(),
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
        "legacy-source.html",
        report_templates::starter::create("ExportDocument", "旧格式源模板")
            .unwrap()
            .as_bytes(),
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
