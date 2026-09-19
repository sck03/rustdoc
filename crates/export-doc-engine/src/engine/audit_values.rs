//! Audits retain meaningful changes without a second copy of personal data or credentials.
use super::media::digest;
use serde_json::{Map, Value, json};
use std::collections::BTreeSet;

fn sanitize(key: &str, value: &Value, depth: usize) -> Value {
    let name = key.to_ascii_lowercase();
    if name.starts_with("identity")
        || name == "registeredaddress"
        || [
            "password",
            "secret",
            "apikey",
            "token",
            "credential",
            "privatekey",
        ]
        .iter()
        .any(|part| name.contains(part))
    {
        return json!("[REDACTED]");
    }
    match value {
        Value::String(text)
            if !text.is_empty()
                && ![
                    "status",
                    "type",
                    "category",
                    "currency",
                    "currencycode",
                    "unit",
                    "unitcode",
                    "accesslevel",
                    "role",
                    "action",
                    "provider",
                    "mode",
                    "source",
                    "reporttype",
                    "invoicetype",
                    "calculationmode",
                ]
                .contains(&name.as_str()) =>
        {
            json!(format!(
                "[TEXT length={} sha256={}]",
                text.encode_utf16().count(),
                &digest(text.as_bytes())[..16].to_ascii_uppercase()
            ))
        }
        Value::Array(items) => json!(format!(
            "[ARRAY items={} sha256={}]",
            items.len(),
            &digest(value.to_string().as_bytes())[..16].to_ascii_uppercase()
        )),
        Value::Object(object) if depth < 8 => Value::Object(
            object
                .iter()
                .filter(|(key, _)| !key.starts_with('_'))
                .map(|(key, value)| (key.clone(), sanitize(key, value, depth + 1)))
                .collect(),
        ),
        Value::Object(object) => json!(format!("[OBJECT properties={}]", object.len())),
        _ => value.clone(),
    }
}
pub fn changes(previous: Option<&Value>, next: Option<&Value>) -> Value {
    let mut keys = BTreeSet::new();
    for object in [previous, next]
        .into_iter()
        .flatten()
        .filter_map(Value::as_object)
    {
        keys.extend(
            object
                .keys()
                .filter(|key| {
                    !key.starts_with('_')
                        && ![
                            "updatedAt",
                            "rowVersion",
                            "expectedVersion",
                            "effectiveGrants",
                        ]
                        .contains(&key.as_str())
                })
                .cloned(),
        );
    }
    let mut old = Map::new();
    let mut new = Map::new();
    for key in keys {
        let before = previous.and_then(|value| value.get(&key));
        let after = next.and_then(|value| value.get(&key));
        if before == after {
            continue;
        }
        if let Some(value) = before {
            old.insert(key.clone(), sanitize(&key, value, 0));
        }
        if let Some(value) = after {
            new.insert(key.clone(), sanitize(&key, value, 0));
        }
    }
    json!({"oldValues":old,"newValues":new})
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn nested_identity_and_credentials_are_redacted_and_unchanged_fields_are_omitted() {
        let before = json!({"status":"Draft","profile":{"identityNumber":"sensitive-id","fullName":"保密姓名"},"password":"password-before","count":1});
        let after = json!({"status":"Reviewed","profile":{"identityNumber":"other-sensitive-id","fullName":"新姓名"},"password":"password-after","count":1});
        let delta = changes(Some(&before), Some(&after));
        assert_eq!(delta["newValues"]["status"], "Reviewed");
        assert_eq!(
            delta["oldValues"]["profile"]["identityNumber"],
            "[REDACTED]"
        );
        assert_eq!(delta["newValues"]["password"], "[REDACTED]");
        assert!(delta["newValues"].get("count").is_none());
        for secret in [
            "sensitive-id",
            "other-sensitive-id",
            "保密姓名",
            "新姓名",
            "password-before",
            "password-after",
        ] {
            assert!(!delta.to_string().contains(secret));
        }
    }
}
