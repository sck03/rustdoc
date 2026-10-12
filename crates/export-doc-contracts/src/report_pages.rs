//! Physical paper formats published by OpenAPI and shared with native layout.
use serde::Serialize;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PaperPreset {
    pub size: &'static str,
    pub width_hundredth_mm: i32,
    pub height_hundredth_mm: i32,
}

pub const MIN_DIMENSION: i32 = 4000;
pub const MAX_DIMENSION: i32 = 60000;
pub const PRESETS: &[PaperPreset] = &[
    PaperPreset {
        size: "A4",
        width_hundredth_mm: 21000,
        height_hundredth_mm: 29700,
    },
    PaperPreset {
        size: "A3",
        width_hundredth_mm: 29700,
        height_hundredth_mm: 42000,
    },
    PaperPreset {
        size: "A5",
        width_hundredth_mm: 14800,
        height_hundredth_mm: 21000,
    },
    PaperPreset {
        size: "Letter",
        width_hundredth_mm: 21590,
        height_hundredth_mm: 27940,
    },
    PaperPreset {
        size: "Legal",
        width_hundredth_mm: 21590,
        height_hundredth_mm: 35560,
    },
];

pub fn dimensions(size: &str, landscape: bool) -> Option<(i32, i32)> {
    PRESETS
        .iter()
        .find(|preset| preset.size == size)
        .map(|preset| {
            if landscape {
                (preset.height_hundredth_mm, preset.width_hundredth_mm)
            } else {
                (preset.width_hundredth_mm, preset.height_hundredth_mm)
            }
        })
}
