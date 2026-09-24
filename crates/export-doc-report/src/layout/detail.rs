use super::{PT_MM, measured_wrap, text_svg, wrap};
use crate::{ReportData, Result, error::invalid};
use export_doc_domain::designer::{
    DetailGroupFooter, DetailGroupFooterCell, DetailSummaryCell, DetailSummaryRow, DetailTable,
    ReportTextStyle,
};
use rust_decimal::Decimal;
use serde_json::Value;
use std::{
    collections::BTreeMap,
    sync::atomic::{AtomicBool, Ordering},
};

struct Row {
    cells: Vec<Vec<String>>,
    composed: Vec<Option<super::detail_content::ComposedCell>>,
    height: f32,
    kind: RowKind,
    page_break_before: bool,
}

enum RowKind {
    Data,
    Group(String),
    Fixed(ReportTextStyle),
}

pub(super) struct DetailLayout<'a> {
    pub table: &'a DetailTable,
    pub data: &'a ReportData,
    pub left: f32,
    pub top: f32,
    pub continuation_top: f32,
    pub bottoms: PageBottoms,
    pub width: f32,
    pub page_width: f32,
    pub height: f32,
    pub cancelled: &'a AtomicBool,
}
pub(super) struct PageBottoms {
    pub first: f32,
    pub continuation: f32,
    pub last: f32,
    pub single: f32,
}

pub(super) fn render(
    layout: DetailLayout<'_>,
    fixed: impl Fn(&mut String, usize, usize, f32) -> Result<()>,
) -> Result<Vec<String>> {
    let DetailLayout {
        table,
        data,
        left,
        top,
        continuation_top,
        bottoms,
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
    let body_style = CellStyle::new(&table.body_style, 9. * PT_MM, false);
    let header_style = CellStyle::new(&table.header_style, body_style.size, true);
    let size = body_style.size;
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
        .map(|(column, width)| header_style.wrap(&column.title, *width))
        .collect();
    let header_height = header_style.height(&header_lines);
    let page_capacity = |index: usize| {
        (if index == 0 {
            bottoms.first
        } else {
            bottoms.continuation
        }) - 3.
            - if index == 0 {
                top + header_height
            } else {
                continuation_top
                    + if table.print.repeat_header_on_page_break {
                        header_height
                    } else {
                        0.
                    }
            }
    };
    if page_capacity(0) < 10. || page_capacity(1) < 10. {
        return Err(invalid("页眉与页脚之间没有足够的明细空间。"));
    }

    let mut rows = detail_rows(table, data, &widths, size)?;
    if let Some(intro) = &table.intro_row {
        rows.insert(0, summary_row(table, intro, data, &widths, size)?);
    }
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
        if row.height > page_capacity(page_index).max(page_capacity(page_index + 1)) {
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
            || used + row.height > page_capacity(page_index)
            || (row_limit_reached && !chunks.last().is_some_and(Vec::is_empty))
        {
            chunks.push(Vec::new());
            used = 0.;
            page_index += 1;
            data_rows_on_page = 0;
        }
        if row.height > page_capacity(page_index) {
            return Err(invalid(
                "单行商品内容超过续页可用空间,请调整明细列宽或字体。",
            ));
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
    // Terminal clauses reserve space only on the actual final page. Move a
    // suffix as a unit when necessary, keeping the total with a detail row.
    let final_capacity = |index| {
        page_capacity(index)
            - if index == 0 {
                bottoms.first - bottoms.single
            } else {
                bottoms.continuation - bottoms.last
            }
    };
    let last_index = chunks.len() - 1;
    if chunks[last_index].iter().map(|row| row.height).sum::<f32>() > final_capacity(last_index) {
        let capacity = final_capacity(last_index + 1);
        let last = chunks.last_mut().unwrap();
        let mut start = last.len();
        let mut used = 0.;
        let mut detail_count = 0;
        while start > 0 && used + last[start - 1].height <= capacity {
            let is_detail = matches!(last[start - 1].kind, RowKind::Data);
            if is_detail
                && table
                    .print
                    .continuation_page_rows
                    .is_some_and(|limit| detail_count >= limit)
            {
                break;
            }
            start -= 1;
            used += last[start].height;
            detail_count += usize::from(is_detail);
        }
        if start == last.len()
            || !last[start..]
                .iter()
                .any(|row| matches!(row.kind, RowKind::Data))
        {
            return Err(invalid(
                "末页条款与合计没有足够空间容纳明细，请缩短条款或减小字号。",
            ));
        }
        let tail = last.split_off(start);
        chunks.push(tail);
    }
    let count = chunks.len();
    if count > 500 {
        return Err(invalid("报表页数超过 500 页限制。"));
    }
    let mut pages = Vec::with_capacity(count);
    for (index, rows) in chunks.iter().enumerate() {
        let top = if index == 0 { top } else { continuation_top };
        let content_bottom =
            top + if table.print.repeat_header_on_page_break || index == 0 {
                header_height
            } else {
                0.
            } + rows.iter().map(|row| row.height).sum::<f32>();
        let mut svg = canvas(page_width, height);
        fixed(&mut svg, index, count, content_bottom)?;
        if let Some(side) = &table.side_band {
            render_side_band(
                &mut svg,
                table,
                side,
                data,
                left,
                top,
                content_bottom,
                size,
                !side.first_page_only || index == 0,
            )?;
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
                None,
            );
            y += header_height;
        }
        for row in rows {
            match &row.kind {
                RowKind::Group(label) => group_row(
                    &mut svg, table, label, &row.cells, body_left, y, row.height, size,
                ),
                RowKind::Fixed(style) => summary_render(
                    &mut svg, table, &row.cells, &widths, body_left, y, row.height, size, style,
                ),
                RowKind::Data => table_row(
                    &mut svg,
                    table,
                    &row.cells,
                    &widths,
                    body_left,
                    y,
                    row.height,
                    size,
                    false,
                    Some(&row.composed),
                ),
            }
            y += row.height;
        }
        if table.row_separators == Some(false) {
            super::flow::draw_border(
                &mut svg,
                [body_left, top, body_width, content_bottom - top],
                &table.border,
            );
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
        let style = CellStyle::new(&table.body_style, size, false);
        let composed: Vec<_> = table
            .columns
            .iter()
            .zip(widths)
            .map(|(column, width)| {
                if column.content_kind == "Composite" {
                    super::detail_content::compose(
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
                CellStyle::new(&table.body_style, size, false).wrap(&text, *width)
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

fn group_row_value(label: String, size: f32, _columns: usize, page_break_before: bool) -> Row {
    Row {
        cells: vec![vec![label]],
        composed: vec![],
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
        kind: RowKind::Fixed(footer.style.clone()),
        composed: vec![],
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
        kind: RowKind::Fixed(summary.style.clone()),
        composed: vec![],
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
    show_content: bool,
) -> Result<()> {
    let value = if side.content_kind == "Field" {
        data.text(&side.field_path)
    } else {
        side.text.clone()
    };
    svg.push_str(&format!(
        "<rect x=\"{x}\" y=\"{top}\" width=\"{}\" height=\"{}\" fill=\"white\" stroke=\"#000000\" stroke-width=\"0.2\"/>",
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
        "#000000",
        side.style.align.as_deref().unwrap_or("Left"),
    );
    if !show_content {
        return Ok(());
    }
    if side.content_kind == "Field"
        && let Some(image) = data.images.get(&side.field_path)
    {
        let image_y = top + size * 1.35 + 3.;
        svg.push_str(&format!("<image x=\"{}\" y=\"{image_y}\" width=\"{}\" height=\"{}\" preserveAspectRatio=\"xMidYMid meet\" href=\"{}\"/>",x+1.,side.width_mm-2.,(bottom-image_y-1.).max(1.),image.data_url()?));
        return Ok(());
    }
    text_svg(
        svg,
        &wrap(&value, side.width_mm - 2., size),
        x + 1.,
        top + size * 1.35 + 2.,
        side.width_mm - 2.,
        size,
        false,
        "#000000",
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
    svg.push_str(&format!("<rect x=\"{left}\" y=\"{y}\" width=\"{}\" height=\"{height}\" fill=\"#f2f2f2\" stroke=\"#000000\" stroke-width=\"0.2\"/>", table.columns.iter().map(|column| column.width_mm).sum::<f32>()));
    text_svg(
        svg,
        &wrap(&text, 100., size),
        left + 1.5,
        y + 1.5,
        100.,
        size,
        true,
        "#000000",
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
    text_style: &ReportTextStyle,
) {
    let style = CellStyle::new(text_style, size, false);
    let mut x = left;
    for ((column, lines), width) in table.columns.iter().zip(cells).zip(widths) {
        svg.push_str(&format!(
            "<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"white\"/>"
        ));
        super::flow::draw_border(
            svg,
            [x, y, *width, height],
            column.border.as_ref().unwrap_or(&table.border),
        );
        text_svg(
            svg,
            lines,
            x + style.left,
            y + style.top,
            width - style.left - style.right,
            style.size,
            style.bold,
            "#000000",
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
    composed: Option<&[Option<super::detail_content::ComposedCell>]>,
) {
    let style = CellStyle::new(
        if header {
            &table.header_style
        } else {
            &table.body_style
        },
        size,
        header,
    );
    let mut x = left;
    for (index, ((column, lines), width)) in table.columns.iter().zip(lines).zip(widths).enumerate()
    {
        svg.push_str(&format!(
            "<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"{}\"/>",
            if header { "#f2f2f2" } else { "white" }
        ));
        let mut border = column.border.as_ref().unwrap_or(&table.border).clone();
        if !header && table.row_separators == Some(false) {
            border.top = false;
            border.bottom = false;
        }
        super::flow::draw_border(svg, [x, y, *width, height], &border);
        if let Some(cell) = composed
            .and_then(|cells| cells.get(index))
            .and_then(Option::as_ref)
        {
            for fragment in &cell.fragments {
                text_svg(
                    svg,
                    &fragment.lines,
                    x + style.left + fragment.x,
                    y + style.content_top(height, cell.height) + fragment.y,
                    fragment.width,
                    style.size,
                    style.bold,
                    "#000000",
                    "Left",
                );
            }
        } else {
            text_svg(
                svg,
                lines,
                x + style.left,
                y + style.content_top(height, lines.len() as f32 * style.size * 1.35),
                width - style.left - style.right,
                style.size,
                style.bold,
                "#000000",
                &column.align,
            );
        }
        x += width;
    }
}

/// Measurement and drawing use the same user-selected font and padding.
struct CellStyle {
    size: f32,
    bold: bool,
    top: f32,
    right: f32,
    bottom: f32,
    left: f32,
    vertical_factor: f32,
}
impl CellStyle {
    fn new(style: &ReportTextStyle, size: f32, header: bool) -> Self {
        Self {
            size: style.font_size_pt.map_or(size, |value| value * PT_MM),
            bold: style.bold.unwrap_or(header),
            top: style.margin_top_mm.unwrap_or(1.5),
            right: style.margin_right_mm.unwrap_or(1.5),
            bottom: style
                .margin_bottom_mm
                .unwrap_or(if header { 2.5 } else { 1.5 }),
            left: style.margin_left_mm.unwrap_or(1.5),
            vertical_factor: match style.vertical_align.as_deref() {
                Some("Middle") => 0.5,
                Some("Bottom") => 1.,
                _ => 0.,
            },
        }
    }
    fn wrap(&self, value: &str, width: f32) -> Vec<String> {
        measured_wrap(
            value,
            (width - self.left - self.right).max(self.size),
            "Noto Sans CJK SC",
            self.bold,
            self.size,
        )
    }
    fn height(&self, cells: &[Vec<String>]) -> f32 {
        cells.iter().map(Vec::len).max().unwrap_or(1) as f32 * self.size * 1.35
            + self.top
            + self.bottom
    }
    fn content_top(&self, row_height: f32, content_height: f32) -> f32 {
        self.top
            + (row_height - self.top - self.bottom - content_height).max(0.) * self.vertical_factor
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
