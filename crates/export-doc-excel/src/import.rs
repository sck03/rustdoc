use crate::{Check, Result, mapping};
use calamine::{Data, DataType, ExcelDateTime, ExcelDateTimeType, Range, Reader, Sheets};
use export_doc_contracts::{contracts, generated_api::*};
use export_doc_domain::{
    invoice::{InvoiceDraft, ItemRow, MAX_ROWS, money, parse_number},
    invoice_columns::ITEM_COLUMNS,
};
use exportdoc_excel_analyzer::{SheetAnalysis, analyze_cells};
use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::{collections::BTreeMap, str::FromStr};
use unicode_normalization::UnicodeNormalization;

fn import_date(raw: &str) -> Option<chrono::NaiveDate> {
    // Require a four-digit year before chrono parsing, whose %Y is permissive.
    let separator = ['-', '/', '.'].into_iter().find(|c| raw.contains(*c))?;
    let parts: Vec<_> = raw.split(separator).collect();
    if parts.len() != 3
        || parts
            .iter()
            .any(|p| p.is_empty() || !p.bytes().all(|b| b.is_ascii_digit()))
    {
        return None;
    }
    let (year, month, day) = if parts[0].len() == 4 {
        (parts[0], parts[1], parts[2])
    } else if separator == '/' && parts[2].len() == 4 && parts[0].parse::<u32>().ok()? > 12 {
        // Unambiguous day-first input; 03/04/2026 requires the source to clarify.
        (parts[2], parts[1], parts[0])
    } else {
        return None;
    };
    if month.len() > 2 || day.len() > 2 || year == "0000" {
        return None;
    }
    chrono::NaiveDate::from_ymd_opt(year.parse().ok()?, month.parse().ok()?, day.parse().ok()?)
}

#[cfg(test)]
mod date_tests {
    #[test]
    fn requires_an_unambiguous_four_digit_year() {
        for value in [
            "03/04/26",
            "03/04/2026",
            "2026-02-29",
            "0000-01-01",
            "+2026-01-01",
        ] {
            assert!(super::import_date(value).is_none(), "{value}");
        }
        for value in ["2026-04-13", "2026/4/13", "2026.04.13", "13/04/2026"] {
            assert_eq!(super::import_date(value).unwrap().to_string(), "2026-04-13");
        }
    }
}

fn text(cell: Option<&Data>) -> String {
    match cell {
        // Excel stores numeric cells with 15 significant decimal digits. Drop
        // binary calculation noise here; text cells keep their exact precision.
        Some(Data::Float(value)) if value.is_finite() => {
            Decimal::from_scientific(&format!("{value:.14e}"))
                .map(|number| number.normalize().to_string())
                .unwrap_or_else(|_| value.to_string())
        }
        Some(value @ (Data::DateTime(_) | Data::DateTimeIso(_))) => value
            .as_date()
            .map(|date| date.format("%Y-%m-%d").to_string())
            .unwrap_or_else(|| value.to_string()),
        Some(Data::Empty) | None => String::new(),
        Some(value) => value.to_string().nfc().collect::<String>().trim().into(),
    }
}
pub(crate) fn at(range: &Range<Data>, row: usize, col: usize) -> String {
    if row == 0 || col == 0 {
        return String::new();
    }
    text(range.get_value(((row - 1) as u32, (col - 1) as u32)))
}
fn score(value: f32) -> Decimal {
    Decimal::from_str(&format!("{value:.4}")).unwrap_or_default()
}

fn numeric_date(
    range: &Range<Data>,
    key: &str,
    row: usize,
    col: usize,
    epoch: Option<bool>,
) -> Option<String> {
    if !matches!(key, "invoiceDate" | "shipmentDate") || row == 0 || col == 0 {
        return None;
    }
    let value = match range.get_value(((row - 1) as u32, (col - 1) as u32))? {
        Data::Float(value) => *value,
        Data::Int(value) => *value as f64,
        _ => return None,
    };
    if !(1.0..=2_958_465.0).contains(&value) {
        return None;
    }
    ExcelDateTime::new(value, ExcelDateTimeType::DateTime, epoch?)
        .as_datetime()
        .map(|date| date.date().format("%Y-%m-%d").to_string())
}

// BIFF files can retain formatted/empty records through row 65536. Count
// content, not formatting, and keep original coordinates for field mappings.
fn content_range(range: Range<Data>, total: &mut usize, check: Check<'_>) -> Result<Range<Data>> {
    let Some((row_offset, col_offset)) = range.start() else {
        return Ok(range);
    };
    let mut bounds: Option<((u32, u32), (u32, u32))> = None;
    for (row, cells) in range.rows().enumerate() {
        check()?;
        // A row containing only empty cells and numeric zero has no item or
        // document identity. BIFF may retain such formula caches at row 65536.
        // Keep zeros inside every meaningful row, including zero-quantity items.
        if !cells.iter().any(|cell| match cell {
            Data::Empty => false,
            Data::Float(value) => *value != 0.0,
            Data::Int(value) => *value != 0,
            Data::String(value) => !value.trim().is_empty(),
            _ => true,
        }) {
            continue;
        }
        for (col, cell) in cells.iter().enumerate() {
            if matches!(cell, Data::String(value) if !export_doc_domain::document_text::is_valid(value))
            {
                return Err(format!(
                    "Excel 第 {} 行、第 {} 列含不支持的控制字符，请修正后重试。",
                    row_offset as usize + row + 1,
                    col_offset as usize + col + 1
                ));
            }
            let value = text(Some(cell));
            if value.is_empty() {
                continue;
            }
            *total = total.checked_add(1).ok_or("Excel 容量无效。")?;
            if *total > 500_000 {
                return Err("Excel 有效单元格超过 500000 个。".into());
            }
            if value.chars().count() > 4096 {
                return Err("Excel 单元格文本超过 4096 字。".into());
            }
            let point = (row_offset + row as u32, col_offset + col as u32);
            let (start, end) = bounds.get_or_insert((point, point));
            start.0 = start.0.min(point.0);
            start.1 = start.1.min(point.1);
            end.0 = end.0.max(point.0);
            end.1 = end.1.max(point.1);
        }
    }
    Ok(bounds.map_or_else(Range::empty, |(start, end)| range.range(start, end)))
}

pub fn preview(
    bytes: &[u8],
    name: &str,
    manual: Option<&Value>,
    business_date: &str,
    check: Check<'_>,
) -> Result<ApiExcelImportPreviewResponse> {
    check()?;
    let manual = manual
        .map(crate::manual_mapping::ManualMapping::read)
        .transpose()?;
    let mut workbook = crate::reader::open(bytes, check)?;
    let epoch = match &workbook {
        Sheets::Xls(book) => Some(book.has_1904_epoch()),
        Sheets::Xlsx(book) => Some(book.has_1904_epoch()),
        Sheets::Xlsb(book) => Some(book.has_1904_epoch()),
        _ => None,
    };
    let names = workbook.sheet_names().to_vec();
    if names.len() > 64 {
        return Err("Excel 工作表超过 64 个。".into());
    }
    let mut analyses = Vec::new();
    let mut selected: Option<(Range<Data>, SheetAnalysis)> = None;
    let mut total_cells = 0_usize;
    for name in names {
        check()?;
        if manual.as_ref().is_some_and(|mapping| name != mapping.sheet) {
            continue;
        }
        let range = workbook
            .worksheet_range(&name)
            .map_err(|e| format!("工作表 {name} 无法读取：{e}"))?;
        let range = content_range(range, &mut total_cells, check)?;
        let (height, width) = range.get_size();
        if range.is_empty() {
            continue;
        }
        let end = range.end().ok_or("Excel 工作表范围无效。")?;
        let mut cells = vec![];
        for row in 0..=end.0.min(119) {
            let mut values = vec![];
            for col in 0..=end.1.min(47) {
                let value = text(range.get_value((row, col)));
                values.push(value);
            }
            cells.push(values);
        }
        let mut analysis = analyze_cells(&name, &cells);
        if let Some(mapping) = &manual {
            analysis.table = Some(mapping.table());
            analysis.field_candidates.clear();
            analysis.confidence = 1.0;
        }
        analyses.push(ApiExcelImportSheetAnalysisDto {
            name: name.clone(),
            used_row_count: height as i64,
            used_column_count: width as i64,
            field_candidate_count: analysis.field_candidates.len() as i64,
            has_item_table: analysis.table.is_some(),
            confidence: score(analysis.confidence),
            ..Default::default()
        });
        if analysis.table.is_some()
            && selected
                .as_ref()
                .is_none_or(|(_, old)| analysis.confidence > old.confidence)
        {
            selected = Some((range, analysis));
        }
    }
    let Some((range, analysis)) = selected else {
        let message = manual.as_ref().map_or_else(
            || "没有识别到可导入的商品明细，请核对表头并填写数据后重试。".to_owned(),
            |mapping| {
                format!(
                    "手动方案指定的工作表“{}”不存在或没有有效内容。",
                    mapping.sheet
                )
            },
        );
        return Ok(ApiExcelImportPreviewResponse {
            source_path: name.into(),
            success: false,
            errors: vec![message.clone()],
            analysis_report: Some(ApiExcelImportAnalysisReportDto {
                schema_version: "excel-analysis-rs/0.2".into(),
                analyzer_id: "rust-calamine".into(),
                sheets: analyses,
                issues: vec![ApiExcelImportAnalysisIssueDto {
                    severity: "Error".into(),
                    code: "MissingItemTable".into(),
                    message,
                    ..Default::default()
                }],
                ..Default::default()
            }),
            storage_policy: "解析未生成可用草稿，没有写入业务数据。".into(),
            ..Default::default()
        });
    };
    let detected = analysis.table.as_ref().ok_or("缺少表格识别结果。")?;
    let end = range.end().ok_or("Excel 工作表范围无效。")?;
    if end.0 >= 10000 || end.1 >= 128 {
        return Err(format!(
            "选中的工作表 {} 有效内容超出 10000 行或 128 列，请检查末尾数据或公式。",
            analysis.name
        ));
    }
    let mut columns = BTreeMap::new();
    for field in &detected.fields {
        if let Some(index) = ITEM_COLUMNS
            .iter()
            .position(|column| column.key.eq_ignore_ascii_case(&field.canonical_field))
        {
            columns.entry(index).or_insert(field.column);
        }
    }
    let template_layout = manual.is_none()
        && detected.data_start_row == mapping::ITEMS_START_ROW as usize
        && columns.len() >= 8
        && columns
            .iter()
            .all(|(&index, &column)| mapping::item_column(ITEM_COLUMNS[index].key) == Some(column));
    if template_layout {
        for (index, column) in ITEM_COLUMNS.iter().enumerate() {
            if let Some(col) = mapping::item_column(column.key) {
                columns.insert(index, col);
            }
        }
    }
    let mut draft = InvoiceDraft::new(business_date);
    let mut header = serde_json::to_value(&draft.header).map_err(|e| e.to_string())?;
    for field in &analysis.field_candidates {
        let key = header
            .as_object()
            .and_then(|object| {
                object
                    .keys()
                    .find(|key| key.eq_ignore_ascii_case(&field.field_key))
            })
            .cloned();
        if let Some(key) = key {
            if header[&key].is_string() && !field.value.trim().is_empty() {
                header[&key] = json!(
                    numeric_date(&range, &key, field.row, field.column, epoch)
                        .unwrap_or_else(|| field.value.trim().into())
                );
            }
        }
    }
    if template_layout {
        for &(key, row, col) in mapping::HEADERS {
            let value = numeric_date(&range, key, row as usize, col as usize, epoch)
                .unwrap_or_else(|| at(&range, row as usize, col as usize));
            if !value.is_empty() {
                header[key] = json!(value);
            }
        }
        for &(key, row) in mapping::MULTILINE {
            let lines = (0..4)
                .map(|offset| at(&range, row as usize + offset, 2))
                .filter(|line| !line.is_empty())
                .collect::<Vec<_>>();
            header[key] = json!(lines.join("\n"));
        }
    }
    if let Some(mapping) = &manual {
        for &(key, row, col) in &mapping.headers {
            header[key] =
                json!(
                    numeric_date(&range, key, row as usize, col as usize, epoch)
                        .unwrap_or_else(|| at(&range, row as usize, col as usize))
                );
        }
        for &(key, row, col, count) in &mapping.multiline {
            header[key] = json!(
                (0..count)
                    .map(|offset| at(&range, row as usize + offset, col as usize))
                    .filter(|line| !line.is_empty())
                    .collect::<Vec<_>>()
                    .join("\n")
            );
        }
    }
    let mut errors = vec![];
    for key in ["invoiceDate", "shipmentDate"] {
        let raw = header[key].as_str().unwrap_or("").to_owned();
        if !raw.is_empty() {
            let date = import_date(&raw);
            if let Some(date) = date {
                header[key] = json!(date.format("%Y-%m-%d").to_string());
            } else {
                errors.push(format!(
                    "日期 {raw} 无法确定，请在源工作簿中使用 YYYY-MM-DD。"
                ));
            }
        }
    }
    if header["exporterNameCN"] == "请填写出口商中文名称" {
        header["exporterNameCN"] = json!("");
    }
    let notify = header["notifyPartyName"].as_str().unwrap_or("").trim();
    header["notifyPartyMode"] = json!(if notify.is_empty() {
        "None"
    } else if notify.eq_ignore_ascii_case("SAME AS CONSIGNEE") {
        "SameAsConsignee"
    } else {
        "Separate"
    });
    draft.header = serde_json::from_value(header).map_err(|e| format!("发票抬头数据无效：{e}"))?;
    draft.rows.clear();
    let supplements = if manual.is_some() {
        crate::supplements::Tables::default()
    } else {
        crate::supplements::Tables::read(&range, detected.data_start_row, check)?
    };
    let mut source_fields = vec![];
    let start = detected.data_start_row;
    let end = range.end().unwrap().0 as usize + 1;
    let end = manual
        .as_ref()
        .filter(|mapping| mapping.end > 0)
        .map_or(end, |mapping| end.min(mapping.end));
    let end = supplements.detail_end().map_or(end, |last| end.min(last));
    for row_index in start..=end {
        check()?;
        let mut row = ItemRow::blank();
        let mut changed = vec![];
        for (&index, &col) in &columns {
            let raw = at(&range, row_index, col);
            if !raw.is_empty() {
                row.cells[index] = raw;
                changed.push(index);
            }
        }
        if [1, 2, 3]
            .iter()
            .all(|&index| row.cells[index].trim().is_empty())
            // A footer may occupy the description columns. Product rows also
            // need a value in a mapped numeric column; explicit zero is valid.
            || !changed.iter().any(|&index| ITEM_COLUMNS[index].numeric)
        {
            continue;
        }
        if [1, 2, 3].iter().any(|&index| is_total(&row.cells[index])) {
            continue;
        }
        if row.cells[1] == "款号" || row.cells[2].eq_ignore_ascii_case("description") {
            continue;
        }
        if let Some(field) = detected
            .fields
            .iter()
            .find(|field| field.canonical_field == "Dimension")
        {
            let dimensions = at(&range, row_index, field.column)
                .to_lowercase()
                .replace(['×', '*'], "x");
            let values: Vec<_> = dimensions.split('x').map(str::trim).collect();
            if values.len() == 3 {
                for (offset, value) in values.into_iter().enumerate() {
                    row.cells[15 + offset] = value.into();
                    changed.push(15 + offset);
                }
            }
        }
        let result = (|| {
            let quantity = parse_number(&row.cells[8])?;
            let price = parse_number(&row.cells[23])?;
            if changed.contains(&24) {
                let amount = parse_number(&row.cells[24])?;
                if quantity.checked_mul(price).map(money) == Some(amount) {
                    changed.retain(|index| *index != 24);
                }
            }
            row.recalculate(&changed)?;
            row.to_dto()?;
            Ok::<_, String>(())
        })();
        if let Err(cause) = result {
            errors.push(format!(
                "工作表 {} 第 {row_index} 行：{cause}",
                analysis.name
            ));
            if errors.len() >= 100 {
                break;
            }
            continue;
        }
        draft.rows.push(row);
        source_fields.push(changed);
        if draft.rows.len() > MAX_ROWS {
            return Err(format!("商品明细超过 {MAX_ROWS} 行。"));
        }
    }
    if draft.rows.is_empty() {
        errors.push("未读取到有效商品明细，请核对表头、数据行和公式缓存。".into());
    }
    let mut issues = supplements.apply(&mut draft.rows, &source_fields, check)?;
    let invoice = draft.preview()?;
    let customer = (!invoice.customer_name_en.is_empty()).then(|| ApiImportedCustomerDto {
        customer_name_en: invoice.customer_name_en.clone(),
        display_name: invoice.customer_name_en.clone(),
        address_en: invoice.customer_address_en.clone(),
        notify_party_mode: invoice.notify_party_mode.clone(),
        notify_party_name: invoice.notify_party_name.clone(),
        notify_party_address: invoice.notify_party_address.clone(),
        ..Default::default()
    });
    let exporter = (!invoice.exporter_name_en.is_empty()).then(|| ApiImportedExporterDto {
        exporter_name_en: invoice.exporter_name_en.clone(),
        exporter_name_cn: invoice.exporter_name_cn.clone(),
        address_en: invoice.exporter_address_en.clone(),
        credit_code: invoice.exporter_credit_code.clone(),
        ..Default::default()
    });
    let mut mapped = contracts::initial(contracts::schema("ApiExcelImportItemColumnAnalysisDto"));
    for (index, column) in &columns {
        mapped[format!("{}Col", ITEM_COLUMNS[*index].key)] = json!(column);
    }
    let fields = analysis
        .field_candidates
        .iter()
        .map(|field| ApiExcelImportFieldAnalysisDto {
            field_key: field.field_key.clone(),
            display_name: field.display_name.clone(),
            value: field.value.clone(),
            worksheet_name: field.worksheet_name.clone(),
            row: field.row as i64,
            column: field.column as i64,
            confidence: score(field.confidence),
            source: field.source.clone(),
            ..Default::default()
        })
        .collect();
    issues.extend(
        errors
            .iter()
            .map(|message| ApiExcelImportAnalysisIssueDto {
                severity: "Error".into(),
                code: "ImportValidation".into(),
                message: message.clone(),
                ..Default::default()
            })
            .collect::<Vec<_>>(),
    );
    Ok(ApiExcelImportPreviewResponse {
        source_path: name.into(),
        success: errors.is_empty(),
        invoice: Some(invoice),
        customer,
        exporter,
        analysis_report: Some(ApiExcelImportAnalysisReportDto {
            schema_version: "excel-analysis-rs/0.2".into(),
            analyzer_id: "rust-calamine".into(),
            selected_worksheet_name: analysis.name.clone(),
            confidence: score(analysis.confidence),
            sheets: analyses,
            fields,
            item_table: Some(ApiExcelImportItemTableAnalysisDto {
                worksheet_name: analysis.name,
                header_row: detected.header_start_row as i64,
                header_depth: detected.header_depth as i64,
                data_start_row: start as i64,
                confidence: score(detected.confidence),
                columns: serde_json::from_value(mapped).map_err(|e| e.to_string())?,
                ..Default::default()
            }),
            issues,
            ..Default::default()
        }),
        errors,
        storage_policy: "仅在内存中解析为待核对草稿；确认保存前不写入业务数据库。".into(),
        ..Default::default()
    })
}
fn is_total(value: &str) -> bool {
    matches!(
        value.trim().to_lowercase().as_str(),
        "合计" | "总计" | "total" | "grand total" | "subtotal" | "小计"
    )
}
