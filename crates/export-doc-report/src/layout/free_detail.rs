//! Free-positioned fields form one repeated product row; fixed document objects stay independent.
use super::*;
pub(super) fn is_item(element: &Element) -> bool {
    matches!(&element.kind, Kind::Field { field_path, .. } if field_path.starts_with("item."))
}
pub(super) fn render(
    design: &Design,
    data: &crate::ReportData,
    cancelled: &AtomicBool,
) -> Result<Vec<String>> {
    let mut fields: Vec<_> = design
        .layers
        .iter()
        .filter(|l| l.visible && l.role == "Body")
        .flat_map(|l| &l.elements)
        .filter(|e| e.visible && e.output_enabled && is_item(e))
        .collect();
    fields.sort_by_key(|e| (e.y_hundredth_mm, e.x_hundredth_mm));
    let top = fields
        .iter()
        .map(|e| e.y_hundredth_mm)
        .min()
        .ok_or_else(|| invalid("商品字段必须放在主体区域。"))?;
    let width = design.page.width_hundredth_mm as f32 / 100.;
    let height = design.page.height_hundredth_mm as f32 / 100.;
    let mut fixed = design.clone();
    fixed
        .layers
        .retain(|layer| layer.role != "Footer" || !layer.print.follow_body);
    let following_height = design
        .layers
        .iter()
        .filter(|layer| layer.visible && layer.role == "Footer" && layer.print.follow_body)
        .map(footer_content_height)
        .fold(0., f32::max);
    let bottom = layer_footer_top(&fixed, height)
        .min(height - design.page.margin_bottom_hundredth_mm as f32 / 100.)
        - following_height;
    let pitch = design.detail_row_height_hundredth_mm.unwrap_or(1200);
    let left = fields.iter().map(|e| e.x_hundredth_mm).min().unwrap();
    let right = fields
        .iter()
        .map(|e| e.x_hundredth_mm + e.width_hundredth_mm)
        .max()
        .unwrap();
    for layer in design
        .layers
        .iter()
        .filter(|l| l.visible && l.role == "Body")
    {
        for e in layer
            .elements
            .iter()
            .filter(|e| e.visible && e.output_enabled && !is_item(e))
        {
            if !matches!(e.kind, Kind::Line { .. } | Kind::Rectangle)
                && (e.x_hundredth_mm < right
                    && e.x_hundredth_mm + e.width_hundredth_mm > left
                    && e.y_hundredth_mm + e.height_hundredth_mm > top
                    && (e.y_hundredth_mm as f32) < bottom * 100.)
            {
                return Err(invalid(
                    "固定内容与商品自动输出区域重叠，请移到商品列旁、页眉或页脚。",
                ));
            }
        }
    }
    let mut pages: Vec<Vec<Element>> = vec![Vec::new()];
    let mut bottoms = vec![top as f32 / 100.];
    let mut cursor = top;
    for item in data.items() {
        if cancelled.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(invalid("报表生成已取消。"));
        }
        let mut row: Vec<Element> = Vec::new();
        let mut row_height = pitch;
        for (index, source) in fields.iter().enumerate() {
            let Kind::Field {
                field_path,
                fallback_text,
            } = &source.kind
            else {
                unreachable!()
            };
            let value = data.display(field_path, Some(item));
            let text = if value.is_empty() {
                fallback_text.clone()
            } else {
                value
            };
            let mut field = (*source).clone();
            let size = field.style.font_size_pt * PT_MM;
            let available =
                (field.width_hundredth_mm - field.style.padding_hundredth_mm * 2) as f32 / 100.;
            let lines = measured_wrap(
                &text,
                available,
                &field.style.font_family,
                field.style.bold,
                size,
            );
            field.height_hundredth_mm = field.height_hundredth_mm.max(
                (lines.len() as f32 * size * 1.35 * 100.).ceil() as i32
                    + field.style.padding_hundredth_mm * 2,
            );
            field.y_hundredth_mm -= top;
            // Wrapped text pushes fields below it in the same column, preserving authored gaps.
            for (previous, placed) in fields[..index].iter().zip(&row) {
                if previous.x_hundredth_mm < source.x_hundredth_mm + source.width_hundredth_mm
                    && previous.x_hundredth_mm + previous.width_hundredth_mm > source.x_hundredth_mm
                    && previous.y_hundredth_mm + previous.height_hundredth_mm
                        <= source.y_hundredth_mm
                {
                    let gap = source.y_hundredth_mm
                        - previous.y_hundredth_mm
                        - previous.height_hundredth_mm;
                    field.y_hundredth_mm = field
                        .y_hundredth_mm
                        .max(placed.y_hundredth_mm + placed.height_hundredth_mm + gap);
                }
            }
            row_height = row_height.max(field.y_hundredth_mm + field.height_hundredth_mm + 100);
            field.kind = Kind::Text { text };
            row.push(field);
        }
        let content_bottom = row
            .iter()
            .map(|field| field.y_hundredth_mm + field.height_hundredth_mm)
            .max()
            .unwrap();
        let authored_bottom = fields
            .iter()
            .map(|field| field.y_hundredth_mm + field.height_hundredth_mm - top)
            .max()
            .unwrap();
        for (source, field) in fields.iter().zip(&mut row) {
            if field.style.vertical_align == "Bottom" {
                field.y_hundredth_mm = source.y_hundredth_mm + source.height_hundredth_mm - top
                    + content_bottom
                    - authored_bottom
                    - field.height_hundredth_mm;
            }
        }
        if (top + row_height) as f32 > bottom * 100. {
            return Err(invalid("单件商品内容超过一页，请调宽字段或缩小字号。"));
        }
        if (cursor + row_height) as f32 > bottom * 100. {
            if pages.len() >= 500 {
                return Err(invalid("报表超过 500 页上限。"));
            }
            pages.push(Vec::new());
            bottoms.push(top as f32 / 100.);
            cursor = top;
        }
        for mut field in row {
            field.y_hundredth_mm += cursor;
            pages.last_mut().unwrap().push(field);
        }
        cursor += row_height;
        *bottoms.last_mut().unwrap() = cursor as f32 / 100.;
    }
    let count = pages.len();
    pages
        .iter()
        .enumerate()
        .map(|(index, row)| {
            let mut canvas = Canvas::new(width, height);
            fixed_elements(
                &mut canvas.svg,
                design,
                data,
                index,
                count,
                true,
                Some(bottoms[index]),
            )?;
            for field in row {
                element(&mut canvas.svg, field, data, index, count)?;
            }
            Ok(canvas.finish().svg)
        })
        .collect()
}
