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
    let (mut model_root, mut health, mut recognize) = (None, false, false);
    while let Some(arg) = args.next() {
        match arg.to_str() {
            Some("--model-root") => {
                model_root = Some(PathBuf::from(args.next().context("missing model root")?))
            }
            Some("--health") => health = true,
            Some("--recognize-stdin") => recognize = true,
            _ => bail!("unsupported OCR argument"),
        }
    }
    let root = model_root.context("--model-root is required")?;
    if !root.is_absolute() || health == recognize {
        bail!("provide an absolute model root and exactly one of --health / --recognize-stdin");
    }
    if health {
        engine::Engine::load(&root)?;
        println!("{}", serde_json::json!({"ready":true,"engine":ENGINE}));
        return Ok(());
    }
    let mut encoded = Vec::new();
    io::stdin()
        .take(MAX_IMAGE_BYTES + 1)
        .read_to_end(&mut encoded)?;
    // Reject bad input before loading the native runtime or allocating model sessions.
    let image = image_processing::decode_image(&encoded);
    drop(encoded);
    let response = match image {
        Err(cause) => Response::failure("invalid_input", cause),
        Ok(image) => {
            match engine::Engine::load(&root).and_then(|mut engine| engine.recognize(&image)) {
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
    };
    let mut output = io::BufWriter::new(io::stdout());
    serde_json::to_writer(&mut output, &response)?;
    output.write_all(b"\n")?;
    output.flush()?;
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
