//! Optional invoice settlement lines. Product value, quantities and profit retain their existing meaning.
use crate::generated_api::ApiInvoiceAmountAdjustment;
use rust_decimal::Decimal;
use std::str::FromStr;
use unicode_normalization::UnicodeNormalization;

pub fn normalize(
    rows: &[ApiInvoiceAmountAdjustment],
) -> Result<Vec<ApiInvoiceAmountAdjustment>, String> {
    if rows.len() > 32 {
        return Err("金额调整最多 32 行。".into());
    }
    let mut result = Vec::new();
    for (index, row) in rows.iter().enumerate() {
        let description: String = row.description.trim().nfc().collect();
        let amount = row.amount.trim();
        if description.is_empty() && amount.is_empty() {
            continue;
        }
        let unsigned = amount.strip_prefix(['-', '+']).unwrap_or(amount);
        let mut parts = unsigned.split('.');
        let whole = parts.next().unwrap_or("");
        let fraction = parts.next();
        if amount.len() > 32
            || whole.is_empty()
            || !whole.bytes().all(|c| c.is_ascii_digit())
            || fraction.is_some_and(|s| {
                s.is_empty() || s.len() > 2 || !s.bytes().all(|c| c.is_ascii_digit())
            })
            || parts.next().is_some()
        {
            return Err(format!(
                "金额调整第 {} 行须填写正数或负数金额，最多两位小数。",
                index + 1
            ));
        }
        let amount = Decimal::from_str(amount)
            .map_err(|_| format!("金额调整第 {} 行超出金额范围。", index + 1))?;
        if amount.is_zero() {
            continue;
        }
        if description.is_empty()
            || description.chars().count() > 200
            || description
                .chars()
                .any(|c| c.is_control() && c != '\n' && c != '\r')
        {
            return Err(format!(
                "金额调整第 {} 行须填写 1–200 字符的说明。",
                index + 1
            ));
        }
        result.push(ApiInvoiceAmountAdjustment {
            description,
            amount: format!("{amount:.2}"),
            ..Default::default()
        });
    }
    Ok(result)
}

pub fn total(goods: Decimal, rows: &[ApiInvoiceAmountAdjustment]) -> Result<Decimal, String> {
    normalize(rows)?.iter().try_fold(goods, |total, row| {
        total
            .checked_add(Decimal::from_str(&row.amount).map_err(|_| "金额调整无效。")?)
            .ok_or_else(|| "调整后的应付金额超出范围。".into())
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn deposits_and_multiple_charges_are_exact_optional_and_bounded() {
        let rows: Vec<_> = [
            ("RECEIVED DEPOSIT", "-10963.50"),
            ("SAMPLE CHARGE", "125.10"),
            ("FREIGHT", "0.20"),
        ]
        .into_iter()
        .map(|(description, amount)| ApiInvoiceAmountAdjustment {
            description: description.into(),
            amount: amount.into(),
            ..Default::default()
        })
        .collect();
        assert_eq!(
            total(Decimal::from(20000), &rows).unwrap().to_string(),
            "9161.80"
        );
        assert_eq!(
            total(Decimal::from(20000), &[]).unwrap(),
            Decimal::from(20000)
        );
        for amount in ["1.001", "NaN", "1e3", "1,000", "--1"] {
            assert!(
                normalize(&[ApiInvoiceAmountAdjustment {
                    description: "FEE".into(),
                    amount: amount.into(),
                    ..Default::default()
                }])
                .is_err()
            );
        }
        assert!(normalize(&vec![rows[0].clone(); 33]).is_err());
        assert!(normalize(&[Default::default()]).unwrap().is_empty());
    }
}
