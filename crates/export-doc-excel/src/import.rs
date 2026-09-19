use crate::{Check, MAX_INPUT, Result, archive::Package, mapping};
use calamine::{Data, DataType, Range, Reader, open_workbook_auto_from_rs};
use export_doc_contracts::{contracts, generated_api::*};
use export_doc_domain::{
    invoice::{InvoiceDraft, ItemRow, MAX_ROWS, money, parse_number},
    invoice_columns::ITEM_COLUMNS,
};
use exportdoc_excel_analyzer::{SheetAnalysis, analyze_cells};
use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::{collections::BTreeMap, io::Cursor, str::FromStr};
use unicode_normalization::UnicodeNormalization;

fn text(cell: Option<&Data>) -> String {
    match cell {
        Some(value @ (Data::DateTime(_) | Data::DateTimeIso(_))) => value
            .as_date()
            .map(|date| date.format("%Y-%m-%d").to_string())
            .unwrap_or_else(|| value.to_string()),
        Some(Data::Empty) | None => String::new(),
        Some(value) => value.to_string().nfc().collect::<String>().trim().into(),
    }
}
fn at(range: &Range<Data>, row: usize, col: usize) -> String {
    if row == 0 || col == 0 {
        return String::new();
    }
    text(range.get_value(((row - 1) as u32, (col - 1) as u32)))
}
fn configured(range: &Range<Data>, settings: &Value, key: &str) -> String {
    settings[key]
        .as_str()
        .and_then(mapping::cell)
        .map(|(row, col)| at(range, row as usize, col as usize))
        .unwrap_or_default()
}
fn score(value: f32) -> Decimal {
    Decimal::from_str(&format!("{value:.4}")).unwrap_or_default()
}

pub fn preview(
    bytes: &[u8],
    name: &str,
    settings: &Value,
    business_date: &str,
    check: Check<'_>,
) -> Result<ApiExcelImportPreviewResponse> {
    check()?;
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err("Excel 文件为空或超过 25 MiB。".into());
    }
    if bytes.starts_with(b"PK") {
        Package::open(bytes, check)?;
    } else if !bytes.starts_with(&[0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1]) {
        return Err("文件内容不是受支持的 Excel 工作簿。".into());
    }
    let mut workbook = open_workbook_auto_from_rs(Cursor::new(bytes))
        .map_err(|e| format!("无法读取 Excel 工作簿：{e}"))?;
    let names = workbook.sheet_names().to_vec();
    if names.len() > 64 {
        return Err("Excel 工作表超过 64 个。".into());
    }
    let mut analyses = Vec::new();
    let mut selected: Option<(Range<Data>, SheetAnalysis)> = None;
    let mut total_cells = 0_usize;
    for name in names {
        check()?;
        let range = workbook
            .worksheet_range(&name)
            .map_err(|e| format!("工作表 {name} 无法读取：{e}"))?;
        let (height, width) = range.get_size();
        total_cells = total_cells
            .checked_add(height.saturating_mul(width))
            .ok_or("Excel 容量无效。")?;
        if height > 10000 || width > 128 || total_cells > 500_000 {
            return Err("Excel 工作表超过 10000 行、128 列或总单元格上限。".into());
        }
        if range.is_empty() {
            continue;
        }
        let end = range.end().ok_or("Excel 工作表范围无效。")?;
        if end.0 > 10000 || end.1 > 128 {
            return Err("Excel 有效内容超出可导入区域。".into());
        }
        let mut cells = vec![];
        for row in 0..=end.0.min(119) {
            let mut values = vec![];
            for col in 0..=end.1.min(47) {
                let value = text(range.get_value((row, col)));
                if value.chars().count() > 4096 {
                    return Err("Excel 单元格文本超过 4096 字。".into());
                }
                values.push(value);
            }
            cells.push(values);
        }
        let analysis = analyze_cells(&name, &cells);
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
        let message = "没有识别到可导入的商品明细，请核对表头并填写数据后重试。".to_owned();
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
    let mut columns = BTreeMap::new();
    for field in &detected.fields {
        if let Some(index) = ITEM_COLUMNS
            .iter()
            .position(|column| column.key.eq_ignore_ascii_case(&field.canonical_field))
        {
            columns.entry(index).or_insert(field.column);
        }
    }
    let configured_layout = [(1, "styleNoCol"), (8, "quantityCol")]
        .iter()
        .all(|(index, key)| {
            columns.get(index).copied() == settings[*key].as_u64().map(|n| n as usize)
        });
    if configured_layout {
        for (index, column) in ITEM_COLUMNS.iter().enumerate() {
            if let Some(col) = settings[format!("{}Col", column.key)]
                .as_u64()
                .filter(|n| *n > 0 && *n <= 128)
            {
                columns.insert(index, col as usize);
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
                header[&key] = json!(field.value.trim());
            }
        }
    }
    if configured_layout {
        for (key, setting) in mapping::HEADERS {
            let value = configured(&range, settings, setting);
            if !value.is_empty() {
                header[*key] = json!(value);
            }
        }
        for (key, start, count) in mapping::MULTILINE {
            if let Some((row, col)) = settings[*start].as_str().and_then(mapping::cell) {
                let lines = (0..settings[*count].as_u64().unwrap_or(1).min(20))
                    .map(|offset| at(&range, row as usize + offset as usize, col as usize))
                    .filter(|line| !line.is_empty())
                    .collect::<Vec<_>>();
                header[*key] = json!(lines.join("\n"));
            }
        }
        let shipment = at(&range, 13, 15);
        if !shipment.is_empty() {
            header["shipmentDate"] = json!(shipment);
        }
    }
    let mut errors = vec![];
    for key in ["invoiceDate", "shipmentDate"] {
        let raw = header[key].as_str().unwrap_or("").to_owned();
        if !raw.is_empty() {
            let date = ["%Y-%m-%d", "%Y/%m/%d", "%Y.%m.%d", "%d/%m/%Y"]
                .iter()
                .find_map(|format| chrono::NaiveDate::parse_from_str(&raw, format).ok());
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
        "Custom"
    });
    draft.header = serde_json::from_value(header).map_err(|e| format!("发票抬头数据无效：{e}"))?;
    draft.rows.clear();
    let start = if configured_layout {
        settings["itemsStartRow"]
            .as_u64()
            .unwrap_or(detected.data_start_row as u64) as usize
    } else {
        detected.data_start_row
    };
    let end = settings["itemsEndRow"]
        .as_u64()
        .filter(|end| configured_layout && *end >= start as u64)
        .map(|end| end as usize)
        .unwrap_or(range.end().unwrap().0 as usize + 1);
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
        if draft.rows.len() > MAX_ROWS {
            return Err(format!("商品明细超过 {MAX_ROWS} 行。"));
        }
    }
    if draft.rows.is_empty() {
        errors.push("未读取到有效商品明细，请核对表头、数据行和公式缓存。".into());
    }
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
    let issues = errors
        .iter()
        .map(|message| ApiExcelImportAnalysisIssueDto {
            severity: "Error".into(),
            code: "ImportValidation".into(),
            message: message.clone(),
            ..Default::default()
        })
        .collect();
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
