//! Development-only projections of the same OpenAPI document served by Rust.
mod codegen;

use std::{error::Error, path::PathBuf};

fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let option = |name: &str| -> Result<Option<PathBuf>, Box<dyn Error>> {
        match args.iter().position(|arg| arg == name) {
            Some(index) => Ok(Some(PathBuf::from(
                args.get(index + 1)
                    .filter(|value| !value.starts_with("--"))
                    .ok_or_else(|| format!("{name} requires a path"))?,
            ))),
            None => Ok(None),
        }
    };
    for arg in args.iter().filter(|arg| arg.starts_with("--")) {
        if !["--openapi", "--output", "--check"].contains(&arg.as_str()) {
            return Err(format!("Unknown option: {arg}").into());
        }
    }
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..");
    let document = match option("--openapi")? {
        Some(path) => {
            serde_json::from_str(&std::fs::read_to_string(path)?.trim_start_matches('\u{feff}'))?
        }
        None => export_doc_contracts::openapi::document(),
    };
    let generated = codegen::generate(&document)?;
    let formatted_rust = format_rust(&root, &generated.rust)?;
    let files = [
        (
            option("--output")?.unwrap_or_else(|| {
                root.join("apps/export-doc-web/src/api/generated/exportDocManagerApi.ts")
            }),
            generated.web,
        ),
        (
            root.join("crates/export-doc-contracts/src/generated_api.rs"),
            formatted_rust,
        ),
        (
            root.join("crates/export-doc-contracts/src/generated_contract.json"),
            generated.contract,
        ),
        (
            root.join("crates/export-doc-contracts/src/openapi.json"),
            serde_json::to_string_pretty(&document)? + "\n",
        ),
    ];
    let check = args.iter().any(|arg| arg == "--check");
    // Validate every projection before changing any generated file.
    for (path, content) in &files {
        if check && std::fs::read_to_string(path)?.replace("\r\n", "\n") != *content {
            return Err(format!("Generated contract drift: {}", path.display()).into());
        }
    }
    if !check {
        for (path, content) in files {
            codegen::write_generated(&path, &content)?;
        }
    }
    println!(
        "Rust API generator: {} operations, {} schemas; {}.",
        generated.operation_count,
        generated.schema_count,
        if check { "verified" } else { "generated" }
    );
    Ok(())
}

fn format_rust(root: &std::path::Path, source: &str) -> Result<String, Box<dyn Error>> {
    use std::{
        process::Command,
        time::{Duration, Instant},
    };
    let directory = root.join("artifacts/contracts");
    std::fs::create_dir_all(&directory)?;
    let path = directory.join(format!("codegen-{}.rs", std::process::id()));
    let mut file = std::fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&path)?;
    let result = (|| -> Result<String, Box<dyn Error>> {
        use std::io::Write;
        file.write_all(source.as_bytes())?;
        drop(file);
        let mut child = Command::new("rustfmt")
            .args(["--edition", "2024"])
            .arg(&path)
            .spawn()?;
        let deadline = Instant::now() + Duration::from_secs(60);
        loop {
            if let Some(status) = child.try_wait()? {
                if !status.success() {
                    return Err(format!("rustfmt failed: {status}").into());
                }
                break;
            }
            if Instant::now() >= deadline {
                let _ = child.kill();
                let _ = child.wait();
                return Err("rustfmt timed out".into());
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        Ok(std::fs::read_to_string(&path)?.replace("\r\n", "\n"))
    })();
    let _ = std::fs::remove_file(&path);
    result
}
