# PP-OCRv6 OCR runtime notices

The bundled OCR feature uses only open-source components whose licenses permit commercial use, modification and redistribution subject to their license terms:

- PaddlePaddle PP-OCRv6 Small detection and recognition models: Apache License 2.0.
  - https://huggingface.co/PaddlePaddle/PP-OCRv6_small_det
  - https://huggingface.co/PaddlePaddle/PP-OCRv6_small_rec
- PaddleOCR project: Apache License 2.0.
  - https://github.com/PaddlePaddle/PaddleOCR
- OpenCV: Apache License 2.0.
  - https://github.com/opencv/opencv
- OpenCvSharp and its Windows/Linux native runtime packages: Apache License 2.0.
  - https://github.com/shimat/opencvsharp
- Microsoft ONNX Runtime: MIT License.
  - https://github.com/microsoft/onnxruntime
- ExportDocManager Rust OCR Sidecar dependencies `ort`, `image`, `ndarray`, `serde`, `serde_json` and `anyhow`: MIT and/or Apache License 2.0 according to each crate's published license metadata.
  - https://crates.io/crates/ort
  - https://crates.io/crates/image
  - https://crates.io/crates/ndarray

The current primary OCR path uses the Rust Sidecar and does not load OpenCV. OpenCV/OpenCvSharp remain listed because the transitional .NET fallback is still distributed on previously supported runtime identifiers until cross-platform release verification is complete.

Commercial use is allowed by these licenses. Redistributors must preserve the applicable copyright, license and notice text. Project names and trademarks are not transferred by these licenses, and the components are provided without warranty.
