use super::{Business, catalog, label, reference, text};
use crate::contracts;
use rust_decimal::Decimal;
use serde_json::Value;

#[derive(Clone, Debug)]
pub struct Issue {
    pub scope: &'static str,
    pub field: String,
    pub row: Option<usize>,
    pub message: String,
}
pub fn structure(business: Business, document: &Value) -> Result<(), String> {
    export_doc_contracts::validation::structure(contracts::schema(business.schema()), document)?;
    fn limits(value: &Value, depth: usize) -> Result<(), String> {
        if depth > 12 {
            return Err("单证内容嵌套过深。".into());
        }
        match value {
            Value::String(text)
                if text.len() > 64 * 1024
                    || text
                        .chars()
                        .any(|c| matches!(c as u32,0..=8|11..=12|14..=31|0xfffe|0xffff)) =>
            {
                Err("单证字段超限或含 XML 非法字符。".into())
            }
            Value::Array(items) => {
                if items.len() > 5000 {
                    return Err("单证列表最多 5000 行。".into());
                }
                for item in items {
                    limits(item, depth + 1)?;
                }
                Ok(())
            }
            Value::Object(items) => {
                for item in items.values() {
                    limits(item, depth + 1)?;
                }
                Ok(())
            }
            _ => Ok(()),
        }
    }
    limits(document, 0)?;
    for name in ["items", "nonpartyCorps", "attachments"] {
        if let Some(rows) = document[name].as_array() {
            if name != "items" && rows.len() > 200 {
                return Err("第三方企业或附件最多 200 行。".into());
            }
        }
    }
    if let Some(rows) = document["items"].as_array() {
        let mut identities = std::collections::BTreeSet::new();
        for (index, row) in rows.iter().enumerate() {
            if row["gNo"].as_i64() != Some(index as i64 + 1)
                || !identities.insert(super::draft::identity(row))
            {
                return Err("货物行号须连续，来源货项不能重复。".into());
            }
        }
    }
    Ok(())
}
fn issue(
    issues: &mut Vec<Issue>,
    scope: &'static str,
    row: Option<usize>,
    field: &str,
    message: String,
) {
    issues.push(Issue {
        scope,
        field: field.into(),
        row,
        message,
    });
}
fn required(
    issues: &mut Vec<Issue>,
    scope: &'static str,
    row: Option<usize>,
    value: &Value,
    fields: &[&str],
) {
    for field in fields {
        if text(value, field).is_empty() {
            issue(
                issues,
                scope,
                row,
                field,
                format!("{}不能为空。", label(scope, field)),
            );
        }
    }
}
fn rules(scope: &'static str, row: Option<usize>, value: &Value, issues: &mut Vec<Issue>) {
    for rule in reference()["rules"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|r| r["scope"] == scope)
    {
        let field = text(rule, "key");
        let raw = text(value, field);
        let method = text(rule, "method");
        if raw.is_empty() {
            if method == "RequireValue" {
                required(issues, scope, row, value, &[field]);
            }
            continue;
        }
        let limit = rule["numbers"][0].as_u64().unwrap_or(0) as usize;
        let failure = match method {
            "ValidateMaxLength" if raw.encode_utf16().count() > limit => {
                Some(format!("长度不能超过 {limit}"))
            }
            "ValidateDigits" if raw.len() != limit || !raw.bytes().all(|b| b.is_ascii_digit()) => {
                Some(format!("必须是 {limit} 位数字"))
            }
            "ValidateAllowedValues"
                if !rule["allowed"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .any(|v| v == raw) =>
            {
                Some("不在允许的选项中".into())
            }
            "ValidateExactDate" => {
                let date = if text(rule, "format") == "yyyyMMdd"
                    && raw.len() == 8
                    && raw.bytes().all(|b| b.is_ascii_digit())
                {
                    format!("{}-{}-{}", &raw[..4], &raw[4..6], &raw[6..])
                } else {
                    raw.into()
                };
                (!crate::invoice::valid_date(&date))
                    .then(|| format!("日期格式须为 {}", text(rule, "format")))
            }
            "ValidateDecimal" => {
                let core = raw.trim_start_matches(['+', '-']);
                let parts: Vec<_> = core.split('.').collect();
                let scale = rule["numbers"][1].as_u64().unwrap_or(0) as usize;
                if !raw
                    .bytes()
                    .all(|b| b.is_ascii_digit() || matches!(b, b'+' | b'-' | b'.'))
                    || raw.parse::<Decimal>().is_err()
                {
                    Some("不是有效数值".into())
                } else if parts[0].trim_start_matches('0').len() > limit
                    || parts.get(1).is_some_and(|p| p.len() > scale)
                {
                    Some(format!("整数最多 {limit} 位，小数最多 {scale} 位"))
                } else {
                    None
                }
            }
            _ => None,
        };
        if let Some(message) = failure {
            issue(
                issues,
                scope,
                row,
                field,
                format!("{}：{message}。", text(rule, "label")),
            );
        }
    }
}
fn percent(issues: &mut Vec<Issue>, row: usize, value: &Value, field: &str, maximum: i64) {
    let raw = text(value, field);
    if raw.is_empty() {
        return;
    }
    let raw = raw.strip_suffix('%').unwrap_or(raw);
    if raw
        .parse::<Decimal>()
        .ok()
        .is_none_or(|n| n < Decimal::ZERO || n > Decimal::from(maximum) || n.scale() > 2)
    {
        issue(
            issues,
            "goods",
            Some(row),
            field,
            format!(
                "{}须为 0–{maximum}% 且小数最多 2 位。",
                label("goods", field)
            ),
        );
    }
}
pub fn review(business: Business, document: &Value) -> Vec<Issue> {
    let mut issues = vec![];
    rules(business.scope(), None, document, &mut issues);
    if business == Business::Acd {
        return issues;
    }
    let cert = text(document, "certType").to_uppercase();
    if !reference()["options"]["certTypeOptions"]
        .as_array()
        .into_iter()
        .flatten()
        .any(|o| o["value"] == cert)
    {
        issue(
            &mut issues,
            "coo",
            None,
            "certType",
            "证书类型无效。".into(),
        );
    }
    if cert == "RC" {
        required(
            &mut issues,
            "coo",
            None,
            document,
            &["invDate", "invNo", "curr", "priceTerms"],
        );
    }
    if ["1", "2", "3"].contains(&text(document, "certStatus")) {
        required(
            &mut issues,
            "coo",
            None,
            document,
            &["oldCertNo", "modReason"],
        );
    }
    if cert == "H" && text(document, "applyType") == "1" {
        required(
            &mut issues,
            "coo",
            None,
            document,
            &["exporterTel", "exporterFax", "exporterEmail"],
        );
    }
    if cert == "EC" && text(document, "thirdPartyInvFlag") == "1" {
        required(&mut issues, "coo", None, document, &["producer"]);
    }
    if text(document, "thirdPartyInvFlag") == "1" {
        let rows = document["nonpartyCorps"].as_array();
        if rows.is_none_or(|rows| rows.is_empty()) {
            issue(
                &mut issues,
                "coo",
                None,
                "thirdPartyInvFlag",
                "第三方发票须填写非缔约方公司信息。".into(),
            );
        }
        for (i, row) in rows.into_iter().flatten().enumerate() {
            required(
                &mut issues,
                "corp",
                Some(i),
                row,
                &["entName", "entCountryCode", "entCountryName"],
            );
            for (field, max) in [
                ("entName", 500),
                ("entAddr", 1000),
                ("entCountryCode", 3),
                ("entCountryName", 100),
            ] {
                if text(row, field).encode_utf16().count() > max {
                    issue(
                        &mut issues,
                        "corp",
                        Some(i),
                        field,
                        format!("非缔约方信息 {field} 长度超过 {max}。"),
                    );
                }
            }
        }
    }
    for (index, item) in document["items"]
        .as_array()
        .into_iter()
        .flatten()
        .enumerate()
    {
        rules("goods", Some(index), item, &mut issues);
        required(
            &mut issues,
            "goods",
            Some(index),
            item,
            &[
                "goodsItemFlag",
                "goodsNameE",
                "packQty",
                "packUnit",
                "packType",
                "goodsDesc",
            ],
        );
        if text(item, "goodsItemFlag") == "Y" {
            continue;
        }
        required(
            &mut issues,
            "goods",
            Some(index),
            item,
            &[
                "hsCode",
                "goodsName",
                "goodsQty",
                "goodsUnitE",
                "goodsUnit",
                "invValue",
                "fobValue",
                "ciqRegNo",
                "prdcEtpsName",
                "prdcEtpsConcEr",
                "prdcEtpsTel",
            ],
        );
        let origin = text(item, "oriCriteria");
        let sub = text(item, "oriCriteriaSub");
        if !origin.is_empty()
            && !catalog::origin_options(&cert)
                .as_array()
                .into_iter()
                .flatten()
                .any(|o| o["value"] == origin)
        {
            issue(
                &mut issues,
                "goods",
                Some(index),
                "oriCriteria",
                "原产标准与证书类型不匹配。".into(),
            );
        }
        if !sub.is_empty() && (cert != "E" || origin != "PSR" || !["1", "2", "3"].contains(&sub)) {
            issue(
                &mut issues,
                "goods",
                Some(index),
                "oriCriteriaSub",
                "原产标准子项与证书类型／主标准不匹配。".into(),
            );
        }
        if !["C", "AD", "CA", "NI", "SE", "HD"].contains(&cert.as_str()) {
            required(&mut issues, "goods", Some(index), item, &["oriCriteria"]);
        }
        if cert == "E" && origin == "PSR" {
            required(&mut issues, "goods", Some(index), item, &["oriCriteriaSub"]);
        }
        if cert == "G" && ["W", "Y"].contains(&origin) {
            required(&mut issues, "goods", Some(index), item, &["oriCriteriaRef"]);
        }
        if cert == "G" && origin == "Y" {
            percent(&mut issues, index, item, "oriCriteriaRef", 50);
        }
        if cert == "E" {
            percent(&mut issues, index, item, "oriCriteriaRef", 100);
        }
        if cert == "RC" {
            required(
                &mut issues,
                "goods",
                Some(index),
                item,
                &["goodsOriginCountry", "goodsOriginCountryEn", "invNo"],
            );
            if origin == "RVC" {
                required(&mut issues, "goods", Some(index), item, &["iCompPrpr"]);
            }
            percent(&mut issues, index, item, "iCompPrpr", 100);
        }
    }
    issues
}
