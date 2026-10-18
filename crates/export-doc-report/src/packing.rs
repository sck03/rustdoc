//! Shared vector views for container previews and Rust PDF output.
use crate::{Document, Result, canvas::Canvas, error::invalid, layout::escape};
use export_doc_domain::{generated_api::*, packing::Dimensions};
use rust_decimal::{Decimal, prelude::ToPrimitive};
mod job_sheet;

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
    diagram_labeled(
        container, analysis, projection, angle, filled, selected, None,
    )
}

fn diagram_labeled(
    container: &ApiContainerDimensionsDto,
    analysis: &ApiContainerPackingAnalysisDto,
    projection: i32,
    angle: f64,
    filled: bool,
    selected: i32,
    labels: Option<&[usize]>,
) -> Result<String> {
    let d = validate(container, analysis)?;
    if !angle.is_finite() {
        return Err(invalid("视角无效。"));
    }
    let (length, width, height) = (number(d.length), number(d.width), number(d.height));
    let radians = angle.to_radians();
    let (sin, cos) = radians.sin_cos();
    let (elevation_sin, elevation_cos) = 28_f64.to_radians().sin_cos();
    let project = |x: f64, y: f64, z: f64| -> (f64, f64, f64) {
        match projection {
            1 => (x, y, z),
            2 => (x, -z, y),
            3 => (-y, -z, x),
            _ => {
                let u = x * cos - y * sin;
                let v = x * sin + y * cos;
                (
                    u,
                    v * elevation_sin - z * elevation_cos,
                    v * elevation_cos + z * elevation_sin,
                )
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
    let scale = (880. / (bounds.2 - bounds.0).max(1.)).min(390. / (bounds.3 - bounds.1).max(1.));
    let position = |p: &(f64, f64, f64)| {
        (
            (p.0 - (bounds.0 + bounds.2) / 2.) * scale + 480.,
            (p.1 - (bounds.1 + bounds.3) / 2.) * scale + 230.,
        )
    };
    let mut svg = String::from(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"960\" height=\"500\" viewBox=\"0 0 960 500\"><rect width=\"960\" height=\"500\" fill=\"#f5f8f7\"/>",
    );
    if projection == 0 {
        let floor = [0, 1, 2, 3]
            .map(|i| {
                let (x, y) = position(&frame[i]);
                format!("{x:.2},{y:.2}")
            })
            .join(" ");
        svg.push_str(&format!("<polygon points=\"{floor}\" fill=\"#e1e8e6\"/>"));
    }
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
        // Face normals must point toward the same observer used for depth sorting.
        let perspective_faces = [
            (
                if cos >= 0. {
                    [3, 2, 6, 7]
                } else {
                    [0, 1, 5, 4]
                },
                0.72,
            ),
            (
                if sin >= 0. {
                    [1, 2, 6, 5]
                } else {
                    [0, 3, 7, 4]
                },
                0.84,
            ),
            ([4, 5, 6, 7], 1.),
        ];
        let faces: &[([usize; 4], f64)] = match projection {
            1 => &[([4, 5, 6, 7], 1.)],
            2 => &[([3, 2, 6, 7], 1.)],
            3 => &[([1, 2, 6, 5], 1.)],
            _ => &perspective_faces,
        };
        for (face, shade) in faces {
            let color = format!(
                "#{:02x}{:02x}{:02x}",
                ((rgb >> 16 & 255) as f64 * shade) as u8,
                ((rgb >> 8 & 255) as f64 * shade) as u8,
                ((rgb & 255) as f64 * shade) as u8
            );
            let polygon_points = face
                .iter()
                .map(|i| {
                    let (x, y) = position(&points[*i]);
                    format!("{x:.2},{y:.2}")
                })
                .collect::<Vec<_>>()
                .join(" ");
            svg.push_str(&format!("<polygon points=\"{polygon_points}\" fill=\"{}\" stroke=\"{}\" stroke-width=\"{}\"><title>{} · {}</title></polygon>",
                if filled { &color } else { "none" }, if index as i32 == selected { "#df512f" } else { "#334b50" },
                if index as i32 == selected { 3. } else { 0.7 },escape(&item.name),escape(&item.display_text)));
            if filled && !item.is_palletized && face[0] < 4 && (2..=64).contains(&item.load_count) {
                let [a, b, c, d] = face.map(|i| position(&points[i]));
                if (d.1 - a.1).abs() / item.load_count as f64 >= 3. {
                    for layer in 1..item.load_count {
                        let t = layer as f64 / item.load_count as f64;
                        let (x1, y1) = (a.0 + (d.0 - a.0) * t, a.1 + (d.1 - a.1) * t);
                        let (x2, y2) = (b.0 + (c.0 - b.0) * t, b.1 + (c.1 - b.1) * t);
                        svg.push_str(&format!("<path d=\"M{x1:.2},{y1:.2} L{x2:.2},{y2:.2}\" fill=\"none\" stroke=\"#334b50\" stroke-width=\"0.6\"/>"));
                    }
                }
            }
        }
        if let Some(label) = labels.and_then(|labels| labels.get(index)) {
            let face = faces.last().unwrap().0;
            let positions: Vec<_> = face.iter().map(|i| position(&points[*i])).collect();
            let (min_x, max_x, min_y, max_y) = positions.iter().fold(
                (
                    f64::INFINITY,
                    f64::NEG_INFINITY,
                    f64::INFINITY,
                    f64::NEG_INFINITY,
                ),
                |(a, b, c, d), (x, y)| (a.min(*x), b.max(*x), c.min(*y), d.max(*y)),
            );
            if max_x - min_x >= 32. && max_y - min_y >= 28. {
                let x = (min_x + max_x) / 2.;
                let y = (min_y + max_y) / 2.;
                svg.push_str(&format!("<circle cx=\"{x:.2}\" cy=\"{y:.2}\" r=\"14\" fill=\"white\" stroke=\"#203d37\"/><text x=\"{x:.2}\" y=\"{:.2}\" font-family=\"Noto Sans CJK SC\" font-size=\"21\" text-anchor=\"middle\" fill=\"#203d37\">{label}</text>", y + 7.));
            }
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
        let door = projection == 0 && [1, 2, 5, 6].contains(&a) && [1, 2, 5, 6].contains(&b);
        let (color, stroke, dash) = if door {
            ("#b76223", 2.5, "")
        } else {
            ("#849792", 0.8, "5 4")
        };
        svg.push_str(&format!("<line x1=\"{x1:.2}\" y1=\"{y1:.2}\" x2=\"{x2:.2}\" y2=\"{y2:.2}\" stroke=\"{color}\" stroke-width=\"{stroke}\" stroke-dasharray=\"{dash}\"/>"));
    }
    if labels.is_some() && projection == 0 {
        for (x, label) in [(0., "柜头"), (length, "柜门")] {
            let (x, y) = position(&project(x, width, 0.));
            svg.push_str(&format!("<text x=\"{x:.2}\" y=\"{:.2}\" font-family=\"Noto Sans CJK SC\" font-size=\"22\" text-anchor=\"middle\" fill=\"#203d37\">{label}</text>", y + 27.));
        }
        svg.push_str("<text x=\"480\" y=\"485\" font-family=\"Noto Sans CJK SC\" font-size=\"22\" text-anchor=\"middle\" fill=\"#203d37\">装入顺序：柜头 → 柜门；下层 → 上层</text>");
    }
    svg.push_str("</svg>");
    Ok(svg)
}

pub fn document(request: &ApiContainerPackingPdfRequest) -> Result<Document> {
    job_sheet::document(request)
}
