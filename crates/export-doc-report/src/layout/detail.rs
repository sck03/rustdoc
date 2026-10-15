mod cells;
mod headers;
mod rows;
#[cfg(test)]
mod tests;
use super::{PT_MM, measured_wrap, text_svg, wrap};
use crate::{ReportData, Result, error::invalid};
use export_doc_domain::designer::{DetailTable, ReportBorderStyle, ReportTextStyle};
use rows::{detail_rows, summary_row};
use std::sync::atomic::{AtomicBool, Ordering};

struct Row {
    cells: Vec<Vec<String>>,
    composed: Vec<Option<super::detail_content::ComposedCell>>,
    height: f32,
    kind: RowKind,
    page_break_before: bool,
}

enum RowKind {
    Data,
    Adjustment,
    Group(ReportTextStyle),
    Fixed(ReportTextStyle, Option<ReportBorderStyle>, usize),
}

pub(super) struct DetailLayout<'a> {
    pub table: &'a DetailTable,
    pub minimum_height: f32,
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
    fixed: impl Fn(&mut String, usize, usize, f32, f32) -> Result<()>,
) -> Result<Vec<String>> {
    let DetailLayout {
        table,
        minimum_height,
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
    let (header_groups, group_header_height) = headers::prepare(table, &widths, &header_style);
    let header_height = table
        .side_band
        .as_ref()
        .map_or(0., |side| {
            header_style.height(&[header_style.wrap(&side.title, side.width_mm)])
        })
        .max(header_style.height(&header_lines))
        + group_header_height;
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
    if let Some(adjustments) = &table.adjustments {
        for entry in data.root["Invoice"]["amountAdjustments"]
            .as_array()
            .into_iter()
            .flatten()
        {
            let cells: Vec<_> = table
                .columns
                .iter()
                .zip(&widths)
                .map(|(column, width)| {
                    let value = if column.id == adjustments.description_column_id {
                        entry["description"].as_str().unwrap_or("").to_owned()
                    } else if column.id == adjustments.amount_column_id {
                        crate::data::currency_text(
                            entry["amount"].as_str().unwrap_or(""),
                            &data.text("Invoice.Currency"),
                        )
                    } else {
                        String::new()
                    };
                    body_style.wrap(&value, *width)
                })
                .collect();
            rows.push(Row {
                height: body_style.height(&cells),
                cells,
                composed: vec![],
                kind: RowKind::Adjustment,
                page_break_before: false,
            });
        }
    }
    if let Some(summary) = &table.summary_row {
        rows.push(summary_row(table, summary, data, &widths, size)?);
    }
    let mut chunks: Vec<Vec<Row>> = vec![Vec::new()];
    let mut used = 0.;
    let mut page_index = 0usize;
    let mut data_rows_on_page = 0usize;
    let mut pending = rows.into_iter().peekable();
    while let Some(row) = pending.next() {
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
        let keep_with_next = matches!(row.kind, RowKind::Group(_))
            && table
                .grouping
                .as_ref()
                .is_some_and(|group| group.keep_together);
        let keep_height = if keep_with_next {
            row.height + pending.peek().map_or(0., |next| next.height)
        } else {
            row.height
        };
        let group_needs_page = keep_with_next
            && !chunks.last().is_some_and(Vec::is_empty)
            && (row_limit.is_some_and(|limit| data_rows_on_page >= limit)
                || keep_height <= page_capacity(page_index + 1)
                    && used + keep_height > page_capacity(page_index));
        if (row.page_break_before && !chunks.last().is_some_and(Vec::is_empty))
            || used + row.height > page_capacity(page_index)
            || (row_limit_reached && !chunks.last().is_some_and(Vec::is_empty))
            || group_needs_page
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
    let is_entry = |row: &Row| matches!(row.kind, RowKind::Data | RowKind::Adjustment);
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
        if start == last.len() || !last[start..].iter().any(is_entry) {
            return Err(invalid(
                "末页条款与合计没有足够空间容纳明细，请缩短条款或减小字号。",
            ));
        }
        if start > 0
            && matches!(last[start - 1].kind, RowKind::Group(_))
            && table
                .grouping
                .as_ref()
                .is_some_and(|group| group.keep_together)
        {
            if used + last[start - 1].height > capacity {
                return Err(invalid(
                    "末页无法同时容纳分组标题及首条明细，请调整字号或页脚。",
                ));
            }
            start -= 1;
        }
        let tail = last.split_off(start);
        chunks.push(tail);
    }
    // A summary that overflowed during the initial pagination still needs a preceding entry.
    if chunks.len() > 1 && !chunks.last().unwrap().iter().any(is_entry) {
        let last = chunks.len() - 1;
        let mut previous = chunks[last - 1]
            .iter()
            .rposition(is_entry)
            .ok_or_else(|| invalid("合计前没有可衔接的明细。"))?;
        if previous > 0
            && matches!(chunks[last - 1][previous - 1].kind, RowKind::Group(_))
            && table
                .grouping
                .as_ref()
                .is_some_and(|group| group.keep_together)
        {
            previous -= 1;
        }
        let tail = chunks[last - 1].split_off(previous);
        if tail
            .iter()
            .chain(chunks[last].iter())
            .map(|row| row.height)
            .sum::<f32>()
            > final_capacity(last)
        {
            return Err(invalid("末页调整与合计空间不足，请调整字号或页脚。"));
        }
        chunks[last].splice(0..0, tail);
        if chunks[last - 1].is_empty() {
            chunks.remove(last - 1);
        }
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
        let limit = match (index == 0, index + 1 == count) {
            (true, true) => bottoms.single,
            (true, false) => bottoms.first,
            (false, true) => bottoms.last,
            (false, false) => bottoms.continuation,
        };
        let frame_bottom = content_bottom.max((top + minimum_height).min(limit - 3.));
        fixed(&mut svg, index, count, content_bottom, frame_bottom)?;
        if let Some(side) = &table.side_band {
            render_side_band(
                &mut svg,
                table,
                side,
                data,
                left,
                top,
                frame_bottom,
                size,
                if table.print.repeat_header_on_page_break || index == 0 {
                    header_height
                } else {
                    0.
                },
                !side.first_page_only || index == 0,
            )?;
        }
        let show_header = table.print.repeat_header_on_page_break || index == 0;
        let mut y = top + if show_header { header_height } else { 0. };
        for row in rows {
            match &row.kind {
                RowKind::Group(style) => group_row(
                    &mut svg,
                    table,
                    &row.cells,
                    [body_left, y, body_width, row.height],
                    size,
                    style,
                ),
                RowKind::Fixed(style, border, span) => summary_render(
                    &mut svg,
                    table,
                    &row.cells,
                    &widths,
                    body_left,
                    y,
                    row.height,
                    size,
                    style,
                    border.as_ref(),
                    *span,
                ),
                RowKind::Data | RowKind::Adjustment => table_row(
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
        // Paint the header last so the first row's white fill cannot cover its bottom rule.
        if show_header {
            table_row(
                &mut svg,
                table,
                &header_lines,
                &widths,
                body_left,
                top + group_header_height,
                header_height - group_header_height,
                size,
                true,
                None,
            );
            if !header_groups.is_empty() {
                headers::draw(
                    &mut svg,
                    table,
                    &header_groups,
                    [body_left, top, group_header_height],
                    &header_style,
                );
            }
        }
        if table.row_separators == Some(false) && minimum_height <= 0. {
            super::flow::draw_border(
                &mut svg,
                [body_left, top, body_width, frame_bottom - top],
                &table.border,
            );
        }
        if minimum_height > 0. {
            let body_top = top + if show_header { header_height } else { 0. };
            let mut x = body_left;
            for (index, (column, width)) in table.columns.iter().zip(&widths).enumerate() {
                let mut border = column.border.as_ref().unwrap_or(&table.border).clone();
                if index == 0 || index + 1 == table.columns.len() {
                    let mut outer = border.clone();
                    outer.top = false;
                    outer.bottom = false;
                    outer.left = index == 0 && border.left;
                    outer.right = index + 1 == table.columns.len() && border.right;
                    super::flow::draw_border(
                        &mut svg,
                        [x, body_top, *width, frame_bottom - body_top],
                        &outer,
                    );
                    if index == 0 {
                        border.left = false;
                    }
                    if index + 1 == table.columns.len() {
                        border.right = false;
                    }
                }
                border.top = false;
                super::flow::draw_border(
                    &mut svg,
                    [x, content_bottom, *width, frame_bottom - content_bottom],
                    &border,
                );
                x += width;
            }
        }
        svg.push_str("</svg>");
        pages.push(svg);
    }
    Ok(pages)
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
    header_height: f32,
    show_content: bool,
) -> Result<()> {
    let value = if side.content_kind == "Field" {
        data.text(&side.field_path)
    } else {
        side.text.clone()
    };
    super::flow::draw_border(svg, [x, top, side.width_mm, bottom - top], &table.border);
    if header_height > 0. {
        let style = CellStyle::new(&table.header_style, size, true);
        let lines = style.wrap(&side.title, side.width_mm);
        text_svg(
            svg,
            &lines,
            x + style.left,
            top + style.content_top(header_height, lines.len() as f32 * style.size * 1.35),
            side.width_mm - style.left - style.right,
            style.size,
            style.bold,
            "#000000",
            side.style.align.as_deref().unwrap_or("Left"),
        );
        super::flow::draw_border(svg, [x, top, side.width_mm, header_height], &table.border);
    }
    if !show_content {
        return Ok(());
    }
    if side.content_kind == "Field"
        && let Some(image) = data.images.get(&side.field_path)
    {
        let image_y = top + header_height + 1.;
        svg.push_str(&format!("<image x=\"{}\" y=\"{image_y}\" width=\"{}\" height=\"{}\" preserveAspectRatio=\"xMidYMid meet\" href=\"{}\"/>",x+1.,side.width_mm-2.,(bottom-image_y-1.).max(1.),image.data_url()?));
        return Ok(());
    }
    text_svg(
        svg,
        &wrap(&value, side.width_mm - 2., size),
        x + 1.,
        top + header_height + 1.5,
        side.width_mm - 2.,
        size,
        false,
        "#000000",
        side.style.align.as_deref().unwrap_or("Left"),
    );
    Ok(())
}

fn group_row(
    svg: &mut String,
    table: &DetailTable,
    cells: &[Vec<String>],
    [left, y, width, height]: [f32; 4],
    size: f32,
    text_style: &ReportTextStyle,
) {
    let style = CellStyle::new(text_style, size, true);
    svg.push_str(&format!(
        "<rect x=\"{left}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"#f2f2f2\"/>"
    ));
    super::flow::draw_border(svg, [left, y, width, height], &table.border);
    text_svg(
        svg,
        cells.first().map_or(&[], Vec::as_slice),
        left + style.left,
        y + style.top,
        width - style.left - style.right,
        style.size,
        style.bold,
        "#000000",
        text_style.align.as_deref().unwrap_or("Left"),
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
    border: Option<&ReportBorderStyle>,
    span: usize,
) {
    let style = CellStyle::new(text_style, size, false);
    cells::draw(
        svg,
        [left, y, height],
        &style,
        cells.iter().enumerate().map(|(index, lines)| {
            let column_index = if index == 0 { 0 } else { index + span - 1 };
            let column = &table.columns[column_index];
            let cell_span = if index == 0 { span } else { 1 };
            let mut cell_border = cells::span_border(table, column_index, cell_span);
            if table.print.fill_height {
                if index == 0 {
                    cell_border.left = false;
                }
                if column_index + cell_span == table.columns.len() {
                    cell_border.right = false;
                }
            }
            cells::SpanCell {
                lines,
                width: if index == 0 {
                    widths[..span].iter().sum()
                } else {
                    widths[column_index]
                },
                border: cell_border,
                align: text_style.align.as_deref().unwrap_or(if index == 0 {
                    "Right"
                } else {
                    &column.align
                }),
            }
        }),
        border,
    );
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
            "<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"white\"/>"
        ));
        let mut border = column.border.as_ref().unwrap_or(&table.border).clone();
        if !header && table.row_separators == Some(false) {
            border.top = false;
            border.bottom = false;
        }
        if !header && table.print.fill_height {
            if index == 0 {
                border.left = false;
            }
            if index + 1 == table.columns.len() {
                border.right = false;
            }
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
                    &column.align,
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
