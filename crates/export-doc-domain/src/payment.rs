//! Payment validation and exact expense calculations shared by every host.
use crate::{
    business_date::is_valid as valid_date,
    generated_api::{ApiPaymentDto, ApiPaymentGoodsItem},
};
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

/// One ordered set of goods belongs to the payment, independently of invoice items.
pub fn goods_items(value: &ApiPaymentDto) -> Vec<ApiPaymentGoodsItem> {
    value.goods_items.clone().unwrap_or_default()
}

pub fn normalize_goods(value: &mut ApiPaymentDto) {
    value.goods_items = Some(
        goods_items(value)
            .into_iter()
            .filter_map(|mut item| {
                item.goods_name = item.goods_name.trim().into();
                item.quantity = item.quantity.trim().into();
                item.quantity_unit = item.quantity_unit.trim().into();
                (!item.goods_name.is_empty()
                    || !item.quantity.is_empty()
                    || !item.quantity_unit.is_empty())
                .then_some(item)
            })
            .collect(),
    );
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
    let goods = value.goods_items.as_deref().unwrap_or(&[]);
    if goods.len() > 4 {
        return Err(error("goodsItems", "货物明细最多填写 4 项。"));
    }
    for (index, item) in goods.iter().enumerate() {
        for (label, limit, text) in [
            ("货物品名", 500, &item.goods_name),
            ("数量", 100, &item.quantity),
            ("数量单位", 20, &item.quantity_unit),
        ] {
            if text.trim().encode_utf16().count() > limit {
                return Err(error(
                    "goodsItems",
                    format!("第 {} 项{label}不能超过 {limit} 个字符。", index + 1),
                ));
            }
        }
    }
    for (field, label, limit, text) in [
        ("invoiceNo", "发票号", 100, &value.invoice_no),
        ("voucherNo", "付款单号", 100, &value.voucher_no),
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
        ("shipmentCountry", "出运国家", 100, &value.shipment_country),
        ("project", "项目", 200, &value.project),
        ("payeeName", "收款方", 200, &value.payee_name),
        ("payerName", "付款方", 200, &value.payer_name),
        ("bankName", "银行", 200, &value.bank_name),
        ("accountNo", "账号", 100, &value.account_no),
        ("notes", "备注", 2000, &value.notes),
    ] {
        // Match the browser's UTF-16 text length boundary.
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
    fn independent_goods_preserve_order_and_decimal_text() {
        let mut value = ApiPaymentDto {
            goods_items: Some(vec![ApiPaymentGoodsItem {
                goods_name: "付款货物".into(),
                quantity: "12.5".into(),
                quantity_unit: "件".into(),
                ..Default::default()
            }]),
            ..Default::default()
        };
        normalize_goods(&mut value);
        assert_eq!(goods_items(&value).len(), 1);
        assert_eq!(goods_items(&value)[0].quantity, "12.5");
        value
            .goods_items
            .as_mut()
            .unwrap()
            .extend((2..=4).map(|i| ApiPaymentGoodsItem {
                goods_name: format!("货物{i}"),
                quantity: "9007199254740993.125".into(),
                quantity_unit: "箱".into(),
                ..Default::default()
            }));
        validate(&value).unwrap();
        let saved = serde_json::to_string(&value).unwrap();
        let read: ApiPaymentDto = serde_json::from_str(&saved).unwrap();
        assert_eq!(goods_items(&read), goods_items(&value));
        value
            .goods_items
            .as_mut()
            .unwrap()
            .push(ApiPaymentGoodsItem::default());
        assert_eq!(validate(&value).unwrap_err().field, "goodsItems");
        value.goods_items = Some(vec![ApiPaymentGoodsItem {
            goods_name: " ".into(),
            ..Default::default()
        }]);
        normalize_goods(&mut value);
        assert!(
            goods_items(&value).is_empty(),
            "removed goods cannot reappear from single fields"
        );
    }

    #[test]
    fn goods_limits_apply_to_each_item() {
        let mut value = ApiPaymentDto {
            goods_items: Some(vec![ApiPaymentGoodsItem {
                quantity_unit: "箱".repeat(21),
                ..Default::default()
            }]),
            ..Default::default()
        };
        assert_eq!(validate(&value).unwrap_err().field, "goodsItems");
        value.goods_items.as_mut().unwrap()[0].quantity_unit.clear();
        value.goods_items.as_mut().unwrap()[0].goods_name = "😀".repeat(251);
        assert!(validate(&value).is_err());
    }

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
