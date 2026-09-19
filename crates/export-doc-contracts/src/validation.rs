//! Structural checks for generated contracts. Business rules remain in Domain
//! and application services; this is not a second schema or routing catalog.
use crate::contracts;
use serde_json::Value;

pub fn response(operation: &str, value: &Value) -> Result<(), String> {
    structure(contracts::response(operation), value)
}
pub fn structure(schema: &Value, value: &Value) -> Result<(), String> {
    check(schema, value, "$", 0)
}
fn check(schema: &Value, value: &Value, path: &str, depth: usize) -> Result<(), String> {
    if depth > 64 {
        return Err(format!("{path}: contract nesting limit exceeded"));
    }
    if schema == &Value::Bool(false) {
        return Err(format!("{path}: value is not allowed"));
    }
    if let Some(reference) = schema["$ref"].as_str() {
        let name = reference
            .strip_prefix("#/components/schemas/")
            .ok_or_else(|| format!("{path}: unresolved schema reference"))?;
        let schema = contracts::schema(name);
        if schema.is_null() {
            return Err(format!("{path}: missing schema {name}"));
        }
        return check(schema, value, path, depth + 1);
    }
    for key in ["allOf", "anyOf", "oneOf"] {
        if let Some(options) = schema[key].as_array() {
            let passed = options
                .iter()
                .filter(|option| check(option, value, path, depth + 1).is_ok())
                .count();
            if (key == "allOf" && passed != options.len())
                || (key == "anyOf" && passed == 0)
                || (key == "oneOf" && passed != 1)
            {
                return Err(format!("{path}: {key} mismatch"));
            }
        }
    }
    if let Some(options) = schema["enum"].as_array() {
        if !options.contains(value) {
            return Err(format!("{path}: unknown enumeration value"));
        }
    }
    let matches = |kind: &str| match kind {
        "object" => value.is_object(),
        "array" => value.is_array(),
        "string" => value.is_string(),
        "integer" => value.as_i64().is_some() || value.as_u64().is_some(),
        "number" => value.is_number(),
        "boolean" => value.is_boolean(),
        "null" => value.is_null(),
        _ => false,
    };
    let valid = match &schema["type"] {
        Value::String(kind) => matches(kind),
        Value::Array(types) => types.iter().any(|kind| kind.as_str().is_some_and(matches)),
        _ => true,
    };
    if !valid {
        return Err(format!("{path}: expected {}", schema["type"]));
    }
    if let Some(object) = value.as_object() {
        for key in schema["required"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
        {
            if !object.contains_key(key) {
                return Err(format!("{path}.{key}: required property missing"));
            }
        }
        for (key, value) in object {
            if let Some(property) = schema["properties"].get(key) {
                check(property, value, &format!("{path}.{key}"), depth + 1)?;
            } else if let Some(additional) = schema.get("additionalProperties") {
                check(additional, value, &format!("{path}.{key}"), depth + 1)?;
            }
        }
    }
    if let Some(items) = value.as_array() {
        if let Some(schema) = schema.get("items") {
            for (index, item) in items.iter().enumerate() {
                check(schema, item, &format!("{path}[{index}]"), depth + 1)?;
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test]
    fn missing_nested_paging_and_invalid_scalars_are_reported() {
        assert!(
            response("GetWorklist", &json!({"items":[]}))
                .unwrap_err()
                .contains("$.page")
        );
        let request = contracts::object("CreateMeetingRoom", true);
        assert!(structure(contracts::request("CreateMeetingRoom"), &request).is_ok());
        let mut invalid = request;
        invalid["capacity"] = json!(false);
        assert!(
            structure(contracts::request("CreateMeetingRoom"), &invalid)
                .unwrap_err()
                .contains("capacity")
        );
    }
}
