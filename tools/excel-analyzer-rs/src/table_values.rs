use super::*;

pub(super) fn get_cell(cells: &[Vec<String>], row: usize, column: usize) -> &str {
    cells
        .get(row)
        .and_then(|values| values.get(column))
        .map(|value| value.trim())
        .unwrap_or("")
}

pub(super) fn cell_to_string(cell: &Data) -> String {
    match cell {
        Data::Empty => String::new(),
        Data::String(value) => bounded_cell_text(value),
        Data::Float(value) => trim_number(*value),
        Data::Int(value) => value.to_string(),
        Data::Bool(value) => value.to_string(),
        Data::DateTime(value) => trim_number(value.as_f64()),
        Data::DateTimeIso(value) | Data::DurationIso(value) => bounded_cell_text(value),
        Data::Error(value) => format!("{value:?}"),
    }
}

fn bounded_cell_text(value: &str) -> String {
    value.trim().chars().take(MAX_CELL_CHARACTERS).collect()
}

fn trim_number(value: f64) -> String {
    if value.fract().abs() < f64::EPSILON {
        format!("{}", value as i64)
    } else {
        format!("{value:.6}")
            .trim_end_matches('0')
            .trim_end_matches('.')
            .to_string()
    }
}

pub(super) fn parse_excel_decimal(text: &str) -> Option<f64> {
    let normalized = text
        .trim()
        .replace('\u{00a0}', " ")
        .replace('，', ",")
        .replace('．', ".")
        .replace('－', "-")
        .replace('（', "(")
        .replace('）', ")");
    let negative = normalized.starts_with('(') && normalized.ends_with(')');
    let number = normalized
        .chars()
        .filter(|c| c.is_ascii_digit() || matches!(c, '.' | ',' | '-' | '(' | ')'))
        .collect::<String>()
        .trim_matches(['(', ')'])
        .replace(',', "");
    number
        .chars()
        .any(|c| c.is_ascii_digit())
        .then(|| number.parse::<f64>().ok())
        .flatten()
        .map(|value| if negative { -value } else { value })
}

pub(super) fn parse_dimension(text: &str) -> Option<DimensionValue> {
    let trimmed = text.trim();
    let digits: String = trimmed.chars().filter(char::is_ascii_digit).collect();
    let numbers = if trimmed == digits && digits.len() == 6 {
        vec![&digits[0..2], &digits[2..4], &digits[4..6]]
    } else {
        trimmed
            .split(|c: char| !c.is_ascii_digit() && c != '.')
            .filter(|value| !value.is_empty())
            .collect()
    };
    if numbers.len() != 3 {
        return None;
    }
    Some(DimensionValue {
        length: numbers[0].parse().ok()?,
        width: numbers[1].parse().ok()?,
        height: numbers[2].parse().ok()?,
    })
}

pub(super) fn normalize_text(value: &str) -> String {
    value
        .chars()
        .filter(|c| c.is_alphanumeric() || is_cjk(*c))
        .flat_map(char::to_lowercase)
        .collect()
}

pub(super) fn is_cjk(value: char) -> bool {
    ('\u{4e00}'..='\u{9fff}').contains(&value)
}
