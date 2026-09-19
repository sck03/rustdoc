use crate::{
    Check, Result,
    archive::Package,
    mapping,
    sheet::{Sheet, shift_references},
    xml::{Node, Part},
};
use export_doc_contracts::generated_api::ApiInvoiceDetailDto;
use export_doc_domain::{
    invoice::{InvoiceDraft, MAX_ROWS},
    invoice_columns::ITEM_COLUMNS,
};
use serde_json::{Value, json};
use std::collections::BTreeMap;

struct Workbook {
    package: Package,
    workbook: Node,
    sheet: Sheet,
    sheet_path: String,
}
impl Workbook {
    fn open(bytes: &[u8], check: Check<'_>) -> Result<Self> {
        let package = Package::open(bytes, check)?;
        let workbook = Node::parse(package.get("xl/workbook.xml")?)?;
        let sheet = workbook
            .child("sheets")
            .and_then(|sheets| sheets.nodes().next())
            .ok_or("Excel 没有工作表。")?;
        let id = sheet.attr("r:id").ok_or("Excel 工作表缺少关联。")?;
        let relations = Node::parse(package.get("xl/_rels/workbook.xml.rels")?)?;
        let relation = relations
            .nodes()
            .find(|node| node.attr("Id") == Some(id))
            .ok_or("Excel 工作表关联不存在。")?;
        if relation.attr("TargetMode") == Some("External") {
            return Err("Excel 工作表不能使用外部关联。".into());
        }
        let target = relation.attr("Target").ok_or("Excel 工作表路径缺失。")?;
        let sheet_path = if target.starts_with("/xl/") {
            target.trim_start_matches('/').into()
        } else {
            format!("xl/{target}")
        };
        if sheet_path.split('/').any(|part| matches!(part, ".." | ".")) || sheet_path.contains('\\')
        {
            return Err("Excel 工作表路径无效。".into());
        }
        let sheet = Sheet::new(Node::parse(package.get(&sheet_path)?)?)?;
        Ok(Self {
            package,
            workbook,
            sheet,
            sheet_path,
        })
    }
    fn finish(mut self, recalculate: bool, check: Check<'_>) -> Result<Vec<u8>> {
        if recalculate {
            self.sheet.recalculate_sums()?;
            if self.workbook.child("calcPr").is_none() {
                let node = self.workbook.named("calcPr");
                self.workbook.children.push(Part::Node(node));
            }
            let calc = self.workbook.child_mut("calcPr").unwrap();
            calc.set("fullCalcOnLoad", 1);
            calc.set("forceFullCalc", 1);
            calc.set("calcMode", "auto");
        }
        self.package
            .0
            .insert(self.sheet_path, self.sheet.0.bytes()?);
        self.package
            .0
            .insert("xl/workbook.xml".into(), self.workbook.bytes()?);
        self.package.finish(check)
    }
}
pub fn blank_template(
    template: &[u8],
    exporter_name: &str,
    booking: bool,
    check: Check<'_>,
) -> Result<Vec<u8>> {
    let mut workbook = Workbook::open(template, check)?;
    workbook.sheet.set(1, 1, &json!(exporter_name))?;
    if booking {
        workbook.sheet.booking()?;
    }
    workbook.finish(true, check)
}
pub fn convert_booking(source: &[u8], check: Check<'_>) -> Result<Vec<u8>> {
    let mut workbook = Workbook::open(source, check)?;
    workbook.sheet.booking()?;
    workbook.finish(false, check)
}
pub fn booking_from_invoice(
    template: &[u8],
    invoice: &ApiInvoiceDetailDto,
    settings: &Value,
    check: Check<'_>,
) -> Result<Vec<u8>> {
    let invoice = InvoiceDraft::from_dto(invoice.clone()).build()?;
    if invoice.items.len() > MAX_ROWS {
        return Err("商品明细超过输出上限。".into());
    }
    let mut workbook = Workbook::open(template, check)?;
    let value = serde_json::to_value(&invoice).map_err(|e| e.to_string())?;
    for (key, setting) in mapping::HEADERS {
        if let Some((row, col)) = settings[*setting].as_str().and_then(mapping::cell) {
            workbook.sheet.set(row, col, &value[*key])?;
        }
    }
    for (key, start, count) in mapping::MULTILINE {
        if let Some((row, col)) = settings[*start].as_str().and_then(mapping::cell) {
            let text = value[*key]
                .as_str()
                .unwrap_or("")
                .replace("\r\n", "\n")
                .replace('\r', "\n");
            let lines: Vec<_> = text
                .lines()
                .map(str::trim)
                .filter(|line| !line.is_empty())
                .collect();
            let capacity = settings[*count].as_u64().unwrap_or(1).clamp(1, 20) as usize;
            // Preserve all address text even when it exceeds the configured rows.
            for offset in 0..capacity {
                let text = if offset == capacity - 1 {
                    lines.get(offset..).unwrap_or(&[]).join("\n")
                } else {
                    lines.get(offset).copied().unwrap_or("").into()
                };
                workbook.sheet.set(row + offset as u32, col, &json!(text))?;
            }
        }
    }
    if invoice.notify_party_mode == "SameAsConsignee" {
        if let Some((row, col)) = settings["notifyPartyNameCell"]
            .as_str()
            .and_then(mapping::cell)
        {
            workbook.sheet.set(row, col, &json!("SAME AS CONSIGNEE"))?;
        }
    }
    workbook.sheet.set(13, 15, &json!(invoice.shipment_date))?;
    let start = settings["itemsStartRow"]
        .as_u64()
        .filter(|row| *row > 0 && *row <= 1000)
        .ok_or("商品起始行配置无效。")? as u32;
    if let Some((at, added)) = workbook.sheet.extend(start, invoice.items.len())? {
        if let Some(names) = workbook.workbook.child_mut("definedNames") {
            for node in names
                .nodes_mut()
                .filter(|node| node.attr("localSheetId") == Some("0"))
            {
                let value = shift_references(&node.text(), at, added);
                node.children = vec![Part::Text(value)];
            }
        }
    }
    for (index, item) in invoice.items.iter().enumerate() {
        check()?;
        let item = serde_json::to_value(item).map_err(|e| e.to_string())?;
        for column in ITEM_COLUMNS {
            if let Some(col) = settings[format!("{}Col", column.key)]
                .as_u64()
                .filter(|col| *col > 0 && *col <= 128)
            {
                let value = if column.numeric {
                    let text = item[column.key]
                        .as_str()
                        .map(str::to_owned)
                        .unwrap_or_else(|| item[column.key].to_string());
                    serde_json::from_str::<Value>(&text).map_err(|_| "Excel 数值无效。")?
                } else {
                    item[column.key].clone()
                };
                workbook
                    .sheet
                    .set(start + index as u32, col as u32, &value)?;
            }
        }
    }
    workbook.sheet.booking()?;
    workbook.finish(true, check)
}

/// General tabular exports write text as inline strings, never as formulas.
pub fn table(columns: &[(&str, &str)], rows: &[Value], check: Check<'_>) -> Result<Vec<u8>> {
    let sheet = crate::tabular::sheet(columns, rows, check)?;
    let files = [
        (
            "[Content_Types].xml",
            r#"<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>"#,
        ),
        (
            "_rels/.rels",
            r#"<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>"#,
        ),
        (
            "xl/workbook.xml",
            r#"<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="导出数据" sheetId="1" r:id="rId1"/></sheets></workbook>"#,
        ),
        (
            "xl/_rels/workbook.xml.rels",
            r#"<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>"#,
        ),
        (
            "xl/worksheets/sheet1.xml",
            r#"<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetFormatPr defaultColWidth="18" defaultRowHeight="20"/><sheetData/></worksheet>"#,
        ),
    ];
    let mut package = Package(
        files
            .into_iter()
            .map(|(name, bytes)| (name.into(), bytes.as_bytes().to_vec()))
            .collect::<BTreeMap<_, _>>(),
    );
    package.0.insert("xl/worksheets/sheet1.xml".into(), sheet);
    package.finish(check)
}
