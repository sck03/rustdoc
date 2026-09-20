//! Shared vector views for container previews and Rust PDF output.
use crate::{Document, Result, canvas::Canvas, error::invalid, layout::escape};
use export_doc_domain::{generated_api::*, packing::Dimensions};
use rust_decimal::{Decimal, prelude::ToPrimitive};

fn validate(
    container: &ApiContainerDimensionsDto,
    analysis: &ApiContainerPackingAnalysisDto,
) -> Result<Dimensions> {
    let dimensions = Dimensions::new(container).map_err(invalid)?;
    if analysis.packed_items.is_empty() || analysis.packed_items.len() > 20_000 {
        return Err(invalid("装柜预览须包含 1 至 20000 个装载块。"));
    }
    for item in &analysis.packed_items {
        if item.name.chars().count() > 200
            || item.priority_group.chars().count() > 100
            || item.x < Decimal::ZERO
            || item.y < Decimal::ZERO
            || item.base_height < Decimal::ZERO
            || item.width <= Decimal::ZERO
            || item.height <= Decimal::ZERO
            || item.occupied_height <= Decimal::ZERO
            || item.x > dimensions.length
            || item.width > dimensions.length - item.x
            || item.y > dimensions.width
            || item.height > dimensions.width - item.y
            || item.base_height > dimensions.height
            || item.occupied_height > dimensions.height - item.base_height
            || item.total_weight < Decimal::ZERO
            || item.total_weight > Decimal::from(1_000_000_000_000_000_i64)
            || !(1..=1_000_000).contains(&item.units_represented)
            || !(1..=5_000).contains(&item.load_count)
        {
            return Err(invalid("装载块尺寸、数量或重量无效，请重新分析。"));
        }
    }
    Ok(dimensions)
}
fn number(value: Decimal) -> f64 {
    value.to_f64().unwrap_or(0.)
}

/// 0: perspective, 1: top, 2: side, 3: door. Drawing never changes placement.
pub fn diagram(
    container: &ApiContainerDimensionsDto,
    analysis: &ApiContainerPackingAnalysisDto,
    projection: i32,
    angle: f64,
    filled: bool,
    selected: i32,
) -> Result<String> {
    let d = validate(container, analysis)?;
    if !angle.is_finite() {
        return Err(invalid("视角无效。"));
    }
    let (length, width, height) = (number(d.length), number(d.width), number(d.height));
    let radians = angle.to_radians();
    let (sin, cos) = radians.sin_cos();
    let project = |x: f64, y: f64, z: f64| -> (f64, f64, f64) {
        match projection {
            1 => (x, y, z),
            2 => (x, -z, y),
            3 => (y, -z, x),
            _ => {
                let u = x * cos - y * sin;
                let v = x * sin + y * cos;
                (u, v * 0.38 - z * 0.93, v * 0.93 + z * 0.38)
            }
        }
    };
    let corners = |x: f64, y: f64, z: f64, l: f64, w: f64, h: f64| -> Vec<(f64, f64, f64)> {
        [
            (x, y, z),
            (x + l, y, z),
            (x + l, y + w, z),
            (x, y + w, z),
            (x, y, z + h),
            (x + l, y, z + h),
            (x + l, y + w, z + h),
            (x, y + w, z + h),
        ]
        .into_iter()
        .map(|(x, y, z)| project(x, y, z))
        .collect()
    };
    let frame = corners(0., 0., 0., length, width, height);
    let bounds = frame.iter().fold(
        (
            f64::INFINITY,
            f64::INFINITY,
            f64::NEG_INFINITY,
            f64::NEG_INFINITY,
        ),
        |(x1, y1, x2, y2), p| (x1.min(p.0), y1.min(p.1), x2.max(p.0), y2.max(p.1)),
    );
    let scale = (880. / (bounds.2 - bounds.0).max(1.)).min(420. / (bounds.3 - bounds.1).max(1.));
    let position = |p: &(f64, f64, f64)| {
        (
            (p.0 - bounds.0) * scale + 40.,
            (p.1 - bounds.1) * scale + 35.,
        )
    };
    let mut svg = String::from(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"960\" height=\"500\" viewBox=\"0 0 960 500\"><rect width=\"960\" height=\"500\" fill=\"#f5f8f7\"/>",
    );
    let mut blocks: Vec<_> = analysis
        .packed_items
        .iter()
        .enumerate()
        .map(|(index, item)| {
            let points = corners(
                number(item.x),
                number(item.y),
                number(item.base_height),
                number(item.width),
                number(item.height),
                number(item.occupied_height),
            );
            (index, item, points)
        })
        .collect();
    blocks.sort_by(|a, b| {
        let depth = |points: &Vec<(f64, f64, f64)>| points.iter().map(|p| p.2).sum::<f64>();
        depth(&a.2).total_cmp(&depth(&b.2))
    });
    for (index, item, points) in blocks {
        let rgb = item.color_argb as u32 & 0xffffff;
        let faces: &[([usize; 4], f64)] = match projection {
            1 => &[([4, 5, 6, 7], 1.)],
            2 => &[([0, 1, 5, 4], 1.)],
            3 => &[([1, 2, 6, 5], 1.)],
            _ => &[
                ([0, 1, 5, 4], 0.72),
                ([1, 2, 6, 5], 0.84),
                ([4, 5, 6, 7], 1.),
            ],
        };
        for (face, shade) in faces {
            let color = format!(
                "#{:02x}{:02x}{:02x}",
                ((rgb >> 16 & 255) as f64 * shade) as u8,
                ((rgb >> 8 & 255) as f64 * shade) as u8,
                ((rgb & 255) as f64 * shade) as u8
            );
            let points = face
                .iter()
                .map(|i| {
                    let (x, y) = position(&points[*i]);
                    format!("{x:.2},{y:.2}")
                })
                .collect::<Vec<_>>()
                .join(" ");
            svg.push_str(&format!("<polygon points=\"{points}\" fill=\"{}\" stroke=\"{}\" stroke-width=\"{}\"><title>{} · {}</title></polygon>",
                if filled { &color } else { "none" }, if index as i32 == selected { "#df512f" } else { "#334b50" },
                if index as i32 == selected { 3. } else { 0.7 },escape(&item.name),escape(&item.display_text)));
        }
    }
    for (a, b) in [
        (0, 1),
        (1, 2),
        (2, 3),
        (3, 0),
        (4, 5),
        (5, 6),
        (6, 7),
        (7, 4),
        (0, 4),
        (1, 5),
        (2, 6),
        (3, 7),
    ] {
        let (x1, y1) = position(&frame[a]);
        let (x2, y2) = position(&frame[b]);
        svg.push_str(&format!("<line x1=\"{x1:.2}\" y1=\"{y1:.2}\" x2=\"{x2:.2}\" y2=\"{y2:.2}\" stroke=\"#203d37\" stroke-width=\"1\"/>"));
    }
    svg.push_str("</svg>");
    Ok(svg)
}

pub fn document(request: &ApiContainerPackingPdfRequest) -> Result<Document> {
    let container = request
        .container
        .as_ref()
        .ok_or_else(|| invalid("缺少柜型尺寸。"))?;
    let analysis = request
        .analysis
        .as_ref()
        .ok_or_else(|| invalid("请先完成装箱分析。"))?;
    validate(container, analysis)?;
    let name = request.project_name.as_deref().unwrap_or("装柜方案");
    let kind = request.container_type.as_deref().unwrap_or("");
    if name.chars().count() > 200 || kind.chars().count() > 100 {
        return Err(invalid("方案名称或柜型名称过长。"));
    }
    let mut page = Canvas::new(297., 210.);
    page.text("装柜现场作业单", 12., 10., 273., 20., true, "center");
    let title_height = page.text(
        &format!("{name}   {kind}"),
        12.,
        24.,
        273.,
        11.,
        false,
        "left",
    );
    let y = 24. + title_height + 4.;
    let summary = format!(
        "已装 {} / {} 件    托盘 {}    重量 {:.2} kg    体积 {:.3} m³    未装 {} 件\n容积利用率 {:.2}%    载重利用率 {:.2}%    重心偏差：长 {:.2}% / 宽 {:.2}%",
        analysis.packed_packages,
        analysis.total_packages,
        analysis.packed_pallets,
        analysis.packed_weight,
        analysis.packed_volume,
        analysis.unpacked_packages,
        analysis.volume_utilization_percent,
        analysis.weight_utilization_percent,
        analysis.center_of_gravity_length_deviation_percent,
        analysis.center_of_gravity_width_deviation_percent
    );
    page.text(&summary, 12., y, 273., 10., false, "left");
    let graphic = diagram(container, analysis, 0, 30., true, -1)?;
    page.svg.push_str(&format!(
        "<g transform=\"translate(12,{}) scale(0.28)\">{graphic}</g>",
        y + 16.
    ));
    let mut pages = vec![page.finish()];
    let mut page = Canvas::new(297., 210.);
    page.text("装载明细（厘米 / 千克）", 12., 10., 273., 16., true, "left");
    let mut y = 24.;
    for (index, item) in analysis.packed_items.iter().enumerate() {
        let row = format!(
            "{}. {}   {}   重量 {:.2} kg\n位置 ({}, {}, {})   占用 {} × {} × {} cm   {}{}",
            index + 1,
            item.name,
            item.display_text,
            item.total_weight,
            item.x.normalize(),
            item.y.normalize(),
            item.base_height.normalize(),
            item.width.normalize(),
            item.height.normalize(),
            item.occupied_height.normalize(),
            if item.is_rotated { "已旋转 " } else { "" },
            item.priority_group
        );
        let row_height = Canvas::text_height(&row, 269., 10.) + 5.;
        if y + row_height > 194. {
            pages.push(page.finish());
            page = Canvas::new(297., 210.);
            y = 14.;
        }
        page.rect(12., y, 273., row_height, "white", 0.2);
        page.text(&row, 14., y + 2., 269., 10., false, "left");
        y += row_height;
    }
    pages.push(page.finish());
    Ok(Document { pages })
}
