use super::*;

pub(super) fn document_field_definitions() -> Vec<DocumentFieldDefinition> {
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

pub(super) fn find_nearby_value(
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

pub(super) fn find_best_below_value(
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

pub(super) fn should_probe_below_neighbor_column(
    cells: &[Vec<String>],
    row: usize,
    column: usize,
) -> bool {
    let neighbor_header = get_cell(cells, row, column + 1);
    neighbor_header.is_empty()
        || (!is_field_boundary_value(neighbor_header)
            && !looks_like_sequence_header(neighbor_header))
}

pub(super) fn looks_like_sequence_header(value: &str) -> bool {
    matches!(
        normalize_text(value).as_str(),
        "序号" | "编号" | "行号" | "no" | "number" | "serialno" | "serialnumber" | "itemno"
    )
}

pub(super) fn find_same_row_values(
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

pub(super) fn find_below_values(
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

pub(super) fn has_field_boundary_between(
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

pub(super) fn score_value_completeness(value: &str, multi_line: bool) -> f32 {
    if value.trim().is_empty() {
        return 0.0;
    }

    if !multi_line {
        return if value.chars().count() >= 3 { 2.0 } else { 0.0 };
    }

    let line_count = value.lines().filter(|line| !line.trim().is_empty()).count() as f32;
    (line_count * 1.5).min(6.0)
}

pub(super) fn collect_vertical_block(
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

pub(super) fn match_document_label(value: &str, labels: &[&str]) -> Option<(f32, &'static str)> {
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

pub(super) fn is_address_label_for_different_field(
    value: &str,
    definition: &DocumentFieldDefinition,
) -> bool {
    if definition.field_key.contains("Address") {
        return false;
    }

    let normalized = normalize_text(value);
    normalized.contains("address") || normalized.contains("地址")
}

pub(super) fn extract_inline_value(value: &str, labels: &[&str]) -> String {
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

pub(super) fn inline_text_after_label<'a>(value: &'a str, label: &str) -> Option<&'a str> {
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

pub(super) fn is_single_word_ascii_label(label: &str) -> bool {
    label.is_ascii() && !label.chars().any(char::is_whitespace) && !label.contains('/')
}

pub(super) fn looks_like_code_value(value: &str) -> bool {
    value.contains('-')
        && value.chars().any(|c| c.is_ascii_digit())
        && !value.contains(':')
        && !value.contains('：')
}

pub(super) fn is_generic_placeholder_value(value: &str) -> bool {
    matches!(
        normalize_text(value).as_str(),
        "name" | "address" | "名称" | "地址"
    ) || looks_like_role_assistive_text(value)
}

pub(super) fn looks_like_role_assistive_text(value: &str) -> bool {
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

pub(super) fn looks_like_business_party_value(value: &str) -> bool {
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

pub(super) fn is_same_or_near_short_label(value: &str, label: &str) -> bool {
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

pub(super) fn levenshtein_distance_at_most_one(left: &str, right: &str) -> bool {
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

pub(super) fn is_party_name_field(field_key: &str) -> bool {
    matches!(
        field_key,
        "ExporterNameEN" | "CustomerNameEN" | "NotifyPartyName"
    )
}

pub(super) fn normalize_party_name_candidate_value(value: &str) -> String {
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

pub(super) fn split_single_line_party_name(value: &str) -> String {
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

pub(super) fn normalize_address_candidate_value(value: &str) -> String {
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

pub(super) fn split_single_line_party_address(value: &str) -> String {
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

pub(super) fn looks_like_address_fragment(value: &str) -> bool {
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

pub(super) fn looks_like_known_document_label(value: &str) -> bool {
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

pub(super) fn extra_document_boundary_labels() -> [&'static str; 10] {
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

pub(super) fn is_field_boundary_value(value: &str) -> bool {
    looks_like_known_document_label(value)
        || looks_like_service_code_option(value)
        || detect_field(&normalize_text(value)).is_some()
}

pub(super) fn looks_like_service_code_option(value: &str) -> bool {
    let normalized = normalize_text(value);
    normalized.contains("lclfcl")
        || normalized.contains("fclfcl")
        || normalized.contains("lcllcl")
        || normalized.contains("fcllcl")
        || value.contains('□')
}

pub(super) fn has_field_boundary_before_column(
    cells: &[Vec<String>],
    row: usize,
    column: usize,
) -> bool {
    let start_column = column.saturating_sub(3);
    (start_column..column).any(|candidate_column| {
        let value = get_cell(cells, row, candidate_column);
        !value.is_empty() && is_field_boundary_value(value)
    })
}

pub(super) fn pick_better_field(
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

pub(super) fn normalize_field_value(value: &str) -> String {
    value
        .replace('\u{00a0}', " ")
        .lines()
        .map(|line| line.split_whitespace().collect::<Vec<_>>().join(" "))
        .filter(|line| !line.is_empty())
        .collect::<Vec<_>>()
        .join("\n")
}
