use anyhow::{anyhow, bail, Context, Result};
use image::{imageops, DynamicImage, GrayImage, Rgb, RgbImage};
use ndarray::Array4;
use ort::{session::Session, value::Tensor};
use serde::{Deserialize, Serialize};
use std::{
    collections::VecDeque,
    env, fs,
    io::{self, BufRead, Write},
    path::{Path, PathBuf},
};

const DET_MAX_SIDE: u32 = 960;
const DET_BINARY_THRESHOLD: f32 = 0.20;
const DET_BOX_THRESHOLD: f32 = 0.45;
const DET_UNCLIP_RATIO: f32 = 1.40;
const REC_HEIGHT: u32 = 48;
const REC_MAX_WIDTH: u32 = 3200;

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

#[derive(Clone, Copy)]
struct Rect {
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
    let allowed_root = argument(&args, "--allowed-root")
        .map(PathBuf::from)
        .unwrap_or_else(|| env::current_dir().unwrap());
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
        let encoded =
            std::fs::read(path).with_context(|| format!("cannot read {}", path.display()))?;
        let image = image::load_from_memory(&encoded)
            .with_context(|| format!("cannot decode {}", path.display()))?
            .to_rgb8();
        let mut rects = self.detect(&image)?;
        if rects.is_empty() {
            rects.push(Rect {
                x: 0,
                y: 0,
                width: image.width(),
                height: image.height(),
            });
        }
        rects = merge_lines(rects);
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
        Ok(component_rects(&map, w, h)
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
            .collect())
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

fn rgb_tensor(img: &RgbImage, detection: bool) -> Array4<f32> {
    let (w, h) = img.dimensions();
    let mut a = Array4::zeros((1, 3, h as usize, w as usize));
    let mean = [0.485, 0.456, 0.406];
    let std = [0.229, 0.224, 0.225];
    for y in 0..h {
        for x in 0..w {
            let p = img.get_pixel(x, y);
            let bgr = [p[2], p[1], p[0]];
            for c in 0..3 {
                let v = bgr[c] as f32 / 255.0;
                a[[0, c, y as usize, x as usize]] = if detection {
                    (v - mean[c]) / std[c]
                } else {
                    (v - 0.5) / 0.5
                };
            }
        }
    }
    a
}
fn component_rects(map: &[f32], w: usize, h: usize) -> Vec<Rect> {
    let mut seen = vec![false; w * h];
    let mut out = vec![];
    for i in 0..w * h {
        if seen[i] || map[i] <= DET_BINARY_THRESHOLD {
            continue;
        }
        let mut q = VecDeque::from([i]);
        seen[i] = true;
        let (mut minx, mut maxx, mut miny, mut maxy) = (w, 0, h, 0);
        while let Some(v) = q.pop_front() {
            let x = v % w;
            let y = v / w;
            minx = minx.min(x);
            maxx = maxx.max(x);
            miny = miny.min(y);
            maxy = maxy.max(y);
            for (nx, ny) in [
                (x.wrapping_sub(1), y),
                (x + 1, y),
                (x, y.wrapping_sub(1)),
                (x, y + 1),
            ] {
                if nx < w && ny < h {
                    let n = ny * w + nx;
                    if !seen[n] && map[n] > DET_BINARY_THRESHOLD {
                        seen[n] = true;
                        q.push_back(n)
                    }
                }
            }
        }
        let r = Rect {
            x: minx as u32,
            y: miny as u32,
            width: (maxx - minx + 1) as u32,
            height: (maxy - miny + 1) as u32,
        };
        if r.width >= 3 && r.height >= 3 {
            out.push(r)
        }
    }
    out
}
fn box_score(map: &[f32], w: usize, r: Rect) -> f32 {
    let mut sum = 0.;
    let mut n = 0;
    for y in r.y..r.y + r.height {
        for x in r.x..r.x + r.width {
            let v = map[y as usize * w + x as usize];
            if v > DET_BINARY_THRESHOLD {
                sum += v;
                n += 1
            }
        }
    }
    if n == 0 {
        0.
    } else {
        sum / n as f32
    }
}
fn det_size(w: u32, h: u32) -> (u32, u32) {
    let s = (DET_MAX_SIDE as f64 / w.max(h) as f64).min(1.);
    (
        round32((w as f64 * s).round() as u32),
        round32((h as f64 * s).round() as u32),
    )
}
fn round32(v: u32) -> u32 {
    (((v.max(32) + 16) / 32) * 32).max(32)
}
fn expand_ratio(r: Rect, mw: u32, mh: u32, ratio: f32) -> Rect {
    let ex = r.width as f32 * (ratio - 1.) / 2.;
    let ey = r.height as f32 * (ratio - 1.) / 2.;
    let x = (r.x as f32 - ex).floor().max(0.) as u32;
    let y = (r.y as f32 - ey).floor().max(0.) as u32;
    let right = ((r.x + r.width) as f32 + ex).ceil().min(mw as f32) as u32;
    let bottom = ((r.y + r.height) as f32 + ey).ceil().min(mh as f32) as u32;
    Rect {
        x,
        y,
        width: right.saturating_sub(x),
        height: bottom.saturating_sub(y),
    }
}
fn pad_rect(r: Rect, mw: u32, mh: u32, p: u32) -> Rect {
    let x = r.x.saturating_sub(p);
    let y = r.y.saturating_sub(p);
    let right = (r.x + r.width + p).min(mw);
    let bottom = (r.y + r.height + p).min(mh);
    Rect {
        x,
        y,
        width: right - x,
        height: bottom - y,
    }
}
fn merge_lines(mut rs: Vec<Rect>) -> Vec<Rect> {
    rs.sort_by_key(|r| (r.y, r.x));
    let mut out: Vec<Rect> = vec![];
    for r in rs {
        if let Some(i) = out.iter().position(|e| {
            ((e.y + e.height / 2) as i64 - (r.y + r.height / 2) as i64).unsigned_abs()
                <= 18.max(e.height.min(r.height)) as u64
        }) {
            let e = out[i];
            let x = e.x.min(r.x);
            let y = e.y.min(r.y);
            let right = (e.x + e.width).max(r.x + r.width);
            let bottom = (e.y + e.height).max(r.y + r.height);
            out[i] = Rect {
                x,
                y,
                width: right - x,
                height: bottom - y,
            }
        } else {
            out.push(r)
        }
    }
    out.sort_by_key(|r| (r.y, r.x));
    out
}
fn recognition_candidates(img: &RgbImage) -> Vec<RgbImage> {
    let mut out = vec![
        img.clone(),
        imageops::resize(
            img,
            img.width() * 2,
            img.height() * 2,
            imageops::FilterType::CatmullRom,
        ),
    ];
    let gray = DynamicImage::ImageRgb8(img.clone()).to_luma8();
    let t = otsu(&gray);
    let binary = RgbImage::from_fn(gray.width(), gray.height(), |x, y| {
        let v = if gray.get_pixel(x, y)[0] > t { 255 } else { 0 };
        Rgb([v, v, v])
    });
    out.push(binary);
    out
}
fn otsu(g: &GrayImage) -> u8 {
    let mut hist = [0u64; 256];
    for p in g.pixels() {
        hist[p[0] as usize] += 1
    }
    let total = g.width() as u64 * g.height() as u64;
    let sum: f64 = hist
        .iter()
        .enumerate()
        .map(|(i, n)| i as f64 * (*n as f64))
        .sum();
    let (mut wb, mut sb, mut best, mut threshold) = (0u64, 0f64, -1f64, 0u8);
    for (t, n) in hist.iter().enumerate() {
        wb += *n;
        if wb == 0 {
            continue;
        }
        let wf = total - wb;
        if wf == 0 {
            break;
        }
        sb += t as f64 * (*n as f64);
        let mb = sb / wb as f64;
        let mf = (sum - sb) / wf as f64;
        let between = wb as f64 * wf as f64 * (mb - mf).powi(2);
        if between > best {
            best = between;
            threshold = t as u8
        }
    }
    threshold
}
fn decode_ctc(data: Vec<f32>, shape: &[usize], labels: &[String]) -> Result<(String, f32)> {
    if shape.len() < 2 {
        bail!("invalid recognition output shape")
    }
    let seq = if shape.len() == 3 { shape[1] } else { shape[0] };
    let classes = *shape.last().unwrap();
    let blank = classes == labels.len() + 1 || classes == labels.len() + 2;
    let (mut last, mut text, mut sum, mut n) = (usize::MAX, String::new(), 0f32, 0usize);
    for t in 0..seq {
        let row = &data[t * classes..(t + 1) * classes];
        let (idx, score) = row
            .iter()
            .copied()
            .enumerate()
            .max_by(|a, b| a.1.total_cmp(&b.1))
            .unwrap();
        if idx == last {
            continue;
        }
        last = idx;
        if blank && idx == 0 {
            continue;
        }
        let li = if blank { idx - 1 } else { idx };
        if li == labels.len() && classes == labels.len() + 2 {
            text.push(' ');
            sum += score;
            n += 1;
        } else if let Some(label) = labels.get(li) {
            text.push_str(label);
            sum += score;
            n += 1
        }
    }
    Ok((text, if n == 0 { 0. } else { sum / n as f32 }))
}
fn load_labels(path: &Path) -> Result<Vec<String>> {
    let text = fs::read_to_string(path)?;
    let mut in_dict = false;
    let mut out = vec![];
    for raw in text.lines() {
        let t = raw.trim();
        if !in_dict {
            in_dict = t == "character_dict:";
            continue;
        }
        let lead = raw.trim_start();
        if !lead.starts_with('-') {
            if !lead.is_empty() && !lead.starts_with('#') {
                break;
            }
            continue;
        }
        let mut v = lead[1..].trim_start().trim().to_string();
        if v.len() >= 2
            && ((v.starts_with('\'') && v.ends_with('\''))
                || (v.starts_with('"') && v.ends_with('"')))
        {
            v = v[1..v.len() - 1].to_string()
        }
        out.push(v)
    }
    if out.is_empty() {
        bail!("character_dict is missing in {}", path.display())
    }
    Ok(out)
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
fn text_quality(s: &str) -> usize {
    s.chars()
        .filter(|c| c.is_alphanumeric() || ('\u{4e00}'..='\u{9fff}').contains(c))
        .count()
}
