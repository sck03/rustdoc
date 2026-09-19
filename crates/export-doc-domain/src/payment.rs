//! Payment validation and exact expense calculations shared by every host.
use crate::{generated_api::ApiPaymentDto, invoice::valid_date};
use rust_decimal::Decimal;

pub const EXPENSE_FIELDS: &[(&str, &str)] = &[
    ("travelExpense", "差旅费"),
    ("businessEntertainmentExpense", "业务招待费"),
    ("telephoneExpense", "电话费"),
    ("officeExpense", "办公费"),
    ("repairExpense", "维修费"),
    ("freightMiscExpense", "运杂费"),
    ("inspectionExpense", "商检费"),
    ("otherExpense", "其他费用"),
];

#[derive(Debug, PartialEq)]
pub struct FieldError {
    pub field: &'static str,
    pub message: String,
}
impl std::fmt::Display for FieldError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}
impl std::error::Error for FieldError {}
fn error(field: &'static str, message: impl Into<String>) -> FieldError {
    FieldError {
        field,
        message: message.into(),
    }
}

pub fn new(date: &str) -> Result<ApiPaymentDto, FieldError> {
    let payment = ApiPaymentDto {
        payment_date: Some(date.into()),
        shipment_date: Some(date.into()),
        receipt_date: Some(date.into()),
        ..Default::default()
    };
    validate(&payment)?;
    Ok(payment)
}

fn expenses(value: &ApiPaymentDto) -> [Decimal; 8] {
    [
        value.travel_expense,
        value.business_entertainment_expense,
        value.telephone_expense,
        value.office_expense,
        value.repair_expense,
        value.freight_misc_expense,
        value.inspection_expense,
        value.other_expense,
    ]
}

pub fn expense_total(value: &ApiPaymentDto) -> Result<Decimal, FieldError> {
    expenses(value)
        .into_iter()
        .try_fold(Decimal::ZERO, |total, amount| {
            total
                .checked_add(amount)
                .ok_or_else(|| error("cnyAmount", "费用合计超出金额范围。"))
        })
}

pub fn validate(value: &ApiPaymentDto) -> Result<(), FieldError> {
    for (field, label, date) in [
        ("paymentDate", "付款日期", &value.payment_date),
        ("shipmentDate", "出运日期", &value.shipment_date),
        ("receiptDate", "收汇日期", &value.receipt_date),
    ] {
        if let Some(date) = date {
            if !valid_date(date) || !("1900-01-01"..="2100-12-31").contains(&date.as_str()) {
                return Err(error(
                    field,
                    format!("{label}必须是 1900 至 2100 年之间的有效日期。"),
                ));
            }
        }
    }
    if value.payee_id < 0 {
        return Err(error("payeeId", "支付对象资料编号不能小于 0。"));
    }
    for (field, label, limit, text) in [
        ("invoiceNo", "发票号", 100, &value.invoice_no),
        ("voucherNo", "付款单号", 100, &value.voucher_no),
        ("quantityUnit", "数量单位", 20, &value.quantity_unit),
        ("tradeMethod", "贸易方式", 100, &value.trade_method),
        ("taxRebateRate", "退税率", 100, &value.tax_rebate_rate),
        ("spare1", "备用字段1", 500, &value.spare1),
        ("spare2", "备用字段2", 500, &value.spare2),
        ("spare3", "备用字段3", 500, &value.spare3),
        ("spare4", "备用字段4", 500, &value.spare4),
        ("spare5", "备用字段5", 500, &value.spare5),
        ("spare6", "备用字段6", 500, &value.spare6),
        ("spare7", "备用字段7", 500, &value.spare7),
        ("spare8", "备用字段8", 500, &value.spare8),
        ("spare9", "备用字段9", 500, &value.spare9),
        ("spare10", "备用字段10", 500, &value.spare10),
        ("department", "部门", 100, &value.department),
        ("paymentMethod", "付款方式", 100, &value.payment_method),
        ("quantity", "数量", 100, &value.quantity),
        ("shipmentCountry", "出运国家", 100, &value.shipment_country),
        ("project", "项目", 200, &value.project),
        ("payeeName", "收款方", 200, &value.payee_name),
        ("payerName", "付款方", 200, &value.payer_name),
        ("bankName", "银行", 200, &value.bank_name),
        ("accountNo", "账号", 100, &value.account_no),
        ("goodsName", "品名", 500, &value.goods_name),
        ("notes", "备注", 2000, &value.notes),
    ] {
        // Retain the public .NET contract's UTF-16 length boundary.
        if text.trim().encode_utf16().count() > limit {
            return Err(error(field, format!("{label}不能超过 {limit} 个字符。")));
        }
    }
    let fields = [
        ("usdAmount", "USD 金额", value.usd_amount),
        ("cnyAmount", "CNY 金额", value.cny_amount),
    ];
    for (field, label, amount) in fields.into_iter().chain(
        EXPENSE_FIELDS
            .iter()
            .zip(expenses(value))
            .map(|((field, label), amount)| (*field, *label, amount)),
    ) {
        if amount < Decimal::ZERO {
            return Err(error(field, format!("{label}不能小于 0。")));
        }
    }
    expense_total(value)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn optional_business_fields_and_precise_expenses_match_payment_rules() {
        let mut value = ApiPaymentDto::default();
        validate(&value).unwrap();
        value.travel_expense = "0.1".parse().unwrap();
        value.other_expense = "0.2".parse().unwrap();
        assert_eq!(expense_total(&value).unwrap().to_string(), "0.3");
        assert_eq!(value.cny_amount, Decimal::ZERO);
        value.travel_expense = Decimal::NEGATIVE_ONE;
        assert_eq!(validate(&value).unwrap_err().field, "travelExpense");
    }

    #[test]
    fn dates_and_text_have_the_same_limits_in_native_and_http_use_cases() {
        for date in ["2026-02-29", "1899-12-31", "2101-01-01", "2026-9-16", ""] {
            assert!(new(date).is_err(), "{date}");
        }
        let mut value = new("2028-02-29").unwrap();
        value.notes = "备".repeat(2001);
        assert_eq!(validate(&value).unwrap_err().field, "notes");
        value.notes.clear();
        value.payee_id = -1;
        assert_eq!(validate(&value).unwrap_err().field, "payeeId");
        value.payee_id = 0;
        value.travel_expense = Decimal::MAX;
        value.other_expense = Decimal::ONE;
        assert!(expense_total(&value).is_err());
    }
}
