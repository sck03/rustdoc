//! Server-created V3 drafts shared by file and user-template creation.
use super::*;
use crate::designer::{Element, Grid, Kind, Layer, Page, Print, Style};

pub(crate) fn create(kind: &str, title: &str) -> Result<String> {
    let mut design = Design {
        version: 3,
        ast_kind: "ReportDocument".into(),
        coordinate_unit: "hundredth-mm".into(),
        contract_version: "3.0".into(),
        report_type: kind.into(),
        page: Page {
            size: "A4".into(),
            orientation: "Portrait".into(),
            width_hundredth_mm: 21000,
            height_hundredth_mm: 29700,
            margin_top_hundredth_mm: 1000,
            margin_right_hundredth_mm: 1000,
            margin_bottom_hundredth_mm: 1000,
            margin_left_hundredth_mm: 1000,
            font_family: "Noto Sans CJK SC".into(),
            font_size_pt: 10.,
        },
        grid: Grid {
            enabled: true,
            snap: true,
            size_hundredth_mm: 500,
        },
        layers: vec![],
        detail_row_height_hundredth_mm: None,
        resources: vec![],
        release: None,
        metadata: None,
    };
    for (role, name) in [
        ("Header", "页眉"),
        ("Body", "主体"),
        ("Footer", "页脚"),
        ("Overlay", "覆盖层"),
    ] {
        design.layers.push(Layer {
            id: role.into(),
            name: name.into(),
            role: role.into(),
            visible: true,
            locked: false,
            print: Print {
                repeat_on_every_page: matches!(role, "Header" | "Footer"),
                keep_together: matches!(role, "Header" | "Footer"),
                pin_to_page_bottom: role == "Footer",
                follow_body: false,
                first_page_only: false,
                min_height_hundredth_mm: 0,
            },
            elements: vec![],
            design_height_hundredth_mm: None,
        });
    }
    design.layers[0].elements.push(Element {
        id: "title".into(),
        label: "标题".into(),
        x_hundredth_mm: 1000,
        y_hundredth_mm: 900,
        width_hundredth_mm: 19000,
        height_hundredth_mm: 1100,
        rotation_deg: 0,
        z_index: 0,
        visible: true,
        locked: false,
        output_enabled: true,
        style: Style {
            font_size_pt: 16.,
            bold: true,
            ..Default::default()
        },
        kind: Kind::Text { text: title.into() },
    });
    serde_json::to_string_pretty(&design).map_err(Into::into)
}
