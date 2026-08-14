use super::*;

pub(super) fn analyze_workbook(path: PathBuf) -> Result<AnalysisReport, String> {
    let metadata = fs::metadata(&path)
        .map_err(|error| format!("无法读取 Excel 文件 '{}': {error}", path.display()))?;
    if !metadata.is_file() || metadata.len() == 0 || metadata.len() > MAX_WORKBOOK_BYTES {
        return Err("Excel 文件必须存在、不能为空且不能超过 25 MB。".to_string());
    }
    let mut workbook = open_workbook_auto(&path)
        .map_err(|error| format!("无法打开 Excel 文件 '{}': {error}", path.display()))?;

    let sheet_names = workbook.sheet_names().to_owned();
    if sheet_names.len() > MAX_WORKSHEETS {
        return Err(format!("Excel 工作表数量不能超过 {MAX_WORKSHEETS} 个。"));
    }
    let mut sheets = Vec::new();
    let mut remaining_text_characters = MAX_PROFILE_TEXT_CHARACTERS;
    for sheet_name in sheet_names {
        let Ok(range) = workbook.worksheet_range(&sheet_name) else {
            continue;
        };

        let cells = collect_cells(&range, &mut remaining_text_characters)?;
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

pub(super) fn collect_cells(
    range: &calamine::Range<Data>,
    remaining_text_characters: &mut usize,
) -> Result<Vec<Vec<String>>, String> {
    let mut rows = Vec::new();
    for row in range.rows().take(MAX_PROFILE_ROWS) {
        let mut values = Vec::new();
        for cell in row.iter().take(MAX_PROFILE_COLUMNS) {
            let value = cell_to_string(cell);
            let character_count = value.chars().count();
            if character_count > *remaining_text_characters {
                return Err(format!(
                    "Excel 分析文本超过 {MAX_PROFILE_TEXT_CHARACTERS} 个字符，请精简工作簿后重试。"
                ));
            }
            *remaining_text_characters -= character_count;
            values.push(value);
        }
        rows.push(values);
    }
    Ok(rows)
}

pub(super) fn detect_used_range(cells: &[Vec<String>]) -> UsedRange {
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

pub(super) fn detect_table(cells: &[Vec<String>]) -> Option<TableAnalysis> {
    let mut best: Option<TableAnalysis> = None;
    let mut best_quality = f32::MIN;

    for header_row in 0..cells.len().min(80) {
        if count_detected_fields_in_row(cells, header_row) == 0 {
            continue;
        }

        let max_depth = 3usize.min(cells.len().saturating_sub(header_row));
        for header_depth in 1..=max_depth {
            let columns = build_field_candidates(cells, header_row, header_depth);
            let mut unique_fields = std::collections::BTreeMap::new();
            for field in &columns {
                unique_fields
                    .entry(field.canonical_field.as_str())
                    .and_modify(|confidence: &mut f32| {
                        *confidence = confidence.max(field.confidence)
                    })
                    .or_insert(field.confidence);
            }

            let has_quantity = unique_fields.contains_key("Quantity");
            let has_style =
                unique_fields.contains_key("StyleNo") || unique_fields.contains_key("StyleName");
            let score: f32 = unique_fields.values().sum();
            if unique_fields.len() < 3 || !has_quantity || !has_style || score < 2.5 {
                continue;
            }

            let search_start_row = header_row + header_depth;
            let Some(data_start_row) = find_first_data_row(cells, search_start_row, &columns)
            else {
                continue;
            };

            let duplicate_count = columns.len().saturating_sub(unique_fields.len()) as f32;
            let data_gap = data_start_row.saturating_sub(search_start_row) as f32;
            let quality = score - duplicate_count * 0.75 - data_gap * 0.10;
            let sample_rows = collect_sample_rows(cells, data_start_row, &columns);
            let candidate = TableAnalysis {
                header_start_row: header_row + 1,
                header_depth,
                data_start_row: data_start_row + 1,
                confidence: (score / 8.0).min(1.0),
                fields: columns,
                sample_rows,
            };

            let should_replace = quality > best_quality
                || ((quality - best_quality).abs() < f32::EPSILON
                    && best
                        .as_ref()
                        .map(|current| candidate.data_start_row < current.data_start_row)
                        .unwrap_or(true));
            if should_replace {
                best_quality = quality;
                best = Some(candidate);
            }
        }
    }

    best
}

pub(super) fn detect_document_fields(
    cells: &[Vec<String>],
    sheet_name: &str,
) -> Vec<DocumentFieldCandidate> {
    let definitions = document_field_definitions();
    let mut fields = Vec::new();

    for definition in definitions {
        if let Some(candidate) = find_document_field(cells, sheet_name, &definition) {
            fields.push(candidate);
        }
    }

    fields
}

pub(super) fn find_document_field(
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
                if value.is_empty() || !is_plausible_party_name_candidate(&value) {
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

pub(super) fn sheet_confidence(
    fields: &[DocumentFieldCandidate],
    table: Option<&TableAnalysis>,
) -> f32 {
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

pub(super) fn build_issues(
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
