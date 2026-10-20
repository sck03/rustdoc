use super::{CellStyle, Row, RowKind};
use crate::{ReportData, Result, error::invalid, layout::detail_content};
use export_doc_domain::designer::{
    DetailGroupFooter, DetailGroupFooterCell, DetailSummaryCell, DetailSummaryRow, DetailTable,
    ReportBorderStyle, ReportTextStyle,
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
    if data.root["Invoice"]["useEntryPrintRows"] == true
        && table.entry_description_column_id.is_some()
    {
        return super::entry_rows::prepare(table, data, widths, size);
    }
    let mut rows = Vec::new();
    let mut current_group = None::<String>;
    let mut group_sums = empty_group_sums(table);
    let sum_cells: Vec<_> = group_sum_cells(table).collect();
    let mut group_count = 0usize;
    for item in data.items() {
        let group = table
            .grouping
            .as_ref()
            .map(|grouping| data.item_text(item, field_name(&grouping.field_path)))
            .unwrap_or_default();
        if table.grouping.is_some() && current_group.as_deref() != Some(group.as_str()) {
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
            group_sums.values_mut().for_each(|sum| *sum = Decimal::ZERO);
            group_count = 0;
            if let Some(grouping) = &table.grouping {
                let label = if grouping.show_field_value && !group.is_empty() {
                    format!("{} {}", grouping.label, group)
                } else {
                    grouping.label.clone()
                };
                rows.push(group_row_value(
                    label,
                    &grouping.style,
                    widths.iter().sum(),
                    size,
                    grouping.page_break_before,
                ));
            }
        }
        let row = item_row(table, data, item, widths, size, None)?;
        for cell in &sum_cells {
            if let Some(total) = group_sums.get_mut(&cell.column_id) {
                add_sum(total, data.value(&cell.field_path, Some(item)))?;
            }
        }
        group_count += 1;
        rows.push(row);
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

pub(super) fn item_row(
    table: &DetailTable,
    data: &ReportData,
    item: &Value,
    widths: &[f32],
    size: f32,
    description: Option<(usize, &str)>,
) -> Result<Row> {
    let style = CellStyle::new(&table.body_style, size, false);
    let composed: Vec<_> = table
        .columns
        .iter()
        .zip(widths)
        .enumerate()
        .map(|(index, (column, width))| {
            if description.is_none_or(|(selected, _)| selected != index)
                && column.content_kind == "Composite"
                && !column.content.is_empty()
            {
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
        .enumerate()
        .map(|(index, ((column, width), composed))| {
            if let Some((selected, text)) = description {
                if selected == index {
                    return style.wrap(text, *width);
                }
            }
            if composed.is_some() {
                return vec![String::new()];
            }
            let text = column_text(column, data, item);
            style.wrap(&text, *width)
        })
        .collect();
    let height = composed
        .iter()
        .flatten()
        .map(|cell| cell.height + style.top + style.bottom)
        .fold(style.height(&cells), f32::max);
    Ok(Row {
        cells,
        composed,
        height,
        kind: RowKind::Data,
        page_break_before: false,
    })
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

fn group_row_value(
    label: String,
    text_style: &ReportTextStyle,
    width: f32,
    size: f32,
    page_break_before: bool,
) -> Row {
    let style = CellStyle::new(text_style, size, true);
    let cells = vec![style.wrap(&label, width)];
    Row {
        height: style.height(&cells),
        cells,
        composed: vec![],
        kind: RowKind::Group(text_style.clone()),
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
    let cells: BTreeMap<_, _> = footer
        .cells
        .iter()
        .map(|cell| (cell.column_id.as_str(), cell))
        .collect();
    let values = table
        .columns
        .iter()
        .map(|column| {
            cells
                .get(column.id.as_str())
                .map(|cell| footer_cell(cell, sums, count))
                .unwrap_or_default()
        })
        .collect();
    let label = if group.is_empty() {
        footer.label.clone()
    } else {
        format!("{} {group}", footer.label)
    };
    Ok(fixed_row(
        values,
        label,
        footer.label_column_span,
        &footer.style,
        None,
        widths,
        size,
    ))
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
    let cells: BTreeMap<_, _> = summary
        .cells
        .iter()
        .map(|cell| (cell.column_id.as_str(), cell))
        .collect();
    let values = table
        .columns
        .iter()
        .map(|column| {
            cells
                .get(column.id.as_str())
                .map(|cell| summary_cell(cell, data))
                .unwrap_or_default()
        })
        .collect();
    Ok(fixed_row(
        values,
        summary.label.clone(),
        summary.label_column_span,
        &summary.style,
        summary.border.as_ref(),
        widths,
        size,
    ))
}

fn fixed_row(
    mut values: Vec<String>,
    label: String,
    span: i32,
    text_style: &ReportTextStyle,
    border: Option<&ReportBorderStyle>,
    widths: &[f32],
    size: f32,
) -> Row {
    let span = (span.max(1) as usize).min(widths.len());
    let style = CellStyle::new(text_style, size, false);
    let label = std::iter::once(label)
        .chain(values.drain(..span))
        .filter(|value| !value.is_empty())
        .collect::<Vec<_>>()
        .join("\n");
    let mut cells = Vec::with_capacity(widths.len() - span + 1);
    cells.push(style.wrap(&label, widths[..span].iter().sum()));
    cells.extend(
        values
            .iter()
            .zip(&widths[span..])
            .map(|(value, width)| style.wrap(value, *width)),
    );
    Row {
        height: style.height(&cells),
        cells,
        kind: RowKind::Fixed(text_style.clone(), border.cloned(), span),
        composed: vec![],
        page_break_before: false,
    }
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
