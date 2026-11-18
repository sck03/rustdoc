//! Readiness to verify an invoice is stricter than saving an unfinished draft.
use crate::generated_api::{ApiInvoiceDetailDto, InvoiceReviewIssue, InvoiceReviewResult};
use serde_json::json;

fn issue(field: &str, row: Option<usize>, label: &str) -> InvoiceReviewIssue {
    let path = row.map_or_else(
        || field.to_owned(),
        |index| format!("items[{index}].{field}"),
    );
    InvoiceReviewIssue {
        field: field.into(),
        message: row.map_or_else(
            || format!("请填写{label}"),
            |index| format!("第 {} 行缺少{label}", index + 1),
        ),
        row_number: row.map(|index| index as i64 + 1),
        extra: [
            ("code".into(), json!("required")),
            ("fieldPath".into(), json!(path)),
            ("severity".into(), json!("Error")),
        ]
        .into(),
    }
}

pub fn review(invoice: &ApiInvoiceDetailDto) -> InvoiceReviewResult {
    let mut issues = Vec::new();
    for (field, label, value) in [
        ("invoiceNo", "发票号", &invoice.invoice_no),
        ("invoiceDate", "发票日期", &invoice.invoice_date),
        ("exporterNameEN", "出口商", &invoice.exporter_name_en),
        ("customerNameEN", "客户", &invoice.customer_name_en),
        ("currency", "币种", &invoice.currency),
        ("destinationCountry", "目的国", &invoice.destination_country),
        ("portOfLoading", "装货港", &invoice.port_of_loading),
        ("portOfDestination", "目的港", &invoice.port_of_destination),
    ] {
        if value.trim().is_empty() {
            issues.push(issue(field, None, label));
        }
    }
    if invoice.items.is_empty() {
        let mut missing = issue("items", None, "商品明细");
        missing.message = "请录入商品明细".into();
        missing.extra.insert("code".into(), json!("items"));
        issues.push(missing);
    }
    for (index, item) in invoice.items.iter().enumerate() {
        for (field, label, value) in [
            ("styleNo", "款号", &item.style_no),
            ("styleName", "英文品名", &item.style_name),
            ("hsCode", "HS 编码", &item.hs_code),
        ] {
            if value.trim().is_empty() {
                issues.push(issue(field, Some(index), label));
            }
        }
    }
    InvoiceReviewResult {
        ready: issues.is_empty(),
        issues,
        ..Default::default()
    }
}
