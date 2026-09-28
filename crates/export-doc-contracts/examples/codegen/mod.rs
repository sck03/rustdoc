pub mod native;
#[cfg(test)]
mod tests;
pub mod typescript;

use serde_json::Value;
use std::{collections::HashSet, error::Error, io::Write, path::Path};
pub type Result<T> = std::result::Result<T, Box<dyn Error>>;

pub struct Generated {
    pub web: String,
    pub rust: String,
    pub contract: String,
    pub operation_count: usize,
    pub schema_count: usize,
}
pub struct Operation<'a> {
    pub id: &'a str,
    pub method: &'a str,
    pub path: &'a str,
    pub value: &'a Value,
}
pub fn ordered(value: &Value) -> Vec<(&str, &Value)> {
    let mut entries = value
        .as_object()
        .map(|object| {
            object
                .iter()
                .map(|(key, value)| (key.as_str(), value))
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    entries.sort_by_key(|(key, _)| *key);
    entries
}
pub fn array(value: &Value) -> &[Value] {
    value.as_array().map(Vec::as_slice).unwrap_or_default()
}
pub fn text(value: &Value) -> &str {
    value.as_str().unwrap_or_default()
}
pub fn quoted(value: &str) -> String {
    serde_json::to_string(value).expect("string serialization")
}
pub fn required(schema: &Value, name: &str) -> bool {
    array(&schema["required"]).iter().any(|value| value == name)
}
pub fn generate(document: &Value) -> Result<Generated> {
    if !text(&document["openapi"]).starts_with("3.")
        || !document["paths"].is_object()
        || !document["components"]["schemas"].is_object()
    {
        return Err("Expected OpenAPI 3 paths and schemas".into());
    }
    fn validate_refs(value: &Value, schemas: &Value) -> Result<()> {
        if let Some(reference) = value.get("$ref").and_then(Value::as_str) {
            let name = reference
                .strip_prefix("#/components/schemas/")
                .ok_or("Only local schema references are supported")?;
            if schemas.get(name).is_none() {
                return Err(format!("Unresolved schema: {reference}").into());
            }
        }
        match value {
            Value::Object(object) => {
                for child in object.values() {
                    validate_refs(child, schemas)?;
                }
            }
            Value::Array(items) => {
                for child in items {
                    validate_refs(child, schemas)?;
                }
            }
            _ => {}
        }
        Ok(())
    }
    validate_refs(document, &document["components"]["schemas"])?;
    let mut operations = Vec::new();
    let mut names = HashSet::new();
    for (path, item) in ordered(&document["paths"]) {
        for method in ["get", "post", "put", "delete", "patch"] {
            if let Some(value) = item.get(method) {
                let id = value["operationId"]
                    .as_str()
                    .ok_or("Operation ID is required")?;
                if !names.insert(typescript::identifier(id, true)) {
                    return Err(format!("Duplicate operation: {id}").into());
                }
                if !value["x-exportdoc-policy"].is_object() {
                    return Err(format!("Missing policy: {id}").into());
                }
                operations.push(Operation {
                    id,
                    method,
                    path,
                    value,
                });
            }
        }
    }
    let web = typescript::generate(document, &operations);
    let (rust, contract, schema_count) = native::generate(document, &operations)?;
    Ok(Generated {
        web,
        rust,
        contract,
        operation_count: operations.len(),
        schema_count,
    })
}

pub fn write_generated(path: &Path, content: &str) -> Result<()> {
    if std::fs::read_to_string(path).ok().as_deref() == Some(content) {
        return Ok(());
    }
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let temporary = path.with_extension(format!("{}.tmp", std::process::id()));
    let mut file = std::fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)?;
    let result = (|| -> Result<()> {
        file.write_all(content.as_bytes())?;
        file.sync_all()?;
        drop(file);
        std::fs::rename(&temporary, path)?;
        Ok(())
    })();
    if temporary.exists() {
        let _ = std::fs::remove_file(&temporary);
    }
    result
}
