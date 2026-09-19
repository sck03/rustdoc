//! Shared candidate values. A choice never reads or rewrites another document.
use super::{
    error::{Result, invalid},
    records::text,
    store::{self, Actor, Store},
};
use crate::generated_api::*;
use serde_json::{Value, json};
use unicode_normalization::UnicodeNormalization;

const DEFINITIONS: &[(&str, &[&str], bool)] = &[
    ("Currency", &["USD", "EUR", "CNY", "GBP", "JPY"], true),
    ("PaymentTerms", &["T/T", "L/C", "D/P", "D/A", "O/A"], true),
    ("PortOfLoading", &[], true),
    ("PortOfDestination", &[], true),
    (
        "TransportMode",
        &["BY SEA", "BY AIR", "BY TRAIN", "BY DHL", "BY FedEx"],
        true,
    ),
    (
        "SupervisionMode",
        &[
            "一般贸易",
            "来料加工",
            "进料加工",
            "补偿贸易",
            "易货贸易",
            "寄售代销",
            "边境小额贸易",
            "加工贸易",
            "保税仓库",
            "出料加工",
        ],
        true,
    ),
    ("PaymentMethod", &["支票", "电汇", "预付"], true),
    ("PaymentPayerName", &[], true),
    ("PayeeCategory", &[], true),
    ("Type", &["报关数据", "实际数据"], false),
];

pub fn handle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let requested = parameters
        .iter()
        .find(|(key, _)| *key == "optionType")
        .map(|(_, value)| value.trim())
        .unwrap_or("");
    let (kind, defaults, custom) = DEFINITIONS
        .iter()
        .find(|(kind, _, _)| kind.eq_ignore_ascii_case(requested))
        .ok_or_else(|| invalid("不支持的自定义选项类型。"))?;
    store.transaction(|tx| {
        let mut entries: Vec<_> = store::all(tx, "custom-options")?.into_iter().filter(|entry| entry["optionType"] == *kind).collect();
        if operation == SAVE_CUSTOM_OPTION {
            if !custom { return Err(invalid("此类型只允许使用固定内置候选值。")); }
            let value = text(body, "value").nfc().collect::<String>();
            if value.is_empty() || value.encode_utf16().count() > 500 { return Err(invalid("自定义选项须为 1–500 个字符。")); }
            let identity = store::normalize(&value);
            if !defaults.iter().any(|value| store::normalize(value) == identity)
                && !entries.iter().any(|entry| store::normalize(&text(entry, "value")) == identity)
            {
                entries.push(store::save(tx, "custom-options", 0, json!({"optionType":kind,"value":value}), Some(format!("{kind}:{identity}")), actor, "create")?);
            }
        }
        entries.sort_by_key(|value| value["id"].as_i64().unwrap_or(0));
        let selected = entries.len().saturating_sub(500);
        let custom_values: Vec<_> = entries.iter().skip(selected).map(|entry| text(entry, "value")).collect();
        let mut options: Vec<String> = defaults.iter().map(|value| (*value).into()).collect();
        for value in &custom_values {
            if !options.iter().any(|option| store::normalize(option) == store::normalize(value)) { options.push(value.clone()); }
        }
        Ok(json!({"optionType":kind,"predefinedOptions":defaults,"customOptions":custom_values,"options":options,"allowCustomValues":custom,"storagePolicy":"自定义候选项保存在业务数据库，随备份恢复。"}))
    })
}
