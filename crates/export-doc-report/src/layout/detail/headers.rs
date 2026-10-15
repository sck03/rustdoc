use super::{
    CellStyle,
    cells::{self, SpanCell},
};
use export_doc_domain::designer::DetailTable;

pub(super) struct HeaderGroup {
    column: usize,
    span: usize,
    width: f32,
    lines: Vec<String>,
}

pub(super) fn prepare(
    table: &DetailTable,
    widths: &[f32],
    style: &CellStyle,
) -> (Vec<HeaderGroup>, f32) {
    if !table
        .columns
        .iter()
        .any(|column| !column.header_group_title.is_empty())
    {
        return (Vec::new(), 0.);
    }
    let mut groups = Vec::new();
    let mut index = 0;
    let mut height: f32 = 0.;
    while index < table.columns.len() {
        let column = &table.columns[index];
        let span = if column.header_group_title.is_empty() {
            1
        } else {
            (column.header_group_span.max(1) as usize).min(table.columns.len() - index)
        };
        let width = widths[index..index + span].iter().sum();
        let lines = style.wrap(&column.header_group_title, width);
        height = height.max(style.height(std::slice::from_ref(&lines)));
        groups.push(HeaderGroup {
            column: index,
            span,
            width,
            lines,
        });
        index += span;
    }
    (groups, height)
}

pub(super) fn draw(
    svg: &mut String,
    table: &DetailTable,
    groups: &[HeaderGroup],
    bounds: [f32; 3],
    style: &CellStyle,
) {
    cells::draw(
        svg,
        bounds,
        style,
        groups.iter().map(|group| {
            let column = &table.columns[group.column];
            SpanCell {
                width: group.width,
                lines: &group.lines,
                border: cells::span_border(table, group.column, group.span),
                align: &column.align,
            }
        }),
        None,
    );
}
