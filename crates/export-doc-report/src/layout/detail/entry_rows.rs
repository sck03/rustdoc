use super::{CellStyle, Row, RowKind, rows::item_row};
use crate::{ReportData, Result, error::invalid};
use export_doc_domain::designer::DetailTable;

pub(super) fn prepare(
    table: &DetailTable,
    data: &ReportData,
    widths: &[f32],
    size: f32,
) -> Result<Vec<Row>> {
    let description = table
        .entry_description_column_id
        .as_ref()
        .and_then(|id| table.columns.iter().position(|column| &column.id == id))
        .ok_or_else(|| {
            invalid("当前模板尚未指定录入说明列，请在明细表属性中选择“按录入行打印的说明列”。")
        })?;
    // Grouping belongs to product composition. Entry rows already carry the
    // author's order and headings, so both modes can share the same template.
    let style = CellStyle::new(&table.body_style, size, false);
    let mut rows = Vec::new();
    for item in data.items() {
        let entries = item["printRows"]
            .as_array()
            .ok_or_else(|| invalid("打印明细行缺失。"))?;
        for entry in entries {
            let text = entry["text"].as_str().unwrap_or_default();
            if entry["kind"] == "Item" {
                rows.push(item_row(
                    table,
                    data,
                    item,
                    widths,
                    size,
                    Some((description, text)),
                )?);
            } else {
                let cells: Vec<_> = widths
                    .iter()
                    .enumerate()
                    .map(|(index, width)| {
                        style.wrap(if index == description { text } else { "" }, *width)
                    })
                    .collect();
                rows.push(Row {
                    height: style.height(&cells).max(4.),
                    cells,
                    composed: vec![],
                    kind: RowKind::Data,
                    page_break_before: false,
                });
            }
        }
    }
    Ok(rows)
}
