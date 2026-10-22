# Reviewed source dependencies

## calamine 0.37.0

- Source: https://crates.io/api/v1/crates/calamine/0.37.0/download
- Upstream: https://github.com/tafia/calamine
- Published archive SHA-256: `c9ca341b94bc4b29e08970b1a03914fad2dfe177e2ecb67fcd6de9046004aafb`
- License: MIT; original text in `calamine-0.37.0/LICENSE-MIT.md`.
- Local changes: widen BIFF8 `PtgRef` and `PtgRef3d` row indices to `u32` before converting to one-based row numbers. Row 65536 must neither panic in Debug nor wrap to zero in Release. The reviewed 0.37.0 release still needs these two corrections; its ZIP 9 support is now upstream, so the previous ZIP API patch is removed.
- Retain only upstream library source, Cargo manifest, README, included changelog and license. Omitted examples, benchmarks and their development dependencies are removed from the vendored manifest; production dependencies and features are unchanged.
- Both the application workspace and the standalone Excel analyzer pin 0.37.0 and patch crates.io to this same source. Remove the patch when a reviewed stable upstream release includes the row-index fix.
- Regression: the anonymous `crates/export-doc-excel/tests/fixtures/last-row-formulas.xls` workbook exercises local and cross-sheet references to row 65536, cached zero values on an auxiliary sheet, and three product rows. No customer workbook is committed.
