//! Opt-in, measured grid rows. The same editable cells are split into bounded
//! continuation blocks; no text is discarded to satisfy a one-page target.
use super::*;
use export_doc_domain::designer::GridCell;

pub(super) fn expand(
    flows: &[&Element],
    data: &crate::ReportData,
    capacity: f32,
    header_bottom: f32,
    canceled: &AtomicBool,
) -> Result<Vec<Element>> {
    let mut result = vec![];
    let mut shift = 0i32;
    let mut original_bottom = header_bottom;
    let mut used = 0f32;
    for element in flows {
        let gap = (element.y_hundredth_mm as f32 / 100. - original_bottom).max(0.);
        original_bottom = (element.y_hundredth_mm + element.height_hundredth_mm) as f32 / 100.;
        let growing = matches!(&element.kind, Kind::Flow { block: ReportBlock::Grid(grid), .. } if grid.grow_to_content);
        if !growing {
            let mut fixed = (*element).clone();
            fixed.y_hundredth_mm += shift;
            let height = fixed.height_hundredth_mm as f32 / 100.;
            if matches!(
                &fixed.kind,
                Kind::Flow {
                    block: ReportBlock::PageBreak(_),
                    ..
                }
            ) {
                used = 0.;
            } else if used + gap + height > capacity {
                used = height;
            } else {
                used += gap + height;
            }
            result.push(fixed);
            continue;
        }
        used += gap;
        let Kind::Flow {
            block: ReportBlock::Grid(grid),
            ..
        } = &element.kind
        else {
            unreachable!()
        };
        if !grid.title.is_empty() || element.rotation_deg != 0 {
            return Err(invalid("自动增高表格请使用独立标题，且保持不旋转。"));
        }
        let width = element.width_hundredth_mm as f32 / 100.;
        let total: f32 = grid.columns.iter().map(|c| c.width_percent).sum();
        let mut offset = 0i32;
        for row in &grid.rows {
            if canceled.load(std::sync::atomic::Ordering::Relaxed) {
                return Err(crate::Error {
                    kind: crate::ErrorKind::Cancelled,
                    message: "排版已取消。".into(),
                });
            }
            let mut column = 0usize;
            let mut cells = vec![];
            let mut line_height = 0f32;
            let mut padding = 0f32;
            for cell in &row.cells {
                if cell.row_span != 1
                    || cell.vertical_text
                    || !matches!(cell.content_kind.as_str(), "Text" | "Field")
                    || cell.diagonal_header.is_some()
                {
                    return Err(invalid(
                        "自动增高表格仅支持无跨行合并的横排文字和字段单元格。",
                    ));
                }
                let end = column + cell.col_span as usize;
                let columns = grid
                    .columns
                    .get(column..end)
                    .ok_or_else(|| invalid("表格列跨度无效。"))?;
                let style = flow::inherit(&cell.style, &grid.default_cell_style);
                let size = style.font_size_pt.unwrap_or(element.style.font_size_pt) * PT_MM;
                let w = width * columns.iter().map(|c| c.width_percent).sum::<f32>() / total
                    - style.margin_left_mm.unwrap_or(1.2)
                    - style.margin_right_mm.unwrap_or(1.2);
                if w <= size {
                    return Err(invalid("自动增高表格列宽不足。"));
                }
                let value = flow::value(
                    data,
                    &cell.content_kind,
                    &cell.text,
                    &cell.field_path,
                    &cell.label,
                    &cell.fallback_text,
                );
                let lines = measured_wrap(
                    &value,
                    w,
                    &element.style.font_family,
                    style.bold.unwrap_or(element.style.bold),
                    size,
                );
                line_height = line_height.max(size * 1.35);
                padding = padding.max(
                    style.margin_top_mm.unwrap_or(1.2) + style.margin_bottom_mm.unwrap_or(1.2),
                );
                cells.push((cell, lines));
                column = end;
            }
            let minimum = row.height_mm.unwrap_or(8.).min(capacity);
            if line_height <= 0.
                || !line_height.is_finite()
                || line_height + padding + 0.1 > capacity
            {
                return Err(invalid("表格行没有可用打印空间。"));
            }
            let count = cells
                .iter()
                .map(|(_, lines)| lines.len())
                .max()
                .unwrap_or(1);
            let mut start = 0;
            while start < count {
                if capacity - used < minimum.max(line_height + padding + 0.1) {
                    used = 0.;
                }
                let lines_per_page =
                    ((capacity - used - padding - 0.1) / line_height).floor() as usize;
                let take = (count - start).min(lines_per_page);
                let height = (take as f32 * line_height + padding + 0.1).max(minimum);
                let mut part = (*element).clone();
                part.id = format!("{}-{}-{start}", element.id, row.id);
                part.y_hundredth_mm = element.y_hundredth_mm + shift + offset;
                part.height_hundredth_mm = (height * 100.).ceil() as i32;
                if let Kind::Flow {
                    block: ReportBlock::Grid(block),
                    ..
                } = &mut part.kind
                {
                    block.grow_to_content = false;
                    let mut row = row.clone();
                    row.height_mm = Some(part.height_hundredth_mm as f32 / 100.);
                    row.cells = cells
                        .iter()
                        .map(|(cell, lines)| {
                            let mut cell: GridCell = (*cell).clone();
                            cell.content_kind = "Text".into();
                            cell.text = lines
                                .iter()
                                .skip(start)
                                .take(take)
                                .cloned()
                                .collect::<Vec<_>>()
                                .join("\n");
                            cell.label.clear();
                            cell.field_path.clear();
                            cell.fallback_text.clear();
                            cell
                        })
                        .collect();
                    block.rows = vec![row];
                }
                offset += part.height_hundredth_mm;
                used += part.height_hundredth_mm as f32 / 100.;
                start += take;
                result.push(part);
                if result.len() > 10000 {
                    return Err(invalid("自动增高表格超出排版容量。"));
                }
            }
        }
        shift += offset - element.height_hundredth_mm;
    }
    Ok(result)
}
