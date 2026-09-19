use std::{env, panic, path::PathBuf, process};

fn main() {
    let mut args = env::args().skip(1);
    let Some(path) = args.next() else {
        eprintln!("Usage: exportdoc-excel-analyzer <excel-file>");
        process::exit(2);
    };

    let default_panic_hook = panic::take_hook();
    panic::set_hook(Box::new(|_| {}));
    let result =
        panic::catch_unwind(|| exportdoc_excel_analyzer::analyze_workbook(PathBuf::from(path)));
    panic::set_hook(default_panic_hook);
    match result {
        Ok(Ok(report)) => {
            println!(
                "{}",
                serde_json::to_string_pretty(&report).expect("serialize analysis report")
            );
        }
        Ok(Err(error)) => {
            eprintln!("{error}");
            process::exit(1);
        }
        Err(_) => {
            eprintln!("Rust Excel analyzer failed while reading this workbook. The host should fall back to the .NET Excel reader.");
            process::exit(3);
        }
    }
}
