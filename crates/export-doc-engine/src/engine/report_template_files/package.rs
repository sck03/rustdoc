//! Template package manifests, bounded archive validation and transactional import.
use super::*;

pub(super) fn save_template_package_to_path(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    let target = package_path(&service.paths, &text(body, "packagePath"))?;
    let (bytes, count) = package_bytes(service, actor)?;
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

pub(super) fn template_files(paths: &RuntimePaths) -> Result<Vec<(String, Vec<u8>)>> {
    let root = user_root(paths);
    if !root.is_dir() {
        return Ok(vec![]);
    }
    let mut files = vec![];
    let mut total_bytes = 0usize;
    let mut stack = vec![root.clone()];
    while let Some(directory) = stack.pop() {
        ensure_managed(&directory, &root)?;
        for entry in fs::read_dir(&directory)? {
            crate::operation::check()?;
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
            if !matches!(
                path.extension().and_then(|value| value.to_str()),
                Some(REPORT_TEMPLATE_EXTENSION_NAME)
            ) {
                return Err(invalid("报表模板扩展名必须使用小写 .dtpl。"));
            }
            let bytes = media::read_local(&path, MAX_TEMPLATE_BYTES)?;
            total_bytes = total_bytes.saturating_add(bytes.len());
            if files.len() >= MAX_PACKAGE_ENTRIES || total_bytes > MAX_PACKAGE_BYTES {
                return Err(invalid("模板目录超过 2000 个文件或 50 MiB，请先整理模板。"));
            }
            files.push((relative, bytes));
        }
    }
    files.sort_by(|left, right| left.0.to_lowercase().cmp(&right.0.to_lowercase()));
    Ok(files)
}

pub(super) fn files_digest(files: &[(String, Vec<u8>)]) -> String {
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

pub(super) fn manifest_items(items: &Value, show_seal: bool) -> Vec<Value> {
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

pub(super) fn package_bytes(service: &NativeService, actor: &Actor) -> Result<(Vec<u8>, i64)> {
    let _access = storage_lock(&service.paths)?;
    let root = user_root(&service.paths);
    fs::create_dir_all(&root)?;
    ensure_managed(&root, &root)?;
    let files = template_files(&service.paths)?;
    for (relative, _) in &files {
        demand_type(
            actor,
            kind_of_category(relative.split('/').next().unwrap_or("")),
        )?;
    }
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
            "ExportDocumentTemplatePath": normalize_default(&service.paths, &text(&defaults, "exportDocumentTemplatePath"), "ExportDocument")?,
            "PaymentVoucherTemplatePath": normalize_default(&service.paths, &text(&defaults, "paymentVoucherTemplatePath"), "PaymentVoucher")?,
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

pub(super) fn normalize_default(paths: &RuntimePaths, stored: &str, kind: &str) -> Result<String> {
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

pub(super) fn extract_package(bytes: &[u8]) -> Result<BTreeMap<String, Vec<u8>>> {
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

pub(super) fn validate_manifest(manifest: &Value) -> Result<()> {
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

pub(super) fn validate_file_manifest(
    manifest: &Value,
    templates: &[(String, Vec<u8>)],
) -> Result<()> {
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

pub(super) fn merge_default(existing: &str, incoming: &str, strategy: &str) -> String {
    if strategy == "Overwrite" || existing.is_empty() {
        incoming.to_owned()
    } else {
        existing.to_owned()
    }
}
pub(super) fn row_key(row: &Value) -> String {
    format!(
        "{}|{}",
        store::normalize(&text(row, "type")),
        store::normalize(&text(row, "fileName"))
    )
}

pub(super) fn merge_rows(existing: Vec<Value>, incoming: Vec<Value>, strategy: &str) -> Vec<Value> {
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

pub(super) fn manifest_item_to_settings(item: &Value, show_seal: bool) -> Value {
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

pub(super) fn merge_items(
    existing: &Value,
    incoming: &Value,
    strategy: &str,
    show_seal: bool,
) -> Vec<Value> {
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

pub(super) fn import_template_package(
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
    let mut files = FileTransaction::new(&service.paths)?;
    files.execute(|files| {
        service.store.transaction(|tx| {
        for (relative, bytes) in &templates {
            crate::operation::check()?;
            let kind = kind_of_category(relative.split('/').next().unwrap_or(EXPORT_CATEGORY));
            demand_type(actor, kind)?;
            let target = root.join(relative);
            ensure_managed(&target, &root)?;
            validate_existing(&target)?;
            ensure_no_collision(&target, None)?;
            files.capture(&target)?;
            let editable = report_templates::editable_content(kind, bytes)?;
            report_assets::validate_template(tx, actor, &editable, Some(&service.paths))?;
            let stored = report_templates::stored_content(kind, &editable)?;
            if strategy == "AddOnly" && target.exists() {
                continue;
            }
            fs::create_dir_all(target.parent().unwrap_or(&target))?;
            paths::atomic_write(&target, &stored).map_err(unavailable)?;
        }
        let mut rows = catalog_rows(&service.paths)?;
        files.capture(&root.join(CATALOG_FILE))?;
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
        let defaults = &manifest["TemplateDefaults"];
        let export_default = normalize_default(&service.paths, &text(defaults, "ExportDocumentTemplatePath"), "ExportDocument")?;
        let payment_default = normalize_default(&service.paths, &text(defaults, "PaymentVoucherTemplatePath"), "PaymentVoucher")?;
        update_settings(tx, |settings| {
            for (key, incoming) in [
                (
                    "exportDocumentTemplatePath",
                    export_default.as_str(),
                ),
                (
                    "paymentVoucherTemplatePath",
                    payment_default.as_str(),
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
    })
}
