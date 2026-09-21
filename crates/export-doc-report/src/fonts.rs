//! Controlled font metrics for report layout.
//!
//! The report templates are restricted to the three bundled Noto CJK files.
//! Measuring those real faces avoids the previous per-character estimate while
//! keeping browser preview and Rust PDF output on the same approved resources.
use crate::{Result, error::unavailable};
use rustybuzz::{Face, UnicodeBuffer};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::RwLock,
};

const FONT_FILES: [&str; 3] = [
    "NotoSansCJKsc-Regular.otf",
    "NotoSansCJKsc-Bold.otf",
    "NotoSerifCJKsc-Regular.otf",
];

#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
enum FaceKey {
    SansRegular,
    SansBold,
    SerifRegular,
}

struct FaceData {
    data: Vec<u8>,
}

struct FontSet {
    root: PathBuf,
    faces: std::result::Result<HashMap<FaceKey, FaceData>, String>,
}

static FONTS: RwLock<Option<FontSet>> = RwLock::new(None);

/// Register the directory containing the approved Noto files. The engine's
/// `RuntimePaths.font_path` remains the single source of the runtime location.
pub fn configure(path: &Path) {
    let root = path
        .parent()
        .map(Path::to_path_buf)
        .unwrap_or_else(|| PathBuf::from("."));
    if let Ok(current) = FONTS.read()
        && current.as_ref().is_some_and(|fonts| fonts.root == root)
    {
        return;
    }
    let faces = load_faces(&root);
    if let Ok(mut current) = FONTS.write() {
        *current = Some(FontSet { root, faces });
    }
}

fn load_faces(root: &Path) -> std::result::Result<HashMap<FaceKey, FaceData>, String> {
    let mut faces = HashMap::new();
    for (key, file) in [
        (FaceKey::SansRegular, FONT_FILES[0]),
        (FaceKey::SansBold, FONT_FILES[1]),
        (FaceKey::SerifRegular, FONT_FILES[2]),
    ] {
        let path = root.join(file);
        let data = std::fs::read(&path)
            .map_err(|error| format!("无法读取随包报表字体 {}:{error}", path.display()))?;
        if Face::from_slice(&data, 0).is_none() {
            return Err(format!("随包报表字体 {} 无法解析。", path.display()));
        }
        faces.insert(key, FaceData { data });
    }
    Ok(faces)
}

fn with_face<T>(key: FaceKey, action: impl FnOnce(&Face<'_>) -> Result<T>) -> Result<T> {
    let fonts = FONTS
        .read()
        .map_err(|_| unavailable("报表字体缓存不可用。"))?;
    let fonts = fonts
        .as_ref()
        .ok_or_else(|| unavailable("报表字体尚未初始化,无法进行受控测量。"))?;
    let faces = fonts
        .faces
        .as_ref()
        .map_err(|message| unavailable(message.clone()))?;
    let data = faces
        .get(&key)
        .ok_or_else(|| unavailable("报表字体未随包提供。"))?;
    let face =
        Face::from_slice(&data.data, 0).ok_or_else(|| unavailable("随包报表字体无法解析。"))?;
    action(&face)
}

/// Width of `text` in millimetres using the approved face selected by family
/// and weight. `size_mm` is the em size; invalid/unknown family falls back to
/// the bundled Sans Regular, matching the renderer's documented fallback.
pub fn text_width_mm(text: &str, family: &str, bold: bool, size_mm: f32) -> Result<f32> {
    if text.is_empty() || size_mm <= 0. {
        return Ok(0.);
    }
    let key = face_key(family, bold);
    with_face(key, |face| {
        let units_per_em = face.units_per_em() as f32;
        if units_per_em <= 0. {
            return Err(unavailable("随包报表字体缺少有效的 em 单位。"));
        }
        let mut buffer = UnicodeBuffer::new();
        buffer.push_str(text);
        let glyphs = rustybuzz::shape(face, &[], buffer);
        let width_units: i64 = glyphs
            .glyph_positions()
            .iter()
            .map(|position| i64::from(position.x_advance))
            .sum();
        Ok(width_units as f32 / units_per_em * size_mm)
    })
}

fn face_key(family: &str, bold: bool) -> FaceKey {
    if family.eq_ignore_ascii_case("Noto Serif CJK SC") {
        FaceKey::SerifRegular
    } else if bold {
        FaceKey::SansBold
    } else {
        FaceKey::SansRegular
    }
}
