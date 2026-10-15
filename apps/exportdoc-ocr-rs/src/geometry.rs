//! Oriented text geometry and a bounded, single-pass affine crop.
use image::{Rgb, RgbImage};

#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) struct Point {
    pub x: f32,
    pub y: f32,
}

#[derive(Clone, Debug)]
pub(crate) struct TextBox {
    pub corners: [Point; 4],
}

fn cross(origin: Point, left: Point, right: Point) -> f32 {
    (left.x - origin.x) * (right.y - origin.y) - (left.y - origin.y) * (right.x - origin.x)
}

pub(crate) fn minimum_box(mut points: Vec<Point>, unclip_ratio: f32) -> Option<TextBox> {
    points.sort_by(|a, b| a.x.total_cmp(&b.x).then(a.y.total_cmp(&b.y)));
    points.dedup();
    if points.len() < 3 {
        return None;
    }
    let mut hull = Vec::new();
    for point in &points {
        while hull.len() >= 2 && cross(hull[hull.len() - 2], hull[hull.len() - 1], *point) <= 0.0 {
            hull.pop();
        }
        hull.push(*point);
    }
    let lower = hull.len();
    for point in points.iter().rev().skip(1) {
        while hull.len() > lower && cross(hull[hull.len() - 2], hull[hull.len() - 1], *point) <= 0.0
        {
            hull.pop();
        }
        hull.push(*point);
    }
    hull.pop();
    let mut best = None;
    let mut area = f32::INFINITY;
    for index in 0..hull.len() {
        let a = hull[index];
        let b = hull[(index + 1) % hull.len()];
        let length = (b.x - a.x).hypot(b.y - a.y);
        if length == 0.0 {
            continue;
        }
        let (mut ux, mut uy) = ((b.x - a.x) / length, (b.y - a.y) / length);
        if uy.abs() > ux.abs() {
            (ux, uy) = (-uy, ux);
        }
        if ux < 0.0 {
            (ux, uy) = (-ux, -uy);
        }
        let (vx, vy) = (-uy, ux);
        let (mut min_u, mut max_u, mut min_v, mut max_v) = (
            f32::INFINITY,
            f32::NEG_INFINITY,
            f32::INFINITY,
            f32::NEG_INFINITY,
        );
        for point in &hull {
            let (u, v) = (point.x * ux + point.y * uy, point.x * vx + point.y * vy);
            min_u = min_u.min(u);
            max_u = max_u.max(u);
            min_v = min_v.min(v);
            max_v = max_v.max(v);
        }
        let (width, height) = (max_u - min_u, max_v - min_v);
        if width < 2.0 || height < 2.0 || width * height >= area {
            continue;
        }
        area = width * height;
        let expand = area * unclip_ratio / (2.0 * (width + height));
        let point = |u: f32, v: f32| Point {
            x: u * ux + v * vx,
            y: u * uy + v * vy,
        };
        best = Some(TextBox {
            corners: [
                point(min_u - expand, min_v - expand),
                point(max_u + expand, min_v - expand),
                point(max_u + expand, max_v + expand),
                point(min_u - expand, max_v + expand),
            ],
        });
    }
    best
}

impl TextBox {
    pub fn scale(&mut self, x: f32, y: f32) {
        for point in &mut self.corners {
            point.x *= x;
            point.y *= y;
        }
    }

    pub fn bounds(&self, width: u32, height: u32) -> (u32, u32, u32, u32) {
        let left = self
            .corners
            .iter()
            .map(|p| p.x)
            .fold(f32::INFINITY, f32::min)
            .floor()
            .clamp(0.0, width as f32) as u32;
        let top = self
            .corners
            .iter()
            .map(|p| p.y)
            .fold(f32::INFINITY, f32::min)
            .floor()
            .clamp(0.0, height as f32) as u32;
        let right = self
            .corners
            .iter()
            .map(|p| p.x)
            .fold(0.0, f32::max)
            .ceil()
            .clamp(0.0, width as f32) as u32;
        let bottom = self
            .corners
            .iter()
            .map(|p| p.y)
            .fold(0.0, f32::max)
            .ceil()
            .clamp(0.0, height as f32) as u32;
        (
            left,
            top,
            right.saturating_sub(left),
            bottom.saturating_sub(top),
        )
    }

    pub fn crop_for_recognition(&self, image: &RgbImage) -> (RgbImage, bool) {
        let [a, b, _, d] = self.corners;
        let width = (b.x - a.x).hypot(b.y - a.y);
        let height = (d.x - a.x).hypot(d.y - a.y);
        let vertical = height >= width * 1.5;
        let ratio = if vertical {
            height / width
        } else {
            width / height
        };
        let output_width = (48.0 * ratio).ceil().clamp(16.0, 3200.0) as u32;
        let crop = RgbImage::from_fn(output_width, 48, |x, y| {
            let (u, v) = (
                (x as f32 + 0.5) / output_width as f32,
                (y as f32 + 0.5) / 48.0,
            );
            let (u, v) = if vertical { (v, 1.0 - u) } else { (u, v) };
            sample(
                image,
                a.x + (b.x - a.x) * u + (d.x - a.x) * v,
                a.y + (b.y - a.y) * u + (d.y - a.y) * v,
            )
        });
        (crop, vertical)
    }
}

fn sample(image: &RgbImage, x: f32, y: f32) -> Rgb<u8> {
    let (x, y) = (x - 0.5, y - 0.5);
    let (ix, iy) = (x.floor() as i32, y.floor() as i32);
    let (dx, dy) = (x - x.floor(), y - y.floor());
    let mut channels = [0.0; 3];
    for (ox, oy, weight) in [
        (0, 0, (1.0 - dx) * (1.0 - dy)),
        (1, 0, dx * (1.0 - dy)),
        (0, 1, (1.0 - dx) * dy),
        (1, 1, dx * dy),
    ] {
        let (px, py) = (ix + ox, iy + oy);
        let pixel = if px >= 0 && py >= 0 && px < image.width() as i32 && py < image.height() as i32
        {
            image.get_pixel(px as u32, py as u32).0
        } else {
            [255; 3]
        };
        for channel in 0..3 {
            channels[channel] += f32::from(pixel[channel]) * weight;
        }
    }
    Rgb(channels.map(|value| value.round() as u8))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rotated_box_preserves_the_text_baseline_and_clips_returned_bounds() {
        let text = minimum_box(
            vec![
                Point { x: 10.0, y: 10.0 },
                Point { x: 110.0, y: 0.0 },
                Point { x: 112.0, y: 20.0 },
                Point { x: 12.0, y: 30.0 },
            ],
            0.0,
        )
        .unwrap();
        let [a, b, _, _] = text.corners;
        assert!(((b.y - a.y) / (b.x - a.x) + 0.1).abs() < 0.001);
        let (x, y, width, height) = text.bounds(100, 25);
        assert!(x + width <= 100 && y + height <= 25);
        let (crop, vertical) = text.crop_for_recognition(&RgbImage::new(120, 40));
        assert!(!vertical);
        assert_eq!(crop.height(), 48);
        assert!(crop.width() > 200 && crop.width() <= 3200);
    }
}
