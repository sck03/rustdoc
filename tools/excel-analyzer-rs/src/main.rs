use calamine::{open_workbook_auto, Data, Reader};
use serde::Serialize;
use std::{env, fs, panic, path::PathBuf, process};

const SCHEMA_VERSION: &str = "excel-analysis-rs/0.2";
const MAX_PROFILE_ROWS: usize = 120;
const MAX_PROFILE_COLUMNS: usize = 48;
const MAX_WORKBOOK_BYTES: u64 = 25 * 1024 * 1024;
const MAX_WORKSHEETS: usize = 64;
const MAX_CELL_CHARACTERS: usize = 4_096;
const MAX_PROFILE_TEXT_CHARACTERS: usize = 1_000_000;

fn main() {
    let mut args = env::args().skip(1);
    let Some(path) = args.next() else {
        eprintln!("Usage: exportdoc-excel-analyzer <excel-file>");
        process::exit(2);
    };

    let default_panic_hook = panic::take_hook();
    panic::set_hook(Box::new(|_| {}));
    let result = panic::catch_unwind(|| analyze_workbook(PathBuf::from(path)));
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

mod document_fields;
mod table_analysis;
mod workbook_analysis;

use document_fields::*;
use table_analysis::*;
use workbook_analysis::*;
#[derive(Serialize)]
struct AnalysisReport {
    schema_version: String,
    analyzer_id: String,
    source_path: String,
    selected_worksheet_name: String,
    confidence: f32,
    fields: Vec<DocumentFieldCandidate>,
    issues: Vec<AnalysisIssue>,
    sheets: Vec<SheetAnalysis>,
}

#[derive(Serialize)]
struct SheetAnalysis {
    name: String,
    used_range: UsedRange,
    confidence: f32,
    field_candidates: Vec<DocumentFieldCandidate>,
    table: Option<TableAnalysis>,
}

#[derive(Serialize)]
struct UsedRange {
    first_row: usize,
    first_column: usize,
    last_row: usize,
    last_column: usize,
}

#[derive(Serialize)]
struct TableAnalysis {
    header_start_row: usize,
    header_depth: usize,
    data_start_row: usize,
    confidence: f32,
    fields: Vec<FieldCandidate>,
    sample_rows: Vec<SampleRow>,
}

#[derive(Clone, Serialize)]
struct FieldCandidate {
    canonical_field: String,
    column: usize,
    header_path: Vec<String>,
    confidence: f32,
}

#[derive(Clone, Serialize)]
struct DocumentFieldCandidate {
    field_key: String,
    display_name: String,
    value: String,
    worksheet_name: String,
    row: usize,
    column: usize,
    confidence: f32,
    source: String,
}

#[derive(Serialize)]
struct AnalysisIssue {
    severity: String,
    code: String,
    message: String,
    field_key: String,
}

struct DocumentFieldDefinition {
    field_key: &'static str,
    display_name: &'static str,
    labels: &'static [&'static str],
    multi_line: bool,
    prefer_below: bool,
}

impl DocumentFieldDefinition {
    fn new(
        field_key: &'static str,
        display_name: &'static str,
        labels: &'static [&'static str],
    ) -> Self {
        Self {
            field_key,
            display_name,
            labels,
            multi_line: false,
            prefer_below: false,
        }
    }

    fn multi(
        field_key: &'static str,
        display_name: &'static str,
        labels: &'static [&'static str],
    ) -> Self {
        Self {
            field_key,
            display_name,
            labels,
            multi_line: true,
            prefer_below: false,
        }
    }

    fn below(
        field_key: &'static str,
        display_name: &'static str,
        labels: &'static [&'static str],
    ) -> Self {
        Self {
            field_key,
            display_name,
            labels,
            multi_line: true,
            prefer_below: true,
        }
    }
}

#[derive(Default)]
struct NearbyValue {
    value: String,
    row: usize,
    column: usize,
    score: f32,
}

#[derive(Serialize)]
struct SampleRow {
    row: usize,
    values: Vec<SampleValue>,
}

#[derive(Serialize)]
struct SampleValue {
    canonical_field: String,
    raw: String,
    normalized_decimal: Option<f64>,
    normalized_dimension: Option<DimensionValue>,
}

#[derive(Serialize)]
struct DimensionValue {
    length: f64,
    width: f64,
    height: f64,
}

#[cfg(test)]
mod tests;
