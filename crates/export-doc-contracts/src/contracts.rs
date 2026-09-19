//! Native forms consume the generated OpenAPI contract, including nested fields.
use serde_json::{Map, Value, json};
use std::sync::OnceLock;

pub fn contract() -> &'static Value {
    static CONTRACT: OnceLock<Value> = OnceLock::new();
    CONTRACT.get_or_init(|| {
        serde_json::from_str(include_str!("generated_contract.json"))
            .expect("generated OpenAPI contract")
    })
}

pub fn resolve(schema: &Value) -> &Value {
    if let Some(reference) = schema.get("$ref").and_then(Value::as_str) {
        &contract()["schemas"][reference.rsplit('/').next().unwrap_or("")]
    } else if let Some(alternatives) = schema
        .get("anyOf")
        .or_else(|| schema.get("oneOf"))
        .and_then(Value::as_array)
    {
        alternatives
            .iter()
            .find(|item| item["type"] != "null")
            .map(resolve)
            .unwrap_or(schema)
    } else {
        schema
    }
}

pub fn kind(schema: &Value) -> &str {
    let schema = resolve(schema);
    match &schema["type"] {
        Value::String(value) => value,
        Value::Array(values) => ["object", "array", "integer", "number", "boolean", "string"]
            .into_iter()
            .find(|kind| values.iter().any(|value| value == kind))
            .unwrap_or("object"),
        _ if schema.get("properties").is_some() => "object",
        _ if schema.get("enum").is_some() => {
            match schema["enum"]
                .as_array()
                .and_then(|values| values.iter().find(|value| !value.is_null()))
            {
                Some(Value::String(_)) => "string",
                Some(Value::Bool(_)) => "boolean",
                Some(Value::Number(number)) if number.is_i64() || number.is_u64() => "integer",
                Some(Value::Number(_)) => "number",
                Some(Value::Array(_)) => "array",
                _ => "object",
            }
        }
        _ => "object",
    }
}

pub fn nullable(schema: &Value) -> bool {
    nullable_at(schema, 0)
}
fn nullable_at(schema: &Value, depth: usize) -> bool {
    if depth > 16 {
        return false;
    }
    if schema["type"] == "null"
        || schema["type"]
            .as_array()
            .is_some_and(|types| types.iter().any(|kind| kind == "null"))
    {
        return true;
    }
    if ["anyOf", "oneOf"].iter().any(|key| {
        schema[*key].as_array().is_some_and(|alternatives| {
            alternatives.iter().any(|item| nullable_at(item, depth + 1))
        })
    }) {
        return true;
    }
    schema.get("$ref").is_some() && nullable_at(resolve(schema), depth + 1)
}

pub fn initial(schema: &Value) -> Value {
    initial_at(schema, 0)
}
fn initial_at(schema: &Value, depth: usize) -> Value {
    if depth > 16 {
        return Value::Null;
    }
    if let Some(default) = schema.get("default") {
        return default.clone();
    }
    if nullable(schema) {
        return Value::Null;
    }
    let schema = resolve(schema);
    if let Some(default) = schema.get("default") {
        return default.clone();
    }
    if let Some(value) = schema["enum"].as_array().and_then(|values| values.first()) {
        return value.clone();
    }
    match kind(schema) {
        "object" => Value::Object(
            schema["properties"]
                .as_object()
                .map(|properties| {
                    properties
                        .iter()
                        .map(|(key, value)| (key.clone(), initial_at(value, depth + 1)))
                        .collect()
                })
                .unwrap_or_default(),
        ),
        "array" => json!([]),
        "integer" | "number" => json!(0),
        "boolean" => json!(false),
        _ => json!(""),
    }
}

pub fn request(operation: &str) -> &'static Value {
    &contract()["operations"][operation]["request"]
}
pub fn response(operation: &str) -> &'static Value {
    &contract()["operations"][operation]["response"]
}
pub fn schema(name: &str) -> &'static Value {
    &contract()["schemas"][name]
}

pub fn object(operation: &str, body: bool) -> Value {
    initial(if body {
        request(operation)
    } else {
        response(operation)
    })
}

pub fn overlay(mut target: Value, source: &Value) -> Value {
    if let (Some(target), Some(source)) = (target.as_object_mut(), source.as_object()) {
        for (key, value) in source {
            let next = if target.get(key).is_some_and(Value::is_object) && value.is_object() {
                overlay(target[key].clone(), value)
            } else {
                value.clone()
            };
            target.insert(key.clone(), next);
        }
    } else {
        target = source.clone();
    }
    target
}

/// Project a service record onto its public schema without changing values or
/// inventing missing fields. Internal persistence metadata is not part of DTOs.
pub fn project(schema: &Value, value: Value) -> Value {
    project_value(schema, value, false)
}

/// Serialize a DTO using its declared properties. Unlike validation, an omitted
/// additionalProperties keyword does not expose private storage fields.
pub fn dto(schema: &Value, value: Value) -> Value {
    project_value(schema, value, true)
}

fn project_value(schema: &Value, value: Value, declared_only: bool) -> Value {
    let schema = resolve(schema);
    match value {
        Value::Object(values) => {
            let properties = schema["properties"].as_object();
            let additional = schema.get("additionalProperties");
            Value::Object(
                values
                    .into_iter()
                    .filter_map(|(key, value)| {
                        if let Some(field) = properties.and_then(|fields| fields.get(&key)) {
                            Some((key, project_value(field, value, declared_only)))
                        } else if additional == Some(&Value::Bool(false)) {
                            None
                        } else if let Some(field) = additional.filter(|field| field.is_object()) {
                            Some((key, project_value(field, value, declared_only)))
                        } else if declared_only
                            && properties.is_some()
                            && additional != Some(&Value::Bool(true))
                        {
                            None
                        } else {
                            Some((key, value))
                        }
                    })
                    .collect(),
            )
        }
        Value::Array(values) => Value::Array(
            values
                .into_iter()
                .map(|value| project_value(&schema["items"], value, declared_only))
                .collect(),
        ),
        value => value,
    }
}

pub fn page(items: Vec<Value>, total: usize, number: usize, size: usize) -> Value {
    let pages = total.div_ceil(size).max(1);
    json!({"items":items,"totalCount":total,"pageNumber":number,"pageSize":size,"totalPages":pages,"hasPreviousPage":number>1,"hasNextPage":number<pages})
}

pub fn properties(schema: &Value) -> Option<&Map<String, Value>> {
    resolve(schema)["properties"].as_object()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn enums_without_an_explicit_type_remain_editable_scalar_fields() {
        let employment = &schema("PersonnelCreateRequest")["properties"]["employmentType"];
        assert_eq!(kind(employment), "string");
        assert_eq!(initial(employment), "FullTime");
        assert_eq!(kind(&json!({"enum":[null, false, true]})), "boolean");
        assert_eq!(kind(&json!({"enum":[1, 2]})), "integer");
        assert_eq!(kind(&json!({"enum":[1.5, 2.5]})), "number");
    }
    #[test]
    fn nullable_compositions_do_not_create_phantom_nested_records() {
        let account = &schema("PersonnelRecord")["properties"]["account"];
        assert!(nullable(account));
        assert_eq!(initial(account), Value::Null);
        assert!(nullable(
            &json!({"oneOf":[{"type":"null"},{"type":"object","properties":{"id":{"type":"integer"}}}]})
        ));
        assert!(nullable(
            &json!({"anyOf":[{"type":"string"},{"type":"null"}]})
        ));
        assert!(!nullable(&json!({"type":["integer","string"]})));
        assert_eq!(
            initial(&json!({"type":["string","null"],"default":"explicit"})),
            "explicit"
        );
    }
    #[test]
    fn generated_contract_retains_decimal_date_and_nested_personnel_fields() {
        assert_eq!(
            kind(&schema("ApiPaymentDto")["properties"]["usdAmount"]),
            "number"
        );
        assert_eq!(
            schema("ApiPaymentDto")["properties"]["paymentDate"]["format"],
            "date"
        );
        let profile = object("CreatePersonnel", true);
        assert!(profile["profile"].is_object());
        assert!(profile["profile"].get("identityNumber").is_some());
    }

    #[test]
    fn projection_keeps_public_values_and_dictionaries_without_exposing_record_metadata() {
        let value = project(
            response("CreatePayment"),
            json!({
                "success": true, "message": "已保存", "payment": {
                    "id": 1, "cnyAmount": "1234567890123.456789", "paymentDate": null,
                    "rowVersion": "native:2", "createdAt": "internal", "versionNumber": 2
                }
            }),
        );
        assert_eq!(value["payment"]["cnyAmount"], "1234567890123.456789");
        assert_eq!(value["payment"]["rowVersion"], "native:2");
        assert!(value["payment"]["paymentDate"].is_null());
        assert!(value["payment"].get("createdAt").is_none());
        assert!(value["payment"].get("versionNumber").is_none());
        assert!(value["payment"].get("voucherNo").is_none());
        let dictionary = json!({"自定义字段": {"value": "保留"}});
        assert_eq!(
            project(
                &json!({"type":"object","additionalProperties":{}}),
                dictionary.clone()
            ),
            dictionary
        );
    }
}
