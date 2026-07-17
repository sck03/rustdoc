# ExportDoc Excel Analyzer (Rust)

Rust enhanced Excel structure analyzer for import preview.

## Purpose

This tool does not save invoices and does not update `excelImport` or `excelImportSchemes`.
It reads a user-selected workbook and emits a JSON analysis report:

- worksheets and used ranges
- invoice header and party fields such as SHIPPER, consignee, invoice number, loading port, destination, trade/payment terms, currency, and shipping marks
- likely table header start row and data start row
- multi-row header field candidates
- sample rows with normalized decimal and dimension values
- confidence scores for field mapping review

The host application uses this Rust analyzer first for import preview. If the executable is missing, disabled, times out, or analysis fails, the host falls back to the built-in .NET analyzer and existing Excel reader. This keeps the fast Tauri/Rust path as the default while preserving compatibility for legacy `.xls` workbooks that calamine may not parse reliably.

## Usage

```powershell
cargo run -- "H:\path\to\file.xlsx"
```

In the packaged desktop runtime the platform-native binary is copied to:

```text
Tools/exportdoc-excel-analyzer.exe   # Windows
Tools/exportdoc-excel-analyzer       # macOS/Linux
```

The API sidecar also accepts an explicit analyzer path through `EXPORTDOCMANAGER_EXCEL_ANALYZER`.
The analyzer mode can be controlled through `EXPORTDOCMANAGER_EXCEL_ANALYZER_MODE`:

- `auto` (default): try this Rust helper first, then fuse with or fall back to the .NET analyzer when needed
- `module`: use only the built-in .NET module
- `external-first`: try this helper first, then fuse with the .NET analyzer when needed

The analyzer is packaged by default in the Windows desktop run directory. The .NET module is cross-platform and remains the stable fallback for legacy `.xls` edge cases.

Exit codes:

- `0`: analysis JSON was written to stdout
- `1`: workbook could not be opened or analyzed
- `2`: missing command-line argument
- `3`: the Rust reader panicked on this workbook; use the .NET reader fallback

## Current Notes

The analyzer uses `calamine`. It works for many workbooks, but some legacy `.xls` files can still trigger upstream BIFF parsing bugs. For production import:

1. Keep `.xls` reading through .NET `ExcelDataReader` as the stable fallback.
2. Use Rust analysis as the preferred structure profiler and confidence reporter, with .NET fallback.
3. Never let the Rust analyzer write business database records directly.
4. Save to invoice only after the existing preview page shows a draft and the user confirms.
