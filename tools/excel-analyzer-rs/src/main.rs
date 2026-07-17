use calamine::{open_workbook_auto, Data, Reader};
use serde::Serialize;
use std::{env, panic, path::PathBuf, process};

const SCHEMA_VERSION: &str = "excel-analysis-rs/0.2";
const MAX_PROFILE_ROWS: usize = 120;
const MAX_PROFILE_COLUMNS: usize = 48;

fn main() {
    let mut args = env::args().skip(1);
    let Some(path) = args.next() else {
        eprintln!("Usage: exportdoc-excel-analyzer <excel-file>");
        process::exit(2);
    };

    let default_panic_hook = panic::take_hook();
    panic::set_hook(Box::new(|_| {}));
    let result = panic::catch_unwind(|| analyze_workbook(PathBuf::from(path)));
    panic::set_hook(default_panic_hook);
    match result {
        Ok(Ok(report)) => {
            println!(
                "{}",
                serde_json::to_string_pretty(&report).expect("serialize analysis report")
            );
        }
        Ok(Err(error)) => {
            eprintln!("{error}");
            process::exit(1);
        }
        Err(_) => {
            eprintln!("Rust Excel analyzer failed while reading this workbook. The host should fall back to the .NET Excel reader.");
            process::exit(3);
        }
    }
}

fn analyze_workbook(path: PathBuf) -> Result<AnalysisReport, String> {
    let mut workbook = open_workbook_auto(&path)
        .map_err(|error| format!("无法打开 Excel 文件 '{}': {error}", path.display()))?;

    let mut sheets = Vec::new();
    for sheet_name in workbook.sheet_names().to_owned() {
        let Ok(range) = workbook.worksheet_range(&sheet_name) else {
            continue;
        };

        let cells = collect_cells(&range);
        let used_range = detect_used_range(&cells);
        let fields = detect_document_fields(&cells, &sheet_name);
        let table = detect_table(&cells);
        let confidence = sheet_confidence(&fields, table.as_ref());
        sheets.push(SheetAnalysis {
            name: sheet_name,
            used_range,
            confidence,
            field_candidates: fields,
            table,
        });
    }

    let selected_sheet_name = sheets
        .iter()
        .max_by(|left, right| left.confidence.total_cmp(&right.confidence))
        .map(|sheet| sheet.name.clone())
        .unwrap_or_default();
    let selected_sheet = sheets
        .iter()
        .find(|sheet| sheet.name == selected_sheet_name);
    let fields = selected_sheet
        .map(|sheet| sheet.field_candidates.clone())
        .unwrap_or_default();
    let confidence = selected_sheet.map(|sheet| sheet.confidence).unwrap_or(0.0);
    let issues = build_issues(
        &fields,
        selected_sheet.and_then(|sheet| sheet.table.as_ref()),
    );

    Ok(AnalysisReport {
        schema_version: SCHEMA_VERSION.to_string(),
        analyzer_id: "rust-calamine".to_string(),
        source_path: path.display().to_string(),
        selected_worksheet_name: selected_sheet_name,
        confidence,
        fields,
        issues,
        sheets,
    })
}

fn collect_cells(range: &calamine::Range<Data>) -> Vec<Vec<String>> {
    range
        .rows()
        .take(MAX_PROFILE_ROWS)
        .map(|row| {
            row.iter()
                .take(MAX_PROFILE_COLUMNS)
                .map(cell_to_string)
                .collect::<Vec<_>>()
        })
        .collect()
}

fn detect_used_range(cells: &[Vec<String>]) -> UsedRange {
    let mut last_row = 0usize;
    let mut last_column = 0usize;
    for (row_index, row) in cells.iter().enumerate() {
        for (column_index, value) in row.iter().enumerate() {
            if !value.trim().is_empty() {
                last_row = row_index + 1;
                last_column = last_column.max(column_index + 1);
            }
        }
    }

    UsedRange {
        first_row: if last_row == 0 { 0 } else { 1 },
        first_column: if last_column == 0 { 0 } else { 1 },
        last_row,
        last_column,
    }
}

fn detect_table(cells: &[Vec<String>]) -> Option<TableAnalysis> {
    let mut best: Option<TableAnalysis> = None;

    for header_row in 0..cells.len().min(80) {
        let header_depth = 3usize.min(cells.len().saturating_sub(header_row));
        if header_depth == 0 {
            continue;
        }

        if count_detected_fields_in_row(cells, header_row) < 3 {
            continue;
        }

        let columns = build_field_candidates(cells, header_row, header_depth);
        let score: f32 = columns.iter().map(|field| field.confidence).sum();
        let has_quantity = columns
            .iter()
            .any(|field| field.canonical_field == "Quantity");
        let has_style = columns.iter().any(|field| {
            field.canonical_field == "StyleNo" || field.canonical_field == "StyleName"
        });

        if !has_quantity || !has_style || score < 2.5 {
            continue;
        }

        let Some(data_start_row) = find_first_data_row(cells, header_row + 1, &columns) else {
            continue;
        };

        let sample_rows = collect_sample_rows(cells, data_start_row, &columns);
        let candidate = TableAnalysis {
            header_start_row: header_row + 1,
            header_depth,
            data_start_row: data_start_row + 1,
            confidence: (score / 8.0).min(1.0),
            fields: columns,
            sample_rows,
        };

        if best
            .as_ref()
            .map(|current| candidate.confidence > current.confidence)
            .unwrap_or(true)
        {
            best = Some(candidate);
        }
    }

    best
}

fn detect_document_fields(cells: &[Vec<String>], sheet_name: &str) -> Vec<DocumentFieldCandidate> {
    let definitions = document_field_definitions();
    let mut fields = Vec::new();

    for definition in definitions {
        if let Some(candidate) = find_document_field(cells, sheet_name, &definition) {
            fields.push(candidate);
        }
    }

    fields
}

fn find_document_field(
    cells: &[Vec<String>],
    sheet_name: &str,
    definition: &DocumentFieldDefinition,
) -> Option<DocumentFieldCandidate> {
    let mut best: Option<DocumentFieldCandidate> = None;

    for row in 0..cells.len().min(100) {
        let max_columns = cells.get(row).map(Vec::len).unwrap_or_default().min(50);
        for column in 0..max_columns {
            let label = get_cell(cells, row, column);
            if label.is_empty() {
                continue;
            }

            let Some((confidence, source)) = match_document_label(label, definition.labels) else {
                continue;
            };

            if is_address_label_for_different_field(label, definition) {
                continue;
            }

            let mut value = extract_inline_value(label, definition.labels);
            let mut value_row = row;
            let mut value_column = column;
            if looks_like_role_assistive_text(&value) {
                value.clear();
            }

            if value.is_empty() {
                let nearby = if definition.prefer_below {
                    find_best_below_value(cells, row, column, definition.multi_line)
                } else {
                    find_nearby_value(cells, row, column, definition.multi_line)
                };
                value = nearby.value;
                value_row = nearby.row;
                value_column = nearby.column;
            }

            if value.is_empty() {
                continue;
            }

            if definition.field_key.contains("Address") {
                value = normalize_address_candidate_value(&value);
                if value.is_empty() {
                    continue;
                }
            } else if is_party_name_field(definition.field_key) {
                value = normalize_party_name_candidate_value(&value);
                if value.is_empty() {
                    continue;
                }
            }

            if is_generic_placeholder_value(&value) {
                continue;
            }

            let candidate = DocumentFieldCandidate {
                field_key: definition.field_key.to_string(),
                display_name: definition.display_name.to_string(),
                value: normalize_field_value(&value),
                worksheet_name: sheet_name.to_string(),
                row: value_row + 1,
                column: value_column + 1,
                confidence: (confidence + if definition.multi_line { 0.02 } else { 0.05 })
                    .min(0.98),
                source: source.to_string(),
            };

            best = pick_better_field(best, candidate);
        }
    }

    best
}

fn sheet_confidence(fields: &[DocumentFieldCandidate], table: Option<&TableAnalysis>) -> f32 {
    let required_score = [
        "InvoiceNo",
        "CustomerNameEN",
        "ExporterNameEN",
        "PortOfLoading",
        "PortOfDestination",
    ]
    .iter()
    .filter(|key| {
        fields
            .iter()
            .any(|field| &field.field_key == *key && !field.value.is_empty())
    })
    .count() as f32
        * 1.5;
    let field_score = fields.len() as f32 * 0.7;
    let table_score = table
        .map(|value| 5.0 + value.confidence * 5.0)
        .unwrap_or_default();

    ((field_score + required_score + table_score) / 22.0).min(1.0)
}

fn build_issues(
    fields: &[DocumentFieldCandidate],
    table: Option<&TableAnalysis>,
) -> Vec<AnalysisIssue> {
    let mut issues = Vec::new();
    for (field_key, message) in [
        ("InvoiceNo", "未能高置信度识别发票号。"),
        ("CustomerNameEN", "未能高置信度识别收货人。"),
        ("ExporterNameEN", "未能高置信度识别出口商/SHIPPER。"),
        ("PortOfLoading", "未能高置信度识别起运港。"),
        ("PortOfDestination", "未能高置信度识别目的港/目的地。"),
    ] {
        let confident = fields.iter().any(|field| {
            field.field_key == field_key && !field.value.is_empty() && field.confidence >= 0.65
        });
        if !confident {
            issues.push(AnalysisIssue {
                severity: "Warning".to_string(),
                code: "LowConfidenceField".to_string(),
                message: message.to_string(),
                field_key: field_key.to_string(),
            });
        }
    }

    if table.is_none() {
        issues.push(AnalysisIssue {
            severity: "Warning".to_string(),
            code: "MissingItemTable".to_string(),
            message: "未识别到商品明细表头，主程序应回退到当前 Excel 导入方案的固定行列配置。"
                .to_string(),
            field_key: String::new(),
        });
    }

    issues
}

fn document_field_definitions() -> Vec<DocumentFieldDefinition> {
    vec![
        DocumentFieldDefinition::new(
            "ExporterNameEN",
            "出口商/SHIPPER",
            &[
                "发票抬头",
                "出口商英文名称",
                "出口商",
                "发货人",
                "shipper/exporter",
                "shipper name",
                "exporter name",
                "shipper",
                "exporter",
                "seller",
                "consignor",
            ],
        ),
        DocumentFieldDefinition::new(
            "ExporterNameCN",
            "出口商中文名称",
            &["出口商中文名称", "出口商中文", "中文抬头"],
        ),
        DocumentFieldDefinition::multi(
            "ExporterAddressEN",
            "出口商地址",
            &[
                "发票抬头",
                "出口商",
                "发货人",
                "shipper/exporter",
                "shipper name",
                "exporter name",
                "出口商地址",
                "shipper address",
                "exporter address",
                "shipper",
            ],
        ),
        DocumentFieldDefinition::new(
            "CustomerNameEN",
            "收货人/CONSIGNEE",
            &[
                "收货人",
                "客户",
                "consignee name",
                "customer name",
                "buyer",
                "consignee",
                "customer",
            ],
        ),
        DocumentFieldDefinition::multi(
            "CustomerAddressEN",
            "收货人地址",
            &[
                "收货人地址",
                "客户地址",
                "consignee address",
                "customer address",
                "consignee",
            ],
        ),
        DocumentFieldDefinition::new(
            "NotifyPartyName",
            "通知人",
            &["通知人", "通知方", "notify party name", "notify party"],
        ),
        DocumentFieldDefinition::multi(
            "NotifyPartyAddress",
            "通知人地址",
            &[
                "通知人地址",
                "通知方地址",
                "notify party address",
                "notify party",
            ],
        ),
        DocumentFieldDefinition::new(
            "InvoiceNo",
            "发票号",
            &[
                "发票号",
                "发票号码",
                "invoice no",
                "invoice number",
                "invoice#",
                "invoice",
                "inv no",
            ],
        ),
        DocumentFieldDefinition::new(
            "ContractNo",
            "合同号",
            &[
                "合同号",
                "合同号码",
                "contract no",
                "contract number",
                "contract#",
                "contract",
                "s/c no",
                "sc no",
            ],
        ),
        DocumentFieldDefinition::new(
            "InvoiceDate",
            "发票日期",
            &["发票日期", "日期", "时间", "invoice date", "date"],
        ),
        DocumentFieldDefinition::new(
            "PortOfLoading",
            "起运港",
            &[
                "起运港",
                "装运港",
                "起运地",
                "port of loading",
                "loading port",
                "pol",
            ],
        ),
        DocumentFieldDefinition::new(
            "PortOfDestination",
            "目的港/目的地",
            &[
                "目的港",
                "目的地",
                "目的口岸",
                "port of destination",
                "destination port",
                "port of discharge",
                "discharge port",
                "pod",
                "destination",
            ],
        ),
        DocumentFieldDefinition::new(
            "DestinationCountry",
            "目的国",
            &["目的国", "目的国家", "destination country", "country"],
        ),
        DocumentFieldDefinition::new(
            "TradeTerms",
            "贸易条款",
            &[
                "贸易条款",
                "价格条款",
                "成交方式",
                "incoterms",
                "trade terms",
                "price terms",
            ],
        ),
        DocumentFieldDefinition::new(
            "TransportMode",
            "运输方式",
            &[
                "运输方式",
                "运输模式",
                "transport mode",
                "shipment mode",
                "mode of transport",
            ],
        ),
        DocumentFieldDefinition::new(
            "PaymentTerms",
            "付款方式",
            &[
                "付款方式",
                "收汇方式",
                "收回方式",
                "payment terms",
                "terms of payment",
                "payment",
            ],
        ),
        DocumentFieldDefinition::new("Currency", "币种", &["币种", "货币", "currency", "curr"]),
        DocumentFieldDefinition::new(
            "SupervisionMode",
            "监管方式",
            &["监管方式", "贸易方式", "trade mode", "customs mode"],
        ),
        DocumentFieldDefinition::new(
            "LetterOfCreditNo",
            "信用证号",
            &[
                "信用证号",
                "l/c no",
                "lc no",
                "letter of credit",
                "letter of credit no",
            ],
        ),
        DocumentFieldDefinition::new("IssuingBank", "开证行", &["开证行", "issuing bank"]),
        DocumentFieldDefinition::below(
            "ShippingMarks",
            "唛头",
            &[
                "唛头",
                "箱唛",
                "唛头信息",
                "shipping mark",
                "shipping marks",
                "marks",
                "marks and numbers",
            ],
        ),
    ]
}

fn find_nearby_value(
    cells: &[Vec<String>],
    row: usize,
    column: usize,
    multi_line: bool,
) -> NearbyValue {
    let mut candidates = Vec::new();
    candidates.extend(find_same_row_values(cells, row, column, multi_line));
    candidates.extend(find_below_values(cells, row, column, multi_line));
    if should_probe_below_neighbor_column(cells, row, column) {
        candidates.extend(find_below_values(cells, row, column + 1, multi_line));
    }

    candidates
        .into_iter()
        .filter(|candidate| !candidate.value.is_empty())
        .max_by(|left, right| {
            left.score
                .total_cmp(&right.score)
                .then_with(|| right.row.cmp(&left.row))
                .then_with(|| right.column.cmp(&left.column))
        })
        .unwrap_or_default()
}

fn find_best_below_value(
    cells: &[Vec<String>],
    row: usize,
    column: usize,
    multi_line: bool,
) -> NearbyValue {
    let mut candidates = Vec::new();
    candidates.extend(find_below_values(cells, row, column, multi_line));
    if should_probe_below_neighbor_column(cells, row, column) {
        candidates.extend(find_below_values(cells, row, column + 1, multi_line));
    }

    candidates
        .into_iter()
        .filter(|candidate| !candidate.value.is_empty())
        .max_by(|left, right| {
            left.score
                .total_cmp(&right.score)
                .then_with(|| right.row.cmp(&left.row))
                .then_with(|| right.column.cmp(&left.column))
        })
        .unwrap_or_default()
}

fn should_probe_below_neighbor_column(cells: &[Vec<String>], row: usize, column: usize) -> bool {
    let neighbor_header = get_cell(cells, row, column + 1);
    neighbor_header.is_empty()
        || (!is_field_boundary_value(neighbor_header)
            && !looks_like_sequence_header(neighbor_header))
}

fn looks_like_sequence_header(value: &str) -> bool {
    matches!(
        normalize_text(value).as_str(),
        "序号" | "编号" | "行号" | "no" | "number" | "serialno" | "serialnumber" | "itemno"
    )
}

fn find_same_row_values(
    cells: &[Vec<String>],
    row: usize,
    label_column: usize,
    multi_line: bool,
) -> Vec<NearbyValue> {
    let mut candidates = Vec::new();
    let start_column = label_column + 1;
    let max_column = cells
        .get(row)
        .map(Vec::len)
        .unwrap_or_default()
        .min(start_column + if multi_line { 9 } else { 3 });
    for column in start_column..max_column {
        let value = get_cell(cells, row, column);
        if value.is_empty() {
            continue;
        }

        if is_field_boundary_value(value) {
            break;
        }

        if has_field_boundary_between(cells, row, label_column + 1, column.saturating_sub(1)) {
            break;
        }

        let candidate_value = if multi_line {
            collect_vertical_block(cells, row, column, value)
        } else {
            value.to_string()
        };
        let score = 100.0 - ((column - start_column) as f32 * 4.0)
            + score_value_completeness(&candidate_value, multi_line);

        candidates.push(NearbyValue {
            value: candidate_value,
            row,
            column,
            score,
        });
    }

    candidates
}

fn find_below_values(
    cells: &[Vec<String>],
    row: usize,
    column: usize,
    multi_line: bool,
) -> Vec<NearbyValue> {
    let mut candidates = Vec::new();
    for next_row in (row + 1)..cells.len().min(row + 9) {
        let mut value = get_cell(cells, next_row, column);
        let mut value_column = column;
        if value.is_empty() {
            value = get_cell(cells, next_row, column + 1);
            value_column = column + 1;
        }

        if value.is_empty() {
            continue;
        }

        if is_field_boundary_value(value)
            || has_field_boundary_before_column(cells, next_row, value_column)
        {
            break;
        }

        let candidate_value = if multi_line {
            collect_vertical_block(cells, next_row, value_column, value)
        } else {
            value.to_string()
        };
        let score =
            88.0 - ((next_row - row - 1) as f32 * 6.0) - ((value_column - column) as f32 * 2.0)
                + score_value_completeness(&candidate_value, multi_line);

        candidates.push(NearbyValue {
            value: candidate_value,
            row: next_row,
            column: value_column,
            score,
        });
    }

    candidates
}

fn has_field_boundary_between(
    cells: &[Vec<String>],
    row: usize,
    start_column: usize,
    end_column: usize,
) -> bool {
    if start_column > end_column {
        return false;
    }

    (start_column..=end_column).any(|column| is_field_boundary_value(get_cell(cells, row, column)))
}

fn score_value_completeness(value: &str, multi_line: bool) -> f32 {
    if value.trim().is_empty() {
        return 0.0;
    }

    if !multi_line {
        return if value.chars().count() >= 3 { 2.0 } else { 0.0 };
    }

    let line_count = value.lines().filter(|line| !line.trim().is_empty()).count() as f32;
    (line_count * 1.5).min(6.0)
}

fn collect_vertical_block(
    cells: &[Vec<String>],
    start_row: usize,
    column: usize,
    first_value: &str,
) -> String {
    let mut lines = vec![normalize_field_value(first_value)];
    for row in (start_row + 1)..cells.len().min(start_row + 13) {
        let value = get_cell(cells, row, column);
        if value.is_empty() {
            break;
        }

        if is_field_boundary_value(value) {
            break;
        }

        if has_field_boundary_before_column(cells, row, column) {
            break;
        }

        let normalized = normalize_field_value(value);
        if !normalized.is_empty()
            && !lines
                .iter()
                .any(|line| line.eq_ignore_ascii_case(&normalized))
        {
            lines.push(normalized);
        }
    }

    lines.join("\n")
}

fn match_document_label(value: &str, labels: &[&str]) -> Option<(f32, &'static str)> {
    let normalized = normalize_text(value);
    if normalized.is_empty() {
        return None;
    }

    for label in labels {
        let normalized_label = normalize_text(label);
        if normalized == normalized_label {
            return Some((0.9, "LabelExact"));
        }

        if normalized_label.len() >= 4
            && normalized.starts_with(&normalized_label)
            && normalized.len() <= normalized_label.len() + 16
        {
            if inline_text_after_label(value, label).is_none() {
                continue;
            }

            if looks_like_code_value(value) {
                continue;
            }

            return Some((0.82, "LabelPrefix"));
        }

        if normalized_label.len() >= 3
            && normalized.contains(&normalized_label)
            && normalized.len() <= normalized_label.len().saturating_mul(3).max(12)
        {
            return Some((0.72, "LabelContains"));
        }
    }

    None
}

fn is_address_label_for_different_field(value: &str, definition: &DocumentFieldDefinition) -> bool {
    if definition.field_key.contains("Address") {
        return false;
    }

    let normalized = normalize_text(value);
    normalized.contains("address") || normalized.contains("地址")
}

fn extract_inline_value(value: &str, labels: &[&str]) -> String {
    let normalized_value = value.trim();
    for label in labels {
        if let Some(after_label) = inline_text_after_label(normalized_value, label) {
            let rest = after_label
                .trim_start_matches([' ', '\t', ':', '：', '#'])
                .trim();
            if !rest.is_empty() && !looks_like_known_document_label(rest) {
                return rest.to_string();
            }
        }
    }

    String::new()
}

fn inline_text_after_label<'a>(value: &'a str, label: &str) -> Option<&'a str> {
    let trimmed = value.trim();
    let lower_value = trimmed.to_lowercase();
    let lower_label = label.to_lowercase();
    if !lower_value.starts_with(&lower_label) {
        return None;
    }

    let after_label = &trimmed[label.len().min(trimmed.len())..];
    if after_label.trim().is_empty() {
        return None;
    }

    let first = after_label.chars().next()?;
    if matches!(first, ':' | '：' | '#') {
        return Some(after_label);
    }

    if first.is_whitespace() && !is_single_word_ascii_label(label) {
        return Some(after_label);
    }

    None
}

fn is_single_word_ascii_label(label: &str) -> bool {
    label.is_ascii() && !label.chars().any(char::is_whitespace) && !label.contains('/')
}

fn looks_like_code_value(value: &str) -> bool {
    value.contains('-')
        && value.chars().any(|c| c.is_ascii_digit())
        && !value.contains(':')
        && !value.contains('：')
}

fn is_generic_placeholder_value(value: &str) -> bool {
    matches!(
        normalize_text(value).as_str(),
        "name" | "address" | "名称" | "地址"
    ) || looks_like_role_assistive_text(value)
}

fn looks_like_role_assistive_text(value: &str) -> bool {
    if looks_like_business_party_value(value) {
        return false;
    }

    let normalized = normalize_text(value);
    if normalized.is_empty() || normalized.chars().any(|c| c.is_ascii_digit()) {
        return false;
    }

    [
        "发货人",
        "出口商",
        "收货人",
        "客户",
        "通知人",
        "通知方",
        "shipper",
        "exporter",
        "consignor",
        "seller",
        "consignee",
        "customer",
        "buyer",
        "notify party",
        "notify",
    ]
    .iter()
    .map(|label| normalize_text(label))
    .any(|label| is_same_or_near_short_label(&normalized, &label))
}

fn looks_like_business_party_value(value: &str) -> bool {
    let normalized = normalize_field_value(value);
    if normalized.is_empty() {
        return false;
    }

    if looks_like_address_fragment(&normalized) {
        return true;
    }

    let upper = normalized.to_uppercase();
    [
        " CO., LTD.",
        " CO. LTD.",
        " CO LTD",
        " LTD.",
        " LIMITED",
        " LLC.",
        " LLC",
        " INC.",
        " INC",
        " CORP.",
        " CORP",
        " COMPANY",
        " GROUP",
    ]
    .iter()
    .any(|suffix| upper.contains(suffix))
}

fn is_same_or_near_short_label(value: &str, label: &str) -> bool {
    if value.is_empty() || label.is_empty() {
        return false;
    }

    if value == label {
        return true;
    }

    let length_delta = value.len().abs_diff(label.len());
    if value.len() < 5 || label.len() < 5 || length_delta > 1 {
        return false;
    }

    levenshtein_distance_at_most_one(value, label)
}

fn levenshtein_distance_at_most_one(left: &str, right: &str) -> bool {
    if left == right {
        return true;
    }

    if left.len().abs_diff(right.len()) > 1 {
        return false;
    }

    let left_chars = left.chars().collect::<Vec<_>>();
    let right_chars = right.chars().collect::<Vec<_>>();
    let mut differences = 0usize;
    let mut left_index = 0usize;
    let mut right_index = 0usize;

    while left_index < left_chars.len() && right_index < right_chars.len() {
        if left_chars[left_index] == right_chars[right_index] {
            left_index += 1;
            right_index += 1;
            continue;
        }

        differences += 1;
        if differences > 1 {
            return false;
        }

        if left_chars.len() > right_chars.len() {
            left_index += 1;
        } else if right_chars.len() > left_chars.len() {
            right_index += 1;
        } else {
            left_index += 1;
            right_index += 1;
        }
    }

    true
}

fn is_party_name_field(field_key: &str) -> bool {
    matches!(
        field_key,
        "ExporterNameEN" | "CustomerNameEN" | "NotifyPartyName"
    )
}

fn normalize_party_name_candidate_value(value: &str) -> String {
    let normalized = normalize_field_value(value);
    let lines = normalized
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .collect::<Vec<_>>();

    if let Some(first_line) = lines.first() {
        return split_single_line_party_name(first_line);
    }

    String::new()
}

fn split_single_line_party_name(value: &str) -> String {
    let line = value.trim();
    for suffix in [
        " CO., LTD.",
        " CO. LTD.",
        " CO LTD",
        " LTD.",
        " LIMITED",
        " LLC.",
        " LLC",
        " INC.",
        " INC",
        " CORP.",
        " CORP",
        " COMPANY",
    ] {
        let upper = line.to_uppercase();
        if let Some(index) = upper.find(suffix) {
            let split_index = index + suffix.len();
            if split_index < line.len() {
                let rest = line[split_index..].trim_matches([' ', '\t', ',', ';', '，', '；']);
                if looks_like_address_fragment(rest) {
                    return line[..split_index].trim().to_string();
                }
            }
        }
    }

    line.to_string()
}

fn normalize_address_candidate_value(value: &str) -> String {
    let lines = normalize_field_value(value)
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .map(str::to_string)
        .collect::<Vec<_>>();

    if lines.is_empty() {
        return String::new();
    }

    if lines.len() == 1 {
        return split_single_line_party_address(&lines[0]);
    }

    let first_address_line = lines
        .iter()
        .position(|line| looks_like_address_fragment(line))
        .unwrap_or(0);

    let kept_lines = if first_address_line > 0 {
        &lines[first_address_line..]
    } else {
        &lines[..]
    };

    kept_lines.join("\n")
}

fn split_single_line_party_address(value: &str) -> String {
    let line = value.trim();
    let upper = line.to_uppercase();
    for suffix in [
        " CO., LTD.",
        " CO. LTD.",
        " CO LTD",
        " LTD.",
        " LIMITED",
        " LLC.",
        " LLC",
        " INC.",
        " INC",
        " CORP.",
        " CORP",
        " COMPANY",
    ] {
        if let Some(index) = upper.find(suffix) {
            let split_index = index + suffix.len();
            if split_index < line.len() {
                let rest = line[split_index..].trim_matches([' ', '\t', ',', ';', '，', '；']);
                if looks_like_address_fragment(rest) {
                    return rest.to_string();
                }
            }
        }
    }

    if looks_like_address_fragment(line) {
        return line.to_string();
    }

    String::new()
}

fn looks_like_address_fragment(value: &str) -> bool {
    if value.trim().is_empty() {
        return false;
    }

    let normalized = value.to_lowercase();
    normalized.chars().any(|c| c.is_ascii_digit())
        || normalized.contains("road")
        || normalized.contains("rd")
        || normalized.contains("street")
        || normalized.contains("st")
        || normalized.contains("avenue")
        || normalized.contains("ave")
        || normalized.contains("building")
        || normalized.contains("floor")
        || normalized.contains("fl")
        || normalized.contains("china")
        || normalized.contains("usa")
        || normalized.contains("united states")
        || normalized.contains("tel")
        || normalized.contains("mail")
        || normalized.contains("路")
        || normalized.contains("号")
}

fn looks_like_known_document_label(value: &str) -> bool {
    let normalized = normalize_text(value);
    if extra_document_boundary_labels()
        .iter()
        .map(|label| normalize_text(label))
        .any(|label| normalized == label || normalized.starts_with(&label))
    {
        return true;
    }

    document_field_definitions().iter().any(|definition| {
        definition.labels.iter().any(|label| {
            let normalized_label = normalize_text(label);
            normalized == normalized_label || inline_text_after_label(value, label).is_some()
        })
    })
}

fn extra_document_boundary_labels() -> [&'static str; 10] {
    [
        "place of receipt",
        "place of delivery",
        "pre-carriage by",
        "vessel/voyage no.",
        "service code",
        "nos. of original b/l required",
        "quantity & type",
        "description of goods",
        "gross weight",
        "measurement",
    ]
}

fn is_field_boundary_value(value: &str) -> bool {
    looks_like_known_document_label(value)
        || looks_like_service_code_option(value)
        || detect_field(&normalize_text(value)).is_some()
}

fn looks_like_service_code_option(value: &str) -> bool {
    let normalized = normalize_text(value);
    normalized.contains("lclfcl")
        || normalized.contains("fclfcl")
        || normalized.contains("lcllcl")
        || normalized.contains("fcllcl")
        || value.contains('□')
}

fn has_field_boundary_before_column(cells: &[Vec<String>], row: usize, column: usize) -> bool {
    let start_column = column.saturating_sub(3);
    (start_column..column).any(|candidate_column| {
        let value = get_cell(cells, row, candidate_column);
        !value.is_empty() && is_field_boundary_value(value)
    })
}

fn pick_better_field(
    current: Option<DocumentFieldCandidate>,
    candidate: DocumentFieldCandidate,
) -> Option<DocumentFieldCandidate> {
    let Some(current_value) = current else {
        return Some(candidate);
    };

    if candidate.confidence > current_value.confidence
        || (candidate.confidence == current_value.confidence && candidate.row < current_value.row)
    {
        Some(candidate)
    } else {
        Some(current_value)
    }
}

fn normalize_field_value(value: &str) -> String {
    value
        .replace('\u{00a0}', " ")
        .lines()
        .map(|line| line.split_whitespace().collect::<Vec<_>>().join(" "))
        .filter(|line| !line.is_empty())
        .collect::<Vec<_>>()
        .join("\n")
}

fn build_field_candidates(
    cells: &[Vec<String>],
    header_row: usize,
    header_depth: usize,
) -> Vec<FieldCandidate> {
    let mut fields = Vec::new();
    let max_columns = cells
        .iter()
        .skip(header_row)
        .take(header_depth)
        .map(Vec::len)
        .max()
        .unwrap_or(0)
        .min(MAX_PROFILE_COLUMNS);

    let mut current_group = String::new();
    for column in 0..max_columns {
        let mut path = Vec::new();
        for row_offset in 0..header_depth {
            let row = header_row + row_offset;
            let value = get_cell(cells, row, column);
            if !value.is_empty() {
                if row_offset == 0 {
                    current_group = if is_group_header(value) {
                        value.to_string()
                    } else {
                        String::new()
                    };
                }
                path.push(value.to_string());
            } else if row_offset > 0 && !current_group.is_empty() {
                path.push(current_group.clone());
            }
        }

        path.dedup();
        let detected = detect_field_from_path(&path).or_else(|| {
            let joined = normalize_text(&path.join(" "));
            detect_field(&joined)
        });
        if let Some((canonical_field, confidence)) = detected {
            fields.push(FieldCandidate {
                canonical_field,
                column: column + 1,
                header_path: path,
                confidence,
            });
        }
    }

    if !fields
        .iter()
        .any(|field| field.canonical_field == "StyleName")
    {
        if let (Some(style_no), Some(quantity)) = (
            fields
                .iter()
                .find(|field| field.canonical_field == "StyleNo"),
            fields
                .iter()
                .find(|field| field.canonical_field == "Quantity"),
        ) {
            if style_no.column + 1 < quantity.column {
                fields.push(FieldCandidate {
                    canonical_field: "StyleName".to_string(),
                    column: style_no.column + 1,
                    header_path: vec!["inferred detail text".to_string()],
                    confidence: 0.55,
                });
            }
        }
    }

    disambiguate_weight_fields(fields)
}

fn count_detected_fields_in_row(cells: &[Vec<String>], row: usize) -> usize {
    let Some(values) = cells.get(row) else {
        return 0;
    };

    values
        .iter()
        .filter(|value| detect_field(&normalize_text(value)).is_some())
        .count()
}

fn detect_field_from_path(path: &[String]) -> Option<(String, f32)> {
    if let Some(detected) = detect_field_from_header_path_context(path) {
        return Some(detected);
    }

    path.iter()
        .rev()
        .find_map(|value| detect_field(&normalize_text(value)))
}

fn detect_field_from_header_path_context(path: &[String]) -> Option<(String, f32)> {
    if header_path_contains(
        path,
        &[
            "中文品名",
            "中文名称",
            "品名中文",
            "中文描述",
            "报关品名",
            "货物中文名称",
            "中文货物名称",
        ],
    ) {
        return Some(("StyleNameCN".to_string(), 0.86));
    }

    if header_path_contains(path, &["品牌", "品牌名", "商标", "brand", "label"]) {
        return Some(("Brand".to_string(), 0.82));
    }

    if header_path_contains(
        path,
        &[
            "箱子尺寸",
            "箱规",
            "外箱尺寸",
            "包装尺寸",
            "尺寸",
            "长宽高",
            "carton size",
            "ctn size",
            "cartonsize",
            "dimension",
            "dimensions",
        ],
    ) {
        return Some(("Dimension".to_string(), 0.90));
    }

    if header_path_contains(
        path,
        &[
            "客人款号",
            "款号",
            "货号",
            "产品编号",
            "styleno",
            "style no",
            "style no.",
            "style code",
            "sku",
        ],
    ) {
        return Some(("StyleNo".to_string(), 0.92));
    }

    if header_path_contains(
        path,
        &[
            "英文品名",
            "英文名称",
            "货物英文品名",
            "货物名称",
            "商品名称",
            "产品名称",
            "stylename",
            "description",
            "product description",
        ],
    ) {
        return Some(("StyleName".to_string(), 0.88));
    }

    None
}

fn header_path_contains(path: &[String], aliases: &[&str]) -> bool {
    path.iter().any(|value| {
        let normalized_value = normalize_text(value);
        aliases
            .iter()
            .any(|alias| normalized_value == normalize_text(alias))
    })
}

fn disambiguate_weight_fields(mut fields: Vec<FieldCandidate>) -> Vec<FieldCandidate> {
    let mut gross_columns = Vec::new();
    let mut net_columns = Vec::new();

    for (index, field) in fields.iter().enumerate() {
        match field.canonical_field.as_str() {
            "GrossWeight" => gross_columns.push(index),
            "NetWeight" => net_columns.push(index),
            _ => {}
        }
    }

    if gross_columns.len() >= 2 {
        fields[gross_columns[0]].canonical_field = "GWPerCtn".to_string();
        fields[gross_columns[1]].canonical_field = "GWTotal".to_string();
    } else if gross_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "GWTotal")
    {
        fields[gross_columns[0]].canonical_field = "GWPerCtn".to_string();
    }

    if gross_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "GWPerCtn")
    {
        fields[gross_columns[0]].canonical_field = "GWTotal".to_string();
    }

    if net_columns.len() >= 2 {
        fields[net_columns[0]].canonical_field = "NWPerCtn".to_string();
        fields[net_columns[1]].canonical_field = "NWTotal".to_string();
    } else if net_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "NWTotal")
    {
        fields[net_columns[0]].canonical_field = "NWPerCtn".to_string();
    }

    if net_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "NWPerCtn")
    {
        fields[net_columns[0]].canonical_field = "NWTotal".to_string();
    }

    fields
}

fn detect_field(header: &str) -> Option<(String, f32)> {
    let candidates = [
        (
            "PoNumber",
            0.85,
            [
                "客人订单号",
                "客户订单号",
                "订单号",
                "采购订单号",
                "销售订单号",
                "pono",
                "po",
                "po#",
                "purchaseorder",
                "orderno",
                "order",
            ]
            .as_slice(),
        ),
        (
            "StyleNo",
            0.90,
            [
                "客人款号",
                "款号",
                "货号",
                "品号",
                "产品编号",
                "产品货号",
                "商品编号",
                "商品货号",
                "物料号",
                "物料编号",
                "物料编码",
                "零件号",
                "零件编号",
                "部件号",
                "部件编号",
                "配件号",
                "产品型号",
                "款式",
                "型号",
                "款号款名",
                "款名款号",
                "styleno",
                "style#",
                "stylecode",
                "itemno",
                "item#",
                "itemcode",
                "itemnumber",
                "sku",
                "skuno",
                "productcode",
                "productno",
                "productid",
                "partno",
                "partnumber",
                "partcode",
                "partid",
                "materialno",
                "materialcode",
                "materialnumber",
                "componentno",
                "componentcode",
                "goodsno",
                "goodscode",
                "articleno",
                "article",
                "model",
                "modelno",
            ]
            .as_slice(),
        ),
        (
            "StyleNameCN",
            0.80,
            [
                "中文品名",
                "中文名称",
                "品名中文",
                "款式描述",
                "中文描述",
                "报关品名",
                "货物中文名称",
                "中文货物名称",
            ]
            .as_slice(),
        ),
        (
            "StyleName",
            0.85,
            [
                "英文品名",
                "英文名称",
                "品名",
                "名称",
                "货物英文品名",
                "货物名称",
                "货物描述",
                "商品名称",
                "商品描述",
                "产品名称",
                "产品描述",
                "物料名称",
                "物料描述",
                "零件名称",
                "零件描述",
                "部件名称",
                "部件描述",
                "品名规格",
                "规格描述",
                "style",
                "stylename",
                "description",
                "desc",
                "name",
                "product",
                "productname",
                "productdescription",
                "goods",
                "goodsname",
                "goodsdescription",
                "itemname",
                "itemdescription",
                "descriptionofgoods",
                "commodity",
                "commodityname",
                "commoditydescription",
                "materialname",
                "materialdescription",
                "partname",
                "partdescription",
                "componentname",
                "componentdescription",
            ]
            .as_slice(),
        ),
        (
            "FabricComposition",
            0.75,
            [
                "面料",
                "面料成分",
                "成份",
                "成分",
                "材质",
                "fabric",
                "composition",
                "material",
            ]
            .as_slice(),
        ),
        (
            "Brand",
            0.75,
            ["品牌", "品牌名", "商标", "brand", "label"].as_slice(),
        ),
        (
            "HSCode",
            0.90,
            [
                "hscode",
                "hs",
                "hs编码",
                "海关编码",
                "商品编码",
                "商品hs编码",
                "编码",
                "税号",
                "税则号",
                "customscode",
                "commoditycode",
                "tariffcode",
                "tariffno",
                "htscode",
            ]
            .as_slice(),
        ),
        (
            "Origin",
            0.75,
            [
                "原产地",
                "产地",
                "原产国",
                "生产国",
                "制造国",
                "境内货源地",
                "origin",
                "madein",
                "countryoforigin",
                "countryofmanufacture",
                "manufacturingcountry",
            ]
            .as_slice(),
        ),
        (
            "Quantity",
            0.95,
            [
                "数量",
                "总数量",
                "件数",
                "出货数量",
                "装运数量",
                "交货数量",
                "申报数量",
                "quantity",
                "qty",
                "pcs",
                "piece",
                "pieces",
                "qtypcs",
                "pcsqty",
                "totalqty",
                "units",
                "totalunits",
                "shipqty",
                "shippedqty",
                "deliveryqty",
                "exportqty",
                "declaredqty",
                "orderqty",
                "orderedqty",
            ]
            .as_slice(),
        ),
        (
            "UnitEN",
            0.70,
            [
                "单位",
                "数量单位",
                "计量单位",
                "英文单位",
                "unit",
                "uom",
                "unitofmeasure",
                "measureunit",
                "um",
            ]
            .as_slice(),
        ),
        ("UnitCN", 0.70, ["中文单位", "单位中文"].as_slice()),
        (
            "Cartons",
            0.95,
            [
                "箱数",
                "总箱数",
                "箱量",
                "包装件数",
                "包装数量",
                "包装",
                "件数箱数",
                "carton",
                "cartons",
                "ctns",
                "ctn",
                "ctnqty",
                "cartonqty",
                "noofctns",
                "noofcartons",
                "packages",
                "packageqty",
                "packagesqty",
                "numberofpackages",
                "pkg",
                "pkgs",
                "boxes",
                "box",
                "cases",
                "case",
                "pallets",
                "pallet",
            ]
            .as_slice(),
        ),
        (
            "CtnUnitEN",
            0.70,
            ["箱数单位", "ctnunit", "cartonunit"].as_slice(),
        ),
        (
            "Dimension",
            0.85,
            [
                "箱子尺寸",
                "箱规",
                "外箱尺寸",
                "包装尺寸",
                "规格",
                "尺寸",
                "长宽高",
                "cartonsize",
                "ctnsize",
                "cartondimension",
                "cartondimensions",
                "packingsize",
                "packsize",
                "packagedimension",
                "packagedimensions",
                "dimension",
                "dimensions",
                "size",
                "measurement",
            ]
            .as_slice(),
        ),
        ("Length", 0.90, ["长", "长度", "长cm", "length"].as_slice()),
        ("Width", 0.90, ["宽", "宽度", "宽cm", "width"].as_slice()),
        ("Height", 0.90, ["高", "高度", "高cm", "height"].as_slice()),
        (
            "Volume",
            0.95,
            [
                "体积",
                "总体积",
                "体积立方数",
                "立方数",
                "立方米",
                "方数",
                "空间",
                "m3",
                "m³",
                "cbm",
                "cbms",
                "totalcbm",
                "totalcbms",
                "volume",
                "measurement",
                "meas",
            ]
            .as_slice(),
        ),
        (
            "GWPerCtn",
            0.95,
            [
                "毛重箱",
                "毛重每箱",
                "每箱毛重",
                "单箱毛重",
                "毛重ctn",
                "gwctn",
                "gwperctn",
                "gwcarton",
                "gwctns",
                "grossweightctn",
                "grossweightcarton",
                "grossweightpercarton",
            ]
            .as_slice(),
        ),
        (
            "GWTotal",
            0.95,
            [
                "总毛重",
                "合计毛重",
                "毛重合计",
                "总重量",
                "毛重kg",
                "totalgw",
                "gwt",
                "grosskg",
                "grosskgs",
                "gwkg",
                "gwkgs",
                "totalgrossweight",
                "grossweighttotal",
                "grossweightkg",
                "grossweightkgs",
                "grosswt",
                "totalgross",
                "totalgrosskg",
                "totalgrosskgs",
                "totalgwkg",
                "totalgwkgs",
                "totalgweight",
            ]
            .as_slice(),
        ),
        (
            "GrossWeight",
            0.75,
            ["毛重", "gw", "grossweight"].as_slice(),
        ),
        (
            "NWPerCtn",
            0.95,
            [
                "净重箱",
                "净重每箱",
                "每箱净重",
                "单箱净重",
                "净重ctn",
                "nwctn",
                "nwperctn",
                "nwcarton",
                "nwctns",
                "netweightctn",
                "netweightcarton",
                "netweightpercarton",
            ]
            .as_slice(),
        ),
        (
            "NWTotal",
            0.95,
            [
                "总净重",
                "合计净重",
                "净重合计",
                "净重kg",
                "totalnw",
                "nwt",
                "netkg",
                "netkgs",
                "nwkg",
                "nwkgs",
                "totalnetweight",
                "netweighttotal",
                "netweightkg",
                "netweightkgs",
                "netwt",
                "totalnet",
                "totalnetkg",
                "totalnetkgs",
                "totalnwkg",
                "totalnwkgs",
                "totalnweight",
            ]
            .as_slice(),
        ),
        ("NetWeight", 0.75, ["净重", "nw", "netweight"].as_slice()),
        (
            "UnitPrice",
            0.90,
            [
                "单价",
                "单价usd",
                "销售单价",
                "报关单价",
                "申报单价",
                "fob价",
                "unitprice",
                "unitpriceusd",
                "unitvalue",
                "unitvalueusd",
                "unitamount",
                "unitcost",
                "price",
                "priceusd",
                "priceperunit",
                "fobusd",
                "uprice",
                "customsunitprice",
                "declaredunitprice",
            ]
            .as_slice(),
        ),
        (
            "TotalPrice",
            0.95,
            [
                "总价",
                "金额",
                "金额usd",
                "总金额",
                "合计金额",
                "货值",
                "申报总价",
                "申报金额",
                "小计",
                "amount",
                "amountusd",
                "lineamount",
                "linevalue",
                "itemamount",
                "goodsvalue",
                "customsvalue",
                "declaredvalue",
                "exportamount",
                "invoiceamount",
                "total",
                "totalprice",
                "totalamount",
                "totalvalue",
                "subtotal",
                "value",
            ]
            .as_slice(),
        ),
    ];

    for (field, confidence, aliases) in candidates {
        if aliases
            .iter()
            .any(|alias| header_matches_alias(header, alias))
        {
            return Some((field.to_string(), confidence));
        }
    }

    None
}

fn header_matches_alias(header: &str, alias: &str) -> bool {
    let normalized_alias = normalize_text(alias);
    if normalized_alias.is_empty() {
        return false;
    }

    if normalized_alias.len() <= 2 || alias_requires_exact_match(&normalized_alias) {
        return header == normalized_alias;
    }

    header.contains(&normalized_alias)
}

fn alias_requires_exact_match(alias: &str) -> bool {
    matches!(
        alias,
        "order"
            | "unit"
            | "units"
            | "price"
            | "value"
            | "total"
            | "product"
            | "goods"
            | "name"
            | "box"
            | "case"
    )
}

fn is_group_header(value: &str) -> bool {
    let normalized = normalize_text(value);
    ["毛重", "净重", "体积", "体积立方数", "立方数"]
        .iter()
        .any(|candidate| normalized.contains(&normalize_text(candidate)))
}

fn find_first_data_row(
    cells: &[Vec<String>],
    start_row: usize,
    fields: &[FieldCandidate],
) -> Option<usize> {
    let quantity_column = fields
        .iter()
        .find(|field| field.canonical_field == "Quantity")
        .map(|field| field.column - 1)?;

    let style_columns: Vec<usize> = fields
        .iter()
        .filter(|field| field.canonical_field == "StyleNo" || field.canonical_field == "StyleName")
        .map(|field| field.column - 1)
        .collect();

    for row in start_row..cells.len().min(start_row + 40) {
        let quantity = get_cell(cells, row, quantity_column);
        if parse_excel_decimal(quantity).is_none() {
            continue;
        }

        if style_columns.iter().any(|column| {
            let value = get_cell(cells, row, *column);
            !value.is_empty() && !is_summary_or_note_row(value)
        }) {
            return Some(row);
        }
    }

    None
}

fn is_summary_or_note_row(value: &str) -> bool {
    let normalized = normalize_text(value);
    normalized.contains("合计")
        || normalized.contains("总计")
        || normalized.contains("小计")
        || normalized.contains("total")
        || normalized.contains("subtotal")
        || normalized.contains("唛头")
        || normalized.contains("shippingmark")
}

fn collect_sample_rows(
    cells: &[Vec<String>],
    start_row: usize,
    fields: &[FieldCandidate],
) -> Vec<SampleRow> {
    let mut rows = Vec::new();
    for row in start_row..cells.len().min(start_row + 8) {
        if fields
            .iter()
            .any(|field| is_summary_or_note_row(get_cell(cells, row, field.column - 1)))
        {
            break;
        }

        let mut values = Vec::new();
        for field in fields {
            let raw = get_cell(cells, row, field.column - 1).to_string();
            if raw.is_empty() {
                continue;
            }

            values.push(SampleValue {
                canonical_field: field.canonical_field.clone(),
                raw: raw.clone(),
                normalized_decimal: parse_excel_decimal(&raw),
                normalized_dimension: parse_dimension(&raw),
            });
        }

        if !values.is_empty() {
            rows.push(SampleRow {
                row: row + 1,
                values,
            });
        }
    }

    rows
}

fn get_cell(cells: &[Vec<String>], row: usize, column: usize) -> &str {
    cells
        .get(row)
        .and_then(|values| values.get(column))
        .map(|value| value.trim())
        .unwrap_or("")
}

fn cell_to_string(cell: &Data) -> String {
    match cell {
        Data::Empty => String::new(),
        Data::String(value) => value.trim().to_string(),
        Data::Float(value) => trim_number(*value),
        Data::Int(value) => value.to_string(),
        Data::Bool(value) => value.to_string(),
        Data::DateTime(value) => trim_number(value.as_f64()),
        Data::DateTimeIso(value) => value.clone(),
        Data::DurationIso(value) => value.clone(),
        Data::Error(value) => format!("{value:?}"),
    }
}

fn trim_number(value: f64) -> String {
    if value.fract().abs() < f64::EPSILON {
        format!("{}", value as i64)
    } else {
        let text = format!("{value:.6}");
        text.trim_end_matches('0').trim_end_matches('.').to_string()
    }
}

fn parse_excel_decimal(text: &str) -> Option<f64> {
    let mut normalized = text
        .trim()
        .replace('\u{00a0}', " ")
        .replace('，', ",")
        .replace('．', ".")
        .replace('－', "-")
        .replace('（', "(")
        .replace('）', ")");

    if normalized.is_empty() {
        return None;
    }

    let negative = normalized.starts_with('(') && normalized.ends_with(')');
    normalized = normalized
        .chars()
        .filter(|c| c.is_ascii_digit() || matches!(c, '.' | ',' | '-' | '(' | ')'))
        .collect::<String>()
        .trim_matches(['(', ')'])
        .replace(',', "");

    if !normalized.chars().any(|c| c.is_ascii_digit()) {
        return None;
    }

    normalized
        .parse::<f64>()
        .ok()
        .map(|value| if negative { -value } else { value })
}

fn parse_dimension(text: &str) -> Option<DimensionValue> {
    let trimmed = text.trim();
    let digits_only: String = trimmed.chars().filter(|c| c.is_ascii_digit()).collect();
    if trimmed == digits_only && digits_only.len() == 6 {
        return Some(DimensionValue {
            length: digits_only[0..2].parse().ok()?,
            width: digits_only[2..4].parse().ok()?,
            height: digits_only[4..6].parse().ok()?,
        });
    }

    let mut numbers = Vec::new();
    let mut current = String::new();
    for c in trimmed.chars() {
        if c.is_ascii_digit() || c == '.' {
            current.push(c);
        } else if !current.is_empty() {
            numbers.push(current.clone());
            current.clear();
        }
    }
    if !current.is_empty() {
        numbers.push(current);
    }

    if numbers.len() == 3 {
        return Some(DimensionValue {
            length: numbers[0].parse().ok()?,
            width: numbers[1].parse().ok()?,
            height: numbers[2].parse().ok()?,
        });
    }

    None
}

fn normalize_text(value: &str) -> String {
    value
        .chars()
        .filter(|c| c.is_alphanumeric() || is_cjk(*c))
        .flat_map(char::to_lowercase)
        .collect()
}

fn is_cjk(value: char) -> bool {
    ('\u{4e00}'..='\u{9fff}').contains(&value)
}

#[derive(Serialize)]
struct AnalysisReport {
    schema_version: String,
    analyzer_id: String,
    source_path: String,
    selected_worksheet_name: String,
    confidence: f32,
    fields: Vec<DocumentFieldCandidate>,
    issues: Vec<AnalysisIssue>,
    sheets: Vec<SheetAnalysis>,
}

#[derive(Serialize)]
struct SheetAnalysis {
    name: String,
    used_range: UsedRange,
    confidence: f32,
    field_candidates: Vec<DocumentFieldCandidate>,
    table: Option<TableAnalysis>,
}

#[derive(Serialize)]
struct UsedRange {
    first_row: usize,
    first_column: usize,
    last_row: usize,
    last_column: usize,
}

#[derive(Serialize)]
struct TableAnalysis {
    header_start_row: usize,
    header_depth: usize,
    data_start_row: usize,
    confidence: f32,
    fields: Vec<FieldCandidate>,
    sample_rows: Vec<SampleRow>,
}

#[derive(Clone, Serialize)]
struct FieldCandidate {
    canonical_field: String,
    column: usize,
    header_path: Vec<String>,
    confidence: f32,
}

#[derive(Clone, Serialize)]
struct DocumentFieldCandidate {
    field_key: String,
    display_name: String,
    value: String,
    worksheet_name: String,
    row: usize,
    column: usize,
    confidence: f32,
    source: String,
}

#[derive(Serialize)]
struct AnalysisIssue {
    severity: String,
    code: String,
    message: String,
    field_key: String,
}

struct DocumentFieldDefinition {
    field_key: &'static str,
    display_name: &'static str,
    labels: &'static [&'static str],
    multi_line: bool,
    prefer_below: bool,
}

impl DocumentFieldDefinition {
    fn new(
        field_key: &'static str,
        display_name: &'static str,
        labels: &'static [&'static str],
    ) -> Self {
        Self {
            field_key,
            display_name,
            labels,
            multi_line: false,
            prefer_below: false,
        }
    }

    fn multi(
        field_key: &'static str,
        display_name: &'static str,
        labels: &'static [&'static str],
    ) -> Self {
        Self {
            field_key,
            display_name,
            labels,
            multi_line: true,
            prefer_below: false,
        }
    }

    fn below(
        field_key: &'static str,
        display_name: &'static str,
        labels: &'static [&'static str],
    ) -> Self {
        Self {
            field_key,
            display_name,
            labels,
            multi_line: true,
            prefer_below: true,
        }
    }
}

#[derive(Default)]
struct NearbyValue {
    value: String,
    row: usize,
    column: usize,
    score: f32,
}

#[derive(Serialize)]
struct SampleRow {
    row: usize,
    values: Vec<SampleValue>,
}

#[derive(Serialize)]
struct SampleValue {
    canonical_field: String,
    raw: String,
    normalized_decimal: Option<f64>,
    normalized_dimension: Option<DimensionValue>,
}

#[derive(Serialize)]
struct DimensionValue {
    length: f64,
    width: f64,
    height: f64,
}

#[cfg(test)]
mod tests {
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
}
