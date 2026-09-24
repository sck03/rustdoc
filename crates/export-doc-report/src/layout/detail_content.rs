use super::measured_wrap;
use crate::ReportData;
use export_doc_domain::designer::DetailCellContent;
use serde_json::Value;

pub(super) struct Fragment {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub lines: Vec<String>,
}
pub(super) struct ComposedCell {
    pub fragments: Vec<Fragment>,
    pub height: f32,
}

pub(super) fn compose(
    parts: &[DetailCellContent],
    data: &ReportData,
    item: &Value,
    width: f32,
    size: f32,
    bold: bool,
    omit_empty_lines: bool,
    vertical_factor: f32,
) -> crate::Result<Option<ComposedCell>> {
    if !omit_empty_lines && !parts.iter().any(|part| part.kind == "ColumnBreak") {
        return Ok(None);
    }
    let rows = export_doc_domain::designer::composite::rows(parts);
    let mut result = ComposedCell {
        fragments: vec![],
        height: 0.,
    };
    for row in rows {
        if omit_empty_lines
            && row.iter().all(|slot| {
                slot.parts.iter().all(|part| {
                    if part.kind == "Text" {
                        part.text.trim().is_empty()
                    } else {
                        data.display(&part.field_path, Some(item)).trim().is_empty()
                    }
                })
            })
        {
            continue;
        }
        let mut height: f32 = 0.;
        let first_fragment = result.fragments.len();
        for (index, slot) in row.iter().enumerate() {
            let text: String = slot
                .parts
                .iter()
                .map(|part| {
                    if part.kind == "Text" {
                        part.text.clone()
                    } else {
                        data.display(&part.field_path, Some(item))
                    }
                })
                .collect();
            let end = row.get(index + 1).map_or(100., |next| next.position);
            let slot_width = width * (end - slot.position) / 100.;
            if !text.is_empty() && slot_width - 1. < size {
                return Err(crate::error::invalid(
                    "分栏可用宽度小于一个字，请调整分栏位置或字号。",
                ));
            }
            let lines = measured_wrap(
                &text,
                (slot_width - 1.).max(size),
                "Noto Sans CJK SC",
                bold,
                size,
            );
            height = height.max(lines.len() as f32 * size * 1.35);
            result.fragments.push(Fragment {
                x: width * slot.position / 100.,
                y: result.height,
                width: slot_width - 1.,
                lines,
            });
        }
        for fragment in &mut result.fragments[first_fragment..] {
            fragment.y +=
                (height - fragment.lines.len() as f32 * size * 1.35).max(0.) * vertical_factor;
        }
        result.height += height;
    }
    Ok(Some(result))
}
