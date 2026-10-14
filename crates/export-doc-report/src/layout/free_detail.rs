//! Free-positioned fields form one repeated product row; fixed document objects stay independent.
use super::*;
pub(super) fn is_item(element: &Element) -> bool {
    element.is_product()
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
        let (row, row_height) = product_row(&fields, data, item, top, pitch)?;
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

fn overlaps(left: &Element, right: &Element) -> bool {
    left.x_hundredth_mm < right.x_hundredth_mm + right.width_hundredth_mm
        && left.x_hundredth_mm + left.width_hundredth_mm > right.x_hundredth_mm
}
struct Placed {
    element: Element,
    print: bool,
    collapsed: bool,
    anchor: bool,
}
/// Empty horizontal bands contract as a unit; bottom-aligned numeric fields follow them.
/// Authored blank gaps and explicit spacers remain, and wrapping expands the same columns.
fn product_row(
    fields: &[&Element],
    data: &crate::ReportData,
    item: &Value,
    top: i32,
    pitch: i32,
) -> Result<(Vec<Element>, i32)> {
    let mut row = Vec::with_capacity(fields.len());
    for source in fields {
        let spacer = matches!(source.kind, Kind::ProductSpacer);
        let text = match &source.kind {
            Kind::Field {
                field_path,
                fallback_text,
                field_format,
            } => {
                let value = data.field_text(field_path, Some(item), field_format.as_ref());
                if value.is_empty() {
                    fallback_text.clone()
                } else {
                    value
                }
            }
            Kind::ProductSpacer => String::new(),
            _ => unreachable!(),
        };
        let condition = source
            .product
            .as_ref()
            .and_then(|settings| settings.condition.as_ref());
        let matched = condition
            .map_or(Ok(true), |rule| {
                rule.matches(
                    data.value(&rule.field_path, Some(item)),
                    &data.display(&rule.field_path, Some(item)),
                )
            })
            .map_err(invalid)?;
        let hidden = !matched
            || (!spacer
                && source
                    .product
                    .as_ref()
                    .is_some_and(|settings| settings.hide_when_empty)
                && text.trim().is_empty());
        let collapsed = hidden
            && source
                .product
                .as_ref()
                .is_some_and(|settings| settings.collapse_empty);
        let mut field = (*source).clone();
        if collapsed {
            field.height_hundredth_mm = 0;
        } else if !spacer && !hidden {
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
        }
        let anchor = !spacer
            && field.style.vertical_align == "Bottom"
            && !fields.iter().any(|previous| {
                overlaps(previous, source)
                    && previous.y_hundredth_mm + previous.height_hundredth_mm
                        <= source.y_hundredth_mm
            });
        field.kind = Kind::Text { text };
        field.product = None;
        row.push(Placed {
            element: field,
            print: !hidden && !spacer,
            collapsed,
            anchor,
        });
    }
    let mut points: Vec<_> = fields
        .iter()
        .flat_map(|field| {
            [
                field.y_hundredth_mm,
                field.y_hundredth_mm + field.height_hundredth_mm,
            ]
        })
        .collect();
    points.sort_unstable();
    points.dedup();
    let collapsed: Vec<_> = points
        .windows(2)
        .filter_map(|band| {
            let covering: Vec<_> = fields
                .iter()
                .zip(&row)
                .filter(|(field, placed)| {
                    !placed.anchor
                        && field.y_hundredth_mm <= band[0]
                        && field.y_hundredth_mm + field.height_hundredth_mm >= band[1]
                })
                .collect();
            (!covering.is_empty() && covering.iter().all(|(_, placed)| placed.collapsed))
                .then_some((band[0], band[1]))
        })
        .collect();
    let removed = |from: i32, to: i32| {
        collapsed
            .iter()
            .map(|(start, end)| (to.min(*end) - from.max(*start)).max(0))
            .sum::<i32>()
    };
    let authored_bottom = fields
        .iter()
        .map(|field| field.y_hundredth_mm + field.height_hundredth_mm)
        .max()
        .unwrap();
    let removed_height = removed(top, authored_bottom);
    for (index, source) in fields.iter().enumerate() {
        let until = source.y_hundredth_mm
            + if row[index].anchor {
                source.height_hundredth_mm
            } else {
                0
            };
        let mut y = source.y_hundredth_mm - top - removed(top, until);
        if !row[index].anchor {
            for (previous, placed) in fields[..index].iter().zip(&row[..index]) {
                let end = previous.y_hundredth_mm + previous.height_hundredth_mm;
                if !placed.anchor && overlaps(previous, source) && end <= source.y_hundredth_mm {
                    let gap = source.y_hundredth_mm - end - removed(end, source.y_hundredth_mm);
                    y = y.max(
                        placed.element.y_hundredth_mm + placed.element.height_hundredth_mm + gap,
                    );
                }
            }
        }
        row[index].element.y_hundredth_mm = y.max(0);
    }
    let bottom = row
        .iter()
        .map(|placed| placed.element.y_hundredth_mm + placed.element.height_hundredth_mm)
        .max()
        .unwrap_or(0);
    for (source, placed) in fields.iter().zip(&mut row) {
        if placed.element.style.vertical_align == "Bottom" && !placed.collapsed {
            let end = source.y_hundredth_mm + source.height_hundredth_mm;
            placed.element.y_hundredth_mm = end - top - removed(top, end) + bottom
                - (authored_bottom - top - removed_height)
                - placed.element.height_hundredth_mm;
        }
    }
    let height = if row.iter().all(|placed| placed.collapsed) {
        0
    } else {
        (pitch - removed_height).max(bottom + 100)
    };
    Ok((
        row.into_iter()
            .filter(|placed| placed.print)
            .map(|placed| placed.element)
            .collect(),
        height,
    ))
}
