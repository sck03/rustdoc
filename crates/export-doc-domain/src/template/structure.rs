//! Structural limits come from the same generated contract consumed by React.
use crate::{contracts, designer::Design};
use export_doc_contracts::generated_api::ApiReportTemplateV3LimitsContract;
use std::{collections::BTreeSet, sync::OnceLock};

pub(super) fn limits() -> &'static ApiReportTemplateV3LimitsContract {
    static LIMITS: OnceLock<ApiReportTemplateV3LimitsContract> = OnceLock::new();
    LIMITS.get_or_init(|| {
        serde_json::from_value(contracts::initial(contracts::schema(
            "ApiReportTemplateV3LimitsContract",
        )))
        .expect("generated V3 limits")
    })
}

fn identifier(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 120
        && value
            .bytes()
            .next()
            .is_some_and(|c| c.is_ascii_alphanumeric())
        && value
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || b"_.:-".contains(&c))
}

pub(super) fn validate(design: &Design) -> Result<(), String> {
    let limits = limits();
    if !(100..=5000).contains(&design.grid.size_hundredth_mm) {
        return Err("网格间距须为 1–50 mm。".into());
    }
    if design.layers.is_empty()
        || design.layers.len() as i64 > limits.max_layers
        || !design.layers.iter().any(|layer| layer.role == "Body")
    {
        return Err(format!(
            "模板须包含主体图层，图层总数须为 1–{}。",
            limits.max_layers
        ));
    }
    if design
        .layers
        .iter()
        .any(|layer| layer.elements.len() as i64 > limits.max_elements_per_layer)
        || design
            .layers
            .iter()
            .map(|layer| layer.elements.len())
            .sum::<usize>() as i64
            > limits.max_total_elements
    {
        return Err(format!(
            "每层最多 {} 个元素，模板总数最多 {} 个。",
            limits.max_elements_per_layer, limits.max_total_elements
        ));
    }
    let mut ids = BTreeSet::new();
    for id in design.layers.iter().map(|layer| layer.id.as_str()).chain(
        design
            .layers
            .iter()
            .flat_map(|layer| layer.elements.iter().map(|element| element.id.as_str())),
    ) {
        if !identifier(id) || !ids.insert(id) {
            return Err("图层与元素编号须有效且不能重复。".into());
        }
    }
    for layer in &design.layers {
        if layer.name.trim().is_empty()
            || layer.name.chars().count() > 120
            || !(0..=26000).contains(&layer.print.min_height_hundredth_mm)
            || layer
                .design_height_hundredth_mm
                .is_some_and(|height| !(0..=design.page.height_hundredth_mm).contains(&height))
        {
            return Err("图层名称或设计/打印高度无效。".into());
        }
        for element in &layer.elements {
            if !(-100000..=100000).contains(&element.z_index)
                || !(0..=1000).contains(&element.style.padding_hundredth_mm)
                || !["Top", "Middle", "Bottom"].contains(&element.style.vertical_align.as_str())
            {
                return Err("组件层级、文字间距或垂直对齐无效。".into());
            }
        }
    }
    if design.resources.len() as i64 > limits.max_resources {
        return Err("图片资源数量超过契约上限。".into());
    }
    let mut resources = BTreeSet::new();
    for resource in &design.resources {
        let extension = match resource.media_type.as_str() {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            _ => return Err("模板图片只支持 PNG 或 JPEG。".into()),
        };
        if !resources.insert(&resource.id)
            || resource.byte_length == 0
            || resource.byte_length > limits.max_resource_bytes as u64
            || resource.sha256.len() != 64
            || !resource.sha256.bytes().all(|c| c.is_ascii_hexdigit())
            || resource.id != format!("img-{}.{}", resource.sha256.to_ascii_lowercase(), extension)
        {
            return Err("图片资源须唯一、大小有效，编号须与媒体类型和 SHA-256 一致。".into());
        }
    }
    Ok(())
}
