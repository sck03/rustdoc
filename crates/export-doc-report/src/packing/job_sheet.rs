use super::*;
use std::collections::BTreeMap;

pub(super) fn document(request: &ApiContainerPackingPdfRequest) -> Result<Document> {
    let container = request
        .container
        .as_ref()
        .ok_or_else(|| invalid("缺少柜型尺寸。"))?;
    let analysis = request
        .analysis
        .as_ref()
        .ok_or_else(|| invalid("请先完成装箱分析。"))?;
    let dimensions = validate(container, analysis)?;
    let name = request.project_name.as_deref().unwrap_or("装柜方案");
    let kind = request.container_type.as_deref().unwrap_or("");
    if name.chars().count() > 200 || kind.chars().count() > 100 {
        return Err(invalid("方案名称或柜型名称过长。"));
    }
    let mut groups: Vec<(&ApiPackedCargoItemDto, i64, Decimal, Decimal)> = Vec::new();
    let mut positions = BTreeMap::new();
    let mut labels = Vec::with_capacity(analysis.packed_items.len());
    for item in &analysis.packed_items {
        // Keep distinct sizes, orientations and stacks separate; only identical
        // loading instructions share a legend number and accumulated quantity.
        let key = (
            &item.name,
            item.color_argb,
            item.width,
            item.height,
            item.occupied_height,
            item.base_height,
            item.load_count,
            item.is_palletized,
            item.is_rotated,
            &item.priority_group,
        );
        let next = groups.len();
        let index = *positions.entry(key).or_insert(next);
        if index == next {
            groups.push((item, item.units_represented, item.x, item.x + item.width));
        } else {
            let group = &mut groups[index];
            group.1 += item.units_represented;
            group.2 = group.2.min(item.x);
            group.3 = group.3.max(item.x + item.width);
        }
        labels.push(index + 1);
    }
    let mut page = Canvas::new(297., 210.);
    page.text("装柜作业单", 12., 9., 273., 19., true, "left");
    let mut y =
        21. + page.text(
            &format!("{name}  {kind}"),
            12.,
            21.,
            273.,
            11.,
            true,
            "left",
        ) + 3.;
    let summary = format!(
        "装入 {} 件  |  未装 {} 件  |  托盘 {} 个  |  总重 {:.2} kg  |  柜内 {} × {} × {} cm",
        analysis.packed_packages,
        analysis.unpacked_packages,
        analysis.packed_pallets,
        analysis.packed_weight,
        dimensions.length.normalize(),
        dimensions.width.normalize(),
        dimensions.height.normalize()
    );
    y += page.text(&summary, 12., y, 273., 10., false, "left") + 4.;
    for (x, offset, scale, projection, angle, title) in [
        (12., 0., 0.18, 0, 30., "三维总览 · 从柜门侧上方看入"),
        (192., 0., 0.096875, 1, 0., "俯视：柜头在左，柜门在右"),
        (192., 56., 0.096875, 2, 0., "侧视：柜头在左，柜门在右"),
    ] {
        page.text(title, x, y + offset, 960. * scale, 9., true, "left");
        let svg = diagram_labeled(
            container,
            analysis,
            projection,
            angle,
            true,
            -1,
            Some(&labels),
        )?;
        page.svg.push_str(&format!(
            "<g transform=\"translate({x},{}) scale({scale})\">{svg}</g>",
            y + offset + 5.
        ));
    }
    y += 113.;
    let note = format!(
        "按图从柜头向柜门装入，下层先装、上层后装；图号对应下表，尺寸单位为 cm。{}{}现场核对箱唛、承重与绑扎后作业。",
        if analysis.unpacked_packages > 0 {
            "未装货物须另行安排。"
        } else {
            ""
        },
        if !analysis.is_center_of_gravity_within_tolerance {
            "重心超限，须调整后装柜。"
        } else {
            ""
        }
    );
    y += page.text(&note, 12., y, 273., 9., false, "left") + 4.;
    let mut pages = vec![];
    for (index, (item, count, start, end)) in groups.iter().enumerate() {
        let instruction = format!(
            "{} × {} 底面，高 {}；{} 层{}{}；离地 {}",
            item.width.normalize(),
            item.height.normalize(),
            item.occupied_height.normalize(),
            item.load_count,
            if item.is_palletized {
                "（含托盘）"
            } else {
                ""
            },
            if item.is_rotated {
                "；转向摆放"
            } else {
                ""
            },
            item.base_height.normalize()
        );
        let location = format!(
            "距柜头 {}–{}{}",
            start.normalize(),
            end.normalize(),
            if item.priority_group.is_empty() {
                String::new()
            } else {
                format!("；{}", item.priority_group)
            }
        );
        let row_height = [
            Canvas::text_height(&item.name, 75., 10.),
            Canvas::text_height(&instruction, 83., 9.),
            Canvas::text_height(&location, 63., 9.),
        ]
        .into_iter()
        .fold(5_f32, f32::max)
            + 4.;
        if y + row_height > 194. {
            footer(&mut page, pages.len() + 1);
            pages.push(page.finish());
            page = Canvas::new(297., 210.);
            page.text("装柜作业单 · 清单续页", 12., 10., 273., 14., true, "left");
            y = 25.;
        }
        page.rect(
            12.,
            y,
            273.,
            row_height,
            if index % 2 == 0 { "#f4f7f8" } else { "white" },
            0.15,
        );
        page.rect(
            14.,
            y + 2.,
            6.,
            6.,
            &format!("#{:06x}", item.color_argb as u32 & 0xffffff),
            0.2,
        );
        page.text(
            &(index + 1).to_string(),
            21.,
            y + 2.,
            10.,
            10.,
            true,
            "left",
        );
        page.text(&item.name, 32., y + 2., 75., 10., true, "left");
        page.text(
            &format!("{count} 件"),
            109.,
            y + 2.,
            22.,
            10.,
            false,
            "left",
        );
        page.text(&instruction, 133., y + 2., 83., 9., false, "left");
        page.text(&location, 220., y + 2., 63., 9., false, "left");
        y += row_height;
    }
    footer(&mut page, pages.len() + 1);
    pages.push(page.finish());
    Ok(Document { pages })
}

fn footer(page: &mut Canvas, number: usize) {
    page.text(
        &format!("装柜作业单  /  第 {number} 页"),
        12.,
        199.,
        273.,
        8.,
        false,
        "right",
    );
}
