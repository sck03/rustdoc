//! Labels and validation navigation for the dedicated payment editor.
use crate::{FormSection, form_sections, model};
use slint::Model;

pub fn section(field: &str) -> i32 {
    (0..3)
        .find(|tab| {
            form_sections::payment(*tab)
                .iter()
                .any(|group| group.fields.contains(&field))
        })
        .unwrap_or(0)
}

pub fn localize(sections: &mut [FormSection]) {
    for section in sections {
        section.fields = model(
            section
                .fields
                .iter()
                .map(|mut field| {
                    let label = match field.key.as_str() {
                        "invoiceNo" => "发票号／业务参考号",
                        "voucherNo" => "付款单号",
                        "receiptDate" => "收汇日期",
                        "payeeId" => "支付对象资料",
                        "payeeName" => "收款方",
                        "payerName" => "付款方",
                        "quantityUnit" => "数量单位",
                        "shipmentCountry" => "出运国家",
                        "shipmentDate" => "出运日期",
                        "goodsName" => "品名",
                        "department" => "部门",
                        "project" => "项目",
                        "usdAmount" => "USD 金额",
                        "cnyAmount" => "CNY 金额",
                        "notes" => "备注",
                        key => export_doc_domain::payment::EXPENSE_FIELDS
                            .iter()
                            .find(|(name, _)| *name == key)
                            .map_or("", |(_, label)| *label),
                    };
                    if !label.is_empty() {
                        field.label = label.into();
                    }
                    field
                })
                .collect(),
        );
    }
}
