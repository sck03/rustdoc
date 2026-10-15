use crate::{
    detection::text_boxes,
    image_processing::{det_size, rgb_tensor},
    recognition::{decode_ctc, load_labels},
    runtime,
};
use anyhow::{anyhow, Context, Result};
use image::{imageops, RgbImage};
use ort::{session::Session, value::Tensor};
use serde::Serialize;
use std::path::Path;

#[derive(Serialize)]
pub(crate) struct OcrLine {
    pub text: String,
    pub confidence: f32,
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

pub(crate) struct Engine {
    det: Session,
    rec: Session,
    labels: Vec<String>,
}

impl Engine {
    pub fn load(root: &Path) -> Result<Self> {
        runtime::initialize()?;
        let load = |name: &str| -> Result<Session> {
            let threads = std::thread::available_parallelism()
                .map(|n| n.get().clamp(1, 4))
                .unwrap_or(2);
            Session::builder()
                .map_err(|e| anyhow!(e.to_string()))?
                .with_intra_threads(threads)
                .map_err(|e| anyhow!(e.to_string()))?
                .commit_from_file(root.join(name).join("inference.onnx"))
                .map_err(|e| anyhow!(e.to_string()))
                .with_context(|| format!("cannot load {name} model"))
        };
        Ok(Self {
            labels: load_labels(&root.join("rec/inference.yml"))?,
            det: load("det")?,
            rec: load("rec")?,
        })
    }

    pub fn recognize(&mut self, image: &RgbImage) -> Result<Vec<OcrLine>> {
        let (width, height) = det_size(image.width(), image.height());
        let resized = imageops::resize(image, width, height, imageops::FilterType::Triangle);
        let outputs = self.det.run(ort::inputs![Tensor::from_array(rgb_tensor(
            &resized, true
        ))?])?;
        let (shape, map) = outputs[0].try_extract_tensor::<f32>()?;
        let shape: Vec<usize> = shape
            .iter()
            .map(|size| usize::try_from(*size).unwrap_or(usize::MAX))
            .collect();
        let mut boxes = text_boxes(map, &shape)?;
        drop(outputs);
        let mut lines = Vec::with_capacity(boxes.len());
        let mut directions = [0usize; 4];
        for region in &mut boxes {
            region.scale(
                image.width() as f32 / width as f32,
                image.height() as f32 / height as f32,
            );
            let (crop, vertical) = region.crop_for_recognition(image);
            let mut result = self.recognize_line(&crop)?;
            let mut flipped = false;
            // Most text needs one inference. Only uncertain orientation tries a half-turn;
            // never prefer a longer, low-confidence hallucination over a confident result.
            if result.1 < 0.85 {
                let rotated = self.recognize_line(&imageops::rotate180(&crop))?;
                if rotated.1 > result.1 {
                    result = rotated;
                    flipped = true;
                }
            }
            if result.1 >= 0.50 && !result.0.trim().is_empty() {
                directions[usize::from(vertical) + 2 * usize::from(flipped)] += 1;
                let (x, y, width, height) = region.bounds(image.width(), image.height());
                lines.push(OcrLine {
                    text: result.0,
                    confidence: result.1,
                    x,
                    y,
                    width,
                    height,
                });
            }
        }
        // Sort in the dominant recognized orientation, keeping boxes in source coordinates.
        if let Some(direction) = directions.iter().position(|count| *count > lines.len() / 2) {
            order_lines(&mut lines, direction);
        }
        Ok(lines)
    }

    fn recognize_line(&mut self, image: &RgbImage) -> Result<(String, f32)> {
        let outputs = self
            .rec
            .run(ort::inputs![Tensor::from_array(rgb_tensor(image, false))?])?;
        let (shape, values) = outputs[0].try_extract_tensor::<f32>()?;
        let shape: Vec<usize> = shape
            .iter()
            .map(|size| usize::try_from(*size).unwrap_or(usize::MAX))
            .collect();
        decode_ctc(values, &shape, &self.labels)
    }
}

fn order_lines(lines: &mut [OcrLine], direction: usize) {
    lines.sort_by_key(|line| match direction {
        1 => (line.x, u32::MAX - line.y),
        2 => (u32::MAX - line.y, u32::MAX - line.x),
        3 => (u32::MAX - line.x, line.y),
        _ => (line.y, line.x),
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sideways_text_is_ordered_by_columns_without_changing_source_boxes() {
        let mut lines = vec![(80, 30), (20, 90), (50, 10)]
            .into_iter()
            .map(|(x, y)| OcrLine {
                text: x.to_string(),
                confidence: 0.99,
                x,
                y,
                width: 10,
                height: 40,
            })
            .collect::<Vec<_>>();
        order_lines(&mut lines, 1);
        assert_eq!(
            lines.iter().map(|line| line.x).collect::<Vec<_>>(),
            [20, 50, 80]
        );
        assert_eq!(lines[0].y, 90);
        order_lines(&mut lines, 3);
        assert_eq!(
            lines.iter().map(|line| line.x).collect::<Vec<_>>(),
            [80, 50, 20]
        );
    }
}
