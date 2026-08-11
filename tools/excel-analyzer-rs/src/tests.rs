use super::*;
use std::{
    fs,
    io::{self, Write},
    path::Path,
    time::{SystemTime, UNIX_EPOCH},
};

#[test]
fn parse_excel_decimal_accepts_currency_grouping_and_units() {
    assert_eq!(parse_excel_decimal("USD 7,701.45"), Some(7701.45));
    assert_eq!(parse_excel_decimal("$109,592.88"), Some(109592.88));
    assert_eq!(parse_excel_decimal("5.4 kgs"), Some(5.4));
}

#[test]
fn parse_dimension_accepts_compact_and_separated_values() {
    assert_eq!(parse_dimension("302830").unwrap().length, 30.0);
    assert_eq!(parse_dimension("30*28*30").unwrap().width, 28.0);
    assert_eq!(parse_dimension("53 31 14").unwrap().height, 14.0);
}

#[test]
fn document_field_detection_stops_multiline_address_at_blank_row() {
    let cells = vec![
        vec![
            "发票抬头".to_string(),
            "NINGBO BRIDGE IMP. & EXP. CO., LTD.".to_string(),
        ],
        vec![
            "SHIPPER".to_string(),
            "N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA".to_string(),
        ],
        vec!["".to_string(), "".to_string()],
        vec!["收货人".to_string(), "ONIA LLC".to_string()],
        vec![
            "consignee".to_string(),
            "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA".to_string(),
        ],
    ];

    let fields = detect_document_fields(&cells, "报关和清关");
    let exporter_address = fields
        .iter()
        .find(|field| field.field_key == "ExporterAddressEN")
        .unwrap();
    let customer_address = fields
        .iter()
        .find(|field| field.field_key == "CustomerAddressEN")
        .unwrap();

    assert_eq!(
        exporter_address.value,
        "N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA"
    );
    assert_eq!(
        customer_address.value,
        "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA"
    );
}

#[test]
fn document_field_detection_keeps_company_name_containing_brand_separate_from_address() {
    let cells = vec![
        vec!["收货人".to_string(), "Reason Brand Inc".to_string()],
        vec![
            "consignee".to_string(),
            "3 WEST 35TH STREET 10th FL., New York, NY 10001".to_string(),
        ],
    ];

    let fields = detect_document_fields(&cells, "备货单");

    assert_document_field(&fields, "CustomerNameEN", "Reason Brand Inc");
    assert_document_field(
        &fields,
        "CustomerAddressEN",
        "3 WEST 35TH STREET 10th FL., New York, NY 10001",
    );
}

#[test]
fn address_detection_uses_tokens_instead_of_substrings_inside_company_names() {
    assert!(!looks_like_address_fragment("Reason Brand Inc"));
    assert!(looks_like_address_fragment(
        "3 WEST 35TH STREET 10th FL., New York, NY 10001"
    ));
}

#[test]
fn document_field_detection_does_not_use_next_label_as_empty_contract_value() {
    let cells = vec![
        vec!["合同号".to_string(), "信用证号".to_string()],
        vec!["发票号".to_string(), "2026YH018".to_string()],
    ];

    let fields = detect_document_fields(&cells, "报关和清关");

    assert!(fields.iter().all(|field| field.field_key != "ContractNo"));
    assert!(fields
        .iter()
        .all(|field| field.field_key != "LetterOfCreditNo"));
}

#[test]
fn document_field_detection_does_not_strip_company_name_starting_with_role_label() {
    let cells = vec![vec!["Buyer Ltd".to_string()]];

    let fields = detect_document_fields(&cells, "明细单");

    assert!(fields
        .iter()
        .all(|field| !(field.field_key == "CustomerNameEN" && field.value == "Ltd")));
}

#[test]
fn document_field_detection_handles_default_template_party_blocks() {
    let cells = vec![
        vec![
            "发货人 SHIPPER".to_string(),
            "NINGBO BRIDGE IMP. & EXP. CO., LTD.".to_string(),
        ],
        vec![
            "Address".to_string(),
            "N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA".to_string(),
        ],
        vec!["收货人 CONSIGNEEE".to_string(), "ONIA LLC.".to_string()],
        vec![
            "Address".to_string(),
            "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA".to_string(),
        ],
        vec!["通知人 NOTIFY PARTY".to_string(), "ONIA LLC.".to_string()],
        vec![
            "Address".to_string(),
            "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA".to_string(),
        ],
    ];

    let fields = detect_document_fields(&cells, "明细单");

    assert_document_field(&fields, "CustomerNameEN", "ONIA LLC.");
    assert_document_field(
        &fields,
        "CustomerAddressEN",
        "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA",
    );
    assert_document_field(&fields, "NotifyPartyName", "ONIA LLC.");
    assert_document_field(
        &fields,
        "NotifyPartyAddress",
        "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA",
    );
    assert_document_field(
        &fields,
        "ExporterAddressEN",
        "N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA",
    );
}

#[test]
fn document_field_detection_splits_single_cell_party_name_and_address() {
    let cells = vec![
        vec![
            "发票抬头".to_string(),
            "NINGBO BRIDGE IMP. & EXP. CO. LTD.    NO.668 BAIZHANG EAST ROAD.    NINGBO 315040 CHINA".to_string(),
        ],
        vec![
            "收货人   CONSIGNEE".to_string(),
            "GLOBAL FASHION RESOURCE INC\n3315 S.BROADWAY\nLOS ANGELES CA 90007, USA\nTEL:(213)973-5941".to_string(),
        ],
    ];

    let fields = detect_document_fields(&cells, "走货资料");

    assert_document_field(
        &fields,
        "ExporterNameEN",
        "NINGBO BRIDGE IMP. & EXP. CO. LTD.",
    );
    assert_document_field(
        &fields,
        "ExporterAddressEN",
        "NO.668 BAIZHANG EAST ROAD. NINGBO 315040 CHINA",
    );
    assert_document_field(&fields, "CustomerNameEN", "GLOBAL FASHION RESOURCE INC");
    assert_document_field(
        &fields,
        "CustomerAddressEN",
        "3315 S.BROADWAY\nLOS ANGELES CA 90007, USA\nTEL:(213)973-5941",
    );
}

#[test]
fn table_detection_prefers_lowest_header_for_conflicting_weight_labels() {
    let path = vec![
        "净重/箱".to_string(),
        "G.W./CTN".to_string(),
        "净重/箱".to_string(),
    ];

    let detected = detect_field_from_path(&path).unwrap();

    assert_eq!(detected.0, "NWPerCtn");
}

#[test]
fn table_detection_accepts_generic_industry_aliases() {
    let cases = [
        ("Part Number", "StyleNo"),
        ("Product Description", "StyleName"),
        ("Ordered Qty", "Quantity"),
        ("U/M", "UnitEN"),
        ("Boxes", "Cartons"),
        ("Package Dimensions", "Dimension"),
        ("Unit Value", "UnitPrice"),
        ("Line Value", "TotalPrice"),
        ("HTS Code", "HSCode"),
        ("Country of Manufacture", "Origin"),
        ("Gross KGS", "GWTotal"),
        ("Net KGS", "NWTotal"),
    ];

    for (header, expected) in cases {
        let detected = detect_field_from_path(&[header.to_string()]).unwrap();
        assert_eq!(detected.0, expected, "header {header}");
    }
}

#[test]
fn table_detection_uses_parent_header_context_for_bilingual_booking_sheet() {
    let cells = vec![
        vec![
            "唛头".to_string(),
            "客人订单号".to_string(),
            "客人款号".to_string(),
            "英文品名".to_string(),
            "面料".to_string(),
            "中文品名".to_string(),
            "数量".to_string(),
            "箱数".to_string(),
            "箱子尺寸".to_string(),
            "体积".to_string(),
            "毛重/箱".to_string(),
            "毛重".to_string(),
            "净重/箱".to_string(),
            "净重".to_string(),
            "单价".to_string(),
            "总价".to_string(),
        ],
        vec![
            "".to_string(),
            "".to_string(),
            "STYLE NO.".to_string(),
            "STYLE".to_string(),
            "".to_string(),
            "".to_string(),
            "QUANTITY".to_string(),
            "CARTON".to_string(),
            "CARTON SIZE".to_string(),
            "VOLUME".to_string(),
            "N.W./CTN".to_string(),
            "N.W.".to_string(),
            "G.W./CTN".to_string(),
            "G.W.".to_string(),
            "".to_string(),
            "".to_string(),
        ],
        vec![
            "STYLE# & DESCRIPTION".to_string(),
            "300000024".to_string(),
            "HAM01".to_string(),
            "EVERYDAY TEE".to_string(),
            "96% polyester 4% spandex".to_string(),
            "男式短袖圆领衫".to_string(),
            "162".to_string(),
            "2".to_string(),
            "60*38*24".to_string(),
            "0.10944".to_string(),
            "10".to_string(),
            "20".to_string(),
            "9".to_string(),
            "18".to_string(),
            "2.84".to_string(),
            "460.08".to_string(),
        ],
    ];

    let table = detect_table(&cells).expect("bilingual item table should be detected");

    assert_table_field(&table, "StyleNo", 3);
    assert_table_field(&table, "StyleName", 4);
    assert_table_field(&table, "StyleNameCN", 6);
    assert_table_field(&table, "Cartons", 8);
    assert_table_field(&table, "Dimension", 9);
    assert!(!table
        .fields
        .iter()
        .any(|field| field.canonical_field == "Brand"));
}

#[test]
fn analyze_workbook_reads_openxml_xlsx_with_calamine() {
    let path = write_openxml_xlsx_fixture();

    let report = analyze_workbook(path.clone()).expect("calamine should read generated xlsx");

    assert_eq!(report.analyzer_id, "rust-calamine");
    assert_eq!(report.selected_worksheet_name, "OpenXML导入");
    assert!(report
        .fields
        .iter()
        .any(|field| { field.field_key == "InvoiceNo" && field.value == "INV-XLSX-RS-001" }));
    assert!(report.fields.iter().any(|field| {
        field.field_key == "CustomerNameEN" && field.value == "RUST XLSX BUYER LTD."
    }));

    let sheet = report
        .sheets
        .iter()
        .find(|sheet| sheet.name == "OpenXML导入")
        .expect("selected sheet should exist");
    let table = sheet.table.as_ref().expect("item table should be detected");
    assert_eq!(table.header_start_row, 8);
    assert_eq!(table.data_start_row, 9);
    assert_table_field(table, "StyleNo", 1);
    assert_table_field(table, "StyleName", 2);
    assert_table_field(table, "Quantity", 3);
    assert_table_field(table, "Dimension", 5);
    assert_table_field(table, "Volume", 6);
    assert_table_field(table, "HSCode", 13);

    let _ = fs::remove_file(path);
}

fn assert_table_field(table: &TableAnalysis, canonical_field: &str, column: usize) {
    assert!(
        table
            .fields
            .iter()
            .any(|field| field.canonical_field == canonical_field && field.column == column),
        "expected {canonical_field} at column {column}"
    );
}

fn assert_document_field(fields: &[DocumentFieldCandidate], field_key: &str, value: &str) {
    assert!(
        fields
            .iter()
            .any(|field| field.field_key == field_key && field.value == value),
        "expected {field_key} to be {value}; actual fields: {}",
        fields
            .iter()
            .map(|field| format!("{}={}", field.field_key, field.value))
            .collect::<Vec<_>>()
            .join(" | ")
    );
}

fn write_openxml_xlsx_fixture() -> PathBuf {
    let mut directory = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    directory.push("target");
    directory.push("xlsx-rust-tests");
    fs::create_dir_all(&directory).expect("create rust xlsx test directory");

    let unique = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .expect("system time")
        .as_nanos();
    let path = directory.join(format!(
        "openxml-import-{}-{unique}.xlsx",
        std::process::id()
    ));

    let rows = vec![
        vec!["出口商", "NINGBO RUST XLSX EXPORT CO., LTD."],
        vec!["收货人", "RUST XLSX BUYER LTD."],
        vec!["发票号", "INV-XLSX-RS-001"],
        vec!["合同号", "CONTRACT-XLSX-RS-001"],
        vec!["起运港", "NINGBO"],
        vec!["目的港", "ROTTERDAM"],
        vec!["贸易条款", "FOB NINGBO", "付款方式", "T/T"],
        vec![
            "款号",
            "英文品名",
            "数量",
            "箱数",
            "箱子尺寸",
            "体积",
            "毛重/箱",
            "总毛重",
            "净重/箱",
            "总净重",
            "单价USD",
            "金额USD",
            "HS编码",
            "原产地",
        ],
        vec![
            "RS-XLSX-TEE-001",
            "RUST OPENXML T SHIRT",
            "120",
            "12",
            "50*40*30",
            "0.72",
            "8.5",
            "102",
            "7.5",
            "90",
            "3.2",
            "384",
            "6109100021",
            "宁波",
        ],
        vec![
            "RS-XLSX-POLO-002",
            "RUST OPENXML POLO",
            "80",
            "8",
            "60*40*25",
            "0.48",
            "9",
            "72",
            "8",
            "64",
            "4",
            "320",
            "6105100090",
            "宁波",
        ],
    ];

    write_minimal_xlsx(&path, "OpenXML导入", &rows).expect("write xlsx fixture");
    path
}

fn write_minimal_xlsx(path: &Path, sheet_name: &str, rows: &[Vec<&str>]) -> io::Result<()> {
    let workbook_xml = format!(
        r#"<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{}" sheetId="1" r:id="rId1"/></sheets></workbook>"#,
        escape_xml(sheet_name)
    );
    let worksheet_xml = build_worksheet_xml(rows);
    let files = vec![
        (
            "[Content_Types].xml",
            r#"<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>"#
                .as_bytes()
                .to_vec(),
        ),
        (
            "_rels/.rels",
            r#"<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>"#
                .as_bytes()
                .to_vec(),
        ),
        ("xl/workbook.xml", workbook_xml.into_bytes()),
        (
            "xl/_rels/workbook.xml.rels",
            r#"<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>"#
                .as_bytes()
                .to_vec(),
        ),
        ("xl/worksheets/sheet1.xml", worksheet_xml.into_bytes()),
    ];

    write_stored_zip(path, &files)
}

fn build_worksheet_xml(rows: &[Vec<&str>]) -> String {
    let mut xml = String::from(
        r#"<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>"#,
    );

    for (row_index, row) in rows.iter().enumerate() {
        let row_number = row_index + 1;
        xml.push_str(&format!(r#"<row r="{row_number}">"#));
        for (column_index, value) in row.iter().enumerate() {
            if value.is_empty() {
                continue;
            }

            let cell_reference = format!("{}{}", column_name(column_index + 1), row_number);
            xml.push_str(&format!(
                r#"<c r="{cell_reference}" t="inlineStr"><is><t>{}</t></is></c>"#,
                escape_xml(value)
            ));
        }

        xml.push_str("</row>");
    }

    xml.push_str("</sheetData></worksheet>");
    xml
}

fn column_name(mut one_based_column: usize) -> String {
    let mut chars = Vec::new();
    while one_based_column > 0 {
        one_based_column -= 1;
        chars.push((b'A' + (one_based_column % 26) as u8) as char);
        one_based_column /= 26;
    }

    chars.iter().rev().collect()
}

fn escape_xml(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

fn write_stored_zip(path: &Path, files: &[(&str, Vec<u8>)]) -> io::Result<()> {
    let mut output = Vec::new();
    let mut central_directory = Vec::new();

    for (name, content) in files {
        let local_header_offset = output.len() as u32;
        let name_bytes = name.as_bytes();
        let crc = crc32(content);

        write_u32(&mut output, 0x0403_4b50)?;
        write_u16(&mut output, 20)?;
        write_u16(&mut output, 0)?;
        write_u16(&mut output, 0)?;
        write_u16(&mut output, 0)?;
        write_u16(&mut output, 0)?;
        write_u32(&mut output, crc)?;
        write_u32(&mut output, content.len() as u32)?;
        write_u32(&mut output, content.len() as u32)?;
        write_u16(&mut output, name_bytes.len() as u16)?;
        write_u16(&mut output, 0)?;
        output.extend_from_slice(name_bytes);
        output.extend_from_slice(content);

        write_u32(&mut central_directory, 0x0201_4b50)?;
        write_u16(&mut central_directory, 20)?;
        write_u16(&mut central_directory, 20)?;
        write_u16(&mut central_directory, 0)?;
        write_u16(&mut central_directory, 0)?;
        write_u16(&mut central_directory, 0)?;
        write_u16(&mut central_directory, 0)?;
        write_u32(&mut central_directory, crc)?;
        write_u32(&mut central_directory, content.len() as u32)?;
        write_u32(&mut central_directory, content.len() as u32)?;
        write_u16(&mut central_directory, name_bytes.len() as u16)?;
        write_u16(&mut central_directory, 0)?;
        write_u16(&mut central_directory, 0)?;
        write_u16(&mut central_directory, 0)?;
        write_u16(&mut central_directory, 0)?;
        write_u32(&mut central_directory, 0)?;
        write_u32(&mut central_directory, local_header_offset)?;
        central_directory.extend_from_slice(name_bytes);
    }

    let central_directory_offset = output.len() as u32;
    output.extend_from_slice(&central_directory);
    let central_directory_size = central_directory.len() as u32;

    write_u32(&mut output, 0x0605_4b50)?;
    write_u16(&mut output, 0)?;
    write_u16(&mut output, 0)?;
    write_u16(&mut output, files.len() as u16)?;
    write_u16(&mut output, files.len() as u16)?;
    write_u32(&mut output, central_directory_size)?;
    write_u32(&mut output, central_directory_offset)?;
    write_u16(&mut output, 0)?;

    fs::write(path, output)
}

fn write_u16(output: &mut Vec<u8>, value: u16) -> io::Result<()> {
    output.write_all(&value.to_le_bytes())
}

fn write_u32(output: &mut Vec<u8>, value: u32) -> io::Result<()> {
    output.write_all(&value.to_le_bytes())
}

fn crc32(bytes: &[u8]) -> u32 {
    let mut crc = 0xffff_ffffu32;
    for byte in bytes {
        let mut value = (crc ^ u32::from(*byte)) & 0xff;
        for _ in 0..8 {
            value = if value & 1 == 1 {
                (value >> 1) ^ 0xedb8_8320
            } else {
                value >> 1
            };
        }

        crc = (crc >> 8) ^ value;
    }

    !crc
}
