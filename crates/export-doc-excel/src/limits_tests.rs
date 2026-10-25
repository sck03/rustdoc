use super::*;
use calamine::Reader;
use export_doc_contracts::contracts;
use export_doc_domain::invoice::InvoiceDraft;
use serde_json::json;
use std::cell::Cell;

fn check() -> Result<()> {
    Ok(())
}
fn settings() -> serde_json::Value {
    contracts::contract()["configuration"]["defaults"]["excelImport"].clone()
}
fn small_workbook() -> Vec<u8> {
    table(
        &[
            ("name", "英文品名"),
            ("style", "款号"),
            ("quantity", "数量"),
        ],
        &[json!({"name":"SHIRT", "style":"A-1", "quantity":3})],
        &check,
    )
    .unwrap()
}
fn append_sheet_xml(bytes: &[u8], xml: &str) -> Vec<u8> {
    let mut package = archive::Package::open(bytes, &check).unwrap();
    let path = "xl/worksheets/sheet1.xml";
    let source = String::from_utf8(package.get(path).unwrap().to_vec()).unwrap();
    package.0.insert(
        path.into(),
        source
            .replace("</sheetData>", &format!("{xml}</sheetData>"))
            .into_bytes(),
    );
    package.finish(&check).unwrap()
}

#[test]
fn sparse_coordinates_are_rejected_before_dense_allocation_in_all_import_paths() {
    let bytes = append_sheet_xml(
        &small_workbook(),
        "<row r=\"1048576\"><c r=\"XFD1048576\" t=\"inlineStr\"><is><t>far</t></is></c></row>",
    );
    assert!(bytes.len() < 16 * 1024);
    for result in [
        preview(&bytes, "sparse.xlsx", &settings(), "2026-10-25", &check).map(|_| ()),
        read_table_data(&bytes, "sparse.xlsx", 2000, &check).map(|_| ()),
        read_sheet_data(&bytes, "sparse.xlsx", "", 2000, &check).map(|_| ()),
    ] {
        assert!(result.unwrap_err().contains("allocation limit exceeded"));
    }
}

#[test]
fn forged_shared_string_count_and_formula_index_cannot_allocate_from_metadata() {
    let mut package = archive::Package::open(&small_workbook(), &check).unwrap();
    package.0.insert("xl/sharedStrings.xml".into(),
        b"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" uniqueCount=\"4294967295\"><si><t>x</t></si></sst>".to_vec());
    let bytes = package.finish(&check).unwrap();
    assert!(
        reader::open(&bytes, &check)
            .err()
            .unwrap()
            .contains("allocation limit exceeded")
    );
    let bytes = append_sheet_xml(
        &small_workbook(),
        "<row r=\"3\"><c r=\"A3\"><f t=\"shared\" si=\"4294967295\" ref=\"A3:A4\">1+1</f><v>2</v></c></row>",
    );
    let mut book = reader::open(&bytes, &check).unwrap();
    let name = book.sheet_names()[0].clone();
    assert!(
        book.worksheet_formula(&name)
            .unwrap_err()
            .to_string()
            .contains("allocation limit exceeded")
    );
}

#[test]
fn archive_validation_checks_cancellation_while_streaming_a_single_entry() {
    let bytes = archive::Package(std::collections::BTreeMap::from([(
        "payload.bin".into(),
        vec![7; 1024 * 1024],
    )]))
    .finish(&check)
    .unwrap();
    let calls = Cell::new(0);
    let cancel = || {
        calls.set(calls.get() + 1);
        if calls.get() == 5 {
            Err("已取消".into())
        } else {
            Ok(())
        }
    };
    assert_eq!(archive::validate(&bytes, &cancel).unwrap_err(), "已取消");
    assert_eq!(calls.get(), 5);
    archive::validate(&bytes, &check).unwrap();
}

#[test]
fn binary_record_length_is_checked_before_resizing_its_buffer() {
    let mut package = archive::Package::open(&small_workbook(), &check).unwrap();
    let relationships = package.get("xl/_rels/workbook.xml.rels").unwrap().to_vec();
    package
        .0
        .insert("xl/_rels/workbook.bin.rels".into(), relationships);
    // BrtWbProp followed by the maximum four-byte record length, with no body.
    package.0.insert(
        "xl/workbook.bin".into(),
        vec![0x99, 0x01, 0xff, 0xff, 0xff, 0x7f],
    );
    let bytes = package.finish(&check).unwrap();
    let error = calamine::Xlsb::new(std::io::Cursor::new(&bytes))
        .err()
        .unwrap();
    assert!(
        error.to_string().contains("allocation limit exceeded"),
        "{error}"
    );
}

#[test]
fn workbook_paths_keep_case_insensitive_lookup_but_reject_ambiguous_entries() {
    let original = archive::Package::open(&small_workbook(), &check).unwrap();
    let mixed = archive::Package(
        original
            .0
            .into_iter()
            .map(|(name, bytes)| (name.to_ascii_uppercase(), bytes))
            .collect(),
    );
    let bytes = mixed.finish(&check).unwrap();
    let result = preview(&bytes, "mixed-case.xlsx", &settings(), "2026-10-25", &check).unwrap();
    assert!(result.success, "{:?}", result.errors);
    let mut package = archive::Package::open(&small_workbook(), &check).unwrap();
    let duplicate = package.get("xl/workbook.xml").unwrap().to_vec();
    package.0.insert("XL/WORKBOOK.XML".into(), duplicate);
    let bytes = package.finish(&check).unwrap();
    assert!(reader::open(&bytes, &check).err().unwrap().contains("重复"));
}

#[test]
fn configured_numeric_shipment_dates_use_the_workbook_epoch() {
    let template = include_bytes!("../../../Resources/ExcelTemplates/invoice-import-template.xlsx");
    let invoice = InvoiceDraft::demo("2026-10-25", "NUMERIC-SHIPMENT")
        .build()
        .unwrap();
    let bytes = booking_from_invoice(template, &invoice, &settings(), &check).unwrap();
    for (epoch_1904, expected) in [(false, "2026-06-05"), (true, "2030-06-06")] {
        let mut package = archive::Package::open(&bytes, &check).unwrap();
        let path = "xl/worksheets/sheet1.xml";
        let mut sheet =
            sheet::Sheet::new(xml::Node::parse(package.get(path).unwrap()).unwrap()).unwrap();
        sheet.set(13, 15, &json!(46178)).unwrap();
        package.0.insert(path.into(), sheet.0.bytes().unwrap());
        if epoch_1904 {
            let path = "xl/workbook.xml";
            let source = String::from_utf8(package.get(path).unwrap().to_vec()).unwrap();
            package.0.insert(
                path.into(),
                source
                    .replace("<sheets>", "<workbookPr date1904=\"1\"/><sheets>")
                    .into_bytes(),
            );
        }
        let bytes = package.finish(&check).unwrap();
        let result = preview(
            &bytes,
            "numeric-date.xlsx",
            &settings(),
            "2026-10-25",
            &check,
        )
        .unwrap();
        assert!(result.success, "{:?}", result.errors);
        assert_eq!(result.invoice.unwrap().shipment_date, expected);
    }
}
