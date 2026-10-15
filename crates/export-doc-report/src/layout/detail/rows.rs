use super::{CellStyle, Row, RowKind};
use crate::{
    ReportData, Result,
    error::invalid,
    layout::{detail_content, measured_wrap},
};
use export_doc_domain::designer::{
    DetailGroupFooter, DetailGroupFooterCell, DetailSummaryCell, DetailSummaryRow, DetailTable,
};
use rust_decimal::Decimal;
use serde_json::Value;
use std::collections::BTreeMap;

pub(super) fn detail_rows(
    table: &DetailTable,
    data: &ReportData,
    widths: &[f32],
    size: f32,
) -> Result<Vec<Row>> {
    let mut rows = Vec::new();
    let mut current_group = None::<String>;
    let mut group_sums = empty_group_sums(table);
    let mut group_count = 0usize;
    let style = CellStyle::new(&table.body_style, size, false);
    for item in data.items() {
        let group = table
            .grouping
            .as_ref()
            .map(|grouping| data.item_text(item, field_name(&grouping.field_path)))
            .unwrap_or_default();
        if table.grouping.is_some()
            && current_group.as_deref() != Some(group.as_str())
            && !group.is_empty()
        {
            if let Some(previous) = current_group.take() {
                if let Some(grouping) = &table.grouping {
                    if let Some(footer) = &grouping.footer {
                        rows.push(group_footer_row(
                            table,
                            footer,
                            &previous,
                            &group_sums,
                            group_count,
                            widths,
                            size,
                        )?);
                    }
                }
            }
            current_group = Some(group.clone());
            group_sums = empty_group_sums(table);
            group_count = 0;
            if let Some(grouping) = &table.grouping {
                let label = if grouping.show_field_value {
                    format!("{} {}", grouping.label, group)
                } else {
                    grouping.label.clone()
                };
                rows.push(group_row_value(label, size, grouping.page_break_before));
            }
        }
        let composed: Vec<_> = table
            .columns
            .iter()
            .zip(widths)
            .map(|(column, width)| {
                if column.content_kind == "Composite" && !column.content.is_empty() {
                    detail_content::compose(
                        &column.content,
                        data,
                        item,
                        width - style.left - style.right,
                        size,
                        style.bold,
                        column.omit_empty_lines,
                        style.vertical_factor,
                    )
                } else {
                    Ok(None)
                }
            })
            .collect::<Result<Vec<_>>>()?;
        let cells: Vec<Vec<String>> = table
            .columns
            .iter()
            .zip(widths)
            .zip(&composed)
            .map(|((column, width), composed)| {
                if composed.is_some() {
                    return vec![String::new()];
                }
                let text = column_text(column, data, item);
                style.wrap(&text, *width)
            })
            .collect();
        for cell in group_sum_cells(table) {
            if let Some(total) = group_sums.get_mut(&cell.column_id) {
                add_sum(total, data.value(&cell.field_path, Some(item)))?;
            }
        }
        group_count += 1;
        let height = composed
            .iter()
            .flatten()
            .map(|cell| cell.height + style.top + style.bottom)
            .fold(style.height(&cells), f32::max);
        rows.push(Row {
            cells,
            composed,
            height,
            kind: RowKind::Data,
            page_break_before: false,
        });
    }
    if let Some(previous) = current_group {
        if let Some(grouping) = &table.grouping {
            if let Some(footer) = &grouping.footer {
                rows.push(group_footer_row(
                    table,
                    footer,
                    &previous,
                    &group_sums,
                    group_count,
                    widths,
                    size,
                )?);
            }
        }
    }
    Ok(rows)
}

fn column_text(
    column: &export_doc_domain::designer::DetailColumn,
    data: &ReportData,
    item: &Value,
) -> String {
    if column.content_kind == "Composite" && !column.content.is_empty() {
        return column
            .content
            .iter()
            .filter(|part| part.visible != Some(false))
            .map(|part| match part.kind.as_str() {
                "Text" => part.text.clone(),
                "LineBreak" => "\n".into(),
                _ => data.display(&part.field_path, Some(item)),
            })
            .collect();
    }
    data.display(&column.field_path, Some(item))
}

fn group_row_value(label: String, size: f32, page_break_before: bool) -> Row {
    Row {
        cells: vec![vec![label]],
        composed: vec![],
        height: size * 1.35 + 4.,
        kind: RowKind::Group,
        page_break_before,
    }
}

fn group_footer_row(
    table: &DetailTable,
    footer: &DetailGroupFooter,
    group: &str,
    sums: &BTreeMap<String, Decimal>,
    count: usize,
    widths: &[f32],
    size: f32,
) -> Result<Row> {
    let mut cells = Vec::with_capacity(table.columns.len());
    for (index, column) in table.columns.iter().enumerate() {
        let value = footer
            .cells
            .iter()
            .find(|cell| cell.column_id == column.id)
            .map(|cell| footer_cell(cell, sums, count))
            .unwrap_or_default();
        cells.push(measured_wrap(
            &value,
            (widths[index] - 3.).max(size),
            "Noto Sans CJK SC",
            false,
            size,
        ));
    }
    if let Some(first) = cells.first_mut() {
        first.insert(0, format!("{} {}", footer.label, group));
    }
    Ok(Row {
        height: cells.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.,
        cells,
        kind: RowKind::Fixed(footer.style.clone(), None),
        composed: vec![],
        page_break_before: false,
    })
}

fn footer_cell(
    cell: &DetailGroupFooterCell,
    sums: &BTreeMap<String, Decimal>,
    count: usize,
) -> String {
    match cell.content_kind.as_str() {
        "Text" => cell.text.clone(),
        "Count" => count.to_string(),
        "Sum" => sums
            .get(&cell.column_id)
            .map(format_decimal)
            .unwrap_or_else(|| "0".into()),
        _ => String::new(),
    }
}

pub(super) fn summary_row(
    table: &DetailTable,
    summary: &DetailSummaryRow,
    data: &ReportData,
    widths: &[f32],
    size: f32,
) -> Result<Row> {
    let style = CellStyle::new(&summary.style, size, false);
    let mut cells = Vec::with_capacity(table.columns.len());
    for (index, column) in table.columns.iter().enumerate() {
        let cell = summary
            .cells
            .iter()
            .find(|cell| cell.column_id == column.id);
        let value = cell
            .map(|cell| summary_cell(cell, data))
            .unwrap_or_default();
        cells.push(style.wrap(&value, widths[index]));
    }
    if let Some(first) = cells.first_mut() {
        if !summary.label.is_empty() {
            first.insert(0, summary.label.clone());
        }
    }
    Ok(Row {
        height: style.height(&cells),
        cells,
        kind: RowKind::Fixed(summary.style.clone(), summary.border.clone()),
        composed: vec![],
        page_break_before: false,
    })
}

fn summary_cell(cell: &DetailSummaryCell, data: &ReportData) -> String {
    match cell.content_kind.as_str() {
        "Text" => cell.text.clone(),
        "Field" => {
            let value = data.field_text(&cell.field_path, None, cell.field_format.as_ref());
            if value.is_empty() {
                value
            } else {
                format!("{value}{}", cell.suffix)
            }
        }
        _ => String::new(),
    }
}

fn field_name(path: &str) -> &str {
    path.rsplit('.').next().unwrap_or(path)
}

fn decimal(value: &Value) -> Result<Decimal> {
    if value.is_null() || value.as_str() == Some("") {
        return Ok(Decimal::ZERO);
    }
    serde_json::from_value(value.clone()).map_err(|_| invalid("报表金额或数量不是有效十进制数。"))
}

fn format_decimal(value: &Decimal) -> String {
    value.round_dp(2).normalize().to_string()
}

fn empty_group_sums(table: &DetailTable) -> BTreeMap<String, Decimal> {
    group_sum_cells(table)
        .map(|cell| (cell.column_id.clone(), Decimal::ZERO))
        .collect()
}

fn group_sum_cells(table: &DetailTable) -> impl Iterator<Item = &DetailGroupFooterCell> {
    table
        .grouping
        .as_ref()
        .and_then(|grouping| grouping.footer.as_ref())
        .into_iter()
        .flat_map(|footer| footer.cells.iter())
        .filter(|cell| cell.content_kind == "Sum")
}

fn add_sum(total: &mut Decimal, value: &Value) -> Result<()> {
    let number = decimal(value)?;
    *total = total
        .checked_add(number)
        .ok_or_else(|| invalid("报表分组小计超出有效十进制范围。"))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn subtotal_rejects_malformed_values_and_overflow_without_changing_the_sum() {
        let mut sum = Decimal::ONE;
        assert!(add_sum(&mut sum, &serde_json::json!("not a number")).is_err());
        assert_eq!(sum, Decimal::ONE);
        sum = Decimal::MAX;
        assert!(add_sum(&mut sum, &serde_json::json!("1")).is_err());
        assert_eq!(sum, Decimal::MAX);
        add_sum(&mut sum, &Value::Null).unwrap();
        assert_eq!(sum, Decimal::MAX);
    }
}
