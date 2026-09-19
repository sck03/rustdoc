# ExportDocManager Rust OCR Sidecar

Cross-platform PP-OCRv6 ONNX inference process. It uses `ort`, `image` and managed Rust detection post-processing; it does not depend on OpenCV.

Protocol: newline-delimited JSON over stdin/stdout. The API supplies `--model-root` and a restricted `--allowed-root`; image paths outside that runtime cache root are rejected.


Native Rust desktop/server callers can pass `--recognize-stdin` with the same explicit model/runtime paths. In this mode stdin is the encoded image (maximum 25 MiB), stdout is one response with `id: "stdin"`, and the process exits. File and memory input use the same detector, recognizer, dimension limits and line ordering. No source image or recognized text is written to disk; the parent owns the deadline, cancellation and process-tree cleanup.
