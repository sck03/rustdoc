//! Apply a bounded, whitelisted COO source projection without changing invoice identities.
use super::text;
use crate::contracts;
use rust_decimal::Decimal;
use serde_json::{Value, json};

pub fn validate(imported: &Value) -> Result<(), String> {
    export_doc_contracts::validation::structure(
        contracts::schema("ApiSingleWindowCooImportDto"),
        imported,
    )?;
    validate_fields("ApiSingleWindowCooImportDto", imported)?;
    if imported["items"]
        .as_array()
        .is_some_and(|items| items.len() > 5000)
    {
        return Err("产地证资料最多 5000 行。".into());
    }
    let mut sum = Decimal::ZERO;
    for row in imported["items"].as_array().ok_or("产地证导入明细损坏。")? {
        let amount = text(row, "fobValue");
        if !amount.is_empty() {
            sum = sum.checked_add(fob(amount)?).ok_or("FOB 合计超出范围。")?;
        }
        validate_fields("ApiSingleWindowCooImportItemDto", row)?;
    }
    fob(&sum.to_string())?;
    Ok(())
}
fn validate_fields(schema: &str, value: &Value) -> Result<(), String> {
    let fields = &contracts::schema(schema)["properties"];
    for (key, value) in value.as_object().ok_or("产地证导入字段损坏。")? {
        if let Some(value) = value.as_str() {
            if !crate::document_text::is_valid(value) {
                return Err("产地证资料含不支持的控制字符。".into());
            }
            if let Some(maximum) = fields[key]["maxLength"].as_u64() {
                if value.chars().count() as u64 > maximum {
                    return Err(format!("产地证字段 {key} 最多 {maximum} 个字符。"));
                }
            }
        }
    }
    Ok(())
}
fn fob(value: &str) -> Result<Decimal, String> {
    let amount = value
        .parse::<Decimal>()
        .map_err(|_| "FOB 值不是有效十进制数。")?;
    if amount.is_sign_negative() || amount.scale() > 5 || amount.trunc().to_string().len() > 19 {
        return Err("FOB 值须为非负数，整数最多十九位、小数最多五位。".into());
    }
    Ok(amount)
}

pub(super) fn apply(document: &mut Value, source: &Value) -> Result<(), String> {
    let Some(imported) = source.get("coo") else {
        return Ok(());
    };
    validate(imported)?;
    let rows = imported["items"].as_array().ok_or("产地证导入明细损坏。")?;
    let items = document["items"].as_array_mut().ok_or("产地证明细损坏。")?;
    if rows.len() != items.len() {
        return Err("产地证导入明细与来源商品数量不一致。".into());
    }
    let mut total = Some(Decimal::ZERO);
    for (row, item) in rows.iter().zip(items) {
        item["fobValue"] = json!("");
        let fields = contracts::dto(
            contracts::schema("ApiSingleWindowCooImportItemDto"),
            row.clone(),
        );
        for (key, value) in fields.as_object().ok_or("产地证导入字段损坏。")? {
            // Missing FOB is unknown; other empty optional fields keep calculated defaults.
            if key == "fobValue" || !value.as_str().unwrap_or("").is_empty() {
                item[key] = value.clone();
            }
        }
        let amount = text(item, "fobValue");
        if amount.is_empty() {
            total = None;
        } else {
            let amount = fob(amount)?;
            total = total
                .map(|sum| sum.checked_add(amount).ok_or("FOB 合计超出范围。"))
                .transpose()?;
        }
    }
    for key in ["certType", "transDetails", "producer"] {
        if !text(imported, key).is_empty() {
            document[key] = imported[key].clone();
        }
    }
    document["fobValue"] = json!(
        total
            .map(|amount| amount.normalize().to_string())
            .unwrap_or_default()
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{
        invoice::InvoiceDraft,
        single_window::{Business, mapping},
    };
    #[test]
    fn defaults_do_not_confuse_exporter_with_factory_or_cif_with_fob() {
        let mut invoice = serde_json::to_value(
            InvoiceDraft::demo("2026-11-11", "COO-MAPPING")
                .build()
                .unwrap(),
        )
        .unwrap();
        let exporter =
            json!({"exporterNameCN":"出口贸易公司","contactPerson":"出口商联系人","phone":"12345"});
        for terms in ["CIF", "FOB NINGBO"] {
            invoice["tradeTerms"] = json!(terms);
            let document = mapping::defaults(
                Business::Coo,
                &invoice,
                &exporter,
                &json!({}),
                &json!({}),
                &json!({}),
                "2026-11-11",
            )
            .unwrap();
            let fob = terms.starts_with("FOB");
            assert_eq!(text(&document, "fobValue").is_empty(), !fob);
            for row in document["items"].as_array().unwrap() {
                assert_eq!(text(row, "fobValue").is_empty(), !fob);
                for key in ["prdcEtpsName", "prdcEtpsConcEr", "prdcEtpsTel"] {
                    assert_eq!(text(row, key), "");
                }
            }
        }
    }
    #[test]
    fn missing_fob_stays_unknown_and_extra_fields_do_not_replace_identity() {
        let mut document = json!({"items":[{"sourceItemId":1,"fobValue":"100"},{"sourceItemId":2,"fobValue":"200"}]});
        apply(
            &mut document,
            &json!({"coo":{"items":[{"fobValue":"90.12345","sourceItemId":99},{}]}}),
        )
        .unwrap();
        assert_eq!(document["items"][0]["sourceItemId"], 1);
        assert_eq!(document["items"][0]["fobValue"], "90.12345");
        assert_eq!(document["items"][1]["fobValue"], "");
        assert_eq!(document["fobValue"], "");
    }
}
