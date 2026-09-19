use export_doc_native::{paths::RuntimePaths, runtime::Runtime, validation};
use std::path::PathBuf;
mod ui;

fn main() {
    if let Err(error) = run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
fn run() -> Result<(), String> {
    let args: Vec<String> = std::env::args().collect();
    let option = |name: &str| {
        args.iter()
            .position(|argument| argument == name)
            .and_then(|index| args.get(index + 1))
            .map(PathBuf::from)
    };
    if args
        .iter()
        .any(|argument| argument == "--pdf-preview-worker")
    {
        let library = option("--pdfium").ok_or("PDF preview requires --pdfium")?;
        let index = args
            .iter()
            .position(|argument| argument == "--page")
            .and_then(|index| args.get(index + 1))
            .and_then(|value| value.parse::<u32>().ok())
            .ok_or("PDF preview requires --page")?;
        return export_doc_native::pdf::worker(&library, index);
    }
    let root = option("--app-root").unwrap_or(
        std::env::current_exe()
            .map_err(|error| error.to_string())?
            .parent()
            .ok_or("No executable parent")?
            .to_path_buf(),
    );
    let smoke = option("--smoke-test");
    let dump = option("--dump-openapi");
    let ui_smoke = option("--ui-smoke");
    let mut runtime = Runtime::start(RuntimePaths::open(
        &root,
        smoke.is_some() || dump.is_some() || ui_smoke.is_some(),
    )?)?;
    let result = if let Some(output) = smoke {
        validation::run(&runtime, &output)
    } else if let Some(output) = dump {
        validation::dump_openapi(&runtime, &output)
    } else {
        ui::run(runtime.client.clone(), runtime.paths.clone(), ui_smoke)
    };
    let shutdown = runtime.shutdown();
    result.and(shutdown)
}
