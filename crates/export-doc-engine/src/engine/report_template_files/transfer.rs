use super::*;

pub fn download(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<FileOutput> {
    if operation == DOWNLOAD_REPORT_TEMPLATE_FILE {
        let kind = report_templates::report_type(&parameter(parameters, "reportType"))?;
        demand_type(actor, kind)?;
        auth::authorize(actor, PERMISSION, "export")?;
        let resolved =
            resolve_editable(service, kind, &parameter(parameters, "templatePath"), true)?;
        return Ok(FileOutput {
            file_name: "template.html".into(),
            media_type: "text/html; charset=utf-8".into(),
            content: fs::read(&resolved.path)?,
        });
    }
    auth::authorize(actor, PERMISSION, "export")?;
    let (bytes, _) = package_bytes(service)?;
    Ok(FileOutput {
        file_name: format!(
            "templates_{}{}.edtpl",
            chrono::Utc::now().format("%Y%m%d%H%M%S"),
            &paths::nonce().map_err(unavailable)?[..8]
        ),
        media_type: "application/octet-stream".into(),
        content: bytes,
    })
}

fn upload_template_name(file_name: &str) -> Result<String> {
    let name = Path::new(file_name.trim())
        .file_name()
        .and_then(|name| name.to_str())
        .filter(|name| !name.is_empty())
        .ok_or_else(|| invalid("模板文件名不能为空。"))?;
    let name: String = name.nfc().collect();
    if !paths::valid_file_name(&name)
        || Path::new(&name)
            .extension()
            .and_then(|value| value.to_str())
            != Some(EXTENSION_NAME)
    {
        return Err(invalid("报表模板文件只支持小写 .html 扩展名。"));
    }
    Ok(name)
}

fn upload_package_name(file_name: &str) -> Result<String> {
    let name = Path::new(file_name.trim())
        .file_name()
        .and_then(|name| name.to_str())
        .map(|name| name.trim());
    let mut name: String = match name {
        Some(name) if !name.is_empty() => name.nfc().collect(),
        _ => "uploaded_template_package.edtpl".into(),
    };
    let extension = Path::new(&name)
        .extension()
        .and_then(|value| value.to_str());
    if extension.is_none() {
        name.push_str(PACKAGE_EXTENSION);
    } else if !extension.is_some_and(|value| {
        value.eq_ignore_ascii_case("edtpl") || value.eq_ignore_ascii_case("zip")
    }) {
        return Err(invalid("模板包文件只支持 .edtpl 或 .zip。"));
    }
    if !paths::valid_file_name(&name) {
        return Err(invalid("模板包文件名不符合跨平台规则。"));
    }
    Ok(name)
}

pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    metadata: &Value,
    file_name: &str,
    content: &[u8],
) -> Result<Value> {
    if operation == UPLOAD_REPORT_TEMPLATE_PACKAGE {
        auth::authorize(actor, PERMISSION, "import")?;
        let package_name = upload_package_name(file_name)?;
        let directory = service.paths.cache_root.join("TemplatePackages");
        fs::create_dir_all(&directory)?;
        let temporary = directory.join(format!(
            "{}-{package_name}",
            &paths::nonce().map_err(unavailable)?[..12]
        ));
        let result = (|| -> Result<Value> {
            paths::ensure_safe_absolute(&temporary).map_err(invalid)?;
            paths::atomic_write(&temporary, content).map_err(unavailable)?;
            import_template_package(
                service,
                actor,
                &media::read_local(&temporary, MAX_PACKAGE_BYTES)?,
                &upload_value(parameters, metadata, "strategy"),
            )
        })();
        let _ = fs::remove_file(&temporary);
        return result;
    }
    let _ = upload_template_name(file_name)?;
    let kind = report_templates::report_type(&upload_value(parameters, metadata, "reportType"))?;
    demand_type(actor, kind)?;
    auth::authorize(actor, PERMISSION, "import")?;
    if content.is_empty() {
        return Err(invalid("模板文件不能为空。"));
    }
    let content = utf8_template(content)?;
    save_template_content(
        service,
        actor,
        kind,
        &upload_value(parameters, metadata, "templatePath"),
        &upload_value(parameters, metadata, "expectedRevision"),
        &content,
    )
}
