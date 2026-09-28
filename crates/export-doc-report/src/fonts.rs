//! Controlled font metrics for report layout.
//!
//! The report templates are restricted to the three bundled Noto CJK files.
//! Measuring those real faces avoids the previous per-character estimate while
//! keeping browser preview and Rust PDF output on the same approved resources.
use crate::{Result, error::unavailable};
use rustybuzz::{Face, UnicodeBuffer};
use std::{
    cell::RefCell,
    collections::HashMap,
    marker::PhantomData,
    path::{Path, PathBuf},
    rc::Rc,
    sync::{Arc, Mutex, OnceLock},
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
    data: Arc<Vec<u8>>,
}

struct FontSet {
    faces: HashMap<FaceKey, FaceData>,
    database: Arc<usvg::fontdb::Database>,
    widths: Mutex<HashMap<(FaceKey, String), f32>>,
}

/// Owned by one runtime. Loading is lazy; all its requests/jobs share the same
/// immutable faces, SVG database and bounded measurement cache.
pub struct Fonts {
    root: PathBuf,
    loaded: OnceLock<std::result::Result<Arc<FontSet>, String>>,
}

thread_local! {
    // Only the current synchronous layout owns this binding. No filesystem
    // paths or default runtime are cached globally or retained after the scope.
    static ACTIVE_FONTS: RefCell<Option<Arc<FontSet>>> = const { RefCell::new(None) };
}

#[must_use = "Keep the guard alive for the entire synchronous layout"]
pub struct FontScope {
    previous: Option<Arc<FontSet>>,
    _thread: PhantomData<Rc<()>>,
}
impl Drop for FontScope {
    fn drop(&mut self) {
        ACTIVE_FONTS.with(|active| *active.borrow_mut() = self.previous.take());
    }
}

impl Fonts {
    pub fn new(path: &Path) -> Self {
        Self {
            root: path.parent().unwrap_or(path).to_path_buf(),
            loaded: OnceLock::new(),
        }
    }

    fn load(&self) -> Result<Arc<FontSet>> {
        self.loaded
            .get_or_init(|| {
                let faces = load_faces(&self.root)?;
                let mut database = usvg::fontdb::Database::new();
                for face in faces.values() {
                    database.load_font_source(usvg::fontdb::Source::Binary(face.data.clone()));
                }
                Ok(Arc::new(FontSet {
                    faces,
                    database: Arc::new(database),
                    widths: Mutex::new(HashMap::new()),
                }))
            })
            .as_ref()
            .cloned()
            .map_err(|error| unavailable(error.clone()))
    }

    /// Scope a synchronous layout. The guard is thread-bound and must not span
    /// an await. Nested callers are restored even when the layout unwinds.
    pub fn enter(&self) -> Result<FontScope> {
        let fonts = self.load()?;
        Ok(FontScope {
            previous: ACTIVE_FONTS.with(|active| active.replace(Some(fonts))),
            _thread: PhantomData,
        })
    }

    pub(crate) fn svg_options(&self) -> Result<usvg::Options<'static>> {
        Ok(usvg::Options {
            fontdb: self.load()?.database.clone(),
            font_family: "Noto Sans CJK SC".into(),
            ..Default::default()
        })
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
        faces.insert(
            key,
            FaceData {
                data: Arc::new(data),
            },
        );
    }
    Ok(faces)
}

fn with_face<T>(key: FaceKey, action: impl FnOnce(&FaceData, &FontSet) -> Result<T>) -> Result<T> {
    let fonts = ACTIVE_FONTS
        .with(|active| active.borrow().clone())
        .ok_or_else(|| unavailable("报表字体尚未初始化,无法进行受控测量。"))?;
    let data = fonts
        .faces
        .get(&key)
        .ok_or_else(|| unavailable("报表字体未随包提供。"))?;
    action(data, &fonts)
}

/// Width of `text` in millimetres using the approved face selected by family
/// and weight. `size_mm` is the em size; invalid/unknown family falls back to
/// the bundled Sans Regular, matching the renderer's documented fallback.
pub fn text_width_mm(text: &str, family: &str, bold: bool, size_mm: f32) -> Result<f32> {
    if text.is_empty() || size_mm <= 0. {
        return Ok(0.);
    }
    let key = face_key(family, bold);
    with_face(key, |data, fonts| {
        // Cache normalized widths, independent of font size. Only short strings
        // are retained and the bounded cache is replaced with the governed fonts.
        let cache_key = (text.len() <= 512).then(|| (key, text.to_owned()));
        if let Some(key) = &cache_key
            && let Ok(cache) = fonts.widths.lock()
            && let Some(width) = cache.get(key)
        {
            return Ok(width * size_mm);
        }
        let face =
            Face::from_slice(&data.data, 0).ok_or_else(|| unavailable("随包报表字体无法解析。"))?;
        let units_per_em = face.units_per_em() as f32;
        if units_per_em <= 0. {
            return Err(unavailable("随包报表字体缺少有效的 em 单位。"));
        }
        let mut buffer = UnicodeBuffer::new();
        buffer.push_str(text);
        let glyphs = rustybuzz::shape(&face, &[], buffer);
        let width_units: i64 = glyphs
            .glyph_positions()
            .iter()
            .map(|position| i64::from(position.x_advance))
            .sum();
        let width = width_units as f32 / units_per_em;
        if let Some(key) = cache_key
            && let Ok(mut cache) = fonts.widths.lock()
        {
            if cache.len() >= 8192 {
                cache.clear();
            }
            cache.insert(key, width);
        }
        Ok(width * size_mm)
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

#[cfg(test)]
mod tests {
    use super::*;
    use std::{sync::mpsc, time::Duration};

    fn fonts() -> Fonts {
        Fonts::new(
            &PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .join("../../Resources/Fonts/OpenSource/NotoSansCJKsc-Regular.otf"),
        )
    }

    #[test]
    fn concurrent_runtimes_cannot_replace_each_others_fonts() {
        let good = fonts();
        assert!(
            good.loaded.get().is_none(),
            "startup must not load unused report resources"
        );
        let missing =
            Fonts::new(&PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("Cargo.toml/font.otf"));
        let (ready_tx, ready_rx) = mpsc::channel();
        let (changed_tx, changed_rx) = mpsc::channel();
        std::thread::scope(|threads| {
            let good = &good;
            threads.spawn(move || {
                let _scope = good.enter().unwrap();
                let width = text_width_mm("并发 Invoice", "Noto Sans CJK SC", false, 4.).unwrap();
                assert!(width > 0.);
                ready_tx.send(()).unwrap();
                changed_rx.recv_timeout(Duration::from_secs(30)).unwrap();
                assert_eq!(
                    text_width_mm("并发 Invoice", "Noto Sans CJK SC", false, 4.).unwrap(),
                    width
                );
                assert!(Arc::ptr_eq(
                    &good.svg_options().unwrap().fontdb,
                    &good.svg_options().unwrap().fontdb
                ));
            });
            threads.spawn(move || {
                ready_rx.recv_timeout(Duration::from_secs(30)).unwrap();
                assert!(missing.enter().is_err());
                assert!(missing.svg_options().is_err());
                assert!(
                    text_width_mm("no inherited runtime", "Noto Sans CJK SC", false, 4.).is_err()
                );
                changed_tx.send(()).unwrap();
            });
        });
        assert!(ACTIVE_FONTS.with(|active| active.borrow().is_none()));
    }

    #[test]
    fn nested_scope_restores_the_caller_on_unwind_and_releases_the_binding() {
        let outer = fonts();
        let inner = fonts();
        {
            let _outer = outer.enter().unwrap();
            let first = ACTIVE_FONTS.with(|active| active.borrow().clone().unwrap());
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let _inner = inner.enter().unwrap();
                assert!(
                    !ACTIVE_FONTS
                        .with(|active| Arc::ptr_eq(active.borrow().as_ref().unwrap(), &first))
                );
                panic!("test layout failure");
            }));
            assert!(result.is_err());
            assert!(
                ACTIVE_FONTS.with(|active| Arc::ptr_eq(active.borrow().as_ref().unwrap(), &first))
            );
        }
        assert!(ACTIVE_FONTS.with(|active| active.borrow().is_none()));
    }
}
