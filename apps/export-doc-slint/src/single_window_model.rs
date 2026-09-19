use crate::{
    DataRow, FormField, FormSection,
    form_model::{FormModel, Lookups},
    form_sections::{self, DisclosureState},
    model, single_window_sections as sections,
};
use export_doc_domain::{
    history::History,
    single_window::{self as rules, Business},
};
use export_doc_engine::contracts;
use serde_json::{Value, json};
use slint::Model;

pub const DICTIONARIES: &[(&str, &str)] = &[
    ("countries", "原产地证国家"),
    ("acdCountries", "代理委托国家"),
    ("currencies", "币种"),
    ("acdTradeModes", "贸易方式"),
    ("transportModes", "运输方式"),
    ("ports", "港口"),
];
pub const PROFILE_FIELDS: &[&str] = &[
    "profileName",
    "companyScope",
    "cardIdentifier",
    "canSubmitCustomsCoo",
    "customsCooClientRootPath",
    "canSubmitAgentConsignment",
    "agentConsignmentClientRootPath",
];

pub struct SingleWindowModel {
    pub business: Business,
    pub invoice_id: i64,
    pub invoices: Vec<Value>,
    pub form: Option<FormModel>,
    pub history: History<Value>,
    pub disclosures: DisclosureState,
    pub batches: Vec<Value>,
    pub detail: Option<Value>,
    pub review: Value,
    pub locks: Vec<Value>,
    pub profiles: Vec<Value>,
    pub profile: Option<FormModel>,
    pub catalog: Value,
    pub catalog_baseline: Value,
    pub dictionary: usize,
    pub dictionary_row: Option<usize>,
    pub dictionary_form: Option<FormModel>,
    pub producers: Vec<Value>,
    pub receipt_files: Vec<std::path::PathBuf>,
    pub selected_item: usize,
    pub selected_attachment: usize,
    pub selected_corp: usize,
}
impl Default for SingleWindowModel {
    fn default() -> Self {
        Self {
            business: Business::Coo,
            invoice_id: 0,
            invoices: vec![],
            form: None,
            history: History::new(60),
            disclosures: Default::default(),
            batches: vec![],
            detail: None,
            review: Value::Null,
            locks: vec![],
            profiles: vec![],
            profile: None,
            catalog: Value::Null,
            catalog_baseline: Value::Null,
            dictionary: 0,
            dictionary_row: None,
            dictionary_form: None,
            producers: vec![],
            receipt_files: vec![],
            selected_item: 0,
            selected_attachment: 0,
            selected_corp: 0,
        }
    }
}
impl SingleWindowModel {
    pub fn dirty(&self) -> bool {
        self.form.as_ref().is_some_and(|f| f.value != f.baseline)
            || self.profile.as_ref().is_some_and(|f| f.value != f.baseline)
            || self.catalog != self.catalog_baseline
            || self
                .dictionary_form
                .as_ref()
                .is_some_and(|f| f.value != f.baseline)
    }
    pub fn open(&mut self, value: Value) {
        self.invoice_id = value["sourceInvoiceId"].as_i64().unwrap_or(0);
        self.form = Some(FormModel::new(
            contracts::schema(self.business.schema()),
            value,
            self.lookups(),
        ));
        self.history.clear();
        self.review = Value::Null;
        self.locks.clear();
        self.selected_item = 0;
    }
    pub fn lookups(&self) -> Lookups {
        let mut lookups = Lookups::new();
        let options = rules::catalog::editor_options();
        for (field, key) in [
            ("applyType", "applyTypeOptions"),
            ("certStatus", "certStatusOptions"),
            ("certType", "certTypeOptions"),
            ("producerSertFlag", "producerSecretOptions"),
            ("exhibitFlag", "exhibitFlagOptions"),
            ("thirdPartyInvFlag", "thirdPartyInvoiceOptions"),
            ("predictFlag", "predictFlagOptions"),
            ("aplPromiseCode", "promiseOptions"),
            ("curr", "currencyOptions"),
            ("tradeModeCode", "cooTradeModeOptions"),
            ("goodsItemFlag", "goodsItemFlagOptions"),
            ("packType", "packTypeOptions"),
            ("goodsTaxRate", "goodsTaxRateOptions"),
            ("packUnit", "packUnitOptions"),
        ] {
            let choices = options[key]
                .as_array()
                .into_iter()
                .flatten()
                .map(|r| (r["value"].clone(), rules::text(r, "label").to_owned()))
                .collect();
            lookups.insert(field.into(), choices);
        }
        let authorities = rules::catalog::authorities();
        lookups.insert(
            "orgCode".into(),
            authorities["options"]
                .as_array()
                .into_iter()
                .flatten()
                .map(|r| (r["code"].clone(), text(r, "label")))
                .collect(),
        );
        let cert = self
            .form
            .as_ref()
            .map(|f| rules::text(&f.value, "certType"))
            .unwrap_or("C");
        lookups.insert(
            "oriCriteria".into(),
            rules::catalog::origin_options(cert)
                .as_array()
                .into_iter()
                .flatten()
                .map(|r| (r["value"].clone(), text(r, "label")))
                .collect(),
        );
        if self.business == Business::Acd {
            lookups.remove("curr");
            lookups.insert(
                "operType".into(),
                [("1", "1：新增"), ("2", "2：变更"), ("3", "3：删除")]
                    .into_iter()
                    .map(|(v, l)| (json!(v), l.into()))
                    .collect(),
            );
        }
        lookups
    }
    pub fn sections(&mut self, goods: bool) -> Vec<FormSection> {
        let Some(form) = &self.form else {
            return vec![];
        };
        let (mut result, scope, prefix) = if goods {
            let Some(value) = form.value["items"].get(self.selected_item) else {
                return vec![];
            };
            let temporary = FormModel::new(
                contracts::schema("ApiCustomsCooItemDto"),
                value.clone(),
                form.lookups.clone(),
            );
            (
                form_sections::build(&temporary, &sections::goods(), &mut self.disclosures),
                "goods",
                format!("items.{}.", self.selected_item),
            )
        } else {
            (
                form_sections::build(
                    form,
                    &sections::document(self.business),
                    &mut self.disclosures,
                ),
                self.business.scope(),
                String::new(),
            )
        };
        for section in &mut result {
            section.fields = model(
                section
                    .fields
                    .iter()
                    .map(|mut field| {
                        let key = field.key.to_string();
                        field.label = sections::label(scope, &key).into();
                        field.key = format!("{prefix}{key}").into();
                        if [
                            "exporter",
                            "consignee",
                            "goodsDesc",
                            "goodsSpecClause",
                            "transDetails",
                            "producer",
                            "prcsAssembly",
                            "mark",
                        ]
                        .contains(&key.as_str())
                        {
                            field.kind = "multiline".into();
                        }
                        field
                    })
                    .collect(),
            );
        }
        result
    }
    pub fn child_fields(&self, key: &str, index: usize) -> Vec<FormField> {
        let Some(form) = &self.form else {
            return vec![];
        };
        let Some(value) = form.value[key].get(index) else {
            return vec![];
        };
        let schema = contracts::resolve(
            &contracts::schema(self.business.schema())["properties"][key],
        )["items"]
            .clone();
        let child = FormModel::new(&schema, value.clone(), Lookups::new());
        child
            .fields()
            .into_iter()
            .filter(|f| {
                ![
                    "filePath",
                    "certNo",
                    "certType",
                    "aplRegNo",
                    "ciqRegNo",
                    "fileExistsAtBuild",
                    "sortOrder",
                    "sortNo",
                ]
                .contains(&f.key.as_str())
            })
            .map(|mut f| {
                f.label = sections::label("corp", f.key.as_str()).into();
                f.key = format!("{key}.{index}.{}", f.key).into();
                f
            })
            .collect()
    }
    pub fn dictionary_entries(&self) -> &[Value] {
        self.catalog[DICTIONARIES[self.dictionary].0]
            .as_array()
            .map(Vec::as_slice)
            .unwrap_or_default()
    }
    pub fn select_dictionary(&mut self, index: Option<usize>) {
        let key = DICTIONARIES[self.dictionary].0;
        let schema =
            &contracts::schema("SingleWindowReferenceCatalogModel")["properties"][key]["items"];
        let mut value = index
            .and_then(|i| self.dictionary_entries().get(i))
            .cloned()
            .unwrap_or_else(|| contracts::initial(schema));
        let mut schema = contracts::resolve(schema).clone();
        schema["properties"]["aliases"] = json!({"type":"string"});
        value["aliases"] = json!(
            value["aliases"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(Value::as_str)
                .collect::<Vec<_>>()
                .join("\n")
        );
        self.dictionary_row = index;
        self.dictionary_form = Some(FormModel::new(&schema, value, Lookups::new()));
    }
    pub fn save_dictionary_entry(&mut self) -> Result<(), String> {
        let form = self.dictionary_form.as_mut().ok_or("请先选择词典条目。")?;
        form.validate()?;
        let mut row = form.value.clone();
        row["aliases"] = json!(
            text(&row, "aliases")
                .lines()
                .map(str::trim)
                .filter(|s| !s.is_empty())
                .collect::<Vec<_>>()
        );
        let mut next = self.catalog.clone();
        let rows = next[DICTIONARIES[self.dictionary].0]
            .as_array_mut()
            .ok_or("词典未加载。")?;
        if let Some(index) = self.dictionary_row {
            let entry = rows.get_mut(index).ok_or("词典条目已变化。")?;
            *entry = row;
        } else {
            rows.push(row);
        }
        rules::catalog::validate(&next)?;
        self.catalog = next;
        self.dictionary_form = None;
        Ok(())
    }
}

pub fn fields(form: &FormModel, selected: &[&str]) -> Vec<FormField> {
    let source = if selected.is_empty() {
        form.schema.clone()
    } else {
        form_sections::schema(&form.schema, "", selected)
    };
    form.fields_for(&source)
        .into_iter()
        .map(|mut f| {
            f.label = sections::label("", f.key.as_str()).into();
            if f.key == "aliases" {
                f.kind = "multiline".into();
            }
            f
        })
        .collect()
}
pub fn rows(values: &[Value], keys: &[&str]) -> Vec<DataRow> {
    values
        .iter()
        .enumerate()
        .map(|(i, value)| DataRow {
            id: i as i32 + 1,
            cells: model(
                keys.iter()
                    .map(|key| {
                        if *key == "status" || *key == "businessStatus" {
                            status(&text(value, key)).into()
                        } else {
                            text(value, key).into()
                        }
                    })
                    .collect(),
            ),
        })
        .collect()
}
pub fn text(v: &Value, key: &str) -> String {
    v[key].as_str().map(str::to_owned).unwrap_or_else(|| {
        if v[key].is_null() {
            String::new()
        } else {
            v[key].to_string()
        }
    })
}
pub fn status(value: &str) -> &str {
    match value {
        "Draft" => "草稿",
        "SubmitPackageExported" => "已导出提交包",
        "SubmitPackageImported" => "已导入提交包",
        "ClientDispatching" => "正在派发",
        "ClientDispatchFailed" => "派发未完整确认",
        "QueuedToClient" => "已送入客户端",
        "ReceiptPackageExported" => "已导出回执包",
        "ReceiptImported" => "已导入回执",
        "Received" => "已接收",
        "Accepted" => "已受理",
        "PendingReview" => "待审核",
        "Approved" => "已通过",
        "Rejected" => "已退回",
        "Failed" => "失败",
        _ => value,
    }
}
