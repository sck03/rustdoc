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
        let value: Value = serde_json::from_str(&source)
            .map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))?;
        serde_json::to_string(&value).map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))
    })?;
    Ok(())
}
