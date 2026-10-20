//! Presentation rows belong to their item, so reorder, copy and delete keep the
//! business values attached. Only the Item row displays quantities and money.
use crate::generated_api::{ApiInvoiceItemDto, ApiInvoicePrintRow};

pub fn rows(item: &ApiInvoiceItemDto) -> Vec<ApiInvoicePrintRow> {
    item.print_rows
        .as_ref()
        .filter(|rows| !rows.is_empty())
        .cloned()
        .unwrap_or_else(|| {
            vec![ApiInvoicePrintRow {
                kind: "Item".into(),
                text: item.style_name.clone(),
                ..Default::default()
            }]
        })
}

pub fn validate<'a>(items: impl IntoIterator<Item = &'a ApiInvoiceItemDto>) -> Result<(), String> {
    let mut count = 0;
    for item in items {
        let Some(rows) = item.print_rows.as_ref().filter(|rows| !rows.is_empty()) else {
            count += 1;
            continue;
        };
        if rows.len() > 64 || rows.iter().filter(|row| row.kind == "Item").count() != 1 {
            return Err("每项打印明细最多 64 行，且必须保留一条数量金额行。".into());
        }
        for row in rows {
            if !["Item", "Text", "Blank"].contains(&row.kind.as_str())
                || row.text.chars().count() > 1000
                || (row.kind == "Blank" && !row.text.is_empty())
                || row
                    .text
                    .chars()
                    .any(|c| c.is_control() && !['\r', '\n', '\t'].contains(&c))
            {
                return Err("打印行类型或文字无效；空白行不能携带文字，每行最多 1000 字。".into());
            }
        }
        count += rows.len();
    }
    if count > 10_000 {
        return Err("单张发票最多 10000 条打印行。".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn print_layout_roundtrips_and_empty_business_items_are_not_silently_dropped() {
        let mut invoice = crate::invoice::InvoiceDraft::demo("2026-10-19", "PRINT-ROUNDTRIP")
            .build()
            .unwrap();
        invoice.use_entry_print_rows = Some(true);
        invoice.items[0].print_rows = Some(vec![
            ApiInvoicePrintRow {
                kind: "Item".into(),
                text: "".into(),
                ..Default::default()
            },
            ApiInvoicePrintRow {
                kind: "Blank".into(),
                ..Default::default()
            },
        ]);
        let encoded = serde_json::to_vec(&invoice).unwrap();
        let decoded = serde_json::from_slice(&encoded).unwrap();
        let rebuilt = crate::invoice::InvoiceDraft::from_dto(decoded)
            .build()
            .unwrap();
        assert_eq!(rebuilt, invoice);
        let mut draft = crate::invoice::InvoiceDraft::new("2026-10-19");
        draft.rows[0].original.print_rows = invoice.items[0].print_rows.clone();
        assert!(draft.preview().unwrap_err().contains("已有打印行"));
    }
    #[test]
    fn print_rows_keep_empty_descriptions_and_require_exactly_one_amount_row() {
        let mut item = ApiInvoiceItemDto {
            style_name: "Original".into(),
            ..Default::default()
        };
        assert_eq!(rows(&item)[0].text, "Original");
        item.print_rows = Some(vec![
            ApiInvoicePrintRow {
                kind: "Text".into(),
                text: "STYLE\nOrder No. 1".into(),
                ..Default::default()
            },
            ApiInvoicePrintRow {
                kind: "Item".into(),
                text: "".into(),
                ..Default::default()
            },
            ApiInvoicePrintRow {
                kind: "Blank".into(),
                text: "".into(),
                ..Default::default()
            },
        ]);
        validate([&item]).unwrap();
        assert_eq!(rows(&item)[1].text, "");
        item.print_rows.as_mut().unwrap().push(ApiInvoicePrintRow {
            kind: "Item".into(),
            text: "".into(),
            ..Default::default()
        });
        assert!(validate([&item]).is_err());
        item.print_rows.as_mut().unwrap().pop();
        item.print_rows.as_mut().unwrap()[2].text = "hidden".into();
        assert!(validate([&item]).is_err());
    }
}
