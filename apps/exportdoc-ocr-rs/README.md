# ExportDocManager Rust OCR Sidecar

Cross-platform PP-OCRv6 ONNX inference process. It uses `ort`, `image` and managed Rust detection post-processing; it does not depend on OpenCV.

Protocol: newline-delimited JSON over stdin/stdout. The API supplies `--model-root` and a restricted `--allowed-root`; image paths outside that runtime cache root are rejected.
