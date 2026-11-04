//! Pure deterministic JSON/Unicode helpers are registered before schema parsing.
use crate::Result;
use rusqlite::{Connection, functions::FunctionFlags};
use serde_json::Value;
use unicode_normalization::UnicodeNormalization;

pub(crate) fn register(connection: &Connection) -> Result<()> {
    let flags = FunctionFlags::SQLITE_UTF8
        | FunctionFlags::SQLITE_DETERMINISTIC
        | FunctionFlags::SQLITE_INNOCUOUS;
    connection.create_scalar_function("template_normalize", 1, flags, |context| {
        Ok(context
            .get::<Option<String>>(0)?
            .unwrap_or_default()
            .nfc()
            .collect::<String>()
            .trim()
            .to_lowercase())
    })?;
    connection.create_scalar_function("record_json", 1, flags, |context| {
        let source = context
            .get::<Option<String>>(0)?
            .unwrap_or_else(|| "null".into());
        let mut value: Value = serde_json::from_str(&source)
            .map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))?;
        canonicalize(&mut value);
        serde_json::to_string(&value).map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))
    })?;
    Ok(())
}
/// UTF-8 key order is the PostgreSQL C collation used by exportdoc_record_text.
fn canonicalize(value: &mut Value) {
    match value {
        Value::Object(fields) => {
            for value in fields.values_mut() {
                canonicalize(value);
            }
            fields.sort_keys();
        }
        Value::Array(items) => {
            for value in items {
                canonicalize(value);
            }
        }
        _ => {}
    }
}
