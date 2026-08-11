use anyhow::{anyhow, bail, Context, Result};
use image::{imageops, RgbImage};
use ort::{session::Session, value::Tensor};
use serde::{Deserialize, Serialize};
use std::{
    env, fs,
    io::{self, BufRead, Write},
    path::{Path, PathBuf},
};

mod image_processing;
mod recognition;

use image_processing::{
    box_score, component_rects, det_size, expand_ratio, merge_lines, pad_rect, pixel_count,
    recognition_candidates, rgb_tensor, validate_image_dimensions, Rect,
};
use recognition::{decode_ctc, load_labels, text_quality};

const DET_BOX_THRESHOLD: f32 = 0.45;
const DET_UNCLIP_RATIO: f32 = 1.40;
const REC_HEIGHT: u32 = 48;
const REC_MAX_WIDTH: u32 = 3200;
const MAX_IMAGE_BYTES: u64 = 25 * 1024 * 1024;
const MAX_DETECTED_BOXES: usize = 2_000;
const MAX_MERGED_LINES: usize = 1_000;
const MAX_FALLBACK_RECOGNITION_PIXELS: u64 = 4_000_000;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Request {
    id: String,
    command: String,
    image_path: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Response {
    id: String,
    success: bool,
    full_text: String,
    lines: Vec<OcrLine>,
    error: Option<String>,
    engine: String,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct OcrLine {
    text: String,
    confidence: f32,
    x: u32,
    y: u32,
    width: u32,
    height: u32,
}

struct Engine {
    det: Session,
    rec: Session,
    labels: Vec<String>,
}

fn main() -> Result<()> {
    let args: Vec<String> = env::args().collect();
    let model_root = argument(&args, "--model-root")
        .map(PathBuf::from)
        .context("--model-root is required")?;
    let allowed_root = match argument(&args, "--allowed-root") {
        Some(value) => PathBuf::from(value),
        None => env::current_dir().context("failed to resolve the OCR working directory")?,
    };
    let mut engine = Engine::load(&model_root)?;
    if args.iter().any(|v| v == "--health") {
        println!(
            "{}",
            serde_json::json!({"ready":true,"engine":"rust-ort-ppocrv6","modelRoot":model_root})
        );
        return Ok(());
    }
    let stdin = io::stdin();
    let mut stdout = io::BufWriter::new(io::stdout());
    for line in stdin.lock().lines() {
        let line = line?;
        if line.trim().is_empty() {
            continue;
        }
        let payload = line.trim_start_matches('\u{feff}');
        let request: Request = match serde_json::from_str(payload) {
            Ok(v) => v,
            Err(e) => {
                write_response(
                    &mut stdout,
                    Response::error("", format!("invalid request: {e}")),
                )?;
                continue;
            }
        };
        if request.command == "shutdown" {
            write_response(&mut stdout, Response::ok(&request.id, vec![]))?;
            break;
        }
        if request.command == "health" {
            write_response(&mut stdout, Response::ok(&request.id, vec![]))?;
            continue;
        }
        let response = match request.image_path.as_deref() {
            Some(path) if request.command == "recognize" => {
                match validate_image_path(path, &allowed_root)
                    .and_then(|p| engine.recognize_path(&p))
                {
                    Ok(lines) => Response::ok(&request.id, lines),
                    Err(e) => Response::error(&request.id, format!("{e:#}")),
                }
            }
            _ => Response::error(
                &request.id,
                "unsupported command or missing imagePath".into(),
            ),
        };
        write_response(&mut stdout, response)?;
    }
    Ok(())
}

impl Response {
    fn ok(id: &str, lines: Vec<OcrLine>) -> Self {
        Self {
            id: id.into(),
            success: true,
            full_text: lines
                .iter()
                .map(|l| l.text.as_str())
                .collect::<Vec<_>>()
                .join("\n"),
            lines,
            error: None,
            engine: "rust-ort-ppocrv6".into(),
        }
    }
    fn error(id: &str, error: String) -> Self {
        Self {
            id: id.into(),
            success: false,
            full_text: String::new(),
            lines: vec![],
            error: Some(error),
            engine: "rust-ort-ppocrv6".into(),
        }
    }
}

impl Engine {
    fn load(root: &Path) -> Result<Self> {
        let det_path = root.join("det/inference.onnx");
        let rec_path = root.join("rec/inference.onnx");
        if !det_path.is_file() || !rec_path.is_file() {
            bail!(
                "PP-OCRv6 model files are incomplete under {}",
                root.display()
            );
        }
        let labels = load_labels(&root.join("rec/inference.yml"))?;
        let det_builder = Session::builder().map_err(|e| anyhow!(e.to_string()))?;
        let mut det_builder = det_builder
            .with_intra_threads(thread_count())
            .map_err(|e| anyhow!(e.to_string()))?;
        let det = det_builder
            .commit_from_file(det_path)
            .map_err(|e| anyhow!(e.to_string()))?;
        let rec_builder = Session::builder().map_err(|e| anyhow!(e.to_string()))?;
        let mut rec_builder = rec_builder
            .with_intra_threads(thread_count())
            .map_err(|e| anyhow!(e.to_string()))?;
        let rec = rec_builder
            .commit_from_file(rec_path)
            .map_err(|e| anyhow!(e.to_string()))?;
        Ok(Self { det, rec, labels })
    }

    fn recognize_path(&mut self, path: &Path) -> Result<Vec<OcrLine>> {
        let metadata =
            fs::metadata(path).with_context(|| format!("cannot inspect {}", path.display()))?;
        if metadata.len() == 0 || metadata.len() > MAX_IMAGE_BYTES {
            bail!("image must be non-empty and no larger than 25 MB")
        }
        let (width, height) = image::image_dimensions(path)
            .with_context(|| format!("cannot inspect image dimensions for {}", path.display()))?;
        validate_image_dimensions(width, height)?;
        let encoded =
            std::fs::read(path).with_context(|| format!("cannot read {}", path.display()))?;
        let image = image::load_from_memory(&encoded)
            .with_context(|| format!("cannot decode {}", path.display()))?
            .to_rgb8();
        let mut rects = self.detect(&image)?;
        if rects.is_empty() {
            if pixel_count(image.width(), image.height()) > MAX_FALLBACK_RECOGNITION_PIXELS {
                bail!("no text regions detected; the image is too large for full-frame recognition")
            }
            rects.push(Rect {
                x: 0,
                y: 0,
                width: image.width(),
                height: image.height(),
            });
        }
        rects = merge_lines(rects);
        if rects.len() > MAX_MERGED_LINES {
            bail!("too many text lines were detected in the image")
        }
        let mut lines = Vec::new();
        for rect in rects {
            let padded = pad_rect(rect, image.width(), image.height(), 10);
            let crop = imageops::crop_imm(&image, padded.x, padded.y, padded.width, padded.height)
                .to_image();
            let mut best = (String::new(), 0.0f32);
            for candidate in recognition_candidates(&crop) {
                let result = self.recognize_image(&candidate)?;
                if !result.0.trim().is_empty()
                    && (text_quality(&result.0), result.1) > (text_quality(&best.0), best.1)
                {
                    best = result;
                }
            }
            if !best.0.trim().is_empty() {
                lines.push(OcrLine {
                    text: best.0,
                    confidence: best.1,
                    x: padded.x,
                    y: padded.y,
                    width: padded.width,
                    height: padded.height,
                });
            }
        }
        lines.sort_by_key(|l| (l.y, l.x));
        Ok(lines)
    }

    fn detect(&mut self, image: &RgbImage) -> Result<Vec<Rect>> {
        let (rw, rh) = det_size(image.width(), image.height());
        let resized = imageops::resize(image, rw, rh, imageops::FilterType::Triangle);
        let input = rgb_tensor(&resized, true);
        let outputs = self.det.run(ort::inputs![Tensor::from_array(input)?])?;
        let output = outputs[0].try_extract_array::<f32>()?;
        let shape = output.shape();
        if shape.len() < 2 {
            bail!("invalid detection output shape");
        }
        let h = shape[shape.len() - 2];
        let w = shape[shape.len() - 1];
        let map: Vec<f32> = output.iter().copied().collect();
        let rects = component_rects(&map, w, h)
            .into_iter()
            .filter_map(|r| {
                let score = box_score(&map, w, r);
                if score < DET_BOX_THRESHOLD {
                    return None;
                }
                let mapped = Rect {
                    x: (r.x as f64 * image.width() as f64 / w as f64).floor() as u32,
                    y: (r.y as f64 * image.height() as f64 / h as f64).floor() as u32,
                    width: (r.width as f64 * image.width() as f64 / w as f64).ceil() as u32,
                    height: (r.height as f64 * image.height() as f64 / h as f64).ceil() as u32,
                };
                let expanded =
                    expand_ratio(mapped, image.width(), image.height(), DET_UNCLIP_RATIO);
                (expanded.width >= 3 && expanded.height >= 3).then_some(expanded)
            })
            .take(MAX_DETECTED_BOXES + 1)
            .collect::<Vec<_>>();
        if rects.len() > MAX_DETECTED_BOXES {
            bail!("too many text regions were detected in the image")
        }
        Ok(rects)
    }

    fn recognize_image(&mut self, image: &RgbImage) -> Result<(String, f32)> {
        let width = ((image.width() as f64 * (REC_HEIGHT as f64 / image.height().max(1) as f64))
            .ceil() as u32)
            .clamp(16, REC_MAX_WIDTH);
        let resized = imageops::resize(image, width, REC_HEIGHT, imageops::FilterType::CatmullRom);
        let input = rgb_tensor(&resized, false);
        let outputs = self.rec.run(ort::inputs![Tensor::from_array(input)?])?;
        let out = outputs[0].try_extract_array::<f32>()?;
        decode_ctc(out.iter().copied().collect(), out.shape(), &self.labels)
    }
}

fn validate_image_path(value: &str, root: &Path) -> Result<PathBuf> {
    let root = fs::canonicalize(root)?;
    let path = fs::canonicalize(value)?;
    if !path.starts_with(&root) {
        bail!("image path is outside allowed root")
    }
    Ok(path)
}

fn write_response(w: &mut impl Write, r: Response) -> Result<()> {
    serde_json::to_writer(&mut *w, &r)?;
    w.write_all(b"\n")?;
    w.flush()?;
    Ok(())
}

fn argument(args: &[String], name: &str) -> Option<String> {
    args.windows(2).find(|p| p[0] == name).map(|p| p[1].clone())
}

fn thread_count() -> usize {
    std::thread::available_parallelism()
        .map(|n| n.get().clamp(1, 4))
        .unwrap_or(2)
}
