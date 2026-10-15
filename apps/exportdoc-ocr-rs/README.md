# ExportDocManager Rust OCR Sidecar

Cross-platform PP-OCRv6 Small ONNX worker using `ort`, `image`, Rust DB post-processing,
oriented crops and CTC decoding. No Python or OpenCV runtime is included. The parent
releases the worker and model memory after each request.

Provide absolute `--model-root` and `ORT_DYLIB_PATH` values, and exactly one of
`--health` or `--recognize-stdin`. All platforms explicitly initialize ORT; Windows
x64/ARM64 preload their matching, governed app-local CRT libraries.


Stdin contains PNG/JPEG/BMP/WebP/TIFF bytes (maximum 25 MiB, 16384 pixels per side,
40 million pixels). Stdout is one JSON response with `id: "stdin"`. Transparent pixels
are composited on white and EXIF orientation is applied as in the browser preview.
Coordinates use that displayed orientation; text follows its
dominant recognized orientation. Most regions need one inference; uncertain regions
can try a half-turn. Arbitrary handwriting, perspective and orientation are not guaranteed.

Bad input uses `errorKind: "invalid_input"`; model/runtime/inference failures use
`"unavailable"` (HTTP 400 / 503). The parent owns timeout, cancellation, concurrency
and process-tree cleanup. No images or text are written to disk or business records.
The unused file-path/NDJSON protocol was removed; production callers use encoded stdin.

This project is outside the root Cargo workspace. Run its own `cargo fmt --check` and
`cargo test --locked` with `--manifest-path apps/exportdoc-ocr-rs/Cargo.toml`, then
`node scripts/test_native_ocr.mjs <packaged-AppRoot>` to test real models and resources.
