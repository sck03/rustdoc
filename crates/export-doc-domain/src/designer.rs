use crate::generated_api::ApiReportTemplateFieldCatalogResponse;
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;

pub const PROFILE_MARKER: &str = "<!-- EXPORTDOC_NATIVE_VALIDATION_PROFILE_1 -->";
pub const SCHEMA_MARKER: &str = "<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA";

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
    Flow {
        #[serde(rename = "flowKind")]
        flow_kind: String,
        block: DetailTable,
    },
}
impl Kind {
    pub fn name(&self) -> &str {
        match self {
            Self::Text { .. } => "文字",
            Self::Field { .. } => "字段",
            Self::Line { .. } => "线条",
            Self::Rectangle => "矩形",
            Self::Flow { .. } => "商品明细表",
            Self::Image { purpose, .. } => {
                if purpose == "Stamp" {
                    "印章"
                } else {
                    "图片"
                }
            }
            Self::PageNumber { .. } => "页码",
        }
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
    pub r#type: String,
    pub title: String,
    pub source_path: String,
    pub columns: Vec<DetailColumn>,
    pub print: DetailPrint,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailPrint {
    pub repeat_header_on_page_break: bool,
    pub keep_rows_together: bool,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DetailColumn {
    pub id: String,
    pub title: String,
    pub field_path: String,
    #[serde(deserialize_with = "json_float")]
    pub width_mm: f32,
    pub align: String,
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
    pub fn invoice() -> Self {
        let mut design = Self {
            version: 3,
            ast_kind: "ReportDocument".into(),
            coordinate_unit: "hundredth-mm".into(),
            contract_version: "3.0".into(),
            report_type: "ExportDocument".into(),
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
            resources: vec![],
            release: None,
            metadata: None,
        };
        for (role, name) in [("Header", "页眉"), ("Body", "主体"), ("Footer", "页脚")] {
            design.layers.push(Layer {
                id: role.into(),
                name: name.into(),
                role: role.into(),
                visible: true,
                locked: false,
                print: Print {
                    repeat_on_every_page: role != "Body",
                    keep_together: true,
                    pin_to_page_bottom: role == "Footer",
                    min_height_hundredth_mm: 0,
                },
                elements: vec![],
                design_height_hundredth_mm: None,
            });
        }
        let text = |value: &str| Kind::Text { text: value.into() };
        let field = |path: &str| Kind::Field {
            field_path: path.into(),
            fallback_text: String::new(),
        };
        design.insert(
            0,
            "title",
            text("COMMERCIAL INVOICE / 商业发票"),
            [1000, 900, 19000, 1100],
            18.,
            true,
        );
        design.insert(
            0,
            "exporter",
            field("Exporter.ExporterNameEN"),
            [1000, 2200, 19000, 800],
            12.,
            true,
        );
        design.insert(
            0,
            "exporter-address",
            field("Exporter.AddressEN"),
            [1000, 3100, 19000, 700],
            9.,
            false,
        );
        design.insert(
            0,
            "customer-label",
            text("BUYER / 买方"),
            [1000, 4100, 11000, 600],
            9.,
            true,
        );
        design.insert(
            0,
            "customer",
            field("Customer.CustomerNameEN"),
            [1000, 4800, 11000, 700],
            10.,
            false,
        );
        design.insert(
            0,
            "customer-address",
            field("Customer.AddressEN"),
            [1000, 5600, 11000, 900],
            9.,
            false,
        );
        design.insert(
            0,
            "number-label",
            text("INVOICE NO. / 发票号"),
            [13000, 4100, 7000, 600],
            9.,
            true,
        );
        design.insert(
            0,
            "number",
            field("Invoice.InvoiceNo"),
            [13000, 4800, 7000, 700],
            11.,
            true,
        );
        design.insert(
            0,
            "date",
            field("Invoice.InvoiceDate"),
            [13000, 5600, 7000, 700],
            10.,
            false,
        );
        let columns = [
            ("款号 / STYLE", "item.StyleNo", 25., "Left"),
            ("品名 / DESCRIPTION", "item.StyleName", 70., "Left"),
            ("数量 / QTY", "item.Quantity", 25., "Right"),
            ("单位 / UNIT", "item.UnitEN", 20., "Center"),
            ("单价 / PRICE", "item.UnitPrice", 25., "Right"),
            ("金额 / AMOUNT", "item.TotalPrice", 25., "Right"),
        ]
        .into_iter()
        .enumerate()
        .map(|(index, (title, path, width, align))| DetailColumn {
            id: format!("col-{index}"),
            title: title.into(),
            field_path: path.into(),
            width_mm: width,
            align: align.into(),
        })
        .collect();
        design.insert(
            1,
            "details",
            Kind::Flow {
                flow_kind: "DetailTable".into(),
                block: DetailTable {
                    id: "detail-block".into(),
                    r#type: "DetailTable".into(),
                    title: "商品明细".into(),
                    source_path: "Invoice.Items".into(),
                    columns,
                    print: DetailPrint {
                        repeat_header_on_page_break: true,
                        keep_rows_together: true,
                    },
                },
            },
            [1000, 7000, 19000, 13500],
            9.,
            false,
        );
        design.insert(
            2,
            "total-label",
            text("TOTAL / 合计"),
            [11500, 25700, 4000, 950],
            11.,
            true,
        );
        design.insert(
            2,
            "total",
            field("Invoice.TotalAmount"),
            [15500, 25700, 4500, 950],
            13.,
            true,
        );
        design.insert(
            2,
            "terms",
            field("Invoice.PaymentTerms"),
            [1000, 27200, 19000, 900],
            9.,
            false,
        );
        design.insert(
            2,
            "footer",
            text("Authorized signature / 授权签章"),
            [1000, 28400, 19000, 650],
            8.,
            false,
        );
        design
    }
    fn insert(
        &mut self,
        layer: usize,
        id: &str,
        kind: Kind,
        bounds: [i32; 4],
        size: f32,
        bold: bool,
    ) {
        self.layers[layer].elements.push(Element {
            id: id.into(),
            label: kind.name().into(),
            x_hundredth_mm: bounds[0],
            y_hundredth_mm: bounds[1],
            width_hundredth_mm: bounds[2],
            height_hundredth_mm: bounds[3],
            rotation_deg: 0,
            z_index: 0,
            visible: true,
            locked: false,
            output_enabled: true,
            style: Style {
                font_size_pt: size,
                bold,
                ..Default::default()
            },
            kind,
        });
    }
    pub fn element(&self, id: &str) -> Option<&Element> {
        self.layers
            .iter()
            .flat_map(|layer| &layer.elements)
            .find(|element| element.id == id)
    }
    pub fn element_mut(&mut self, id: &str) -> Option<&mut Element> {
        self.layers
            .iter_mut()
            .flat_map(|layer| &mut layer.elements)
            .find(|element| element.id == id)
    }
    pub fn editable(&self, id: &str) -> bool {
        self.layers.iter().any(|layer| {
            layer.visible
                && !layer.locked
                && layer
                    .elements
                    .iter()
                    .any(|element| element.id == id && !element.locked && element.visible)
        })
    }
    pub fn add(&mut self, kind: Kind, layer_index: usize) -> Result<String, String> {
        if self
            .layers
            .iter()
            .map(|layer| layer.elements.len())
            .sum::<usize>()
            >= 200
        {
            return Err("验证版单模板最多 200 个组件。".into());
        }
        if self
            .layers
            .get(layer_index)
            .is_none_or(|layer| layer.locked || !layer.visible || layer.role == "Body")
        {
            return Err("请选择可编辑的页眉或页脚图层。".into());
        }
        let mut index = 1;
        while self.element(&format!("element-{index}")).is_some() {
            index += 1;
        }
        let id = format!("element-{index}");
        let y = if self.layers[layer_index].role == "Footer" {
            27000
        } else {
            7000
        };
        self.insert(layer_index, &id, kind, [1000, y, 6000, 800], 10., false);
        Ok(id)
    }
    pub fn move_selection(&mut self, selected: &BTreeSet<String>, dx: i32, dy: i32) {
        let elements: Vec<_> = selected
            .iter()
            .filter(|id| self.editable(id))
            .filter_map(|id| self.element(id))
            .collect();
        if elements.is_empty() {
            return;
        }
        let left = elements.iter().map(|e| e.x_hundredth_mm).min().unwrap();
        let top = elements.iter().map(|e| e.y_hundredth_mm).min().unwrap();
        let right = elements
            .iter()
            .map(|e| e.x_hundredth_mm + e.width_hundredth_mm)
            .max()
            .unwrap();
        let bottom = elements
            .iter()
            .map(|e| e.y_hundredth_mm + e.height_hundredth_mm)
            .max()
            .unwrap();
        let snap = |value: i32| {
            if self.grid.snap {
                (value as f32 / self.grid.size_hundredth_mm as f32).round() as i32
                    * self.grid.size_hundredth_mm
            } else {
                value
            }
        };
        let dx = (snap(left + dx) - left).clamp(-left, self.page.width_hundredth_mm - right);
        let dy = (snap(top + dy) - top).clamp(-top, self.page.height_hundredth_mm - bottom);
        let ids: Vec<_> = elements.iter().map(|element| element.id.clone()).collect();
        for id in ids {
            let element = self.element_mut(&id).unwrap();
            element.x_hundredth_mm += dx;
            element.y_hundredth_mm += dy;
        }
    }
    pub fn remove_selection(&mut self, selected: &BTreeSet<String>) {
        for layer in &mut self.layers {
            if !layer.locked {
                layer.elements.retain(|element| {
                    !selected.contains(&element.id)
                        || element.locked
                        || matches!(element.kind, Kind::Flow { .. })
                });
            }
        }
    }
    pub fn from_html(html: &str) -> Result<Self, String> {
        let json = html
            .split_once(SCHEMA_MARKER)
            .and_then(|(_, rest)| rest.split_once("-->"))
            .map(|(json, _)| json)
            .ok_or("模板缺少 V3 结构。")?;
        let design: Self = serde_json::from_str(json)
            .map_err(|error| format!("模板含验证版不支持的结构，已保留原内容：{error}"))?;
        if design.version != 3
            || !["ExportDocument", "PaymentVoucher"].contains(&design.report_type.as_str())
            || design.contract_version != "3.0"
        {
            return Err("模板版本或数据域不受支持。".into());
        }
        Ok(design)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn drag_clamps_group_and_respects_locked_elements() {
        let mut design = Design::invoice();
        design.grid.snap = false;
        let ids = BTreeSet::from(["title".into(), "exporter".into()]);
        design.move_selection(&ids, -99999, -99999);
        assert_eq!(design.element("title").unwrap().x_hundredth_mm, 0);
        assert_eq!(design.element("exporter").unwrap().y_hundredth_mm, 1300);
        design.element_mut("title").unwrap().locked = true;
        let before = design.element("title").unwrap().clone();
        design.move_selection(&ids, 500, 500);
        assert_eq!(*design.element("title").unwrap(), before);
    }
}
