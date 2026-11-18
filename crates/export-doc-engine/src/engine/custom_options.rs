//! Shared candidate values. A choice never reads or rewrites another document.
use super::{
    error::{Result, invalid},
    records::text,
    store::{self, Actor, Connection, Store},
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
    let execute = |tx: &Connection, actor: &Actor| {
        if operation == SAVE_CUSTOM_OPTION {
            if !custom {
                return Err(invalid("此类型只允许使用固定内置候选值。"));
            }
            let value = text(body, "value").nfc().collect::<String>();
            if value.is_empty() || value.encode_utf16().count() > 500 {
                return Err(invalid("自定义选项须为 1–500 个字符。"));
            }
            let identity = store::normalize(&value);
            let key = store::normalize(&format!("{kind}:{identity}"));
            if !defaults
                .iter()
                .any(|value| store::normalize(value) == identity)
                && tx.find_identity("custom-options", &key)?.is_none()
            {
                store::save(
                    tx,
                    "custom-options",
                    0,
                    json!({"optionType":kind,"value":value}),
                    Some(key),
                    actor,
                    "create",
                )?;
            }
        }
        let (_, entries) = tx.scan_generic(
            &export_doc_storage::GenericQuery {
                kind: "custom-options",
                scope: export_doc_storage::DataScope {
                    all: true,
                    ..Default::default()
                },
                filters: &[("optionType", (*kind).into())],
                fields: &[("value", "value")],
                limit: 500,
                ..Default::default()
            },
            None,
        )?;
        let custom_values: Vec<_> = entries
            .iter()
            .rev()
            .map(|entry| text(entry, "value"))
            .collect();
        let mut options: Vec<String> = defaults.iter().map(|value| (*value).into()).collect();
        let mut seen: std::collections::BTreeSet<_> = options
            .iter()
            .map(|value| store::normalize(value))
            .collect();
        for value in &custom_values {
            if seen.insert(store::normalize(value)) {
                options.push(value.clone());
            }
        }
        Ok(
            json!({"optionType":kind,"predefinedOptions":defaults,"customOptions":custom_values,"options":options,"allowCustomValues":custom,"storagePolicy":"自定义候选项保存在业务数据库，随备份恢复。"}),
        )
    };
    if operation == SAVE_CUSTOM_OPTION {
        store.transaction_for(actor, operation, &[], execute)
    } else {
        store.read_for(actor, operation, &[], execute)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_are_bounded_without_write_transactions_and_old_values_stay_unique() {
        let scratch = crate::engine::test_support::ScratchDirectory::new();
        let service = scratch.service();
        let login: Value = serde_json::from_slice(
            &service
                .dispatch(
                    LOGIN,
                    &[],
                    &[],
                    Some(json!({"username":"admin","password":""})),
                    "",
                )
                .unwrap(),
        )
        .unwrap();
        let actor = service
            .session_actor(login["accessToken"].as_str().unwrap())
            .unwrap();
        let store = &service.store;
        store.transaction(|tx| {
            for index in 0..505 {
                store::save(tx, "custom-options", 0,
                    json!({"optionType":"PaymentPayerName","value":format!("source-{index:03}")}),
                    Some(format!("paymentpayername:source-{index:03}")), &actor, "create")?;
            }
            store::save(tx, "custom-options", 0, json!({"optionType":"Currency","value":"OTHER"}),
                Some("currency:other".into()), &actor, "create")?;
            Ok(())
        }).unwrap();
        let writes = store.metrics()["writeTransactions"]["completed"].clone();
        let parameters = [("optionType", "PaymentPayerName".into())];
        let result = handle(
            store,
            &actor,
            LIST_CUSTOM_OPTIONS,
            &parameters,
            &Value::Null,
        )
        .unwrap();
        let values = result["customOptions"].as_array().unwrap();
        assert_eq!(values.len(), 500);
        assert_eq!(values.first().unwrap(), "source-005");
        assert_eq!(values.last().unwrap(), "source-504");
        assert_eq!(store.metrics()["writeTransactions"]["completed"], writes);
        handle(
            store,
            &actor,
            SAVE_CUSTOM_OPTION,
            &parameters,
            &json!({"value":"SOURCE-000"}),
        )
        .unwrap();
        assert_eq!(store.all("custom-options").unwrap().len(), 506);
    }
}
