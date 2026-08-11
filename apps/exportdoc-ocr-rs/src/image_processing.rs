use anyhow::{bail, Result};
use image::{imageops, DynamicImage, GrayImage, Rgb, RgbImage};
use ndarray::Array4;
use std::collections::VecDeque;

const DET_MAX_SIDE: u32 = 960;
const DET_BINARY_THRESHOLD: f32 = 0.20;
const MAX_IMAGE_SIDE: u32 = 16_384;
const MAX_IMAGE_PIXELS: u64 = 40_000_000;
const MAX_RECOGNITION_CANDIDATE_PIXELS: u64 = 4_000_000;

#[derive(Clone, Copy)]
pub(crate) struct Rect {
    pub(crate) x: u32,
    pub(crate) y: u32,
    pub(crate) width: u32,
    pub(crate) height: u32,
}

pub(crate) fn rgb_tensor(img: &RgbImage, detection: bool) -> Array4<f32> {
    let (width, height) = img.dimensions();
    let mut tensor = Array4::zeros((1, 3, height as usize, width as usize));
    let mean = [0.485, 0.456, 0.406];
    let standard_deviation = [0.229, 0.224, 0.225];
    for y in 0..height {
        for x in 0..width {
            let pixel = img.get_pixel(x, y);
            let bgr = [pixel[2], pixel[1], pixel[0]];
            for channel in 0..3 {
                let value = bgr[channel] as f32 / 255.0;
                tensor[[0, channel, y as usize, x as usize]] = if detection {
                    (value - mean[channel]) / standard_deviation[channel]
                } else {
                    (value - 0.5) / 0.5
                };
            }
        }
    }
    tensor
}

pub(crate) fn component_rects(map: &[f32], width: usize, height: usize) -> Vec<Rect> {
    let mut seen = vec![false; width * height];
    let mut rectangles = vec![];
    for index in 0..width * height {
        if seen[index] || map[index] <= DET_BINARY_THRESHOLD {
            continue;
        }
        let mut queue = VecDeque::from([index]);
        seen[index] = true;
        let (mut min_x, mut max_x, mut min_y, mut max_y) = (width, 0, height, 0);
        while let Some(value) = queue.pop_front() {
            let x = value % width;
            let y = value / width;
            min_x = min_x.min(x);
            max_x = max_x.max(x);
            min_y = min_y.min(y);
            max_y = max_y.max(y);
            for (next_x, next_y) in [
                (x.wrapping_sub(1), y),
                (x + 1, y),
                (x, y.wrapping_sub(1)),
                (x, y + 1),
            ] {
                if next_x < width && next_y < height {
                    let next = next_y * width + next_x;
                    if !seen[next] && map[next] > DET_BINARY_THRESHOLD {
                        seen[next] = true;
                        queue.push_back(next);
                    }
                }
            }
        }
        let rectangle = Rect {
            x: min_x as u32,
            y: min_y as u32,
            width: (max_x - min_x + 1) as u32,
            height: (max_y - min_y + 1) as u32,
        };
        if rectangle.width >= 3 && rectangle.height >= 3 {
            rectangles.push(rectangle);
        }
    }
    rectangles
}

pub(crate) fn box_score(map: &[f32], width: usize, rectangle: Rect) -> f32 {
    let mut sum = 0.;
    let mut count = 0;
    for y in rectangle.y..rectangle.y + rectangle.height {
        for x in rectangle.x..rectangle.x + rectangle.width {
            let value = map[y as usize * width + x as usize];
            if value > DET_BINARY_THRESHOLD {
                sum += value;
                count += 1;
            }
        }
    }
    if count == 0 {
        0.
    } else {
        sum / count as f32
    }
}

pub(crate) fn det_size(width: u32, height: u32) -> (u32, u32) {
    let scale = (DET_MAX_SIDE as f64 / width.max(height) as f64).min(1.);
    (
        round32((width as f64 * scale).round() as u32),
        round32((height as f64 * scale).round() as u32),
    )
}

fn round32(value: u32) -> u32 {
    (((value.max(32) + 16) / 32) * 32).max(32)
}

pub(crate) fn expand_ratio(rectangle: Rect, max_width: u32, max_height: u32, ratio: f32) -> Rect {
    let expand_x = rectangle.width as f32 * (ratio - 1.) / 2.;
    let expand_y = rectangle.height as f32 * (ratio - 1.) / 2.;
    let x = (rectangle.x as f32 - expand_x).floor().max(0.) as u32;
    let y = (rectangle.y as f32 - expand_y).floor().max(0.) as u32;
    let right = ((rectangle.x + rectangle.width) as f32 + expand_x)
        .ceil()
        .min(max_width as f32) as u32;
    let bottom = ((rectangle.y + rectangle.height) as f32 + expand_y)
        .ceil()
        .min(max_height as f32) as u32;
    Rect {
        x,
        y,
        width: right.saturating_sub(x),
        height: bottom.saturating_sub(y),
    }
}

pub(crate) fn pad_rect(rectangle: Rect, max_width: u32, max_height: u32, padding: u32) -> Rect {
    let x = rectangle.x.saturating_sub(padding);
    let y = rectangle.y.saturating_sub(padding);
    let right = (rectangle.x + rectangle.width + padding).min(max_width);
    let bottom = (rectangle.y + rectangle.height + padding).min(max_height);
    Rect {
        x,
        y,
        width: right - x,
        height: bottom - y,
    }
}

pub(crate) fn merge_lines(mut rectangles: Vec<Rect>) -> Vec<Rect> {
    rectangles.sort_by_key(|rectangle| (rectangle.y, rectangle.x));
    let mut merged: Vec<Rect> = vec![];
    for rectangle in rectangles {
        if let Some(index) = merged.iter().position(|existing| {
            ((existing.y + existing.height / 2) as i64
                - (rectangle.y + rectangle.height / 2) as i64)
                .unsigned_abs()
                <= 18.max(existing.height.min(rectangle.height)) as u64
        }) {
            let existing = merged[index];
            let x = existing.x.min(rectangle.x);
            let y = existing.y.min(rectangle.y);
            let right = (existing.x + existing.width).max(rectangle.x + rectangle.width);
            let bottom = (existing.y + existing.height).max(rectangle.y + rectangle.height);
            merged[index] = Rect {
                x,
                y,
                width: right - x,
                height: bottom - y,
            };
        } else {
            merged.push(rectangle);
        }
    }
    merged.sort_by_key(|rectangle| (rectangle.y, rectangle.x));
    merged
}

pub(crate) fn recognition_candidates(img: &RgbImage) -> Vec<RgbImage> {
    let base = resize_to_pixel_limit(img, MAX_RECOGNITION_CANDIDATE_PIXELS);
    let base_pixels = pixel_count(base.width(), base.height());
    let mut candidates = vec![base.clone()];
    if base_pixels <= MAX_RECOGNITION_CANDIDATE_PIXELS / 4 {
        candidates.push(imageops::resize(
            &base,
            base.width() * 2,
            base.height() * 2,
            imageops::FilterType::CatmullRom,
        ));
    }
    let gray = DynamicImage::ImageRgb8(base).to_luma8();
    let threshold = otsu(&gray);
    let binary = RgbImage::from_fn(gray.width(), gray.height(), |x, y| {
        let value = if gray.get_pixel(x, y)[0] > threshold {
            255
        } else {
            0
        };
        Rgb([value, value, value])
    });
    candidates.push(binary);
    candidates
}

pub(crate) fn validate_image_dimensions(width: u32, height: u32) -> Result<()> {
    if width == 0 || height == 0 {
        bail!("image dimensions must be non-zero")
    }
    if width > MAX_IMAGE_SIDE || height > MAX_IMAGE_SIDE {
        bail!("image width and height must not exceed {MAX_IMAGE_SIDE} pixels")
    }
    if pixel_count(width, height) > MAX_IMAGE_PIXELS {
        bail!("image must not exceed {MAX_IMAGE_PIXELS} pixels")
    }
    Ok(())
}

fn resize_to_pixel_limit(img: &RgbImage, maximum_pixels: u64) -> RgbImage {
    let pixels = pixel_count(img.width(), img.height());
    if pixels <= maximum_pixels || maximum_pixels == 0 {
        return img.clone();
    }

    let scale = (maximum_pixels as f64 / pixels as f64).sqrt();
    let width = ((img.width() as f64 * scale).floor() as u32).max(1);
    let height = ((img.height() as f64 * scale).floor() as u32).max(1);
    imageops::resize(img, width, height, imageops::FilterType::Triangle)
}

pub(crate) fn pixel_count(width: u32, height: u32) -> u64 {
    u64::from(width) * u64::from(height)
}

fn otsu(gray: &GrayImage) -> u8 {
    let mut histogram = [0u64; 256];
    for pixel in gray.pixels() {
        histogram[pixel[0] as usize] += 1;
    }
    let total = u64::from(gray.width()) * u64::from(gray.height());
    let sum: f64 = histogram
        .iter()
        .enumerate()
        .map(|(index, count)| index as f64 * (*count as f64))
        .sum();
    let (mut background_weight, mut background_sum, mut best, mut threshold) =
        (0u64, 0f64, -1f64, 0u8);
    for (candidate, count) in histogram.iter().enumerate() {
        background_weight += *count;
        if background_weight == 0 {
            continue;
        }
        let foreground_weight = total - background_weight;
        if foreground_weight == 0 {
            break;
        }
        background_sum += candidate as f64 * (*count as f64);
        let background_mean = background_sum / background_weight as f64;
        let foreground_mean = (sum - background_sum) / foreground_weight as f64;
        let between = background_weight as f64
            * foreground_weight as f64
            * (background_mean - foreground_mean).powi(2);
        if between > best {
            best = between;
            threshold = candidate as u8;
        }
    }
    threshold
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn merge_lines_combines_nearby_boxes_but_keeps_separate_rows() {
        let merged = merge_lines(vec![
            Rect {
                x: 10,
                y: 10,
                width: 40,
                height: 20,
            },
            Rect {
                x: 55,
                y: 12,
                width: 35,
                height: 18,
            },
            Rect {
                x: 12,
                y: 70,
                width: 30,
                height: 18,
            },
        ]);
        assert_eq!(merged.len(), 2);
        assert_eq!((merged[0].x, merged[0].width), (10, 80));
    }

    #[test]
    fn otsu_separates_dark_and_light_pixels() {
        let mut image = GrayImage::new(20, 10);
        for (x, _, pixel) in image.enumerate_pixels_mut() {
            pixel.0[0] = if x < 10 { 10 } else { 240 };
        }
        let threshold = otsu(&image);
        assert!((10..=240).contains(&threshold));
    }

    #[test]
    fn image_dimensions_reject_decompression_bomb_boundaries() {
        assert!(validate_image_dimensions(0, 100).is_err());
        assert!(validate_image_dimensions(MAX_IMAGE_SIDE + 1, 1).is_err());
        assert!(validate_image_dimensions(10_000, 5_000).is_err());
        assert!(validate_image_dimensions(5_000, 7_000).is_ok());
    }

    #[test]
    fn recognition_preprocessing_respects_pixel_limit() {
        let image = RgbImage::new(100, 100);
        let resized = resize_to_pixel_limit(&image, 2_500);
        assert!(pixel_count(resized.width(), resized.height()) <= 2_500);
        assert_eq!((resized.width(), resized.height()), (50, 50));
    }
}
