//! Legacy file-based report template catalog: managed template roots,
//! builtin:/user: storage paths, single HTML file transfer and .edtpl packages.
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
    designer::{Design, Element, Grid, Kind, Layer, Page, Print, Style},
    generated_api::*,
    paths::{self, RuntimePaths},
    template,
};
use export_doc_storage::Connection;
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

fn user_root(paths: &RuntimePaths) -> PathBuf {
    paths.data_root.join("Templates")
}
fn builtin_root(paths: &RuntimePaths) -> PathBuf {
    paths.app_root.join("Templates")
}
fn category(kind: &str) -> &'static str {
    if kind == "PaymentVoucher" {
        INTERNAL_CATEGORY
    } else {
        EXPORT_CATEGORY
    }
}
fn category_of_path(path: &Path) -> Option<&'static str> {
    let mut components = path.components();
    components.next_back()?;
    match components.next_back()?.as_os_str().to_str() {
        Some(EXPORT_CATEGORY) => Some(EXPORT_CATEGORY),
        Some(INTERNAL_CATEGORY) => Some(INTERNAL_CATEGORY),
        _ => None,
    }
}
fn kind_of_category(category: &str) -> &'static str {
    if category == INTERNAL_CATEGORY {
        "PaymentVoucher"
    } else {
        "ExportDocument"
    }
}
fn demand_type(actor: &Actor, kind: &str) -> Result<()> {
    auth::authorize(
        actor,
        if kind == "PaymentVoucher" {
            "document.payments"
        } else {
            "document.invoices"
        },
        "view",
    )
}
fn parameter<'a>(values: &'a [(&str, String)], name: &str) -> &'a str {
    values
        .iter()
        .find(|(key, _)| *key == name)
        .map(|(_, value)| value.as_str())
        .unwrap_or("")
}
fn value_of<'a>(
    parameters: &'a [(&str, String)],
    query: &'a [(&str, String)],
    name: &str,
) -> &'a str {
    if parameter(parameters, name).is_empty() {
        parameter(query, name)
    } else {
        parameter(parameters, name)
    }
}
fn upload_value<'a>(parameters: &'a [(&str, String)], metadata: &'a Value, name: &str) -> &'a str {
    if parameter(parameters, name).is_empty() {
        metadata.get(name).and_then(Value::as_str).unwrap_or("")
    } else {
        parameter(parameters, name)
    }
}
fn file_name(path: &Path) -> &str {
    path.file_name()
        .and_then(|name| name.to_str())
        .unwrap_or("")
}
fn within(path: &Path, root: &Path) -> bool {
    path.starts_with(root)
}
fn ensure_managed(absolute: &Path, root: &Path) -> Result<()> {
    paths::ensure_safe_absolute(absolute).map_err(invalid)?;
    if !within(absolute, root) {
        return Err(error(403, "模板路径不能离开受管模板目录。"));
    }
    Ok(())
}
fn normalize_relative(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}
fn to_stored(paths: &RuntimePaths, absolute: &Path) -> Result<String> {
    for (prefix, root) in [
        (USER_PREFIX, user_root(paths)),
        (BUILTIN_PREFIX, builtin_root(paths)),
    ] {
        if within(absolute, &root) {
            let relative = absolute.strip_prefix(&root).expect("within root");
            return Ok(format!("{prefix}{}", normalize_relative(relative)));
        }
    }
    Ok(absolute.to_string_lossy().into_owned())
}
fn to_absolute(paths: &RuntimePaths, stored: &str) -> Result<PathBuf> {
    let value = stored.trim();
    if value.is_empty() {
        return Err(invalid("模板路径不能为空。"));
    }
    let normalized = value.replace('\\', "/");
    for (prefix, root) in [
        (USER_PREFIX, user_root(paths)),
        (BUILTIN_PREFIX, builtin_root(paths)),
    ] {
        if let Some(relative) = normalized.strip_prefix(prefix) {
            let candidate = root.join(relative.trim_start_matches('/'));
            ensure_managed(&candidate, &root)?;
            return Ok(candidate);
        }
    }
    let candidate = PathBuf::from(&normalized);
    if candidate.is_absolute() {
        paths::ensure_safe_absolute(&candidate).map_err(invalid)?;
        for root in [user_root(paths), builtin_root(paths)] {
            if within(&candidate, &root) {
                return Ok(candidate);
            }
        }
        return Err(error(
            403,
            "只能读取内置模板，或维护运行数据根 Templates/ 下的用户模板。",
        ));
    }
    let relative = normalized.strip_prefix("Templates/").unwrap_or(&normalized);
    let user_candidate = user_root(paths).join(relative);
    if user_candidate.is_file() {
        return Ok(user_candidate);
    }
    let builtin_candidate = builtin_root(paths).join(relative);
    ensure_managed(&builtin_candidate, &builtin_root(paths))?;
    Ok(if builtin_candidate.is_file() {
        builtin_candidate
    } else {
        user_candidate
    })
}
fn validate_existing(path: &Path) -> Result<()> {
    if path.extension().and_then(|value| value.to_str()) != Some(EXTENSION_NAME)
        || !paths::valid_file_name(file_name(path))
    {
        return Err(invalid("报表模板必须使用安全文件名和小写 .html 扩展名。"));
    }
    Ok(())
}
fn normalize_new(path: &Path) -> Result<PathBuf> {
    let mut normalized = path.to_path_buf();
    if path.extension().is_none() {
        let mut name = file_name(path).to_owned();
        name.push_str(EXTENSION);
        normalized.set_file_name(name);
    } else if path.extension().and_then(|value| value.to_str()) != Some(EXTENSION_NAME) {
        return Err(invalid("报表模板扩展名必须使用小写 .html。"));
    }
    let name: String = file_name(&normalized).nfc().collect();
    if !paths::valid_file_name(&name) {
        return Err(invalid("报表模板文件名不符合跨平台规则或长度超过限制。"));
    }
    Ok(normalized.with_file_name(name))
}
fn ensure_no_collision(candidate: &Path, current: Option<&Path>) -> Result<()> {
    let Some(directory) = candidate.parent() else {
        return Ok(());
    };
    if !directory.is_dir() {
        return Ok(());
    }
    let key = store::normalize(file_name(candidate));
    for entry in fs::read_dir(directory)? {
        let path = entry?.path();
        if store::normalize(file_name(&path)) != key {
            continue;
        }
        if path.as_path() == candidate || Some(path.as_path()) == current {
            continue;
        }
        return Err(conflict(
            "模板文件名与现有文件发生跨平台大小写或 Unicode 冲突。",
        ));
    }
    Ok(())
}
fn ensure_user_path(paths: &RuntimePaths, path: &Path) -> Result<()> {
    if !within(path, &user_root(paths)) {
        return Err(error(
            403,
            "内置模板为只读资源；请先保存为用户模板副本，再执行重命名或删除。",
        ));
    }
    Ok(())
}
fn user_copy_path(paths: &RuntimePaths, builtin: &Path) -> Result<PathBuf> {
    let relative = builtin
        .strip_prefix(&builtin_root(paths))
        .map_err(|_| invalid("指定路径不是内置模板。"))?;
    let target = user_root(paths).join(relative);
    ensure_managed(&target, &user_root(paths))?;
    Ok(target)
}
fn display_name(raw: &str, path: &Path) -> String {
    let trimmed = raw.trim();
    if trimmed.is_empty() {
        path.file_stem()
            .and_then(|value| value.to_str())
            .unwrap_or("报表模板")
            .nfc()
            .collect()
    } else {
        trimmed.nfc().collect()
    }
}
fn revision(content: &str, display: &str) -> String {
    let payload = serde_json::to_vec(&[content, display]).unwrap_or_default();
    Sha256::digest(&payload)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}
fn validate_revision(path: &Path, display: &str, expected: &str) -> Result<()> {
    let revision = fs::read_to_string(path)
        .ok()
        .map(|content| revision(&content, display))
        .unwrap_or_default();
    if revision != expected {
        return Err(conflict("模板已被其他用户修改或删除，请重新加载后重试。"));
    }
    Ok(())
}
fn catalog_rows(paths: &RuntimePaths) -> Result<Vec<Value>> {
    let path = user_root(paths).join(CATALOG_FILE);
    if !path.is_file() {
        return Ok(vec![]);
    }
    let value: Value = serde_json::from_slice(&fs::read(&path)?)
        .map_err(|_| unavailable("模板目录配置已损坏。"))?;
    Ok(value["reports"].as_array().cloned().unwrap_or_default())
}
fn save_catalog(paths: &RuntimePaths, rows: &[Value]) -> Result<()> {
    let root = user_root(paths);
    fs::create_dir_all(&root)?;
    let json = serde_json::to_string_pretty(&json!({"reports": rows}))?;
    paths::atomic_write(&root.join(CATALOG_FILE), json.as_bytes()).map_err(unavailable)
}
fn upsert_catalog_row(
    paths: &RuntimePaths,
    kind: &str,
    stored: &str,
    name: &str,
    with_seal: Option<bool>,
) -> Result<()> {
    let mut rows = catalog_rows(paths)?;
    rows.retain(|row| text(row, "fileName") != stored);
    rows.push(json!({"type":category(kind),"name":name,"fileName":stored,"withSeal":with_seal}));
    save_catalog(paths, &rows)
}
fn move_catalog_row(
    paths: &RuntimePaths,
    old_stored: &str,
    new_stored: &str,
    name: &str,
    with_seal: Option<bool>,
) -> Result<()> {
    let mut rows = catalog_rows(paths)?;
    rows.retain(|row| text(row, "fileName") != old_stored && text(row, "fileName") != new_stored);
    rows.push(json!({
        "type":category_of_stored(paths, new_stored),
        "name":name,
        "fileName":new_stored,
        "withSeal":with_seal
    }));
    save_catalog(paths, &rows)
}
fn category_of_stored(paths: &RuntimePaths, stored: &str) -> &'static str {
    to_absolute(paths, stored)
        .ok()
        .and_then(|path| category_of_path(&path))
        .unwrap_or(EXPORT_CATEGORY)
}
fn settings_of(store: &Store) -> Result<Value> {
    let mut value = contracts::contract()["configuration"]["defaults"].clone();
    if let Some(saved) = store.settings("settings")? {
        value = contracts::overlay(value, &saved);
    }
    Ok(value)
}
fn update_settings(tx: &Connection, mutate: impl FnOnce(&mut Value)) -> Result<()> {
    let previous = tx.settings("settings")?;
    let version = previous
        .as_ref()
        .and_then(|value| value["revision"].as_i64())
        .unwrap_or(0);
    let mut value =
        previous.unwrap_or_else(|| contracts::contract()["configuration"]["defaults"].clone());
    for key in ["reportTemplateDefaults", "batchExport"] {
        if value[key].is_null() {
            value[key] = json!({});
        }
    }
    if value["paymentTemplates"].is_null() {
        value["paymentTemplates"] = json!([]);
    }
    if value["batchExport"]["items"].is_null() {
        value["batchExport"]["items"] = json!([]);
    }
    mutate(&mut value);
    value["revision"] = json!(version + 1);
    tx.set_settings("settings", version + 1, &value)?;
    Ok(())
}
fn default_key(kind: &str) -> &'static str {
    if kind == "PaymentVoucher" {
        "paymentVoucherTemplatePath"
    } else {
        "exportDocumentTemplatePath"
    }
}
fn starter(kind: &str, title: &str) -> Result<String> {
    let mut design = Design {
        version: 3,
        ast_kind: "ReportDocument".into(),
        coordinate_unit: "hundredth-mm".into(),
        contract_version: "3.0".into(),
        report_type: kind.into(),
        page: Page {
            size: "A4".into(),
            orientation: "Portrait".into(),
            width_hundredth_mm: 21000,
            height_hundredth_mm: 29700,
            margin_top_hundredth_mm: 1000,
            margin_right_hundredth_mm: 1000,
            margin_bottom_hundredth_mm: 1000,
            margin_left_hundredth_mm: 1000,
            font_family: "Noto Sans CJK SC".into(),
            font_size_pt: 10.,
        },
        grid: Grid {
            enabled: true,
            snap: true,
            size_hundredth_mm: 500,
        },
        layers: vec![],
        resources: vec![],
        release: None,
        metadata: None,
    };
    for (role, name) in [("Header", "页眉"), ("Body", "主体"), ("Footer", "页脚")] {
        design.layers.push(Layer {
            id: role.into(),
            name: name.into(),
            role: role.into(),
            visible: true,
            locked: false,
            print: Print {
                repeat_on_every_page: role != "Body",
                keep_together: true,
                pin_to_page_bottom: role == "Footer",
                min_height_hundredth_mm: 0,
            },
            elements: vec![],
            design_height_hundredth_mm: None,
        });
    }
    design.layers[0].elements.push(Element {
        id: "title".into(),
        label: "标题".into(),
        x_hundredth_mm: 1000,
        y_hundredth_mm: 900,
        width_hundredth_mm: 19000,
        height_hundredth_mm: 1100,
        rotation_deg: 0,
        z_index: 0,
        visible: true,
        locked: false,
        output_enabled: true,
        style: Style {
            font_size_pt: 16.,
            bold: true,
            ..Default::default()
        },
        kind: Kind::Text { text: title.into() },
    });
    template::export(
        &design,
        &crate::designer::field_catalog(&report_templates::fields(kind)?),
    )
    .map_err(invalid)
}
fn default_file_name(service: &NativeService, kind: &str) -> Result<String> {
    let prefix = if kind == "PaymentVoucher" {
        "internal_template"
    } else {
        "export_template"
    };
    let now = service.clock.now().map_err(unavailable)?;
    Ok(format!(
        "{}_{}{}.html",
        prefix,
        now.utc_now.format("%Y%m%d%H%M%S"),
        &paths::nonce().map_err(unavailable)?[..8]
    ))
}
struct Resolved {
    path: PathBuf,
    display: String,
    with_seal: Option<bool>,
}
fn file_name_of_stored(stored: &str) -> &str {
    stored.rsplit(['/', '\\']).next().unwrap_or("")
}
fn resolve_editable(
    service: &NativeService,
    kind: &str,
    stored: &str,
    must_exist: bool,
) -> Result<Resolved> {
    let absolute = to_absolute(&service.paths, stored)?;
    validate_existing(&absolute)?;
    let resolved_kind = kind_of_category(
        category_of_path(&absolute).ok_or_else(|| invalid("模板路径不在受管模板分类目录下。"))?,
    );
    if resolved_kind != kind {
        return Err(invalid("模板类型与请求的报表类型不匹配。"));
    }
    let user_root = user_root(&service.paths);
    if !within(&absolute, &user_root) && !within(&absolute, &builtin_root(&service.paths)) {
        return Err(error(
            403,
            "只能读取内置模板，或维护运行数据根 Templates/ 下的用户模板。",
        ));
    }
    if must_exist && !absolute.is_file() {
        return Err(error(404, "报表模板不存在。"));
    }
    let stored = to_stored(&service.paths, &absolute)?;
    let row = catalog_rows(&service.paths)?
        .into_iter()
        .find(|row| {
            text(row, "fileName") == stored
                || file_name(&absolute) == file_name_of_stored(&text(row, "fileName"))
        })
        .unwrap_or_default();
    let display = display_name(&text(&row, "name"), &absolute);
    let with_seal = if kind == "PaymentVoucher" {
        None
    } else {
        row["withSeal"].as_bool().or(Some(true))
    };
    if within(&absolute, &user_root) {
        fs::create_dir_all(absolute.parent().unwrap_or(&absolute))?;
    }
    Ok(Resolved {
        path: absolute,
        display,
        with_seal,
    })
}

/// Reads one managed report-template file for callers that need to create a
/// copy. Path validation and catalog lookup stay in this module; callers only
/// receive the validated template identity and HTML content.
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
fn content_dto(
    kind: &str,
    stored: &str,
    display: &str,
    with_seal: Option<bool>,
    content: &str,
) -> Value {
    contracts::project(
        contracts::schema("ApiReportTemplateContentDto"),
        json!({
            "success":true,
            "reportType":kind,
            "displayName":display,
            "templatePath":stored,
            "withSealDefault":with_seal,
            "content":content,
            "revision":revision(content, display),
            "storagePolicy":STORAGE_POLICY
        }),
    )
}
fn lifecycle_target(
    service: &NativeService,
    kind: &str,
    selected: &str,
    fallback: &str,
) -> Result<PathBuf> {
    let directory = user_root(&service.paths).join(category(kind));
    fs::create_dir_all(&directory)?;
    ensure_managed(&directory, &user_root(&service.paths))?;
    let selected = selected.trim();
    let candidate = if selected.is_empty() {
        directory.join(fallback)
    } else {
        let absolute = to_absolute(&service.paths, selected)?;
        if within(&absolute, &directory) {
            absolute
        } else {
            directory.join(file_name(&absolute))
        }
    };
    let candidate = normalize_new(&candidate)?;
    if !within(&candidate, &directory) {
        return Err(error(403, "只能在当前模板分类目录下新建或重命名模板。"));
    }
    ensure_managed(&candidate, &user_root(&service.paths))?;
    Ok(candidate)
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
        let content = starter(kind, &display)?;
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

fn save_template_package_to_path(
    service: &NativeService,
    _actor: &Actor,
    body: &Value,
) -> Result<Value> {
    let target = package_path(&service.paths, &text(body, "packagePath"))?;
    let (bytes, count) = package_bytes(service)?;
    paths::atomic_write(&target, &bytes).map_err(unavailable)?;
    Ok(contracts::project(
        contracts::schema("ApiReportTemplatePackageExportResponse"),
        json!({
            "packagePath":target,
            "templateCount":count,
            "storagePolicy":PACKAGE_POLICY
        }),
    ))
}

fn template_files(paths: &RuntimePaths) -> Result<Vec<(String, Vec<u8>)>> {
    let root = user_root(paths);
    if !root.is_dir() {
        return Ok(vec![]);
    }
    let mut files = vec![];
    let mut stack = vec![root.clone()];
    while let Some(directory) = stack.pop() {
        ensure_managed(&directory, &root)?;
        for entry in fs::read_dir(&directory)? {
            let path = entry?.path();
            ensure_managed(&path, &root)?;
            if path.is_dir() {
                stack.push(path);
                continue;
            }
            let relative = normalize_relative(path.strip_prefix(&root).expect("within root"));
            if relative == CATALOG_FILE {
                continue;
            }
            if path
                .extension()
                .and_then(|value| value.to_str())
                .is_some_and(|value| value.eq_ignore_ascii_case("html") && value != "html")
            {
                return Err(invalid("报表模板扩展名必须使用小写 .html。"));
            }
            let bytes = fs::read(&path)?;
            if bytes.len() > MAX_TEMPLATE_BYTES {
                return Err(invalid("模板文件超过 10 MB。"));
            }
            files.push((relative, bytes));
        }
    }
    files.sort_by(|left, right| left.0.to_lowercase().cmp(&right.0.to_lowercase()));
    Ok(files)
}

fn files_digest(files: &[(String, Vec<u8>)]) -> String {
    let mut input = String::new();
    let mut ordered: Vec<_> = files.iter().collect();
    ordered.sort_by(|left, right| left.0.to_lowercase().cmp(&right.0.to_lowercase()));
    for (path, bytes) in ordered {
        input.push_str(path);
        input.push('\n');
        input.push_str(&bytes.len().to_string());
        input.push('\n');
        input.push_str(&media::digest(bytes));
        input.push('\n');
    }
    media::digest(input.as_bytes())
}

fn manifest_items(items: &Value, show_seal: bool) -> Vec<Value> {
    items
        .as_array()
        .into_iter()
        .flatten()
        .map(|item| {
            let mut object = serde_json::Map::new();
            object.insert("Name".into(), item["name"].clone());
            object.insert("TemplatePath".into(), item["templatePath"].clone());
            object.insert("ReportType".into(), item["reportType"].clone());
            object.insert(
                "IsEnabled".into(),
                json!(item["isEnabled"].as_bool().unwrap_or(true)),
            );
            if show_seal {
                object.insert(
                    "ShowSeal".into(),
                    json!(item["showSeal"].as_bool().unwrap_or(true)),
                );
            }
            Value::Object(object)
        })
        .collect()
}

fn package_bytes(service: &NativeService) -> Result<(Vec<u8>, i64)> {
    let root = user_root(&service.paths);
    fs::create_dir_all(&root)?;
    ensure_managed(&root, &root)?;
    let files = template_files(&service.paths)?;
    let settings = settings_of(&service.store)?;
    let mut templates = vec![];
    for row in catalog_rows(&service.paths)? {
        let stored = text(&row, "fileName");
        if stored.is_empty() {
            continue;
        }
        let absolute = to_absolute(&service.paths, &stored)?;
        if !within(&absolute, &root) || !absolute.is_file() {
            continue;
        }
        let kind = kind_of_category(category_of_path(&absolute).unwrap_or(EXPORT_CATEGORY));
        templates.push(json!({
            "Type": if kind == "PaymentVoucher" { INTERNAL_CATEGORY } else { EXPORT_CATEGORY },
            "Name": text(&row, "name"),
            "FileName": stored,
            "WithSeal": if kind == "PaymentVoucher" { Value::Null } else { json!(row["withSeal"].as_bool().unwrap_or(true)) }
        }));
    }
    let defaults = settings["reportTemplateDefaults"].clone();
    let manifest = json!({
        "PackageVersion": PACKAGE_SCHEMA_VERSION,
        "Templates": templates.clone(),
        "TemplateDefaults": {
            "ExportDocumentTemplatePath": defaults["ExportDocumentTemplatePath"],
            "PaymentVoucherTemplatePath": defaults["PaymentVoucherTemplatePath"],
        },
        "ExportTemplates": manifest_items(&settings["batchExport"]["items"], true),
        "InternalTemplates": manifest_items(&settings["paymentTemplates"], false),
        "Files": files.iter().map(|(path, bytes)| json!({
            "Path": path,
            "SizeBytes": bytes.len() as i64,
            "Sha256": media::digest(bytes)
        })).collect::<Vec<_>>(),
        "FileCount": files.len() as i64,
        "TotalBytes": files.iter().map(|(_, bytes)| bytes.len() as i64).sum::<i64>(),
        "FilesDigest": files_digest(&files)
    });
    let mut buffer = Vec::new();
    {
        let mut archive = ZipWriter::new(Cursor::new(&mut buffer));
        let options = SimpleFileOptions::default();
        archive
            .start_file("config.json", options)
            .map_err(|_| unavailable("模板包写入失败。"))?;
        archive
            .write_all(&serde_json::to_vec(&manifest)?)
            .map_err(|_| unavailable("模板包写入失败。"))?;
        for (relative, bytes) in &files {
            crate::operation::check()?;
            archive
                .start_file(format!("Templates/{relative}"), options)
                .map_err(|_| unavailable("模板包写入失败。"))?;
            archive
                .write_all(bytes)
                .map_err(|_| unavailable("模板包写入失败。"))?;
        }
        archive
            .finish()
            .map_err(|_| unavailable("模板包写入失败。"))?;
    }
    Ok((buffer, templates.len() as i64))
}

fn normalize_default(paths: &RuntimePaths, stored: &str, kind: &str) -> Result<String> {
    if stored.is_empty() || stored.starts_with(USER_TEMPLATE_PREFIX) {
        return Ok(String::new());
    }
    let absolute = to_absolute(paths, stored)?;
    if !absolute.is_file()
        || kind_of_category(category_of_path(&absolute).unwrap_or(EXPORT_CATEGORY)) != kind
    {
        return Err(invalid(format!(
            "默认 {kind} 模板不存在或不属于受管模板目录。"
        )));
    }
    to_stored(paths, &absolute)
}

fn extract_package(bytes: &[u8]) -> Result<BTreeMap<String, Vec<u8>>> {
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|_| invalid("模板包不是有效的 zip 归档。"))?;
    if archive.len() > MAX_PACKAGE_ENTRIES {
        return Err(invalid("模板包条目超过 2000。"));
    }
    let mut entries = BTreeMap::new();
    for index in 0..archive.len() {
        crate::operation::check()?;
        let mut entry = archive
            .by_index(index)
            .map_err(|_| invalid("模板包条目无法读取。"))?;
        let raw = entry
            .enclosed_name()
            .ok_or_else(|| invalid("模板包包含不安全的条目路径。"))?;
        // Windows 上 zip 会把条目名中的 / 转成 \，导入时统一成正斜杠再校验。
        let name = raw.to_string_lossy().replace('\\', "/");
        if name.is_empty() || name.ends_with('/') || name.contains("..") || name.starts_with('/') {
            continue;
        }
        let mut buffer = Vec::new();
        entry
            .read_to_end(&mut buffer)
            .map_err(|_| invalid("模板包条目读取失败。"))?;
        entries.insert(name, buffer);
    }
    Ok(entries)
}

fn validate_manifest(manifest: &Value) -> Result<()> {
    if text(manifest, "PackageVersion") != PACKAGE_SCHEMA_VERSION {
        return Err(invalid(format!(
            "模板包版本无效；当前仅接受 {PACKAGE_SCHEMA_VERSION} 清单。"
        )));
    }
    let templates = manifest["Templates"].as_array().ok_or_else(|| {
        invalid(format!("模板包 {PACKAGE_SCHEMA_VERSION} 清单必须包含 Templates、TemplateDefaults、ExportTemplates、InternalTemplates 和 Files 摘要。"))
    })?;
    for (index, row) in templates.iter().enumerate() {
        let kind = text(row, "Type");
        if kind.is_empty() || text(row, "FileName").is_empty() {
            return Err(invalid(format!(
                "模板包 Templates[{index}] 缺少 Type 或 FileName。"
            )));
        }
        let payment = kind == INTERNAL_CATEGORY;
        let with_seal = row.get("WithSeal").filter(|value| !value.is_null());
        if payment && with_seal.is_some() {
            return Err(invalid(format!(
                "模板包 Templates[{index}] 是付款报销模板，不得包含 WithSeal。"
            )));
        }
        if !payment && with_seal.is_none() {
            return Err(invalid(format!(
                "模板包 Templates[{index}] 是报关单证模板，缺少 WithSeal。"
            )));
        }
    }
    for key in [
        "TemplateDefaults",
        "ExportTemplates",
        "InternalTemplates",
        "Files",
    ] {
        if manifest[key].is_null() {
            return Err(invalid(format!(
                "模板包 {PACKAGE_SCHEMA_VERSION} 清单必须包含 Templates、TemplateDefaults、ExportTemplates、InternalTemplates 和 Files 摘要。"
            )));
        }
    }
    if text(manifest, "FilesDigest").is_empty() {
        return Err(invalid("模板包文件摘要清单无效。"));
    }
    Ok(())
}

fn validate_file_manifest(manifest: &Value, templates: &[(String, Vec<u8>)]) -> Result<()> {
    let mut expected = BTreeMap::new();
    for file in manifest["Files"].as_array().into_iter().flatten() {
        let path = text(file, "Path");
        if path.is_empty() {
            return Err(invalid("模板包文件摘要包含无效条目。"));
        }
        expected.insert(
            path,
            (
                file["SizeBytes"].as_i64().unwrap_or(0),
                text(file, "Sha256").to_string(),
            ),
        );
    }
    let mut actual = BTreeMap::new();
    for (relative, bytes) in templates {
        actual.insert(relative.clone(), (bytes.len() as i64, media::digest(bytes)));
    }
    if actual != expected {
        return Err(invalid("模板包文件与摘要不一致，模板包可能已损坏。"));
    }
    if files_digest(templates) != text(manifest, "FilesDigest") {
        return Err(invalid("模板包文件总摘要不一致，模板包可能已损坏。"));
    }
    Ok(())
}

fn merge_default(existing: &str, incoming: &str, strategy: &str) -> String {
    if strategy == "Overwrite" || existing.is_empty() {
        incoming.to_owned()
    } else {
        existing.to_owned()
    }
}
fn row_key(row: &Value) -> String {
    format!(
        "{}|{}",
        store::normalize(&text(row, "type")),
        store::normalize(&text(row, "fileName"))
    )
}

fn merge_rows(existing: Vec<Value>, incoming: Vec<Value>, strategy: &str) -> Vec<Value> {
    if strategy == "Overwrite" {
        return incoming;
    }
    let mut result = existing;
    let mut positions: BTreeMap<String, usize> = result
        .iter()
        .enumerate()
        .map(|(index, row)| (row_key(row), index))
        .collect();
    for row in incoming {
        let key = row_key(&row);
        match positions.get(&key) {
            None => {
                positions.insert(key, result.len());
                result.push(row);
            }
            Some(index) => {
                if strategy == "Merge" {
                    result[*index] = row;
                }
            }
        }
    }
    result
}

fn manifest_item_to_settings(item: &Value, show_seal: bool) -> Value {
    let mut object = serde_json::Map::new();
    object.insert("name".into(), item["Name"].clone());
    object.insert("templatePath".into(), item["TemplatePath"].clone());
    object.insert("reportType".into(), item["ReportType"].clone());
    object.insert(
        "isEnabled".into(),
        json!(item["IsEnabled"].as_bool().unwrap_or(true)),
    );
    if show_seal {
        object.insert(
            "showSeal".into(),
            json!(item["ShowSeal"].as_bool().unwrap_or(true)),
        );
    }
    Value::Object(object)
}

fn merge_items(existing: &Value, incoming: &Value, strategy: &str, show_seal: bool) -> Vec<Value> {
    let incoming: Vec<Value> = incoming
        .as_array()
        .into_iter()
        .flatten()
        .map(|item| manifest_item_to_settings(item, show_seal))
        .collect();
    merge_rows(
        existing.as_array().into_iter().flatten().cloned().collect(),
        incoming,
        strategy,
    )
}

fn utf8_template(bytes: &[u8]) -> Result<String> {
    let bytes = bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(bytes);
    String::from_utf8(bytes.to_vec()).map_err(|_| invalid("模板文件必须是 UTF-8 文本。"))
}

fn import_template_package(
    service: &NativeService,
    actor: &Actor,
    bytes: &[u8],
    strategy: &str,
) -> Result<Value> {
    if bytes.is_empty() {
        return Err(invalid("模板包文件不能为空。"));
    }
    let strategy = match strategy.trim() {
        "" | "Overwrite" | "overwrite" => "Overwrite",
        "Merge" | "merge" => "Merge",
        "AddOnly" | "addonly" => "AddOnly",
        _ => return Err(invalid("模板包导入策略无效。")),
    };
    let mut entries = extract_package(bytes)?;
    let config = entries
        .remove("config.json")
        .ok_or_else(|| invalid("模板包缺少 config.json 配置清单。"))?;
    let manifest: Value = serde_json::from_slice(&config).map_err(|_| {
        invalid(format!(
            "模板包配置文件已损坏或不符合 {PACKAGE_SCHEMA_VERSION} 清单结构。"
        ))
    })?;
    validate_manifest(&manifest)?;
    let root = user_root(&service.paths);
    let mut templates = vec![];
    let mut total = 0usize;
    for (relative, bytes) in entries {
        let Some(relative) = relative.strip_prefix("Templates/") else {
            return Err(invalid("模板包只能包含 Templates 目录下的模板文件。"));
        };
        if relative.is_empty() {
            continue;
        }
        total = total
            .checked_add(bytes.len())
            .filter(|total| *total <= MAX_PACKAGE_BYTES)
            .ok_or_else(|| invalid("模板包解压容量超限。"))?;
        templates.push((relative.to_owned(), bytes));
    }
    validate_file_manifest(&manifest, &templates)?;
    service.store.transaction(|tx| {
        for (relative, bytes) in &templates {
            crate::operation::check()?;
            let kind = kind_of_category(relative.split('/').next().unwrap_or(EXPORT_CATEGORY));
            let target = root.join(relative);
            ensure_managed(&target, &root)?;
            validate_existing(&target)?;
            ensure_no_collision(&target, None)?;
            let content =
                std::str::from_utf8(bytes).map_err(|_| invalid("模板文件必须是 UTF-8 文本。"))?;
            report_templates::validate_content(kind, content)?;
            report_assets::validate_template(tx, actor, content)?;
            if strategy == "AddOnly" && target.exists() {
                continue;
            }
            fs::create_dir_all(target.parent().unwrap_or(&target))?;
            paths::atomic_write(&target, bytes).map_err(unavailable)?;
        }
        let mut rows = catalog_rows(&service.paths)?;
        let mut incoming = vec![];
        for row in manifest["Templates"].as_array().into_iter().flatten() {
            let file_name = text(row, "FileName");
            let absolute = match to_absolute(&service.paths, &file_name) {
                Ok(absolute) => absolute,
                Err(_) => continue,
            };
            if !within(&absolute, &root) || !absolute.is_file() {
                continue;
            }
            let kind = kind_of_category(category_of_path(&absolute).unwrap_or(EXPORT_CATEGORY));
            incoming.push(json!({
                "type":if kind=="PaymentVoucher" {INTERNAL_CATEGORY} else {EXPORT_CATEGORY},
                "name":text(row,"Name"),
                "fileName":to_stored(&service.paths,&absolute)?,
                "withSeal":if kind=="PaymentVoucher" {Value::Null} else {json!(row["WithSeal"].as_bool().unwrap_or(true))}
            }));
        }
        rows = merge_rows(rows, incoming, strategy);
        save_catalog(&service.paths, &rows)?;
        update_settings(tx, |settings| {
            let defaults = settings["reportTemplateDefaults"].clone();
            for (key, incoming) in [
                (
                    "exportDocumentTemplatePath",
                    defaults["ExportDocumentTemplatePath"].as_str().unwrap_or(""),
                ),
                (
                    "paymentVoucherTemplatePath",
                    defaults["PaymentVoucherTemplatePath"].as_str().unwrap_or(""),
                ),
            ] {
                let existing = settings["reportTemplateDefaults"][key].as_str().unwrap_or("");
                settings["reportTemplateDefaults"][key] =
                    json!(merge_default(existing, incoming, strategy));
            }
            settings["batchExport"]["items"] = json!(merge_items(
                &settings["batchExport"]["items"],
                &manifest["ExportTemplates"],
                strategy,
                true
            ));
            settings["paymentTemplates"] = json!(merge_items(
                &settings["paymentTemplates"],
                &manifest["InternalTemplates"],
                strategy,
                false
            ));
        })?;
        Ok(json!({
            "success":true,
            "templateCount":manifest["Templates"].as_array().map(Vec::len).unwrap_or(0) as i64,
            "packageVersion":text(&manifest,"PackageVersion"),
            "storagePolicy":PACKAGE_POLICY
        }))
    })
}

#[cfg(test)]
mod tests;
