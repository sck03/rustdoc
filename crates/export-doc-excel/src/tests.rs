use super::*;
use calamine::{Reader, open_workbook_auto_from_rs};
use export_doc_contracts::contracts;
use export_doc_domain::invoice::InvoiceDraft;
use serde_json::json;
use std::{collections::BTreeMap, io::Cursor};

const TEMPLATE: &[u8] =
    include_bytes!("../../../Resources/ExcelTemplates/invoice-import-template.xlsx");
fn check() -> Result<()> {
    Ok(())
}
fn settings() -> serde_json::Value {
    contracts::contract()["configuration"]["defaults"]["excelImport"].clone()
}

#[test]
fn booking_uses_the_existing_layout_and_round_trips_exact_invoice_amounts() {
    let draft = InvoiceDraft::demo("2026-09-16", "XL-NATIVE-001");
    let invoice = draft.build().unwrap();
    let bytes = booking_from_invoice(TEMPLATE, &invoice, &settings(), &check).unwrap();
    let original = archive::Package::open(TEMPLATE, &check).unwrap();
    let exported = archive::Package::open(&bytes, &check).unwrap();
    assert_eq!(
        original.get("xl/styles.xml").unwrap(),
        exported.get("xl/styles.xml").unwrap()
    );
    let sheet = xml::Node::parse(exported.get("xl/worksheets/sheet1.xml").unwrap()).unwrap();
    for index in [8, 9, 11, 12, 14] {
        assert!(
            sheet
                .child("cols")
                .unwrap()
                .nodes()
                .any(|col| col.attr("min") == Some(index.to_string().as_str())
                    && col.attr("hidden") == Some("1"))
        );
    }
    let preview = preview(&bytes, "托单.xlsx", &settings(), "2026-09-16", &check).unwrap();
    assert!(preview.success, "{preview:#?}");
    let imported = preview.invoice.as_ref().unwrap();
    assert_eq!(imported.invoice_no, invoice.invoice_no);
    assert_eq!(imported.items.len(), invoice.items.len());
    assert_eq!(imported.total_amount, invoice.total_amount);
    assert_eq!(imported.items[0].hs_code, invoice.items[0].hs_code);
    let response = serde_json::to_value(preview).unwrap();
    export_doc_contracts::validation::response("PreviewUploadedExcelImport", &response).unwrap();
}

#[test]
fn reordered_columns_and_line_amount_driven_prices_are_preserved() {
    let data=table(&[("amount","金额"),("name","英文品名"),("quantity","数量"),("style","款号"),("price","单价"),("hs","HS 编码")],&[json!({"amount":5,"name":"COTTON & SILK SHIRT","quantity":3,"style":"00123","price":1.66,"hs":"6109100000"})],&check).unwrap();
    let result = preview(&data, "导入.xlsx", &settings(), "2026-09-16", &check).unwrap();
    assert!(result.success, "{result:#?}");
    let invoice = result.invoice.unwrap();
    assert_eq!(invoice.items[0].style_no, "00123");
    assert_eq!(invoice.items[0].style_name, "COTTON & SILK SHIRT");
    assert_eq!(invoice.items[0].total_price.to_string(), "5");
    assert_eq!(invoice.items[0].price_calculation_mode, "LineAmountDriven");
    assert_eq!(invoice.items[0].unit_price.to_string(), "1.66667");
}

#[test]
fn extending_the_detail_area_preserves_subtotals_and_footer() {
    let mut invoice = InvoiceDraft::demo("2026-09-16", "XL-LONG").build().unwrap();
    let item = invoice.items[0].clone();
    invoice.items = (0..70)
        .map(|index| {
            let mut item = item.clone();
            item.style_no = format!("LONG-{index:03}");
            item
        })
        .collect();
    let bytes = booking_from_invoice(TEMPLATE, &invoice, &settings(), &check).unwrap();
    let mut workbook = open_workbook_auto_from_rs(Cursor::new(&bytes)).unwrap();
    let range = workbook.worksheet_range_at(0).unwrap().unwrap();
    assert_eq!(range.get_value((89, 9)).unwrap().to_string(), "70000");
    let imported = preview(&bytes, "长托单.xlsx", &settings(), "2026-09-16", &check).unwrap();
    assert!(imported.success, "{:?}", imported.errors);
    assert_eq!(imported.invoice.unwrap().items.len(), 70);
}

#[test]
fn spreadsheet_text_cannot_become_a_formula_and_unsafe_archives_are_rejected() {
    let payload = "=HYPERLINK(\"https://example.invalid\",\"text\")";
    let bytes = table(&[("value", "文本")], &[json!({"value":payload})], &check).unwrap();
    let mut workbook = open_workbook_auto_from_rs(Cursor::new(bytes)).unwrap();
    let range = workbook.worksheet_range_at(0).unwrap().unwrap();
    assert_eq!(range.get_value((1, 0)).unwrap().to_string(), payload);
    assert!(workbook.worksheet_formula("导出数据").unwrap().is_empty());
    let package = archive::Package(BTreeMap::from([("../escape.xml".into(), b"bad".to_vec())]))
        .finish(&check)
        .unwrap();
    assert!(archive::Package::open(&package, &check).is_err());
    assert!(
        xml::Node::parse(b"<!DOCTYPE x [<!ENTITY data SYSTEM 'file:///secret'>]><x>&data;</x>")
            .is_err()
    );
}

#[test]
fn audit_sized_workbooks_reopen_with_all_rows_and_support_cancellation() {
    let rows: Vec<_> = (0..50_000)
        .map(|index| json!({"id":index,"value":format!("审计 & 第 {index} 条")}))
        .collect();
    let bytes = table(&[("id", "编号"), ("value", "内容")], &rows, &check).unwrap();
    let mut workbook = open_workbook_auto_from_rs(Cursor::new(bytes)).unwrap();
    let range = workbook.worksheet_range_at(0).unwrap().unwrap();
    assert_eq!(range.height(), 50_001);
    assert_eq!(
        range.get_value((50_000, 1)).unwrap().to_string(),
        "审计 & 第 49999 条"
    );
    assert!(table(&[("id", "编号")], &rows, &|| Err("已取消".into())).is_err());
}
