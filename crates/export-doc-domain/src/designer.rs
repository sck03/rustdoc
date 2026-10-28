use crate::generated_api::ApiReportTemplateFieldCatalogResponse;
use serde::{Deserialize, Serialize};

pub mod composite;
pub mod conditions;
pub mod grid;
pub mod page;
mod report_blocks;
pub use conditions::{ComparisonType, ConditionMatch, ConditionOperator, ConditionalRule};
pub use report_blocks::{
    BlockOutput, ConditionalContent, GridCell, GridCheckboxOption, GridColumn, GridDiagonalHeader,
    GridRow, ReportBlock, ReportBlockBase, ReportBlockOutput, ReportBorderStyle,
    ReportConditionalBlock, ReportGridBlock, ReportPageBreakBlock, ReportRowBlock, ReportTextStyle,
    RowColumn,
};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Design {
    pub version: u32,
    pub ast_kind: String,
    pub coordinate_unit: String,
    pub contract_version: String,
    pub report_type: String,
    pub page: Page,
    pub layers: Vec<Layer>,
    pub grid: Grid,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub detail_row_height_hundredth_mm: Option<i32>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub resources: Vec<ImageResource>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub release: Option<serde_json::Value>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub metadata: Option<serde_json::Value>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ImageResource {
    pub id: String,
    pub media_type: String,
    pub byte_length: u64,
    pub sha256: String,
    #[serde(default)]
    pub alt_text: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Page {
    pub size: String,
    pub orientation: String,
    pub width_hundredth_mm: i32,
    pub height_hundredth_mm: i32,
    pub margin_top_hundredth_mm: i32,
    pub margin_right_hundredth_mm: i32,
    pub margin_bottom_hundredth_mm: i32,
    pub margin_left_hundredth_mm: i32,
    pub font_family: String,
    #[serde(deserialize_with = "json_float")]
    pub font_size_pt: f32,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Grid {
    pub enabled: bool,
    pub snap: bool,
    pub size_hundredth_mm: i32,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Layer {
    pub id: String,
    pub name: String,
    pub role: String,
    pub visible: bool,
    pub locked: bool,
    pub print: Print,
    pub elements: Vec<Element>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub design_height_hundredth_mm: Option<i32>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Print {
    pub repeat_on_every_page: bool,
    pub keep_together: bool,
    pub pin_to_page_bottom: bool,
    #[serde(default)]
    pub follow_body: bool,
    #[serde(default)]
    pub first_page_only: bool,
    pub min_height_hundredth_mm: i32,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Element {
    pub id: String,
    #[serde(default)]
    pub label: String,
    pub x_hundredth_mm: i32,
    pub y_hundredth_mm: i32,
    pub width_hundredth_mm: i32,
    pub height_hundredth_mm: i32,
    pub rotation_deg: i32,
    pub z_index: i32,
    pub visible: bool,
    pub locked: bool,
    pub output_enabled: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub product: Option<ProductSettings>,
    pub style: Style,
    #[serde(flatten)]
    pub kind: Kind,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "type")]
pub enum Kind {
    Text {
        text: String,
    },
    Field {
        #[serde(rename = "fieldPath")]
        field_path: String,
        #[serde(rename = "fallbackText")]
        #[serde(default)]
        fallback_text: String,
        #[serde(
            rename = "fieldFormat",
            default,
            skip_serializing_if = "Option::is_none"
        )]
        field_format: Option<FieldFormat>,
    },
    Image {
        #[serde(rename = "sourceKind")]
        source_kind: String,
        purpose: String,
        #[serde(rename = "fieldPath", default)]
        field_path: String,
        #[serde(rename = "resourceId", default)]
        resource_id: String,
        #[serde(rename = "altText", default)]
        alt_text: String,
        #[serde(rename = "hideWhenSourceEmpty")]
        hide_when_source_empty: bool,
    },
    PageNumber {
        format: String,
        #[serde(default)]
        prefix: String,
        #[serde(default)]
        suffix: String,
    },
    Line {
        direction: String,
    },
    Rectangle,
    ProductSpacer,
    Flow {
        #[serde(rename = "flowKind")]
        flow_kind: String,
        block: ReportBlock,
    },
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub enum FieldFormat {
    Currency,
}
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ProductSettings {
    #[serde(default)]
    pub hide_when_empty: bool,
    #[serde(default)]
    pub collapse_empty: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub condition: Option<ConditionalRule>,
}
impl Element {
    pub fn is_product(&self) -> bool {
        matches!(self.kind, Kind::ProductSpacer)
            || matches!(&self.kind, Kind::Field { field_path, .. } if field_path.starts_with("item."))
    }
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields, default)]
pub struct Style {
    pub font_family: String,
    #[serde(deserialize_with = "json_float")]
    pub font_size_pt: f32,
    pub bold: bool,
    pub color: String,
    pub background_color: String,
    pub align: String,
    pub vertical_align: String,
    pub border_color: String,
    #[serde(deserialize_with = "json_float")]
    pub border_width_px: f32,
    pub border_style: String,
    pub padding_hundredth_mm: i32,
}
impl Default for Style {
    fn default() -> Self {
        Self {
            font_family: "Noto Sans CJK SC".into(),
            font_size_pt: 10.,
            bold: false,
            color: "#173f3b".into(),
            background_color: "#ffffff".into(),
            align: "Left".into(),
            vertical_align: "Top".into(),
            border_color: "#c5d3cf".into(),
            border_width_px: 0.,
            border_style: "Solid".into(),
            padding_hundredth_mm: 100,
        }
    }
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailTable {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub row_separators: Option<bool>,
    #[serde(default = "detail_table_type")]
    pub r#type: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub title: String,
    pub source_path: String,
    #[serde(default = "default_repeat_mode")]
    pub repeat_mode: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub output: Option<BlockOutput>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub detail_width_mm: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub side_band: Option<DetailSideBand>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub grouping: Option<DetailGrouping>,
    pub columns: Vec<DetailColumn>,
    pub print: DetailPrint,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub summary_row: Option<DetailSummaryRow>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub intro_row: Option<DetailSummaryRow>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub adjustments: Option<DetailAdjustments>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub entry_description_column_id: Option<String>,
    #[serde(default)]
    pub header_style: ReportTextStyle,
    #[serde(default)]
    pub body_style: ReportTextStyle,
    #[serde(default)]
    pub border: ReportBorderStyle,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailAdjustments {
    pub description_column_id: String,
    pub amount_column_id: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailPrint {
    #[serde(default)]
    pub fill_height: bool,
    pub repeat_header_on_page_break: bool,
    pub keep_rows_together: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub first_page_rows: Option<usize>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub continuation_page_rows: Option<usize>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailColumn {
    pub id: String,
    pub title: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub header_group_title: String,
    #[serde(default = "one")]
    pub header_group_span: i32,
    #[serde(default = "field_content_kind")]
    pub content_kind: String,
    pub field_path: String,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub content: Vec<DetailCellContent>,
    #[serde(default)]
    pub omit_empty_lines: bool,
    #[serde(deserialize_with = "json_float")]
    pub width_mm: f32,
    pub align: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub format: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub border: Option<ReportBorderStyle>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailSideBand {
    pub title: String,
    #[serde(deserialize_with = "json_float")]
    pub width_mm: f32,
    pub content_kind: String,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub field_path: String,
    #[serde(default)]
    pub style: ReportTextStyle,
    #[serde(default)]
    pub first_page_only: bool,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailGrouping {
    pub field_path: String,
    pub label: String,
    pub show_field_value: bool,
    pub keep_together: bool,
    #[serde(default)]
    pub page_break_before: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub footer: Option<DetailGroupFooter>,
    #[serde(default)]
    pub style: ReportTextStyle,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailGroupFooter {
    pub label: String,
    pub label_column_span: i32,
    #[serde(default)]
    pub cells: Vec<DetailGroupFooterCell>,
    #[serde(default)]
    pub style: ReportTextStyle,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailGroupFooterCell {
    pub column_id: String,
    pub content_kind: String,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub field_path: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailSummaryRow {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub border: Option<ReportBorderStyle>,
    pub label: String,
    pub label_column_span: i32,
    #[serde(default)]
    pub cells: Vec<DetailSummaryCell>,
    #[serde(default)]
    pub style: ReportTextStyle,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailSummaryCell {
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub suffix: String,
    pub column_id: String,
    pub content_kind: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub field_format: Option<FieldFormat>,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub field_path: String,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailCellContent {
    pub id: String,
    pub kind: String,
    #[serde(default)]
    pub text: String,
    #[serde(default)]
    pub field_path: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub position_percent: Option<f32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub visible: Option<bool>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub condition: Option<ConditionalRule>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub height_mm: Option<f32>,
}
fn one() -> i32 {
    1
}
fn default_repeat_mode() -> String {
    "ScribanFor".into()
}
fn field_content_kind() -> String {
    "Field".into()
}
fn detail_table_type() -> String {
    "DetailTable".into()
}

// JSON keeps business decimals exact. Read layout numbers through Value too,
// so serde's flattened enum representation retains fractional millimetres.
fn json_float<'de, D: serde::Deserializer<'de>>(deserializer: D) -> Result<f32, D::Error> {
    let value = serde_json::Value::deserialize(deserializer)?;
    value
        .as_f64()
        .filter(|value| value.is_finite())
        .map(|value| value as f32)
        .ok_or_else(|| serde::de::Error::custom("expected finite layout number"))
}

#[derive(Clone, Debug)]
pub struct Field {
    pub path: String,
    pub label: String,
    pub category: String,
    pub expression: String,
}
pub fn field_catalog(response: &ApiReportTemplateFieldCatalogResponse) -> Vec<Field> {
    response
        .fields
        .iter()
        .filter_map(|field| {
            let expression = field.value.trim();
            let inner = expression.strip_prefix("{{")?.strip_suffix("}}")?.trim();
            let path = inner.split('|').next()?.trim();
            if path.is_empty()
                || !path
                    .chars()
                    .all(|character| character.is_ascii_alphanumeric() || "._".contains(character))
            {
                return None;
            }
            Some(Field {
                path: path.into(),
                label: field.label.clone(),
                category: field.category.clone(),
                expression: expression.into(),
            })
        })
        .collect()
}

impl Design {
    pub fn element_mut(&mut self, id: &str) -> Option<&mut Element> {
        self.layers
            .iter_mut()
            .flat_map(|layer| &mut layer.elements)
            .find(|element| element.id == id)
    }
    pub fn from_source(source: &str) -> Result<Self, String> {
        let design: Self = serde_json::from_str(source.trim())
            .map_err(|error| format!("模板含验证版不支持的结构,已保留原内容:{error}"))?;
        if design.version != 3
            || !["ExportDocument", "PaymentVoucher", "OaApproval"]
                .contains(&design.report_type.as_str())
            || design.contract_version != "3.0"
        {
            return Err("模板版本或数据域不受支持。".into());
        }
        Ok(design)
    }
}
