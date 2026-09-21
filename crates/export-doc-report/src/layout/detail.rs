use super::{PT_MM, measured_wrap, text_svg, wrap};
use crate::{ReportData, Result, error::invalid};
use export_doc_domain::designer::{
    DetailGroupFooter, DetailGroupFooterCell, DetailSummaryCell, DetailSummaryRow, DetailTable,
};
use rust_decimal::Decimal;
use serde_json::Value;
use std::{
    collections::BTreeMap,
    sync::atomic::{AtomicBool, Ordering},
};

struct Row {
    cells: Vec<Vec<String>>,
    height: f32,
    kind: RowKind,
    page_break_before: bool,
}

enum RowKind {
    Data,
    Group(String),
    GroupFooter,
    Summary,
}

pub(super) struct DetailLayout<'a> {
    pub table: &'a DetailTable,
    pub data: &'a ReportData,
    pub left: f32,
    pub top: f32,
    pub footer_top: f32,
    pub width: f32,
    pub page_width: f32,
    pub height: f32,
    pub cancelled: &'a AtomicBool,
}

pub(super) fn render(
    layout: DetailLayout<'_>,
    fixed: impl Fn(&mut String, usize, usize) -> Result<()>,
) -> Result<Vec<String>> {
    let DetailLayout {
        table,
        data,
        left,
        top,
        footer_top,
        width,
        page_width,
        height,
        cancelled,
    } = layout;
    if table.columns.is_empty() {
        return Err(invalid("商品明细表必须包含至少一列。"));
    }
    let body_left = if let Some(side) = &table.side_band {
        left + side.width_mm
    } else {
        left
    };
    let body_width = width - table.side_band.as_ref().map_or(0., |side| side.width_mm);
    let size = table
        .body_style
        .font_size_pt
        .unwrap_or(table.columns.first().map_or(9., |_| 9.))
        * PT_MM;
    let total_width: f32 = table.columns.iter().map(|column| column.width_mm).sum();
    if total_width <= 0. || body_width <= 10. {
        return Err(invalid("商品明细列宽无效。"));
    }
    let widths: Vec<f32> = table
        .columns
        .iter()
        .map(|column| column.width_mm / total_width * body_width)
        .collect();
    let header_lines: Vec<Vec<String>> = table
        .columns
        .iter()
        .zip(&widths)
        .map(|(column, width)| wrap(&column.title, (width - 3.).max(size), size))
        .collect();
    let header_height =
        header_lines.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.;
    let capacity = footer_top - 3. - top - header_height;
    if capacity < 10. {
        return Err(invalid("页眉与页脚之间没有足够的明细空间。"));
    }

    let mut rows = detail_rows(table, data, &widths, size)?;
    if let Some(summary) = &table.summary_row {
        rows.push(summary_row(table, summary, data, &widths, size)?);
    }
    let mut chunks: Vec<Vec<Row>> = vec![Vec::new()];
    let mut used = 0.;
    let mut page_index = 0usize;
    let mut data_rows_on_page = 0usize;
    for row in rows {
        if cancelled.load(Ordering::Relaxed) {
            return Err(crate::Error {
                kind: crate::ErrorKind::Cancelled,
                message: "报表输出已取消。".into(),
            });
        }
        if row.height > capacity {
            return Err(invalid("单行商品内容超过一页,请调整明细列宽或字体。"));
        }
        let row_limit = if page_index == 0 {
            table.print.first_page_rows
        } else {
            table.print.continuation_page_rows
        };
        let row_limit_reached = row_limit
            .is_some_and(|limit| matches!(row.kind, RowKind::Data) && data_rows_on_page >= limit);
        if (row.page_break_before && !chunks.last().is_some_and(Vec::is_empty))
            || (used + row.height > capacity && !chunks.last().is_some_and(Vec::is_empty))
            || (row_limit_reached && !chunks.last().is_some_and(Vec::is_empty))
        {
            chunks.push(Vec::new());
            used = 0.;
            page_index += 1;
            data_rows_on_page = 0;
        }
        if matches!(row.kind, RowKind::Data) {
            data_rows_on_page += 1;
        }
        used += row.height;
        chunks.last_mut().unwrap().push(row);
        if chunks.len() > 500 {
            return Err(invalid("报表页数超过 500 页限制。"));
        }
    }
    let count = chunks.len();
    let mut pages = Vec::with_capacity(count);
    for (index, rows) in chunks.iter().enumerate() {
        let mut svg = canvas(page_width, height);
        fixed(&mut svg, index, count)?;
        if let Some(side) = &table.side_band {
            render_side_band(&mut svg, table, side, data, left, top, footer_top, size)?;
        }
        let mut y = top;
        if table.print.repeat_header_on_page_break || index == 0 {
            table_row(
                &mut svg,
                table,
                &header_lines,
                &widths,
                body_left,
                y,
                header_height,
                size,
                true,
            );
            y += header_height;
        }
        for row in rows {
            match &row.kind {
                RowKind::Group(label) => group_row(
                    &mut svg, table, label, &row.cells, body_left, y, row.height, size,
                ),
                RowKind::Summary => summary_render(
                    &mut svg, table, &row.cells, &widths, body_left, y, row.height, size,
                ),
                RowKind::GroupFooter => summary_render(
                    &mut svg, table, &row.cells, &widths, body_left, y, row.height, size,
                ),
                RowKind::Data => table_row(
                    &mut svg, table, &row.cells, &widths, body_left, y, row.height, size, false,
                ),
            }
            y += row.height;
        }
        svg.push_str("</svg>");
        pages.push(svg);
    }
    Ok(pages)
}

fn detail_rows(
    table: &DetailTable,
    data: &ReportData,
    widths: &[f32],
    size: f32,
) -> Result<Vec<Row>> {
    let mut rows = Vec::new();
    let mut current_group = None::<String>;
    let mut group_sums = empty_group_sums(table);
    let mut group_count = 0usize;
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
                            grouping,
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
                rows.push(group_row_value(
                    label,
                    size,
                    table.columns.len(),
                    grouping.page_break_before,
                ));
            }
        }
        let cells: Vec<Vec<String>> = table
            .columns
            .iter()
            .zip(widths)
            .map(|(column, width)| {
                let text = column_text(column, data, item);
                measured_wrap(
                    &text,
                    (width - 3.).max(size),
                    "Noto Sans CJK SC",
                    false,
                    size,
                )
            })
            .collect();
        for cell in group_sum_cells(table) {
            if let Some(total) = group_sums.get_mut(&cell.column_id)
                && let Ok(number) = decimal(data.value(&cell.field_path, Some(item)))
            {
                *total += number;
            }
        }
        group_count += 1;
        let height = cells.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.;
        rows.push(Row {
            cells,
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
                    grouping,
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
            .map(|part| match part.kind.as_str() {
                "Text" => part.text.clone(),
                "LineBreak" => "\n".into(),
                _ => crate::data::plain(data.value(&part.field_path, Some(item))),
            })
            .collect();
    }
    crate::data::plain(data.value(&column.field_path, Some(item)))
}

fn group_row_value(label: String, size: f32, _columns: usize, page_break_before: bool) -> Row {
    Row {
        cells: vec![vec![label]],
        height: size * 1.35 + 4.,
        kind: RowKind::Group(String::new()),
        page_break_before,
    }
}

fn group_footer_row(
    table: &DetailTable,
    grouping: &export_doc_domain::designer::DetailGrouping,
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
            .map(|cell| footer_cell(cell, index, sums, count))
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
    let _ = grouping;
    Ok(Row {
        height: cells.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.,
        cells,
        kind: RowKind::GroupFooter,
        page_break_before: false,
    })
}

fn footer_cell(
    cell: &DetailGroupFooterCell,
    _index: usize,
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

fn summary_row(
    table: &DetailTable,
    summary: &DetailSummaryRow,
    data: &ReportData,
    widths: &[f32],
    size: f32,
) -> Result<Row> {
    let mut cells = Vec::with_capacity(table.columns.len());
    for (index, column) in table.columns.iter().enumerate() {
        let cell = summary
            .cells
            .iter()
            .find(|cell| cell.column_id == column.id);
        let value = cell
            .map(|cell| summary_cell(cell, data))
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
        first.insert(0, summary.label.clone());
    }
    Ok(Row {
        height: cells.iter().map(Vec::len).max().unwrap_or(1) as f32 * size * 1.35 + 4.,
        cells,
        kind: RowKind::Summary,
        page_break_before: false,
    })
}

fn summary_cell(cell: &DetailSummaryCell, data: &ReportData) -> String {
    match cell.content_kind.as_str() {
        "Text" => cell.text.clone(),
        "Field" => data.text(&cell.field_path),
        _ => String::new(),
    }
}

fn render_side_band(
    svg: &mut String,
    table: &DetailTable,
    side: &export_doc_domain::designer::DetailSideBand,
    data: &ReportData,
    x: f32,
    top: f32,
    bottom: f32,
    size: f32,
) -> Result<()> {
    let value = if side.content_kind == "Field" {
        data.text(&side.field_path)
    } else {
        side.text.clone()
    };
    svg.push_str(&format!(
        "<rect x=\"{x}\" y=\"{top}\" width=\"{}\" height=\"{}\" fill=\"#f7faf9\" stroke=\"#bdcdc8\" stroke-width=\"0.2\"/>",
        side.width_mm,
        (bottom - top).max(1.)
    ));
    text_svg(
        svg,
        &wrap(&side.title, side.width_mm - 2., size),
        x + 1.,
        top + 1.,
        side.width_mm - 2.,
        size,
        true,
        "#173f3b",
        side.style.align.as_deref().unwrap_or("Left"),
    );
    text_svg(
        svg,
        &wrap(&value, side.width_mm - 2., size),
        x + 1.,
        top + size * 1.35 + 2.,
        side.width_mm - 2.,
        size,
        false,
        "#173f3b",
        side.style.align.as_deref().unwrap_or("Left"),
    );
    let _ = table;
    Ok(())
}

fn group_row(
    svg: &mut String,
    table: &DetailTable,
    _label: &str,
    cells: &[Vec<String>],
    left: f32,
    y: f32,
    height: f32,
    size: f32,
) {
    let text = cells
        .first()
        .and_then(|lines| lines.first())
        .cloned()
        .unwrap_or_default();
    svg.push_str(&format!("<rect x=\"{left}\" y=\"{y}\" width=\"{}\" height=\"{height}\" fill=\"#eef6f5\" stroke=\"#bdcdc8\" stroke-width=\"0.2\"/>", table.columns.iter().map(|column| column.width_mm).sum::<f32>()));
    text_svg(
        svg,
        &wrap(&text, 100., size),
        left + 1.5,
        y + 1.5,
        100.,
        size,
        true,
        "#173f3b",
        "Left",
    );
}

fn summary_render(
    svg: &mut String,
    table: &DetailTable,
    cells: &[Vec<String>],
    widths: &[f32],
    left: f32,
    y: f32,
    height: f32,
    size: f32,
) {
    let mut x = left;
    for (index, ((column, lines), width)) in table.columns.iter().zip(cells).zip(widths).enumerate()
    {
        svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"#f5f7f7\" stroke=\"#bdcdc8\" stroke-width=\"0.2\"/>"));
        text_svg(
            svg,
            lines,
            x + 1.5,
            y + 1.5,
            width - 3.,
            size,
            index == 0,
            "#173f3b",
            &column.align,
        );
        x += width;
    }
}

fn table_row(
    svg: &mut String,
    table: &DetailTable,
    lines: &[Vec<String>],
    widths: &[f32],
    left: f32,
    y: f32,
    height: f32,
    size: f32,
    header: bool,
) {
    let mut x = left;
    for ((column, lines), width) in table.columns.iter().zip(lines).zip(widths) {
        svg.push_str(&format!("<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\" stroke=\"#bdcdc8\" stroke-width=\"0.2\"/>",if header{"#eef6f4"}else{"white"}));
        text_svg(
            svg,
            lines,
            x + 1.5,
            y + 1.5,
            width - 3.,
            size,
            header,
            "#173f3b",
            &column.align,
        );
        x += width;
    }
}

fn canvas(width: f32, height: f32) -> String {
    format!(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" font-family=\"Noto Sans CJK SC\" width=\"{}\" height=\"{}\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"white\"/>",
        width / PT_MM,
        height / PT_MM
    )
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
