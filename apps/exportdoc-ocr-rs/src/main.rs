use anyhow::{bail, Context, Result};
use serde::Serialize;
use std::{
    env,
    io::{self, Read, Write},
    path::PathBuf,
};

mod detection;
mod dictionary;
mod engine;
mod geometry;
mod image_processing;
mod recognition;
mod runtime;

const MAX_IMAGE_BYTES: u64 = 25 * 1024 * 1024;
const ENGINE: &str = "rust-ort-ppocrv6";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Response {
    id: &'static str,
    success: bool,
    full_text: String,
    lines: Vec<engine::OcrLine>,
    error: Option<String>,
    error_kind: Option<&'static str>,
    engine: &'static str,
}

fn main() -> Result<()> {
    let mut args = env::args_os().skip(1);
    let (mut model_root, mut health, mut recognize, mut frames, mut preview) =
        (None, false, false, false, false);
    while let Some(arg) = args.next() {
        match arg.to_str() {
            Some("--model-root") => {
                model_root = Some(PathBuf::from(args.next().context("missing model root")?))
            }
            Some("--health") => health = true,
            Some("--recognize-stdin") => recognize = true,
            Some("--recognize-frames") => frames = true,
            Some("--preview-stdin") => preview = true,
            _ => bail!("unsupported OCR argument"),
        }
    }
    let root = model_root.context("--model-root is required")?;
    if !root.is_absolute()
        || [health, recognize, frames, preview]
            .into_iter()
            .filter(|value| *value)
            .count()
            != 1
    {
        bail!("provide an absolute model root and exactly one of --health / --recognize-stdin / --recognize-frames / --preview-stdin");
    }
    if health {
        engine::Engine::load(&root)?;
        println!("{}", serde_json::json!({"ready":true,"engine":ENGINE}));
        return Ok(());
    }
    if frames {
        return recognize_frames(&root);
    }
    let mut encoded = Vec::new();
    io::stdin()
        .take(MAX_IMAGE_BYTES + 1)
        .read_to_end(&mut encoded)?;
    // Reject bad input before loading the native runtime or allocating model sessions.
    if preview {
        let image = match image_processing::decode_image(&encoded) {
            Ok(image) => image,
            Err(cause) => {
                eprintln!("{cause:#}");
                std::process::exit(2);
            }
        };
        drop(encoded);
        let mut output = io::BufWriter::new(io::stdout());
        image_processing::write_preview(&image, &mut output)?;
        output.flush()?;
        return Ok(());
    }
    let response = recognize_image(&root, &mut None, encoded);
    let mut output = io::BufWriter::new(io::stdout());
    serde_json::to_writer(&mut output, &response)?;
    output.write_all(b"\n")?;
    output.flush()?;
    Ok(())
}

fn recognize_image(
    root: &std::path::Path,
    engine: &mut Option<engine::Engine>,
    encoded: Vec<u8>,
) -> Response {
    let image = image_processing::decode_image(&encoded);
    drop(encoded);
    let image = match image {
        Ok(image) => image,
        Err(cause) => return Response::failure("invalid_input", cause),
    };
    if engine.is_none() {
        match engine::Engine::load(root) {
            Ok(loaded) => *engine = Some(loaded),
            Err(cause) => return Response::failure("unavailable", cause),
        }
    }
    match engine.as_mut().expect("loaded engine").recognize(&image) {
        Ok(lines) => Response {
            id: "stdin",
            success: true,
            full_text: lines
                .iter()
                .map(|line| line.text.as_str())
                .collect::<Vec<_>>()
                .join("\n"),
            lines,
            error: None,
            error_kind: None,
            engine: ENGINE,
        },
        Err(cause) => Response::failure("unavailable", cause),
    }
}

fn read_frame(input: &mut impl Read) -> Result<Option<Vec<u8>>> {
    let mut header = [0; 4];
    if input.read(&mut header[..1])? == 0 {
        return Ok(None);
    }
    input.read_exact(&mut header[1..])?;
    let length = u32::from_le_bytes(header) as u64;
    if length == 0 {
        return Ok(None);
    }
    if length > MAX_IMAGE_BYTES {
        bail!("image frame exceeds 25 MiB");
    }
    let mut bytes = vec![0; length as usize];
    input.read_exact(&mut bytes)?;
    Ok(Some(bytes))
}

fn recognize_frames(root: &std::path::Path) -> Result<()> {
    let mut engine = None;
    let mut input = io::stdin().lock();
    let mut output = io::BufWriter::new(io::stdout());
    for _ in 0..50 {
        let Some(bytes) = read_frame(&mut input)? else {
            return Ok(());
        };
        let response = recognize_image(root, &mut engine, bytes);
        let failed = !response.success;
        let bytes = serde_json::to_vec(&response)?;
        if bytes.len() > 4 * 1024 * 1024 {
            bail!("OCR response exceeds 4 MiB");
        }
        output.write_all(&(bytes.len() as u32).to_le_bytes())?;
        output.write_all(&bytes)?;
        output.flush()?;
        if failed {
            return Ok(());
        }
    }
    Ok(())
}

impl Response {
    fn failure(kind: &'static str, cause: anyhow::Error) -> Self {
        Self {
            id: "stdin",
            success: false,
            full_text: String::new(),
            lines: vec![],
            error: Some(format!("{cause:#}")),
            error_kind: Some(kind),
            engine: ENGINE,
        }
    }
}

#[cfg(test)]
mod frame_tests {
    use super::*;
    #[test]
    fn bounded_frames_reject_oversize_and_truncation() {
        assert!(read_frame(&mut &(MAX_IMAGE_BYTES as u32 + 1).to_le_bytes()[..]).is_err());
        assert!(read_frame(&mut &[2, 0, 0, 0, 1][..]).is_err());
        assert!(read_frame(&mut &[1, 0][..]).is_err());
        assert!(read_frame(&mut &[][..]).unwrap().is_none());
        assert_eq!(
            read_frame(&mut &[2, 0, 0, 0, 1, 2][..]).unwrap(),
            Some(vec![1, 2])
        );
    }
}
