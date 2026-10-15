//! Bounded DB probability-map components with oriented, expanded text boxes.
use crate::geometry::{minimum_box, Point, TextBox};
use anyhow::{bail, Context, Result};

const THRESHOLD: f32 = 0.20;
const BOX_THRESHOLD: f32 = 0.45;
const MAX_BOXES: usize = 2_000;

pub(crate) fn text_boxes(map: &[f32], shape: &[usize]) -> Result<Vec<TextBox>> {
    if shape.len() != 4 || shape[0] != 1 || shape[1] != 1 {
        bail!("detector must return a single probability map");
    }
    let (height, width) = (shape[2], shape[3]);
    let count = width
        .checked_mul(height)
        .context("detector dimensions overflow")?;
    if count == 0
        || width > 960
        || height > 960
        || map.len() != count
        || map
            .iter()
            .any(|score| !score.is_finite() || !(0.0..=1.0).contains(score))
    {
        bail!("invalid detector output dimensions or probabilities");
    }
    let mut seen = vec![false; count];
    let mut queue = Vec::new();
    let mut boxes = Vec::new();
    for start in 0..count {
        if seen[start] || map[start] <= THRESHOLD {
            continue;
        }
        queue.clear();
        queue.push(start);
        seen[start] = true;
        let (mut cursor, mut score) = (0, 0.0);
        let mut boundary = Vec::new();
        while cursor < queue.len() {
            let index = queue[cursor];
            cursor += 1;
            let (x, y) = (index % width, index / width);
            score += map[index];
            let mut edge = false;
            for (nx, ny) in [
                (x.wrapping_sub(1), y),
                (x + 1, y),
                (x, y.wrapping_sub(1)),
                (x, y + 1),
            ] {
                if nx >= width || ny >= height || map[ny * width + nx] <= THRESHOLD {
                    edge = true;
                    continue;
                }
                let next = ny * width + nx;
                if !seen[next] {
                    seen[next] = true;
                    queue.push(next);
                }
            }
            if edge {
                boundary.push(Point {
                    x: x as f32 + 0.5,
                    y: y as f32 + 0.5,
                });
            }
        }
        if score / (queue.len() as f32) < BOX_THRESHOLD {
            continue;
        }
        if let Some(region) = minimum_box(boundary, 1.40) {
            if boxes.len() == MAX_BOXES {
                bail!("too many text regions in the image");
            }
            boxes.push(region);
        }
    }
    boxes.sort_by(|a, b| {
        a.corners[0]
            .y
            .total_cmp(&b.corners[0].y)
            .then(a.corners[0].x.total_cmp(&b.corners[0].x))
    });
    Ok(boxes)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn malformed_maps_fail_without_indexing_or_allocating_unbounded_memory() {
        for shape in [
            &[1, 1, 0, 0][..],
            &[2, 1, 2, 2],
            &[1, 1, usize::MAX, 2],
            &[1, 1, 961, 1],
        ] {
            assert!(text_boxes(&[], shape).is_err());
        }
        assert!(text_boxes(&[f32::NAN], &[1, 1, 1, 1]).is_err());
        assert!(text_boxes(&[1.1], &[1, 1, 1, 1]).is_err());
        assert!(text_boxes(&[0.0; 100], &[1, 1, 10, 10]).unwrap().is_empty());
    }

    #[test]
    fn nearby_rows_and_distant_columns_remain_independent() {
        let mut map = vec![0.0; 80 * 30];
        for (left, top) in [(2, 2), (45, 2), (2, 14)] {
            for y in top..top + 6 {
                for x in left..left + 25 {
                    map[y * 80 + x] = 0.95;
                }
            }
        }
        assert_eq!(text_boxes(&map, &[1, 1, 30, 80]).unwrap().len(), 3);
    }
}
