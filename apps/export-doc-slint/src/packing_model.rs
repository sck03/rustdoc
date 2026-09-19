//! Drafts and presentation data only; calculation and persistence use the service.
use crate::{
    FormSection, PackingCargo,
    form_model::FormModel,
    form_sections::{self, DisclosureState, Section},
    model,
};
use export_doc_engine::{contracts, generated_api::*, history::History};
use serde_json::{Value, json};
use slint::Model;
use std::{collections::BTreeMap, time::Instant};

type Snapshot = (Value, BTreeMap<String, String>);
pub struct PackingModel {
    pub form: FormModel,
    pub projects: Vec<Value>,
    pub types: Vec<Value>,
    pub analysis: Option<ApiContainerPackingAnalysisDto>,
    pub history: History<Snapshot>,
    pub auto_due: Option<Instant>,
    pub view_due: Option<Instant>,
    pub removing_id: Option<i64>,
}
pub const CARGO_FIELDS: &[(&str, &str)] = &[
    ("name", "货物名称"),
    ("length", "长 cm"),
    ("width", "宽 cm"),
    ("height", "高 cm"),
    ("weight", "单重 kg"),
    ("quantity", "件数"),
    ("usePallet", "使用托盘"),
    ("unitsPerPallet", "每托件数"),
    ("maxTopLoadWeight", "顶部承重 kg"),
    ("preferredZone", "优先区域"),
    ("loadSequence", "装载顺序"),
    ("priorityGroup", "优先组"),
];
impl Default for PackingModel {
    fn default() -> Self {
        Self {
            form: make_form(defaults()),
            projects: vec![],
            types: vec![],
            analysis: None,
            history: History::new(60),
            auto_due: None,
            view_due: None,
            removing_id: None,
        }
    }
}
pub fn cargo() -> Value {
    json!({"name":"新货物","length":50,"width":40,"height":30,"weight":10,"quantity":1,"colorArgb":-12417035,"usePallet":false,"unitsPerPallet":1,"maxTopLoadWeight":0,"preferredZone":"Auto","loadSequence":1,"priorityGroup":""})
}
fn defaults() -> Value {
    json!({"id":0,"expectedVersion":0,"name":"新装柜方案","containerType":"20GP",
        "container":{"length":589,"width":235,"height":239,"volume":28,"maxWeight":21000},
        "rules":{"allowRotation":true,"usePalletConstraints":false,"defaultPalletLength":120,"defaultPalletWidth":100,"defaultPalletHeight":15,"defaultPalletWeight":25,"enforceCenterOfGravity":false,"centerOfGravityTolerancePercent":20,"minimumSupportAreaPercent":100,"requireSameFootprintStacking":false},"cargoItems":[]})
}
fn make_form(value: Value) -> FormModel {
    FormModel::new(
        contracts::request(SAVE_CONTAINER_PACKING_PROJECT.id),
        value,
        BTreeMap::from([(
            "preferredZone".into(),
            [
                ("Auto", "自动"),
                ("Head", "柜头段"),
                ("Middle", "中段"),
                ("Door", "柜门段"),
            ]
            .map(|(key, label)| (json!(key), label.into()))
            .to_vec(),
        )]),
    )
}
impl PackingModel {
    pub fn id(&self) -> i64 {
        self.form.value["id"].as_i64().unwrap_or(0)
    }
    pub fn dirty(&self) -> bool {
        self.form.value != self.form.baseline || !self.form.error.is_empty()
    }
    pub fn snapshot(&self) -> Snapshot {
        (self.form.value.clone(), self.form.buffers.clone())
    }
    pub fn change(&mut self, before: Snapshot, group: Option<String>) {
        self.history.record(before, &self.snapshot(), group);
        self.analysis = None;
        self.auto_due = Some(Instant::now() + std::time::Duration::from_millis(650));
    }
    pub fn open(&mut self, mut value: Value) {
        value["expectedVersion"] = value["versionNumber"].clone();
        self.form = make_form(value);
        self.history.clear();
        self.analysis = None;
        self.auto_due = None;
    }
    pub fn reset(&mut self) {
        self.form = make_form(defaults());
        self.history.clear();
        self.analysis = None;
        self.auto_due = None;
    }
    pub fn body(&mut self) -> Result<Value, String> {
        self.form.validate()?;
        let dto: ApiContainerPackingAnalyzeRequest =
            serde_json::from_value(self.form.value.clone()).map_err(|e| e.to_string())?;
        export_doc_domain::packing::Request::new(&dto)?;
        Ok(self.form.value.clone())
    }
    pub fn undo(&mut self, redo: bool) {
        let mut snapshot = self.snapshot();
        if redo {
            self.history.redo(&mut snapshot);
        } else {
            self.history.undo(&mut snapshot);
        }
        self.form.value = snapshot.0;
        self.form.buffers = snapshot.1;
        self.form.error.clear();
        self.form.invalid_field.clear();
        self.analysis = None;
        self.auto_due = Some(Instant::now() + std::time::Duration::from_millis(650));
    }
    pub fn rows(&self) -> Vec<PackingCargo> {
        let fields = self.form.fields();
        self.form.value["cargoItems"]
            .as_array()
            .into_iter()
            .flatten()
            .enumerate()
            .map(|(index, row)| {
                let cells = CARGO_FIELDS
                    .iter()
                    .filter_map(|(key, label)| {
                        fields
                            .iter()
                            .find(|field| field.key == format!("cargoItems.{index}.{key}"))
                            .cloned()
                            .map(|mut field| {
                                field.label = (*label).into();
                                field
                            })
                    })
                    .collect();
                PackingCargo {
                    index: index as i32,
                    fields: model(cells),
                    tint: slint::Color::from_argb_encoded(
                        row["colorArgb"].as_i64().unwrap_or(-12417035) as u32,
                    ),
                }
            })
            .collect()
    }
    pub fn sections(&self, state: &mut DisclosureState) -> Vec<FormSection> {
        let mut sections = form_sections::build(
            &self.form,
            &[
                Section::new(
                    "packing-dimensions",
                    "柜型与尺寸",
                    &[
                        "container.length",
                        "container.width",
                        "container.height",
                        "container.volume",
                        "container.maxWeight",
                    ],
                    true,
                ),
                Section::new(
                    "packing-rules",
                    "装载约束与托盘",
                    &[
                        "rules.allowRotation",
                        "rules.usePalletConstraints",
                        "rules.enforceCenterOfGravity",
                        "rules.requireSameFootprintStacking",
                        "rules.defaultPalletLength",
                        "rules.defaultPalletWidth",
                        "rules.defaultPalletHeight",
                        "rules.defaultPalletWeight",
                        "rules.centerOfGravityTolerancePercent",
                        "rules.minimumSupportAreaPercent",
                    ],
                    false,
                ),
            ],
            state,
        );
        for section in &mut sections {
            section.fields = model(
                section
                    .fields
                    .iter()
                    .map(|mut field| {
                        field.label = match field.key.as_str() {
                            "container.length" => "柜长 cm",
                            "container.width" => "柜宽 cm",
                            "container.height" => "柜高 cm",
                            "container.volume" => "有效容积 m³",
                            "container.maxWeight" => "最大载重 kg",
                            "rules.allowRotation" => "允许水平旋转",
                            "rules.usePalletConstraints" => "启用托盘约束",
                            "rules.enforceCenterOfGravity" => "约束重心",
                            "rules.requireSameFootprintStacking" => "相同底面堆叠",
                            "rules.defaultPalletLength" => "托盘长 cm",
                            "rules.defaultPalletWidth" => "托盘宽 cm",
                            "rules.defaultPalletHeight" => "托盘高 cm",
                            "rules.defaultPalletWeight" => "托盘重 kg",
                            "rules.centerOfGravityTolerancePercent" => "重心偏差 %",
                            "rules.minimumSupportAreaPercent" => "最小支撑面积 %",
                            _ => field.label.as_str(),
                        }
                        .into();
                        field
                    })
                    .collect(),
            );
        }
        sections
    }
    pub fn remove_cargo(&mut self, index: usize) {
        if let Some(rows) = self.form.value["cargoItems"]
            .as_array_mut()
            .filter(|rows| index < rows.len())
        {
            rows.remove(index);
        }
        self.form.buffers = std::mem::take(&mut self.form.buffers)
            .into_iter()
            .filter_map(|(key, value)| {
                if let Some(path) = key.strip_prefix("cargoItems.") {
                    let (row, field) = path.split_once('.')?;
                    let row: usize = row.parse().ok()?;
                    if row == index {
                        return None;
                    }
                    return Some((
                        format!(
                            "cargoItems.{}.{}",
                            if row > index { row - 1 } else { row },
                            field
                        ),
                        value,
                    ));
                }
                Some((key, value))
            })
            .collect();
        self.form.error.clear();
        self.form.invalid_field.clear();
    }
}
