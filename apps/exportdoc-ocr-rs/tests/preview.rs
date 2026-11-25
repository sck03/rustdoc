use std::io::{Cursor, Write};
use std::process::{Command, Stdio};

fn preview(bytes: &[u8]) -> std::process::Output {
    let mut child = Command::new(env!("CARGO_BIN_EXE_exportdoc-ocr"))
        .arg("--model-root")
        .arg(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("uninstalled-models"))
        .arg("--preview-stdin")
        .env_remove("ORT_DYLIB_PATH")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    child.stdin.take().unwrap().write_all(bytes).unwrap();
    child.wait_with_output().unwrap()
}

#[test]
fn tiff_and_oriented_images_preview_without_onnx_or_models() {
    let mut tiff = Cursor::new(Vec::new());
    image::RgbImage::from_pixel(23, 41, image::Rgb([200, 80, 30]))
        .write_to(&mut tiff, image::ImageFormat::Tiff)
        .unwrap();
    for (bytes, size) in [
        (tiff.get_ref().as_slice(), (23, 41)),
        (
            include_bytes!("../../../tests/OcrFixtures/exif_rotated.jpg").as_slice(),
            (1300, 460),
        ),
    ] {
        let output = preview(bytes);
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert!(output.stdout.starts_with(&[0xff, 0xd8, 0xff]));
        let decoded = image::load_from_memory(&output.stdout).unwrap();
        assert_eq!((decoded.width(), decoded.height()), size);
    }
    for bytes in [b"broken TIFF".as_slice(), &[]] {
        let output = preview(bytes);
        assert_eq!(output.status.code(), Some(2));
        assert!(output.stdout.is_empty());
    }
}
