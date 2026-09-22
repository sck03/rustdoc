use super::DetailTable;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportBlockOutput {
    #[serde(default = "yes")]
    pub enabled: bool,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub note: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct BlockOutput {
    #[serde(default = "yes")]
    pub enabled: bool,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub note: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportTextStyle {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub font_size_pt: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub bold: Option<bool>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub align: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub vertical_align: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_top_mm: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_right_mm: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_bottom_mm: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_left_mm: Option<f32>,
}
impl Default for ReportTextStyle {
    fn default() -> Self {
        Self {
            font_size_pt: None,
            bold: None,
            align: None,
            vertical_align: None,
            margin_top_mm: None,
            margin_right_mm: None,
            margin_bottom_mm: None,
            margin_left_mm: None,
        }
    }
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportBorderStyle {
    #[serde(default = "default_border_color")]
    pub color: String,
    #[serde(default)]
    pub width_px: f32,
    #[serde(default = "default_border_style")]
    pub style: String,
    #[serde(default = "yes")]
    pub top: bool,
    #[serde(default = "yes")]
    pub right: bool,
    #[serde(default = "yes")]
    pub bottom: bool,
    #[serde(default = "yes")]
    pub left: bool,
}
impl Default for ReportBorderStyle {
    fn default() -> Self {
        Self {
            color: default_border_color(),
            width_px: 0.,
            style: default_border_style(),
            top: true,
            right: true,
            bottom: true,
            left: true,
        }
    }
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportBlockBase {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub output: Option<ReportBlockOutput>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportRowBlock {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub output: Option<ReportBlockOutput>,
    pub columns: Vec<RowColumn>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_top_mm: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_bottom_mm: Option<f32>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportGridBlock {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub output: Option<ReportBlockOutput>,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub title: String,
    pub columns: Vec<GridColumn>,
    pub rows: Vec<GridRow>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_top_mm: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub margin_bottom_mm: Option<f32>,
    #[serde(default)]
    pub border: ReportBorderStyle,
    #[serde(default)]
    pub default_cell_style: ReportTextStyle,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportConditionalBlock {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub output: Option<ReportBlockOutput>,
    pub condition: ConditionalRule,
    pub content: ConditionalContent,
    #[serde(default)]
    pub style: ReportTextStyle,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub border: Option<ReportBorderStyle>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReportPageBreakBlock {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub output: Option<ReportBlockOutput>,
}
#[derive(Clone, Debug, PartialEq)]
pub enum ReportBlock {
    Row(ReportRowBlock),
    Grid(ReportGridBlock),
    Conditional(ReportConditionalBlock),
    DetailTable(DetailTable),
    PageBreak(ReportPageBreakBlock),
}
impl ReportBlock {
    pub fn id(&self) -> &str {
        match self {
            Self::Row(block) => &block.id,
            Self::Grid(block) => &block.id,
            Self::Conditional(block) => &block.id,
            Self::DetailTable(table) => &table.id,
            Self::PageBreak(block) => &block.id,
        }
    }
    pub fn kind(&self) -> &'static str {
        match self {
            Self::Row(_) => "Row",
            Self::Grid(_) => "Grid",
            Self::Conditional(_) => "Conditional",
            Self::DetailTable(_) => "DetailTable",
            Self::PageBreak(_) => "PageBreak",
        }
    }
    pub fn output_enabled(&self) -> bool {
        match self {
            Self::Row(block) => block.output.as_ref().is_none_or(|value| value.enabled),
            Self::Grid(block) => block.output.as_ref().is_none_or(|value| value.enabled),
            Self::Conditional(block) => block.output.as_ref().is_none_or(|value| value.enabled),
            Self::DetailTable(table) => table.output.as_ref().is_none_or(|value| value.enabled),
            Self::PageBreak(block) => block.output.as_ref().is_none_or(|value| value.enabled),
        }
    }
}
impl Serialize for ReportBlock {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        use serde::ser::SerializeMap;
        let mut map = serializer.serialize_map(None)?;
        map.serialize_entry("type", self.kind())?;
        let value = match self {
            Self::Row(block) => serde_json::to_value(block),
            Self::Grid(block) => serde_json::to_value(block),
            Self::Conditional(block) => serde_json::to_value(block),
            Self::DetailTable(table) => serde_json::to_value(table),
            Self::PageBreak(block) => serde_json::to_value(block),
        }
        .map_err(serde::ser::Error::custom)?;
        for (key, value) in value.as_object().into_iter().flatten() {
            if key != "type" {
                map.serialize_entry(key, value)?;
            }
        }
        map.end()
    }
}
impl<'de> Deserialize<'de> for ReportBlock {
    fn deserialize<D>(deserializer: D) -> std::result::Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        let mut value = serde_json::Value::deserialize(deserializer)?;
        let kind = value
            .get("type")
            .and_then(serde_json::Value::as_str)
            .ok_or_else(|| serde::de::Error::custom("V3 流组件缺少 type。"))?
            .to_owned();
        if let Some(object) = value.as_object_mut() {
            object.remove("type");
        }
        from_wire(&kind, value).map_err(serde::de::Error::custom)
    }
}
fn from_wire(kind: &str, value: serde_json::Value) -> std::result::Result<ReportBlock, String> {
    match kind {
        "Row" => serde_json::from_value(value)
            .map(ReportBlock::Row)
            .map_err(|error| error.to_string()),
        "Grid" => serde_json::from_value(value)
            .map(ReportBlock::Grid)
            .map_err(|error| error.to_string()),
        "Conditional" => serde_json::from_value(value)
            .map(ReportBlock::Conditional)
            .map_err(|error| error.to_string()),
        "DetailTable" => serde_json::from_value(value)
            .map(ReportBlock::DetailTable)
            .map_err(|error| error.to_string()),
        "PageBreak" => serde_json::from_value(value)
            .map(ReportBlock::PageBreak)
            .map_err(|error| error.to_string()),
        _ => Err(format!("V3 流组件类型不受支持:{kind}")),
    }
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct RowColumn {
    pub id: String,
    pub content_kind: String,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub label: String,
    #[serde(default)]
    pub field_path: String,
    #[serde(default)]
    pub fallback_text: String,
    #[serde(deserialize_with = "super::json_float")]
    pub width_percent: f32,
    #[serde(default)]
    pub style: ReportTextStyle,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub border: Option<ReportBorderStyle>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GridColumn {
    pub id: String,
    #[serde(deserialize_with = "super::json_float")]
    pub width_percent: f32,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GridRow {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub height_mm: Option<f32>,
    #[serde(default)]
    pub cells: Vec<GridCell>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GridCell {
    pub id: String,
    #[serde(default = "one")]
    pub col_span: i32,
    #[serde(default = "one")]
    pub row_span: i32,
    pub content_kind: String,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub label: String,
    #[serde(default)]
    pub field_path: String,
    #[serde(default)]
    pub fallback_text: String,
    #[serde(default)]
    pub checkbox_options: Vec<GridCheckboxOption>,
    #[serde(default)]
    pub vertical_text: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub diagonal_header: Option<GridDiagonalHeader>,
    #[serde(default)]
    pub style: ReportTextStyle,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub border: Option<ReportBorderStyle>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GridCheckboxOption {
    pub id: String,
    pub label: String,
    pub value: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct GridDiagonalHeader {
    #[serde(default)]
    pub upper_left_text: String,
    #[serde(default)]
    pub lower_right_text: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ConditionalRule {
    pub field_path: String,
    pub operator: String,
    #[serde(default)]
    pub value: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ConditionalContent {
    pub kind: String,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub label: String,
    #[serde(default)]
    pub field_path: String,
    #[serde(default)]
    pub fallback_text: String,
}

fn yes() -> bool {
    true
}
fn one() -> i32 {
    1
}
fn default_border_color() -> String {
    "#333333".into()
}
fn default_border_style() -> String {
    "Solid".into()
}
