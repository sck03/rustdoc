//! Deterministic, bounded container packing. Geometry and mass use decimal
//! arithmetic; cancellation is supplied by the application, never by the GUI.
mod placement;
mod results;
mod types;

use crate::generated_api::ApiContainerPackingAnalysisDto;
use placement::{Candidate, Planner, Stack};
use rust_decimal::Decimal as D;
use types::Item;
pub use types::{Dimensions, Request, Rules};

pub fn analyze<E>(
    request: &Request,
    mut check: impl FnMut() -> Result<(), E>,
) -> Result<ApiContainerPackingAnalysisDto, E> {
    let mut items = request.items.clone();
    items.sort_by(|a, b| {
        a.sequence
            .cmp(&b.sequence)
            .then_with(|| (a.zone == 0).cmp(&(b.zone == 0)))
            .then_with(|| a.zone.cmp(&b.zone))
            .then_with(|| a.group.to_lowercase().cmp(&b.group.to_lowercase()))
            .then_with(|| (b.length * b.width).cmp(&(a.length * a.width)))
            .then_with(|| b.height.cmp(&a.height))
            .then_with(|| b.weight.cmp(&a.weight))
    });
    let mut planners = (0..4)
        .map(|zone| Planner::new(zone, &request.container))
        .collect::<Vec<_>>();
    let mut stacks: Vec<Stack> = vec![];
    let mut weight = D::ZERO;
    let mut moment_x = D::ZERO;
    let mut moment_y = D::ZERO;
    for item in &items {
        check()?;
        for _ in 0..item.count {
            check()?;
            if item.weight > request.container.max_weight - weight {
                break;
            }
            let mut best: Option<Candidate> = None;
            let orientations = if request.rules.rotate && item.length != item.width {
                2
            } else {
                1
            };
            for orientation in 0..orientations {
                check()?;
                let mut candidates =
                    planners[item.zone].candidates(item, orientation == 1, &stacks, request);
                for (index, stack) in stacks.iter().enumerate() {
                    check()?;
                    if let Some(candidate) = stack.candidate(index, item, orientation == 1, request)
                    {
                        candidates.push(candidate);
                    }
                }
                for mut candidate in candidates {
                    check()?;
                    // Centered alternatives and overhanging layers must also be
                    // collision checked; the floor reservation alone is insufficient.
                    if !candidate.fits(&stacks, request) {
                        continue;
                    }
                    if !candidate.score(weight, moment_x, moment_y, request) {
                        continue;
                    }
                    if best
                        .as_ref()
                        .is_none_or(|previous| candidate.score < previous.score)
                    {
                        best = Some(candidate);
                    }
                }
            }
            let Some(candidate) = best else {
                break;
            };
            weight += candidate.layer.weight;
            moment_x +=
                candidate.layer.weight * (candidate.layer.x + candidate.layer.length / D::TWO);
            moment_y +=
                candidate.layer.weight * (candidate.layer.y + candidate.layer.width / D::TWO);
            if let Some(index) = candidate.stack {
                stacks[index].layers.push(candidate.layer);
            } else {
                planners[item.zone].commit(candidate.reservation.expect("floor candidate"));
                stacks.push(Stack {
                    layers: vec![candidate.layer],
                });
            }
        }
    }
    Ok(results::summarize(
        request, &stacks, weight, moment_x, moment_y,
    ))
}

pub(super) fn close(a: D, b: D) -> bool {
    (a - b).abs() <= D::new(5, 2)
}
pub(super) fn overlap(a: D, size_a: D, b: D, size_b: D) -> D {
    ((a + size_a).min(b + size_b) - a.max(b)).max(D::ZERO)
}
