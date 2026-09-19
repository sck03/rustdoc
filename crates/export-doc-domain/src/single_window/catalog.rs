use super::{reference, text};
use crate::contracts;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::OnceLock,
};
use unicode_normalization::UnicodeNormalization;
pub fn defaults() -> &'static Value {
    static DATA: OnceLock<Value> = OnceLock::new();
    DATA.get_or_init(|| {
        serde_json::from_str(include_str!(
            "../../../../Resources/SingleWindow/singlewindow_reference_catalogs.json"
        ))
        .expect("bundled declaration dictionary")
    })
}
pub fn authorities() -> Value {
    let data: Value = serde_json::from_str(include_str!(
        "../../../../Resources/SingleWindow/customs_coo_issuing_authorities.json"
    ))
    .expect("bundled issuing authorities");
    let overrides: Value = serde_json::from_str(include_str!(
        "../../../../Resources/SingleWindow/customs_coo_issuing_authorities.address_overrides.json"
    ))
    .expect("bundled issuing addresses");
    let options: Vec<_> = data["entries"].as_array().into_iter().flatten().map(|entry| {
        let code = text(entry, "code");
        let address = overrides["entries"].as_array().into_iter().flatten().find(|o| o["code"] == code).map(|o| text(o, "applicationAddress")).filter(|s| !s.is_empty()).unwrap_or_else(|| text(entry, "applicationAddress"));
        json!({"code":code,"label":format!("{code}：{}",text(entry,"name")),"applicationAddress":address})
    }).collect();
    json!({"options":options,"storagePolicy":"内置官方签证机构资料"})
}
pub fn normalized(value: &str) -> String {
    value
        .nfc()
        .flat_map(char::to_uppercase)
        .filter(|c| {
            !c.is_whitespace()
                && !matches!(c, ',' | '，' | '.' | '(' | ')' | '（' | '）' | '-' | '_')
        })
        .collect()
}
pub fn find<'a>(catalog: &'a Value, group: &str, value: &str) -> Option<&'a Value> {
    let value = normalized(value);
    if value.is_empty() {
        return None;
    }
    catalog[group].as_array()?.iter().find(|entry| {
        [
            "code",
            "acdCode",
            "alphaCode",
            "value",
            "name",
            "englishName",
            "chineseName",
        ]
        .iter()
        .any(|key| normalized(text(entry, key)) == value)
            || entry["aliases"].as_array().is_some_and(|a| {
                a.iter()
                    .any(|alias| normalized(alias.as_str().unwrap_or("")) == value)
            })
    })
}
pub fn resolve(catalog: &Value, group: &str, value: &str, field: &str) -> String {
    find(catalog, group, value)
        .map(|entry| text(entry, field))
        .unwrap_or("")
        .into()
}
pub fn validate(value: &Value) -> Result<(), String> {
    export_doc_contracts::validation::structure(
        contracts::schema("SingleWindowReferenceCatalogModel"),
        value,
    )?;
    for group in [
        "countries",
        "acdCountries",
        "currencies",
        "acdTradeModes",
        "transportModes",
        "ports",
    ] {
        let rows = value[group].as_array().ok_or("词典内容必须为列表。")?;
        if rows.len() > 20_000 {
            return Err("每类词典最多 20000 条。".into());
        }
        let mut codes = BTreeSet::new();
        let mut aliases = BTreeMap::new();
        for row in rows {
            let id = if ["ports", "transportModes"].contains(&group) {
                text(row, "value")
            } else {
                text(row, "code")
            };
            if id.is_empty() || id.chars().count() > 100 || !codes.insert(normalized(id)) {
                return Err(format!("{group} 存在空值或重复代码。"));
            }
            for input in row.as_object().into_iter().flat_map(|o| o.values()) {
                let texts: Vec<_> = if let Some(array) = input.as_array() {
                    if array.len() > 100 {
                        return Err("每条词典最多 100 个别名。".into());
                    }
                    array.iter().filter_map(Value::as_str).collect()
                } else {
                    input.as_str().into_iter().collect()
                };
                if texts
                    .iter()
                    .any(|s| s.chars().count() > 500 || s.chars().any(char::is_control))
                {
                    return Err("词典文本超限或含控制字符。".into());
                }
            }
            for alias in row["aliases"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(Value::as_str)
                .map(normalized)
                .filter(|s| !s.is_empty())
            {
                if aliases
                    .insert(alias, id)
                    .is_some_and(|previous| previous != id)
                {
                    return Err("同一词典别名不能指向不同记录。".into());
                }
            }
        }
    }
    Ok(())
}
pub fn origin_options(cert: &str) -> &'static Value {
    let key = match cert {
        "A" => "AustraliaAgreementOptions",
        "F" => "ChileAgreementOptions",
        "K" => "KoreaAgreementOptions",
        "H" => "TaiwanAgreementOptions",
        "E" => "FormEOptions",
        "G" => "GspReferenceOptions",
        "GE" => "GeorgiaAgreementOptions",
        "RC" => "RcepOptions",
        _ => "EmptyOptions",
    };
    &reference()["origin"][key]
}
pub fn editor_options() -> Value {
    let mut value = reference()["options"].clone();
    value["originCriteriaOptionSets"] = json!(
        ["C", "A", "F", "K", "H", "E", "G", "GE", "RC"]
            .into_iter()
            .map(|cert| json!({"certType":cert,"originCriteria":"","options":origin_options(cert)}))
            .collect::<Vec<_>>()
    );
    value["originCriteriaSubOptionSets"] = json!([{"certType":"E","originCriteria":"PSR","options":reference()["origin"]["FormESubOptions"]}]);
    value["storagePolicy"] = json!("原单一窗口业务选项");
    value
}
