use super::{Dimensions, Item, Request, close, overlap};
use rust_decimal::Decimal as D;

#[derive(Clone)]
pub(super) struct Layer {
    pub item: Item,
    pub x: D,
    pub y: D,
    pub length: D,
    pub width: D,
    pub z: D,
    pub rotated: bool,
}
impl std::ops::Deref for Layer {
    type Target = Item;
    fn deref(&self) -> &Item {
        &self.item
    }
}
impl Layer {
    pub fn top(&self) -> D {
        self.z + self.height
    }
    fn new(item: &Item, rotated: bool, x: D, y: D, z: D) -> Self {
        Self {
            item: item.clone(),
            x,
            y,
            z,
            rotated,
            length: if rotated { item.width } else { item.length },
            width: if rotated { item.length } else { item.width },
        }
    }
    fn overlaps(&self, other: &Self) -> bool {
        overlap(self.x, self.length, other.x, other.length) > D::ZERO
            && overlap(self.y, self.width, other.y, other.width) > D::ZERO
            && overlap(self.z, self.height, other.z, other.height) > D::ZERO
    }
}
pub(super) struct Stack {
    pub layers: Vec<Layer>,
}
impl Stack {
    pub fn candidate(
        &self,
        index: usize,
        item: &Item,
        rotated: bool,
        request: &Request,
    ) -> Option<Candidate> {
        let top = self.layers.last()?;
        if top.group_key() != item.group_key() || (item.zone != 0 && top.zone != item.zone) {
            return None;
        }
        let mut layer = Layer::new(item, rotated, D::ZERO, D::ZERO, top.top());
        layer.item.zone = top.zone;
        layer.x = top.x + (top.length - layer.length) / D::TWO;
        layer.y = top.y + (top.width - layer.width) / D::TWO;
        let support = (overlap(layer.x, layer.length, top.x, top.length)
            * overlap(layer.y, layer.width, top.y, top.width))
        .checked_div(layer.length * layer.width)?
            * D::from(100);
        let exact = close(layer.length, top.length) && close(layer.width, top.width);
        if (request.rules.same_footprint && !exact)
            || support + D::new(1, 2) < request.rules.support
        {
            return None;
        }
        let mut above = layer.weight;
        for existing in self.layers.iter().rev() {
            if existing.top_load > D::ZERO && above > existing.top_load + D::new(1, 2) {
                return None;
            }
            above += existing.weight;
        }
        Some(Candidate {
            layer,
            stack: Some(index),
            reservation: None,
            support,
            exact,
            score: [D::ZERO; 8],
        })
    }
}
#[derive(Clone, Copy)]
pub(super) struct Reservation {
    x: D,
    y: D,
    slice: D,
    width: D,
    new_slice: bool,
}
pub(super) struct Planner {
    zone: usize,
    start: D,
    end: D,
    length: D,
    width: D,
    next_x: D,
    slice: D,
    next_y: D,
}
impl Planner {
    pub fn new(zone: usize, dimensions: &Dimensions) -> Self {
        let segment = dimensions.length / D::from(3);
        let start = if zone > 0 {
            segment * D::from(zone - 1)
        } else {
            D::ZERO
        };
        Self {
            zone,
            start,
            end: if zone > 0 {
                segment * D::from(zone)
            } else {
                dimensions.length
            },
            length: dimensions.length,
            width: dimensions.width,
            next_x: start,
            slice: D::ZERO,
            next_y: D::ZERO,
        }
    }
    fn preview(&self, length: D, width: D, stacks: &[Stack]) -> Option<Reservation> {
        if width > self.width {
            return None;
        }
        let (mut x, mut slice, mut y) = (self.next_x, self.slice, self.next_y);
        for _ in 0..128 {
            let new_slice;
            if slice == D::ZERO {
                slice = length;
                y = D::ZERO;
                new_slice = true;
            } else if y + width <= self.width && x + slice.max(length) <= self.length {
                slice = slice.max(length);
                new_slice = false;
            } else {
                x += slice;
                slice = length;
                y = D::ZERO;
                new_slice = true;
            }
            if x < self.start || x >= self.end || x + slice > self.length {
                return None;
            }
            let occupied = stacks
                .iter()
                .filter_map(|stack| stack.layers.first())
                .find(|base| {
                    overlap(x, length, base.x, base.length) > D::ZERO
                        && overlap(y, width, base.y, base.width) > D::ZERO
                });
            if let Some(base) = occupied {
                x = (base.x + base.length).max(x + slice);
                slice = D::ZERO;
                y = D::ZERO;
            } else {
                return Some(Reservation {
                    x,
                    y,
                    slice,
                    width,
                    new_slice,
                });
            }
        }
        None
    }
    pub fn candidates(
        &self,
        item: &Item,
        rotated: bool,
        stacks: &[Stack],
        request: &Request,
    ) -> Vec<Candidate> {
        let (length, width) = if rotated {
            (item.width, item.length)
        } else {
            (item.length, item.width)
        };
        let Some(reservation) = self.preview(length, width, stacks) else {
            return vec![];
        };
        let mut positions = vec![(reservation.x, reservation.y)];
        if request.rules.enforce_center {
            let y = (self.width - width) / D::TWO;
            if y >= D::ZERO && !close(y, reservation.y) {
                positions.push((reservation.x, y));
            }
            if self.zone == 0 && close(reservation.x, D::ZERO) && close(reservation.y, D::ZERO) {
                let x = (self.length - length) / D::TWO;
                if x >= D::ZERO {
                    positions.push((x, reservation.y));
                    positions.push((x, y));
                }
            }
        }
        positions
            .into_iter()
            .map(|(x, y)| Candidate {
                layer: Layer::new(item, rotated, x, y, D::ZERO),
                stack: None,
                reservation: Some(Reservation {
                    x,
                    y,
                    ..reservation
                }),
                support: D::from(100),
                exact: true,
                score: [D::ZERO; 8],
            })
            .collect()
    }
    pub fn commit(&mut self, reservation: Reservation) {
        if reservation.new_slice {
            self.next_x = reservation.x;
            self.slice = reservation.slice;
        } else {
            self.slice = self.slice.max(reservation.slice);
        }
        self.next_y = reservation.y + reservation.width;
    }
}
pub(super) struct Candidate {
    pub layer: Layer,
    pub stack: Option<usize>,
    pub reservation: Option<Reservation>,
    support: D,
    exact: bool,
    pub score: [D; 8],
}
impl Candidate {
    pub fn fits(&self, stacks: &[Stack], request: &Request) -> bool {
        let p = &self.layer;
        p.x >= D::ZERO
            && p.y >= D::ZERO
            && p.z >= D::ZERO
            && p.x + p.length <= request.container.length
            && p.y + p.width <= request.container.width
            && p.top() <= request.container.height
            && !stacks
                .iter()
                .flat_map(|s| &s.layers)
                .any(|other| p.overlaps(other))
    }
    pub fn score(&mut self, weight: D, mx: D, my: D, request: &Request) -> bool {
        let p = &self.layer;
        let primary = D::from(i32::from(self.stack.is_none()));
        let rotation = D::from(i32::from(p.rotated));
        let support = D::from(i32::from(!self.exact));
        self.score = if request.rules.enforce_center {
            let cg = gravity(
                weight + p.weight,
                mx + p.weight * (p.x + p.length / D::TWO),
                my + p.weight * (p.y + p.width / D::TWO),
                request,
            );
            if !cg.4 {
                return false;
            }
            [
                primary,
                cg.2 + cg.3,
                p.z,
                p.x,
                p.y,
                rotation,
                support,
                -self.support,
            ]
        } else {
            [
                primary,
                D::from(p.zone.saturating_sub(1)),
                p.x,
                p.y,
                p.z,
                rotation,
                support,
                -self.support,
            ]
        };
        true
    }
}
pub(super) fn gravity(weight: D, mx: D, my: D, request: &Request) -> (D, D, D, D, bool) {
    let x = request.container.length / D::TWO;
    let y = request.container.width / D::TWO;
    if weight <= D::ZERO {
        return (x, y, D::ZERO, D::ZERO, true);
    }
    let (cx, cy) = (mx / weight, my / weight);
    let (dx, dy) = (
        (cx - x).abs() / x * D::from(100),
        (cy - y).abs() / y * D::from(100),
    );
    (
        cx,
        cy,
        dx,
        dy,
        dx <= request.rules.tolerance && dy <= request.rules.tolerance,
    )
}
