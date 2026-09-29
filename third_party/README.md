# Reviewed source dependencies

## calamine 0.36.1

- Source: https://crates.io/api/v1/crates/calamine/0.36.1/download
- Upstream: https://github.com/tafia/calamine
- Published archive SHA-256: `5fa68281b1a76b54a62156474adb06bb380a67e07dd60656e3217152b42183f3`
- License: MIT; original text in `calamine-0.36.1/LICENSE-MIT.md`.
- Local change: widen BIFF8 `PtgRef` and `PtgRef3d` row indices to `u32` before converting to one-based row numbers. Row 65536 must neither panic in Debug nor wrap to zero in Release. All other upstream Rust source is unchanged; trailing whitespace in the upstream typo workflow and examples README is normalized for repository checks.
- Both the application workspace and the standalone Excel analyzer patch crates.io to this same source. The application's 0.36.1 version and features are unchanged; the standalone analyzer is aligned from 0.36.0 to exactly 0.36.1. Remove the patch when a reviewed stable upstream release includes the fix.
- Regression: the anonymous `crates/export-doc-excel/tests/fixtures/last-row-formulas.xls` workbook exercises local and cross-sheet references to row 65536, cached zero values on an auxiliary sheet, and three product rows. No customer workbook is committed.
