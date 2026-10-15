# OCR fixtures

Anonymous synthetic text, rendered with the repository's SIL OFL Noto Sans CJK SC font.
The images cover an English/Chinese invoice, a 7-degree scan, a 90-degree scan, phone JPEG orientation,
low contrast, 16 densely spaced rows and a blank page. They contain no customer data.

`node scripts/test_native_ocr.mjs <packaged-AppRoot>` loads the actual packaged
ONNX runtime and PP-OCRv6 Small models and checks text, order, coordinates and
invalid-input handling. This is a regression set, not a general OCR accuracy benchmark.
