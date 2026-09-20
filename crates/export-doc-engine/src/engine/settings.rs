use super::{
    auth,
    error::{Result, conflict, invalid, unavailable},
    store::{Actor, Store},
};
use crate::{
    contracts,
    generated_api::*,
    paths::{RuntimePaths, ensure_safe_absolute},
};
use serde_json::{Value, json};
use std::path::{Component, Path};

pub(super) fn defaults(provider: &str) -> Result<Value> {
    let mut value = contracts::contract()["configuration"]["defaults"].clone();
    if !value.is_object() {
        return Err(unavailable("生成的默认设置缺失。"));
    }
    value["system"]["databaseProvider"] = json!(if provider == "SQLite" {
        "Sqlite"
    } else {
        "PostgreSQL"
    });
    value["system"]["sqliteDatabaseFileName"] = json!("exportdoc-native.db");
    Ok(value)
}
fn secrets(value: &Value, allowed: bool) -> Value {
    Value::Object(
        crate::secrets::FIELDS
            .iter()
            .map(|(path, flag)| {
                (
                    (*flag).into(),
                    json!(allowed && value[*path].as_str().is_some_and(|s| !s.is_empty())),
                )
            })
            .collect(),
    )
}
pub fn read(store: &Store, actor: &Actor) -> Result<Value> {
    // This operation explicitly exposes sanitized defaults to authenticated
    // clients. Editing still requires the separate administrator permission.
    let mut settings = current(store)?;
    for pointer in [
        "/email/password",
        "/webDav/password",
        "/system/postgreSqlPassword",
        "/ai/apiKey",
    ] {
        if let Some(value) = settings.pointer_mut(pointer) {
            *value = json!("");
        }
    }
    if !actor.admin {
        for pointer in [
            "/system/updaterEndpoint",
            "/system/postgreSqlHost",
            "/system/postgreSqlDatabase",
            "/system/postgreSqlUsername",
            "/system/postgreSqlAdditionalOptions",
            "/email/smtpHost",
            "/email/userName",
            "/email/fromAddress",
            "/email/fromDisplayName",
            "/email/recipientAllowList",
            "/email/recipientBlockList",
            "/webDav/url",
            "/webDav/userName",
            "/ai/apiEndpoint",
            "/ai/modelName",
            "/ai/systemPrompt",
            "/system/defaultExportDirectory",
        ] {
            if let Some(value) = settings.pointer_mut(pointer) {
                *value = json!("");
            }
        }
        settings["system"]["postgreSqlPort"] = json!(5432);
        settings["webDav"]["enabled"] = json!(false);
        if let Some(value) = settings.pointer_mut("/singleWindow/customsCooDefaults") {
            *value = contracts::contract()["configuration"]["defaults"]["singleWindow"]["customsCooDefaults"].clone();
        }
    }
    if store.provider()? == "PostgreSQL" {
        settings["system"]["defaultExportDirectory"] = json!("");
    }
    Ok(
        json!({"settings":settings,"secrets":secrets(&store.settings("protected-credentials")?.unwrap_or(Value::Null),actor.admin),"storagePolicy":"业务配置及加密凭证保存在当前数据库中；本地主密钥位于运行目录 Security，导出位置由保存对话框选择。"}),
    )
}
pub fn current(store: &Store) -> Result<Value> {
    let mut settings = defaults(store.provider()?)?;
    if let Some(saved) = store.settings("settings")? {
        settings = contracts::overlay(settings, &saved);
    }
    Ok(settings)
}
fn validate(value: &Value, paths: &RuntimePaths, provider: &str) -> Result<()> {
    export_doc_contracts::validation::structure(contracts::schema("AppSettings"), value)
        .map_err(invalid)?;
    let expected = if provider == "SQLite" {
        "Sqlite"
    } else {
        "PostgreSQL"
    };
    if value["system"]["databaseProvider"] != expected
        || value["system"]["sqliteDatabaseFileName"] != "exportdoc-native.db"
    {
        return Err(invalid(
            "数据库由当前运行模式确定，不能通过业务设置切换数据库或文件。",
        ));
    }
    for (path, min, max) in [
        ("/system/itemEntryBlankRowCount", 1, 100),
        ("/system/itemEntrySpareColumnCount", 0, 10),
        ("/system/backupRetentionDays", 0, 3650),
        ("/system/auditLogRetentionDays", 0, 3650),
        ("/system/logRetentionDays", 0, 3650),
        ("/system/logRetainedFileCount", 1, 1000),
        ("/system/logFileSizeLimitMB", 1, 1024),
        ("/system/postgreSqlAutoBackupDayOfWeek", 0, 6),
        ("/system/postgreSqlAutoBackupRetentionCount", 0, 10000),
        ("/exchangeRate/cacheDurationMinutes", 1, 1440),
        ("/email/smtpPort", 1, 65535),
    ] {
        if !value
            .pointer(path)
            .and_then(Value::as_i64)
            .is_some_and(|number| (min..=max).contains(&number))
        {
            return Err(invalid(format!(
                "{} 须在 {min}–{max} 之间。",
                path.trim_start_matches('/').replace('/', ".")
            )));
        }
    }
    fn visit(value: &Value, key: &str, paths: &RuntimePaths) -> Result<()> {
        match value {
            Value::Object(properties) => {
                for (key, value) in properties {
                    visit(value, key, paths)?;
                }
            }
            Value::Array(values) => {
                for value in values {
                    visit(value, key, paths)?;
                }
            }
            Value::String(text) if !text.is_empty() => {
                let key = key.to_ascii_lowercase();
                if key.ends_with("path") || key.ends_with("directory") {
                    if text.starts_with("native:") || text.starts_with("user-template:") {
                        return Ok(());
                    }
                    let path = Path::new(text);
                    if path.is_absolute()
                        || text.contains(':')
                        || path
                            .components()
                            .any(|part| !matches!(part, Component::Normal(_)))
                        || text
                            .split(['/', '\\'])
                            .any(|part| part == ".." || part == ".")
                    {
                        return Err(invalid("配置路径必须是数据目录内的相对路径。"));
                    }
                    ensure_safe_absolute(&paths.data_root.join(path)).map_err(invalid)?;
                }
            }
            _ => {}
        }
        Ok(())
    }
    #[cfg(feature = "mail")]
    for key in ["recipientAllowList", "recipientBlockList"] {
        export_doc_mail::recipient::normalize_rules(value["email"][key].as_str().unwrap_or(""))
            .map_err(invalid)?;
    }
    visit(value, "", paths)
}
pub fn save(
    store: &Store,
    paths: &RuntimePaths,
    protector: &crate::secrets::Protector,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    auth::authorize(actor, "system.settings", "manage")?;
    export_doc_contracts::validation::structure(contracts::request(operation.id), body)
        .map_err(invalid)?;
    let mut settings = body["settings"].clone();
    for (path, _) in crate::secrets::FIELDS {
        let value = settings
            .pointer_mut(path)
            .ok_or_else(|| invalid("设置缺少凭证字段。"))?;
        if value.as_str().is_none_or(|s| s.len() > 16 * 1024) {
            return Err(invalid("凭证必须是最多 16 KiB 的文本。"));
        }
        *value = json!("");
    }
    let validation = validate(&settings, paths, store.provider()?);
    if operation == VALIDATE_SETTINGS {
        let messages=validation.as_ref().err().map(|error|vec![json!({"level":"Error","propertyName":"settings","message":error.message,"isAutoFixable":false})]).unwrap_or_default();
        return Ok(
            json!({"isValid":validation.is_ok(),"hasWarnings":false,"canAutoFix":false,"messages":messages,"normalizedSettings":settings,"storagePolicy":"所有持久化路径均限定在当前数据目录中。"}),
        );
    }
    validation?;
    store.transaction(|tx|{
        let previous=tx.settings("settings")?;
        let version=previous.as_ref().and_then(|value|value["revision"].as_i64()).unwrap_or(0);
        if settings["revision"].as_i64()!=Some(version){return Err(conflict("系统设置已被修改，请重新加载后核对。当前草稿已保留。"));}
        let mut protected=tx.settings("protected-credentials")?.unwrap_or_else(||json!({}));
        if !protected.is_object(){return Err(unavailable("加密凭证存储损坏。"));}
        if body["updateSecrets"]==true {
            for (path,_) in crate::secrets::FIELDS {
                if let Some(plain)=body["settings"].pointer(path).and_then(Value::as_str).filter(|s|!s.is_empty()) {
                    protected[*path]=json!(protector.protect(path,plain).map_err(unavailable)?);
                }
            }
            tx.set_settings("protected-credentials",version+1,&protected)?;
        }
        let mut saved=settings.clone();saved["revision"]=json!(version+1);
        tx.set_settings("settings",version+1,&saved)?;
        tx.append_audit_details(&export_doc_storage::AuditWrite {
            kind:"settings",record_id:0,version:version+1,action:"edit",actor_id:actor.id,
            occurred_at:&super::store::timestamp(),note:"",
        },&super::audit_values::changes(previous.as_ref(),Some(&saved)))?;
        Ok(json!({"success":true,"requiresRestart":false,"settings":saved,"secrets":secrets(&protected,true),"message":"设置已保存"}))
    })
}

pub fn credential(
    store: &Store,
    protector: &crate::secrets::Protector,
    path: &str,
) -> Result<zeroize::Zeroizing<String>> {
    let protected = store.settings("protected-credentials")?;
    let Some(value) = protected.as_ref().and_then(|value| value.get(path)) else {
        return Ok(zeroize::Zeroizing::new(String::new()));
    };
    let value = value
        .as_str()
        .ok_or_else(|| unavailable("加密凭证记录损坏。"))?;
    protector.unprotect(path, value).map_err(unavailable)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn corrupted_credentials_are_not_reported_as_unconfigured() {
        let root = std::env::temp_dir().join(format!(
            "exportdoc-credential-test-{}",
            crate::paths::nonce().unwrap()
        ));
        std::fs::create_dir_all(&root).unwrap();
        let paths = RuntimePaths {
            app_root: root.clone(),
            data_root: root.clone(),
            cache_root: root.join("Cache"),
            log_root: root.join("Logs"),
            font_path: root.join("font.otf"),
        };
        let store = Store::open(&paths).unwrap();
        store
            .connection()
            .unwrap()
            .set_settings(
                "protected-credentials",
                1,
                &json!({"/webDav/password": "edm-rust-aes256gcm-v1:not-valid-base64!"}),
            )
            .unwrap();
        assert!(
            credential(
                &store,
                &crate::secrets::Protector::new(&root),
                "/webDav/password"
            )
            .is_err()
        );
        let _ = std::fs::remove_dir_all(root);
    }
}
