# Reviewed source dependencies

## calamine 0.37.0

- Source: https://crates.io/api/v1/crates/calamine/0.37.0/download
- Upstream: https://github.com/tafia/calamine
- Published archive SHA-256: `c9ca341b94bc4b29e08970b1a03914fad2dfe177e2ecb67fcd6de9046004aafb`
- License: MIT; original text in `calamine-0.37.0/LICENSE-MIT.md`.
- Local changes: widen BIFF8 `PtgRef` and `PtgRef3d` row indices to `u32` before converting to one-based row numbers. Row 65536 must neither panic in Debug nor wrap to zero in Release. Workbook readers also check sparse/dense allocation sizes before constructing ranges, cap each allocation at 64 MiB and retained BIFF ranges at 128 MiB, bound declared shared-string/formula indices, and avoid allocating a sparse cell vector from an unchecked BIFF dimension. Allocation failures return the reader's existing I/O error instead of aborting the application. The public in-memory `Range::from_sparse` API remains available for caller-owned data. ZIP 9 support is upstream, so the previous ZIP API patch is removed.
- Retain only upstream library source, Cargo manifest, README, included changelog and license. Omitted examples, benchmarks and their development dependencies are removed from the vendored manifest; production dependencies and features are unchanged.
- Both the application workspace and the standalone Excel analyzer pin 0.37.0 and patch crates.io to this same source. Remove the patch when a reviewed stable upstream release includes equivalent row-index and allocation protections.
- Regression: the anonymous `crates/export-doc-excel/tests/fixtures/last-row-formulas.xls` workbook exercises local and cross-sheet references to row 65536, cached zero values on an auxiliary sheet, and three product rows. No customer workbook is committed.
- `crates/export-doc-excel/src/limits_tests.rs` additionally covers a tiny OOXML file with extreme sparse coordinates, forged allocation metadata, cancellation inside a ZIP entry and numeric shipment dates under both workbook epochs. Normal BIFF8 tail and 50,000-row export regressions remain in the same suite.
