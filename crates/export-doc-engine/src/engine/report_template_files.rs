//! Legacy file-based report template catalog: managed template roots,
//! builtin:/user: storage paths, single HTML file transfer and .edtpl packages.
mod catalog;
mod package;
mod transfer;
use super::report_templates;
use super::{
    NativeService, auth,
    error::{Result, conflict, error, invalid, unavailable},
    media,
    records::text,
    report_assets,
    store::{self, Actor, Store},
    tasks::FileOutput,
};
use crate::{
    contracts,
    generated_api::*,
    paths::{self, RuntimePaths},
};
use catalog::*;
use export_doc_storage::Connection;
use package::*;
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    fs,
    io::{Cursor, Read, Write},
    path::{Path, PathBuf},
};
pub use transfer::{download, upload};
use unicode_normalization::UnicodeNormalization;
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};

const PERMISSION: &str = "document.report-templates";
const EXTENSION: &str = ".html";
/// `Path::extension()` 不含点，与 `EXTENSION` 后缀分开比较。
const EXTENSION_NAME: &str = "html";
pub(super) const PACKAGE_EXTENSION: &str = ".edtpl";
pub(super) const PACKAGE_SCHEMA_VERSION: &str = "1.3";
const CATALOG_FILE: &str = "report_templates.json";
const BUILTIN_PREFIX: &str = "builtin:";
const USER_PREFIX: &str = "user:";
const USER_TEMPLATE_PREFIX: &str = "user-template:";
pub(super) const EXPORT_CATEGORY: &str = "Export";
pub(super) const INTERNAL_CATEGORY: &str = "Internal";
const MAX_TEMPLATE_BYTES: usize = 10 * 1024 * 1024;
pub(super) const MAX_PACKAGE_BYTES: usize = 50 * 1024 * 1024;
const MAX_PACKAGE_ENTRIES: usize = 2000;
const STORAGE_POLICY: &str = "内置模板从程序根 Templates/ 只读加载；新建、编辑副本、重命名、删除和模板包导入统一写入运行数据根 Templates/，不会改写已安装程序资源。";
const PACKAGE_POLICY: &str = "模板包导出路径来自用户显式输入，相对路径解析到运行数据根 TemplatePackages/；只打包和导入用户模板，内置模板保持只读；临时文件使用运行数据根缓存目录。";
const FILE_POLICY: &str = "单个 HTML 模板文件通过用户显式路径导入或导出；导入仍写入运行数据根 Templates/，内置模板不会被改写。";
const CHECK_POLICY: &str =
    "程序根 Templates/ 仅保存随程序发布的只读内置模板；可写性检查只创建短生命周期探针并立即删除。";

pub const OPERATIONS: &[Operation] = &[
    CHECK_REPORT_TEMPLATE_STORAGE,
    CREATE_REPORT_TEMPLATE,
    DELETE_REPORT_TEMPLATE,
    DOWNLOAD_REPORT_TEMPLATE_FILE,
    DOWNLOAD_REPORT_TEMPLATE_PACKAGE,
    IMPORT_REPORT_TEMPLATE_FILE,
    IMPORT_REPORT_TEMPLATE_PACKAGE,
    RENAME_REPORT_TEMPLATE,
    SAVE_REPORT_TEMPLATE_CONTENT,
    SAVE_REPORT_TEMPLATE_FILE_TO_PATH,
    SAVE_REPORT_TEMPLATE_PACKAGE_TO_PATH,
    SET_DEFAULT_REPORT_TEMPLATE,
    UPDATE_REPORT_TEMPLATE_DISPLAY_NAME,
    UPLOAD_REPORT_TEMPLATE_FILE,
    UPLOAD_REPORT_TEMPLATE_PACKAGE,
];
pub const UPLOADS: &[Operation] = &[UPLOAD_REPORT_TEMPLATE_FILE, UPLOAD_REPORT_TEMPLATE_PACKAGE];
pub const LOCAL: &[Operation] = &[
    IMPORT_REPORT_TEMPLATE_FILE,
    IMPORT_REPORT_TEMPLATE_PACKAGE,
    SAVE_REPORT_TEMPLATE_FILE_TO_PATH,
    SAVE_REPORT_TEMPLATE_PACKAGE_TO_PATH,
];

pub(super) fn load_template_content(
    service: &NativeService,
    actor: &Actor,
    kind: &str,
    stored: &str,
) -> Result<(String, String)> {
    auth::authorize(actor, PERMISSION, "view")?;
    let resolved = resolve_editable(service, kind, stored, true)?;
    let content = fs::read_to_string(&resolved.path)?;
    if content.len() > MAX_TEMPLATE_BYTES {
        return Err(invalid("报表模板内容超过允许的大小。"));
    }
    Ok((resolved.display, content))
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    match operation {
        CHECK_REPORT_TEMPLATE_STORAGE => storage_check(service, actor),
        CREATE_REPORT_TEMPLATE => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "publish")?;
            create_template(service, actor, kind, body)
        }
        SAVE_REPORT_TEMPLATE_CONTENT => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "publish")?;
            save_template_content(
                service,
                actor,
                kind,
                &text(body, "templatePath"),
                &text(body, "expectedRevision"),
                &text(body, "content"),
            )
        }
        RENAME_REPORT_TEMPLATE => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "publish")?;
            rename_template(service, actor, kind, body)
        }
        UPDATE_REPORT_TEMPLATE_DISPLAY_NAME => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "publish")?;
            update_display_name(service, actor, kind, body)
        }
        SET_DEFAULT_REPORT_TEMPLATE => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "publish")?;
            set_default_template(service, actor, kind, body)
        }
        DELETE_REPORT_TEMPLATE => {
            let kind = report_templates::report_type(&value_of(parameters, query, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "archive")?;
            delete_template(service, actor, kind, parameters, query)
        }
        IMPORT_REPORT_TEMPLATE_FILE => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "import")?;
            import_template_file(service, actor, kind, body)
        }
        IMPORT_REPORT_TEMPLATE_PACKAGE => {
            auth::authorize(actor, PERMISSION, "import")?;
            import_template_package(
                service,
                actor,
                &media::read_local(&PathBuf::from(text(body, "packagePath")), MAX_PACKAGE_BYTES)?,
                &text(body, "strategy"),
            )
        }
        SAVE_REPORT_TEMPLATE_FILE_TO_PATH => {
            let kind = report_templates::report_type(&text(body, "reportType"))?;
            demand_type(actor, kind)?;
            auth::authorize(actor, PERMISSION, "export")?;
            save_template_file_to_path(service, actor, kind, body)
        }
        SAVE_REPORT_TEMPLATE_PACKAGE_TO_PATH => {
            auth::authorize(actor, PERMISSION, "export")?;
            save_template_package_to_path(service, actor, body)
        }
        _ => Err(invalid("不是报表模板文件操作。")),
    }
}

fn storage_check(service: &NativeService, actor: &Actor) -> Result<Value> {
    auth::authorize(actor, PERMISSION, "publish")?;
    let root = user_root(&service.paths);
    let probe = root.join(format!(
        ".edm-template-write-check-{}.tmp",
        paths::nonce().map_err(unavailable)?
    ));
    let writable = (|| -> std::result::Result<(), String> {
        fs::create_dir_all(&root).map_err(|cause| cause.to_string())?;
        ensure_managed(&probe, &root).map_err(|cause| cause.message)?;
        fs::write(&probe, b"ExportDocManager template storage write check")
            .map_err(|cause| cause.to_string())?;
        fs::remove_file(&probe).map_err(|cause| cause.to_string())?;
        Ok(())
    })();
    let builtin = builtin_root(&service.paths).is_dir();
    Ok(contracts::project(
        contracts::schema("ApiReportTemplateStorageStatusResponse"),
        json!({
            "success":true,
            "templateRoot":root,
            "exists":root.is_dir(),
            "writable":writable.is_ok(),
            "message":if writable.is_ok() {
                if builtin { "内置模板目录可读取，用户模板目录可写；新建、编辑副本和导入功能可正常使用。" }
                else { "用户模板目录可写，但未发现随程序发布的内置模板目录，请检查安装包资源。" }
            } else { "用户模板目录不可写，请确认运行数据根位于有写入权限的目录，且 Templates 未被其它程序锁定。" },
            "storagePolicy":CHECK_POLICY
        }),
    ))
}

fn create_template(
    service: &NativeService,
    actor: &Actor,
    kind: &str,
    body: &Value,
) -> Result<Value> {
    service.store.transaction(|tx| {
        let path = lifecycle_target(
            service,
            kind,
            &text(body, "templatePath"),
            &default_file_name(service, kind)?,
        )?;
        ensure_no_collision(&path, None)?;
        if path.exists() {
            return Err(conflict("目标模板已存在。"));
        }
        let display = display_name(&text(body, "displayName"), &path);
        let content = report_templates::starter::create(kind, &display)?;
        report_templates::validate_content(kind, &content)?;
        report_assets::validate_template(tx, actor, &content)?;
        fs::create_dir_all(path.parent().unwrap_or(&path))?;
        paths::atomic_write(&path, content.as_bytes()).map_err(unavailable)?;
        let stored = to_stored(&service.paths, &path)?;
        let with_seal = if kind == "PaymentVoucher" {
            None
        } else {
            Some(true)
        };
        upsert_catalog_row(&service.paths, kind, &stored, &display, with_seal)?;
        Ok(content_dto(kind, &stored, &display, with_seal, &content))
    })
}

fn save_template_content(
    service: &NativeService,
    actor: &Actor,
    kind: &str,
    stored: &str,
    expected_revision: &str,
    content: &str,
) -> Result<Value> {
    report_templates::validate_content(kind, content)?;
    service.store.transaction(|tx| {
        let mut resolved = resolve_editable(service, kind, stored, false)?;
        validate_revision(&resolved.path, &resolved.display, expected_revision)?;
        report_assets::validate_template(tx, actor, content)?;
        if within(&resolved.path, &builtin_root(&service.paths)) {
            let copy = user_copy_path(&service.paths, &resolved.path)?;
            if copy.exists() {
                return Err(conflict("已有内置模板的用户副本，请打开该副本后继续编辑。"));
            }
            resolved.display = display_name("", &copy);
            resolved.path = copy;
        }
        fs::create_dir_all(resolved.path.parent().unwrap_or(&resolved.path))?;
        paths::atomic_write(&resolved.path, content.as_bytes()).map_err(unavailable)?;
        let stored = to_stored(&service.paths, &resolved.path)?;
        upsert_catalog_row(
            &service.paths,
            kind,
            &stored,
            &resolved.display,
            resolved.with_seal,
        )?;
        Ok(content_dto(
            kind,
            &stored,
            &resolved.display,
            resolved.with_seal,
            content,
        ))
    })
}

fn rename_template(
    service: &NativeService,
    _actor: &Actor,
    kind: &str,
    body: &Value,
) -> Result<Value> {
    service.store.transaction(|tx| {
        let current = resolve_editable(service, kind, &text(body, "templatePath"), true)?;
        validate_revision(
            &current.path,
            &current.display,
            &text(body, "expectedRevision"),
        )?;
        ensure_user_path(&service.paths, &current.path)?;
        let old_stored = to_stored(&service.paths, &current.path)?;
        let target = lifecycle_target(
            service,
            kind,
            &text(body, "newTemplatePath"),
            file_name(&current.path),
        )?;
        if target != current.path {
            ensure_no_collision(&target, Some(&current.path))?;
            if target.exists() {
                return Err(conflict("目标模板已存在。"));
            }
            fs::create_dir_all(target.parent().unwrap_or(&target))?;
            fs::rename(&current.path, &target)?;
        }
        let stored = to_stored(&service.paths, &target)?;
        move_catalog_row(
            &service.paths,
            &old_stored,
            &stored,
            &current.display,
            current.with_seal,
        )?;
        update_settings(tx, |settings| {
            let defaults = &mut settings["reportTemplateDefaults"];
            for key in ["exportDocumentTemplatePath", "paymentVoucherTemplatePath"] {
                if defaults[key].as_str() == Some(&old_stored) {
                    defaults[key] = json!(stored);
                }
            }
        })?;
        let content = fs::read_to_string(&target)?;
        Ok(content_dto(
            kind,
            &stored,
            &current.display,
            current.with_seal,
            &content,
        ))
    })
}

fn update_display_name(
    service: &NativeService,
    _actor: &Actor,
    kind: &str,
    body: &Value,
) -> Result<Value> {
    let resolved = resolve_editable(service, kind, &text(body, "templatePath"), true)?;
    validate_revision(
        &resolved.path,
        &resolved.display,
        &text(body, "expectedRevision"),
    )?;
    let stored = to_stored(&service.paths, &resolved.path)?;
    let display = display_name(&text(body, "displayName"), &resolved.path);
    upsert_catalog_row(&service.paths, kind, &stored, &display, resolved.with_seal)?;
    let content = fs::read_to_string(&resolved.path)?;
    Ok(content_dto(
        kind,
        &stored,
        &display,
        resolved.with_seal,
        &content,
    ))
}

fn delete_template(
    service: &NativeService,
    _actor: &Actor,
    kind: &str,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
) -> Result<Value> {
    let resolved = resolve_editable(
        service,
        kind,
        &value_of(parameters, query, "templatePath"),
        true,
    )?;
    validate_revision(
        &resolved.path,
        &resolved.display,
        &value_of(parameters, query, "expectedRevision"),
    )?;
    ensure_user_path(&service.paths, &resolved.path)?;
    let stored = to_stored(&service.paths, &resolved.path)?;
    fs::remove_file(&resolved.path)?;
    let mut rows = catalog_rows(&service.paths)?;
    rows.retain(|row| text(row, "fileName") != stored);
    save_catalog(&service.paths, &rows)?;
    service.store.transaction(|tx| {
        update_settings(tx, |settings| {
            let defaults = &mut settings["reportTemplateDefaults"];
            let key = default_key(kind);
            if defaults[key].as_str() == Some(&stored) {
                defaults[key] = json!("");
            }
        })
    })?;
    Ok(json!({"success":true,"message":"模板已删除。"}))
}

fn set_default_template(
    service: &NativeService,
    actor: &Actor,
    kind: &str,
    body: &Value,
) -> Result<Value> {
    let path = text(body, "templatePath");
    let (stored, label) = if let Some(id) = path.trim().strip_prefix(USER_TEMPLATE_PREFIX) {
        let id = id
            .parse::<i64>()
            .ok()
            .filter(|id| *id > 0)
            .ok_or_else(|| invalid("模板编号无效。"))?;
        let template = service.store.get("report-templates", id)?;
        if text(&template, "reportType") != kind || template["status"] != "Published" {
            return Err(error(404, "用户报表模板不存在、已停用或无权访问。"));
        }
        if !report_assets::template_visible(actor, &template) {
            return Err(error(403, "没有读取此报表模板的权限。"));
        }
        (
            format!("{USER_TEMPLATE_PREFIX}{id}"),
            text(&template, "name"),
        )
    } else {
        let resolved = resolve_editable(service, kind, &path, true)?;
        (to_stored(&service.paths, &resolved.path)?, resolved.display)
    };
    let key = default_key(kind);
    service.store.transaction(|tx| {
        update_settings(tx, |settings| {
            settings["reportTemplateDefaults"][key] = json!(stored);
        })
    })?;
    Ok(json!({"success":true,"message":format!("已将“{label}”设为默认模板。")}))
}

fn import_template_file(
    service: &NativeService,
    actor: &Actor,
    kind: &str,
    body: &Value,
) -> Result<Value> {
    let source = PathBuf::from(text(body, "filePath"));
    let bytes = media::read_local(&source, MAX_TEMPLATE_BYTES)?;
    validate_existing(&source)?;
    if bytes.is_empty() {
        return Err(invalid("模板文件不能为空。"));
    }
    let content = utf8_template(&bytes)?;
    save_template_content(
        service,
        actor,
        kind,
        &text(body, "templatePath"),
        &text(body, "expectedRevision"),
        &content,
    )
}

fn save_template_file_to_path(
    service: &NativeService,
    _actor: &Actor,
    kind: &str,
    body: &Value,
) -> Result<Value> {
    let target = export_path(&text(body, "filePath"))?;
    let resolved = resolve_editable(service, kind, &text(body, "templatePath"), true)?;
    let content = fs::read_to_string(&resolved.path)?;
    paths::atomic_write(&target, content.as_bytes()).map_err(unavailable)?;
    Ok(contracts::project(
        contracts::schema("ApiReportTemplateFileExportResponse"),
        json!({
            "success":true,
            "filePath":target,
            "bytes":content.len() as i64,
            "storagePolicy":FILE_POLICY
        }),
    ))
}

fn export_path(raw: &str) -> Result<PathBuf> {
    if raw.trim().is_empty() {
        return Err(invalid("模板文件路径不能为空。"));
    }
    let path = normalize_new(&PathBuf::from(raw.trim()))?;
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
        paths::ensure_safe_absolute(parent).map_err(invalid)?;
    }
    Ok(path)
}

fn package_path(paths: &RuntimePaths, raw: &str) -> Result<PathBuf> {
    if raw.trim().is_empty() {
        return Err(invalid("模板包路径不能为空。"));
    }
    let raw = raw.trim();
    let mut path = if PathBuf::from(raw).is_absolute() {
        PathBuf::from(raw)
    } else {
        paths.data_root.join("TemplatePackages").join(raw)
    };
    let extension = path.extension().and_then(|value| value.to_str());
    if extension.is_none() {
        let mut name = file_name(&path).to_owned();
        name.push_str(PACKAGE_EXTENSION);
        path.set_file_name(name);
    } else if !extension.is_some_and(|value| {
        value.eq_ignore_ascii_case("edtpl") || value.eq_ignore_ascii_case("zip")
    }) {
        return Err(invalid("模板包文件只支持 .edtpl 或 .zip。"));
    }
    paths::ensure_safe_absolute(&path).map_err(invalid)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
        paths::ensure_safe_absolute(parent).map_err(invalid)?;
    }
    Ok(path)
}

#[cfg(test)]
mod tests;
