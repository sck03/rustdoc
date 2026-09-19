//! Sales stages and quotation rules shared by the native and HTTP applications.
use crate::{generated_api::ApiSalesOpportunitySaveRequest, invoice::valid_date};
use rust_decimal::Decimal;
use unicode_normalization::UnicodeNormalization;

pub const STAGES: &[&str] = &["线索", "需求确认", "已报价", "谈判中", "已成交", "已失单"];
pub const QUOTE_FIELDS: &[&str] = &[
    "quotationNo",
    "estimatedAmount",
    "currency",
    "probabilityPercent",
    "expectedCloseDate",
];

pub fn next_stages(stage: &str) -> &'static [&'static str] {
    match stage {
        "线索" => &["需求确认"],
        "需求确认" => &["线索", "已报价"],
        "已报价" => &["需求确认", "谈判中"],
        "谈判中" => &["已报价", "已成交", "已失单"],
        "已成交" | "已失单" => &["谈判中"],
        _ => &[],
    }
}
pub fn closed(stage: &str) -> bool {
    matches!(stage, "已成交" | "已失单")
}
pub fn clean(value: &str) -> String {
    value.trim().nfc().collect()
}

// Same deterministic ISO 4217 business catalog as the retained C# domain.
pub fn currency(value: &str) -> Result<String, String> {
    const CODES: &str = "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD CAD CDF CHE CHF CHW CLF CLP CNY COP COU CRC CUC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD USN UYI UYU UYW UZS VED VES VND VUV WST XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR XOF XPD XPF XPT XSU XTS XUA XXX YER ZAR ZMW ZWG";
    let value = clean(value).to_uppercase();
    if CODES.split_ascii_whitespace().any(|code| code == value) {
        Ok(value)
    } else {
        Err("币种必须使用有效的 ISO 4217 三位代码。".into())
    }
}

pub fn normalize(request: &mut ApiSalesOpportunitySaveRequest) -> Result<(), String> {
    for (field, label, limit, required) in [
        (&mut request.title, "商机名称", 200, true),
        (&mut request.quotation_no, "报价跟踪编号", 100, false),
        (&mut request.next_action, "下一步动作", 500, false),
        (&mut request.notes, "商机备注", 2000, false),
        (&mut request.change_note, "变更说明", 1000, false),
    ] {
        let value = clean(field.as_deref().unwrap_or(""));
        if required && value.is_empty() {
            return Err(format!("{label}不能为空。"));
        }
        if value.chars().count() > limit {
            return Err(format!("{label}不能超过 {limit} 个字符。"));
        }
        *field = Some(value);
    }
    if request.crm_customer_id <= 0 {
        return Err("请选择 CRM 客户。".into());
    }
    let amount = request.estimated_amount.unwrap_or_default();
    if amount < Decimal::ZERO {
        return Err("预计金额不能小于零。".into());
    }
    let probability = request.probability_percent.unwrap_or_default();
    if !(0..=100).contains(&probability) {
        return Err("成交概率必须在 0 至 100 之间。".into());
    }
    request.currency = Some(currency(request.currency.as_deref().unwrap_or("USD"))?);
    request.estimated_amount = Some(amount);
    request.probability_percent = Some(probability);
    request.product_id = request.product_id.filter(|id| *id > 0);
    if request
        .expected_close_date
        .as_ref()
        .is_some_and(|date| !valid_date(date))
    {
        return Err("预计成交日期必须是有效的 YYYY-MM-DD 日期。".into());
    }
    request.extra.clear();
    Ok(())
}

pub fn has_quote(request: &ApiSalesOpportunitySaveRequest) -> bool {
    request
        .quotation_no
        .as_deref()
        .is_some_and(|value| !value.is_empty())
        || request.estimated_amount.unwrap_or_default() != Decimal::ZERO
        || request.probability_percent.unwrap_or_default() != 0
        || request.expected_close_date.is_some()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn stages_dates_and_exact_quotation_values_follow_the_business_contract() {
        assert_eq!(next_stages("谈判中"), ["已报价", "已成交", "已失单"]);
        assert_eq!(next_stages("已失单"), ["谈判中"]);
        assert!(closed("已成交"));
        let mut request = ApiSalesOpportunitySaveRequest {
            crm_customer_id: 1,
            title: Some("  Cafe\u{301}订单 ".into()),
            estimated_amount: Some("123456789.1234".parse().unwrap()),
            currency: Some(" usd ".into()),
            expected_close_date: Some("2026-12-31".into()),
            ..Default::default()
        };
        normalize(&mut request).unwrap();
        assert_eq!(request.title.as_deref(), Some("Café订单"));
        assert_eq!(request.currency.as_deref(), Some("USD"));
        assert_eq!(
            request.estimated_amount.unwrap().to_string(),
            "123456789.1234"
        );
        request.expected_close_date = Some("2026-02-30".into());
        assert!(normalize(&mut request).is_err());
        assert!(currency("ZZZ").is_err());
    }
}
