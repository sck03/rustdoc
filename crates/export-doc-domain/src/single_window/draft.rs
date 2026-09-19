use super::{Business, pascal, reference, text};
use serde_json::{Value, json};
use std::collections::{BTreeMap, BTreeSet};
pub fn identity(row: &Value) -> String {
    if let Some(id) = row["sourceItemId"].as_i64().filter(|n| *n > 0) {
        format!("Item:{id}")
    } else if !text(row, "sourceStyleNo").is_empty() {
        format!("Style:{}", text(row, "sourceStyleNo"))
    } else {
        format!("Row:{}", row["gNo"].as_i64().unwrap_or(1).max(1))
    }
}
fn excluded(business: Business, scope: &str, key: &str) -> bool {
    let name = if scope == "goods" {
        "CustomsCooItemEditableExclusions"
    } else if business == Business::Coo {
        "CustomsCooDocumentEditableExclusions"
    } else {
        "AgentConsignmentEditableExclusions"
    };
    reference()["sets"][name]
        .as_array()
        .into_iter()
        .flatten()
        .any(|s| s == &pascal(key))
}
/// Public lock keys retain the original Goods:Item:id:Property contract.
pub fn fields(business: Business, document: &Value) -> BTreeMap<String, String> {
    let mut result = BTreeMap::new();
    for key in crate::contracts::schema(business.schema())["properties"]
        .as_object()
        .into_iter()
        .flat_map(|o| o.keys())
    {
        let value = &document[key];
        if let Some(value) = value
            .as_str()
            .filter(|_| !excluded(business, business.scope(), key) && !key.starts_with('_'))
        {
            result.insert(pascal(key), value.trim().into());
        }
    }
    for row in document["items"].as_array().into_iter().flatten() {
        let identity = identity(row);
        for (key, value) in row.as_object().into_iter().flatten() {
            if let Some(value) = value.as_str().filter(|_| !excluded(business, "goods", key)) {
                result.insert(
                    format!("Goods:{identity}:{}", pascal(key)),
                    value.trim().into(),
                );
            }
        }
    }
    result
}
pub fn locks(business: Business, document: &Value, defaults: &Value) -> BTreeSet<String> {
    let defaults = fields(business, defaults);
    fields(business, document)
        .into_iter()
        .filter_map(|(key, value)| {
            (defaults.get(&key).is_none_or(|default| *default != value)).then_some(key)
        })
        .collect()
}
pub fn restore_locked(
    business: Business,
    defaults: &mut Value,
    saved: &Value,
    locks: &BTreeSet<String>,
) {
    if let Some(object) = defaults.as_object_mut() {
        for (key, value) in object {
            if value.is_string() && locks.contains(&pascal(key)) {
                *value = saved[key].clone();
            }
        }
    }
    if let Some(rows) = defaults["items"].as_array_mut() {
        let saved_rows: BTreeMap<_, _> = saved["items"]
            .as_array()
            .into_iter()
            .flatten()
            .map(|row| (identity(row), row))
            .collect();
        for row in rows.iter_mut() {
            let id = identity(row);
            let Some(saved_row) = saved_rows.get(&id) else {
                continue;
            };
            for (key, value) in row.as_object_mut().into_iter().flatten() {
                if value.is_string() && locks.contains(&format!("Goods:{id}:{}", pascal(key))) {
                    *value = saved_row[key].clone();
                }
            }
        }
        for row in saved["items"]
            .as_array()
            .into_iter()
            .flatten()
            .filter(|row| row["sourceItemId"].as_i64().unwrap_or(0) <= 0)
        {
            let mut row = row.clone();
            row["gNo"] = json!(rows.len() + 1);
            rows.push(row);
        }
    }
    for key in [
        "id",
        "status",
        "certNo",
        "consignNo",
        "counterpartyStatus",
        "draftRevision",
        "lastGeneratedAt",
        "attachments",
        "nonpartyCorps",
    ] {
        if saved.get(key).is_some() && defaults.get(key).is_some() {
            defaults[key] = saved[key].clone();
        }
    }
    defaults["expectedDraftRevision"] = saved["draftRevision"].clone();
    defaults["manualLockedFieldCount"] = json!(locks.len());
    let _ = business;
}
pub fn changes(business: Business, baseline: &Value, defaults: &Value) -> (usize, String) {
    let previous = fields(business, baseline);
    let current = fields(business, defaults);
    let keys: BTreeSet<_> = previous.keys().chain(current.keys()).collect();
    let changed: Vec<_> = keys
        .into_iter()
        .filter(|key| {
            if business == Business::Coo {
                let set = if key.starts_with("Goods:") {
                    "CustomsCooSourceDiffGoodsFields"
                } else {
                    "CustomsCooSourceDiffFields"
                };
                if !reference()["sets"][set]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .any(|field| field.as_str() == key.rsplit(':').next())
                {
                    return false;
                }
            }
            previous.get(*key) != current.get(*key)
        })
        .collect();
    let summary = changed
        .iter()
        .take(20)
        .map(|key| {
            format!(
                "{}：{} → {}",
                display(business, key),
                previous.get(*key).map(String::as_str).unwrap_or(""),
                current.get(*key).map(String::as_str).unwrap_or("")
            )
        })
        .collect::<Vec<_>>()
        .join("\n");
    (changed.len(), summary)
}
pub fn display(business: Business, key: &str) -> String {
    let property = key.rsplit(':').next().unwrap_or(key);
    let scope = if key.starts_with("Goods:") {
        "goods"
    } else {
        business.scope()
    };
    let found = reference()["labels"]
        .as_object()
        .into_iter()
        .flatten()
        .find(|(k, _)| {
            k.strip_prefix(&format!("{scope}."))
                .is_some_and(|k| pascal(k) == property)
        });
    let description = found.and_then(|(_, v)| v.as_str()).unwrap_or(property);
    if key.starts_with("Goods:") {
        format!(
            "{} {description}",
            key.rsplit_once(':').map(|(s, _)| s).unwrap_or(key)
        )
    } else {
        description.into()
    }
}
pub fn details(
    business: Business,
    document: &Value,
    defaults: &Value,
    locks: &BTreeSet<String>,
) -> Value {
    let current = fields(business, document);
    let suggested = fields(business, defaults);
    json!({"count":locks.len(),"fields":locks.iter().map(|key| json!({"key":key,"displayName":display(business,key),"currentValue":current.get(key).cloned().unwrap_or_default(),"suggestedValue":suggested.get(key).cloned().unwrap_or_default()})).collect::<Vec<_>>()})
}
