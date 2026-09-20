//! Managed file identities, catalog metadata and editable template resolution.
use super::*;

pub(super) fn user_root(paths: &RuntimePaths) -> PathBuf {
    paths.data_root.join("Templates")
}
pub(super) fn builtin_root(paths: &RuntimePaths) -> PathBuf {
    paths.app_root.join("Templates")
}
pub(super) fn category(kind: &str) -> &'static str {
    if kind == "PaymentVoucher" {
        INTERNAL_CATEGORY
    } else {
        EXPORT_CATEGORY
    }
}
pub(super) fn category_of_path(path: &Path) -> Option<&'static str> {
    let mut components = path.components();
    components.next_back()?;
    match components.next_back()?.as_os_str().to_str() {
        Some(EXPORT_CATEGORY) => Some(EXPORT_CATEGORY),
        Some(INTERNAL_CATEGORY) => Some(INTERNAL_CATEGORY),
        _ => None,
    }
}
pub(super) fn kind_of_category(category: &str) -> &'static str {
    if category == INTERNAL_CATEGORY {
        "PaymentVoucher"
    } else {
        "ExportDocument"
    }
}
pub(super) fn demand_type(actor: &Actor, kind: &str) -> Result<()> {
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
pub(super) fn parameter<'a>(values: &'a [(&str, String)], name: &str) -> &'a str {
    values
        .iter()
        .find(|(key, _)| *key == name)
        .map(|(_, value)| value.as_str())
        .unwrap_or("")
}
pub(super) fn value_of<'a>(
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
pub(super) fn upload_value<'a>(
    parameters: &'a [(&str, String)],
    metadata: &'a Value,
    name: &str,
) -> &'a str {
    if parameter(parameters, name).is_empty() {
        metadata.get(name).and_then(Value::as_str).unwrap_or("")
    } else {
        parameter(parameters, name)
    }
}
pub(super) fn file_name(path: &Path) -> &str {
    path.file_name()
        .and_then(|name| name.to_str())
        .unwrap_or("")
}
pub(super) fn within(path: &Path, root: &Path) -> bool {
    path.starts_with(root)
}
pub(super) fn ensure_managed(absolute: &Path, root: &Path) -> Result<()> {
    paths::ensure_safe_absolute(absolute).map_err(invalid)?;
    if !within(absolute, root) {
        return Err(error(403, "模板路径不能离开受管模板目录。"));
    }
    Ok(())
}
pub(super) fn normalize_relative(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}
pub(super) fn to_stored(paths: &RuntimePaths, absolute: &Path) -> Result<String> {
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
pub(super) fn to_absolute(paths: &RuntimePaths, stored: &str) -> Result<PathBuf> {
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
pub(super) fn validate_existing(path: &Path) -> Result<()> {
    if path.extension().and_then(|value| value.to_str()) != Some(EXTENSION_NAME)
        || !paths::valid_file_name(file_name(path))
    {
        return Err(invalid("报表模板必须使用安全文件名和小写 .html 扩展名。"));
    }
    Ok(())
}
pub(super) fn normalize_new(path: &Path) -> Result<PathBuf> {
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
pub(super) fn ensure_no_collision(candidate: &Path, current: Option<&Path>) -> Result<()> {
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
pub(super) fn ensure_user_path(paths: &RuntimePaths, path: &Path) -> Result<()> {
    if !within(path, &user_root(paths)) {
        return Err(error(
            403,
            "内置模板为只读资源；请先保存为用户模板副本，再执行重命名或删除。",
        ));
    }
    Ok(())
}
pub(super) fn user_copy_path(paths: &RuntimePaths, builtin: &Path) -> Result<PathBuf> {
    let relative = builtin
        .strip_prefix(&builtin_root(paths))
        .map_err(|_| invalid("指定路径不是内置模板。"))?;
    let target = user_root(paths).join(relative);
    ensure_managed(&target, &user_root(paths))?;
    Ok(target)
}
pub(super) fn display_name(raw: &str, path: &Path) -> String {
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
pub(super) fn revision(content: &str, display: &str) -> String {
    let payload = serde_json::to_vec(&[content, display]).unwrap_or_default();
    Sha256::digest(&payload)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}
pub(super) fn validate_revision(path: &Path, display: &str, expected: &str) -> Result<()> {
    let revision = match fs::read_to_string(path) {
        Ok(content) => revision(&content, display),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(error) => {
            return Err(unavailable(format!("无法读取报表模板以核对修订号:{error}")));
        }
    };
    if revision != expected {
        return Err(conflict("模板已被其他用户修改或删除，请重新加载后重试。"));
    }
    Ok(())
}
pub(super) fn catalog_rows(paths: &RuntimePaths) -> Result<Vec<Value>> {
    let path = user_root(paths).join(CATALOG_FILE);
    if !path.is_file() {
        return Ok(vec![]);
    }
    let value: Value = serde_json::from_slice(&fs::read(&path)?)
        .map_err(|_| unavailable("模板目录配置已损坏。"))?;
    Ok(value["reports"].as_array().cloned().unwrap_or_default())
}
pub(super) fn save_catalog(paths: &RuntimePaths, rows: &[Value]) -> Result<()> {
    let root = user_root(paths);
    fs::create_dir_all(&root)?;
    let json = serde_json::to_string_pretty(&json!({"reports": rows}))?;
    paths::atomic_write(&root.join(CATALOG_FILE), json.as_bytes()).map_err(unavailable)
}
pub(super) fn upsert_catalog_row(
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
pub(super) fn move_catalog_row(
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
pub(super) fn category_of_stored(paths: &RuntimePaths, stored: &str) -> &'static str {
    to_absolute(paths, stored)
        .ok()
        .and_then(|path| category_of_path(&path))
        .unwrap_or(EXPORT_CATEGORY)
}
pub(super) fn settings_of(store: &Store) -> Result<Value> {
    let mut value = contracts::contract()["configuration"]["defaults"].clone();
    if let Some(saved) = store.settings("settings")? {
        value = contracts::overlay(value, &saved);
    }
    Ok(value)
}
pub(super) fn update_settings(tx: &Connection, mutate: impl FnOnce(&mut Value)) -> Result<()> {
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
pub(super) fn default_key(kind: &str) -> &'static str {
    if kind == "PaymentVoucher" {
        "paymentVoucherTemplatePath"
    } else {
        "exportDocumentTemplatePath"
    }
}
pub(super) fn default_file_name(service: &NativeService, kind: &str) -> Result<String> {
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
pub(super) struct Resolved {
    pub(super) path: PathBuf,
    pub(super) display: String,
    pub(super) with_seal: Option<bool>,
}
pub(super) fn file_name_of_stored(stored: &str) -> &str {
    stored.rsplit(['/', '\\']).next().unwrap_or("")
}
pub(super) fn resolve_editable(
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

/// Resolves a managed template reference without requiring the caller to know
/// whether it is a `builtin:`, `user:`, absolute or catalog-relative value.
/// File maintenance and report rendering share this identity function.
pub(super) fn resolve_template(paths: &RuntimePaths, kind: &str, stored: &str) -> Result<Resolved> {
    let absolute = to_absolute(paths, stored)?;
    validate_existing(&absolute)?;
    let category =
        category_of_path(&absolute).ok_or_else(|| invalid("模板路径不在受管模板分类目录下。"))?;
    if kind_of_category(category) != kind {
        return Err(invalid("模板类型与请求的报表类型不匹配。"));
    }
    if !absolute.is_file() {
        return Err(error(404, "报表模板不存在。"));
    }
    let stored = to_stored(paths, &absolute)?;
    let metadata = catalog_rows(paths)?
        .into_iter()
        .find(|row| text(row, "fileName") == stored)
        .unwrap_or_default();
    Ok(Resolved {
        path: absolute.clone(),
        display: display_name(&text(&metadata, "name"), &absolute),
        with_seal: if kind == "PaymentVoucher" {
            None
        } else {
            metadata["withSeal"].as_bool().or(Some(true))
        },
    })
}

/// Enumerates managed file templates for a report domain. Files without a
/// catalog row remain visible with metadata derived from their stable path.
pub(super) fn catalog_entries(paths: &RuntimePaths, kind: &str) -> Result<Vec<Value>> {
    let root = user_root(paths);
    if !root.is_dir() {
        return Ok(Vec::new());
    }
    let metadata = catalog_rows(paths)?;
    let mut rows = Vec::new();
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
            if file_name(&path) == CATALOG_FILE
                || path.extension().and_then(|value| value.to_str()) != Some(EXTENSION_NAME)
            {
                continue;
            }
            let category = category_of_path(&path)
                .ok_or_else(|| invalid("模板路径不在受管模板分类目录下。"))?;
            if kind_of_category(category) != kind {
                continue;
            }
            let stored = to_stored(paths, &path)?;
            let row = metadata
                .iter()
                .find(|row| text(row, "fileName") == stored)
                .unwrap_or(&Value::Null);
            rows.push(json!({
                "reportType":kind,
                "displayName":display_name(&text(row, "name"), &path),
                "templatePath":stored,
                "withSealDefault":if kind == "PaymentVoucher" {
                    Value::Null
                } else {
                    json!(row["withSeal"].as_bool().unwrap_or(true))
                }
            }));
        }
    }
    rows.sort_by(|left, right| {
        text(left, "displayName")
            .to_lowercase()
            .cmp(&text(right, "displayName").to_lowercase())
            .then_with(|| text(left, "templatePath").cmp(&text(right, "templatePath")))
    });
    Ok(rows)
}

/// Reads one managed report-template file for callers that need to create a
/// copy. Path validation and catalog lookup stay in this module; callers only
/// receive the validated template identity and HTML content.
pub(super) fn content_dto(
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
pub(super) fn lifecycle_target(
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
