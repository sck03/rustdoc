use super::{
    Request, close,
    placement::{Layer, Stack, gravity},
};
use crate::generated_api::{ApiContainerPackingAnalysisDto, ApiPackedCargoItemDto};
use rust_decimal::Decimal as D;

pub(super) fn summarize(
    request: &Request,
    stacks: &[Stack],
    weight: D,
    mx: D,
    my: D,
) -> ApiContainerPackingAnalysisDto {
    let mut ordered: Vec<_> = stacks.iter().collect();
    ordered.sort_by_key(|stack| {
        let base = &stack.layers[0];
        (base.x, base.y, base.zone)
    });
    let mut packed_items: Vec<ApiPackedCargoItemDto> = vec![];
    for stack in ordered {
        let mut current: Option<ApiPackedCargoItemDto> = None;
        for layer in &stack.layers {
            if let Some(previous) = current.as_mut().filter(|p| can_merge(p, layer)) {
                previous.occupied_height += layer.height;
                previous.top_height = previous.base_height + previous.occupied_height;
                previous.units_represented += layer.units;
                previous.stack_count = previous.units_represented;
                previous.load_count += 1;
                previous.total_weight += layer.weight;
                display(previous);
            } else {
                if let Some(previous) = current.take() {
                    packed_items.push(previous);
                }
                let mut row = ApiPackedCargoItemDto {
                    x: layer.x,
                    y: layer.y,
                    width: layer.length,
                    height: layer.width,
                    base_height: layer.z,
                    occupied_height: layer.height,
                    top_height: layer.top(),
                    color_argb: layer.color,
                    units_represented: layer.units,
                    stack_count: layer.units,
                    load_count: 1,
                    is_rotated: layer.rotated,
                    is_palletized: layer.pallet,
                    name: layer.name.clone(),
                    total_weight: layer.weight,
                    priority_group: layer.group.clone(),
                    preferred_zone: zone(layer.zone).into(),
                    ..Default::default()
                };
                display(&mut row);
                current = Some(row);
            }
        }
        if let Some(previous) = current {
            packed_items.push(previous);
        }
    }
    let total_packages: i64 = request.items.iter().map(|i| i.units * i.count).sum();
    let packed_packages: i64 = packed_items.iter().map(|i| i.units_represented).sum();
    let total_pallets: i64 = request
        .items
        .iter()
        .filter(|i| i.pallet)
        .map(|i| i.count)
        .sum();
    let packed_pallets: i64 = packed_items
        .iter()
        .filter(|i| i.is_palletized)
        .map(|i| i.load_count)
        .sum();
    let total_volume: D = request
        .items
        .iter()
        .map(|i| i.length * i.width * i.height * D::from(i.count) / D::from(1_000_000))
        .sum();
    let total_weight: D = request
        .items
        .iter()
        .map(|i| i.weight * D::from(i.count))
        .sum();
    let packed_volume: D = packed_items
        .iter()
        .map(|i| i.width * i.height * i.occupied_height / D::from(1_000_000))
        .sum();
    let (cx, cy, dx, dy, within) = gravity(weight, mx, my, request);
    ApiContainerPackingAnalysisDto {
        packed_items,
        total_packages,
        packed_packages,
        unpacked_packages: total_packages - packed_packages,
        total_pallets,
        packed_pallets,
        total_volume,
        total_weight,
        packed_volume,
        packed_weight: weight,
        volume_utilization_percent: percent(packed_volume, request.container.volume),
        weight_utilization_percent: percent(weight, request.container.max_weight),
        containers_needed_by_volume: request.volume_count,
        containers_needed_by_weight: request.weight_count,
        estimated_container_count: request.volume_count.max(request.weight_count),
        center_of_gravity_x: cx,
        center_of_gravity_y: cy,
        center_of_gravity_length_deviation_percent: dx,
        center_of_gravity_width_deviation_percent: dy,
        is_center_of_gravity_within_tolerance: within,
        ..Default::default()
    }
}
fn zone(zone: usize) -> &'static str {
    ["Auto", "Head", "Middle", "Door"][zone]
}
fn percent(value: D, total: D) -> D {
    if total > D::ZERO {
        value / total * D::from(100)
    } else {
        D::ZERO
    }
}
fn can_merge(p: &ApiPackedCargoItemDto, layer: &Layer) -> bool {
    p.name == layer.name
        && p.color_argb == layer.color
        && p.priority_group == layer.group
        && p.preferred_zone == zone(layer.zone)
        && p.is_rotated == layer.rotated
        && p.is_palletized == layer.pallet
        && close(p.x, layer.x)
        && close(p.y, layer.y)
        && close(p.width, layer.length)
        && close(p.height, layer.width)
        && close(p.top_height, layer.z)
}
fn display(p: &mut ApiPackedCargoItemDto) {
    p.display_text = format!(
        "{}{}",
        p.load_count,
        if p.is_palletized { "托" } else { "箱" }
    );
    p.detail_text = if p.is_palletized {
        format!("{}箱", p.units_represented)
    } else {
        format!("高{}cm", p.occupied_height.normalize())
    };
}
