use calamine::{open_workbook_auto, Data, Reader};
use serde::Serialize;
use std::{fs, path::PathBuf};

const SCHEMA_VERSION: &str = "excel-analysis-rs/0.2";
const MAX_PROFILE_ROWS: usize = 120;
const MAX_PROFILE_COLUMNS: usize = 48;
const MAX_WORKBOOK_BYTES: u64 = 25 * 1024 * 1024;
const MAX_WORKSHEETS: usize = 64;
const MAX_CELL_CHARACTERS: usize = 4_096;
const MAX_PROFILE_TEXT_CHARACTERS: usize = 1_000_000;

mod document_fields;
mod table_analysis;
mod table_values;
mod workbook_analysis;

use document_fields::*;
use table_analysis::*;
use table_values::*;
pub use workbook_analysis::analyze_workbook;
use workbook_analysis::*;
#[derive(Serialize)]
pub struct AnalysisReport {
    pub schema_version: String,
    pub analyzer_id: String,
    pub source_path: String,
    pub selected_worksheet_name: String,
    pub confidence: f32,
    pub fields: Vec<DocumentFieldCandidate>,
    pub issues: Vec<AnalysisIssue>,
    pub sheets: Vec<SheetAnalysis>,
}

#[derive(Serialize)]
pub struct SheetAnalysis {
    pub name: String,
    pub used_range: UsedRange,
    pub confidence: f32,
    pub field_candidates: Vec<DocumentFieldCandidate>,
    pub table: Option<TableAnalysis>,
}

#[derive(Serialize)]
pub struct UsedRange {
    pub first_row: usize,
    pub first_column: usize,
    pub last_row: usize,
    pub last_column: usize,
}

#[derive(Serialize)]
pub struct TableAnalysis {
    pub header_start_row: usize,
    pub header_depth: usize,
    pub data_start_row: usize,
    pub confidence: f32,
    pub fields: Vec<FieldCandidate>,
    pub sample_rows: Vec<SampleRow>,
}

#[derive(Clone, Serialize)]
pub struct FieldCandidate {
    pub canonical_field: String,
    pub column: usize,
    pub header_path: Vec<String>,
    pub confidence: f32,
}

#[derive(Clone, Serialize)]
pub struct DocumentFieldCandidate {
    pub field_key: String,
    pub display_name: String,
    pub value: String,
    pub worksheet_name: String,
    pub row: usize,
    pub column: usize,
    pub confidence: f32,
    pub source: String,
}

#[derive(Serialize)]
pub struct AnalysisIssue {
    pub severity: String,
    pub code: String,
    pub message: String,
    pub field_key: String,
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
pub struct SampleRow {
    pub row: usize,
    pub values: Vec<SampleValue>,
}

#[derive(Serialize)]
pub struct SampleValue {
    pub canonical_field: String,
    pub raw: String,
    pub normalized_decimal: Option<f64>,
    pub normalized_dimension: Option<DimensionValue>,
}

#[derive(Serialize)]
pub struct DimensionValue {
    pub length: f64,
    pub width: f64,
    pub height: f64,
}

#[cfg(test)]
mod tests;

/// Analyze cells already read by a host. This keeps header and field recognition
/// shared by the legacy CLI and the native importer, without a sidecar process.
pub fn analyze_cells(name: &str, cells: &[Vec<String>]) -> SheetAnalysis {
    let fields = detect_document_fields(cells, name);
    let table = detect_table(cells);
    SheetAnalysis {
        name: name.into(),
        used_range: detect_used_range(cells),
        confidence: sheet_confidence(&fields, table.as_ref()),
        field_candidates: fields,
        table,
    }
}
pub fn formatted_cell(cell: &Data) -> String {
    cell_to_string(cell)
}
