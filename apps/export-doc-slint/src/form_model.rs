//! Schema-driven forms are a UI projection of the generated contract.
use crate::{FormField, labels, model};
use export_doc_engine::{contracts, workspace::display};
use serde_json::{Value, json};
use std::collections::BTreeMap;
pub type Lookups = BTreeMap<String, Vec<(Value, String)>>;
pub struct FormModel {
    pub schema: Value,
    pub value: Value,
    pub baseline: Value,
    pub buffers: BTreeMap<String, String>,
    pub lookups: Lookups,
    pub error: String,
    pub invalid_field: String,
}
impl FormModel {
    pub fn new(schema: &Value, value: Value, lookups: Lookups) -> Self {
        Self {
            schema: schema.clone(),
            baseline: value.clone(),
            value,
            buffers: BTreeMap::new(),
            lookups,
            error: String::new(),
            invalid_field: String::new(),
        }
    }
    pub fn fields(&self) -> Vec<FormField> {
        self.fields_for(&self.schema)
    }
    pub fn fields_for(&self, schema: &Value) -> Vec<FormField> {
        let mut fields = vec![];
        self.walk(schema, &self.value, "", 0, &mut fields);
        fields
    }
    fn walk(
        &self,
        schema: &Value,
        value: &Value,
        prefix: &str,
        level: i32,
        fields: &mut Vec<FormField>,
    ) {
        if level > 8 || fields.len() > 800 {
            return;
        }
        let Some(properties) = contracts::properties(schema) else {
            return;
        };
        for (key, schema) in properties {
            if labels::internal(key) || schema["readOnly"] == true {
                continue;
            }
            let path = if prefix.is_empty() {
                key.clone()
            } else {
                format!("{prefix}.{key}")
            };
            let current = &value[key];
            let field = FormField {
                key: path.clone().into(),
                label: contracts::contract()["configuration"]["labels"][&path]
                    .as_str()
                    .map(str::to_owned)
                    .unwrap_or_else(|| labels::label(key))
                    .into(),
                level,
                ..Default::default()
            };
            match contracts::kind(schema) {
                "object" => {
                    fields.push(FormField {
                        kind: "heading".into(),
                        ..field
                    });
                    self.walk(schema, current, &path, level + 1, fields);
                }
                "array" => {
                    let values = current.as_array();
                    fields.push(FormField {
                        kind: "array".into(),
                        value: values.map_or(0, Vec::len).to_string().into(),
                        ..field
                    });
                    for (i, child) in values.into_iter().flatten().enumerate() {
                        let child_path = format!("{path}.{i}");
                        if contracts::kind(&contracts::resolve(schema)["items"]) == "object" {
                            self.walk(
                                &contracts::resolve(schema)["items"],
                                child,
                                &child_path,
                                level + 1,
                                fields,
                            );
                        } else {
                            fields.push(FormField {
                                key: child_path.into(),
                                label: format!("{} {}", labels::label(key), i + 1).into(),
                                value: display(child).into(),
                                kind: "text".into(),
                                ..Default::default()
                            });
                        }
                    }
                }
                _ => {
                    let options = self.choices(key, schema);
                    let mut field = field;
                    field.value = self
                        .buffers
                        .get(&path)
                        .cloned()
                        .unwrap_or_else(|| match current {
                            Value::String(s) => s.clone(),
                            Value::Null => String::new(),
                            v => v.to_string(),
                        })
                        .into();
                    field.kind = if !options.is_empty() {
                        "choice"
                    } else if contracts::kind(schema) == "boolean" {
                        "bool"
                    } else if key.to_lowercase().contains("password") {
                        "password"
                    } else if [
                        "notes",
                        "note",
                        "summary",
                        "nextAction",
                        "description",
                        "bodyHtml",
                        "contentHtml",
                        "mainProducts",
                        "shippingMarks",
                        "specialClauses",
                        "specialTerms",
                        "letterOfCreditContent",
                        "addressEN",
                        "addressCN",
                        "customerAddressEN",
                        "exporterAddressEN",
                        "exporterAddressCN",
                    ]
                    .contains(&key.as_str())
                    {
                        "multiline"
                    } else if ["integer", "number"].contains(&contracts::kind(schema)) {
                        "number"
                    } else {
                        "text"
                    }
                    .into();
                    field.selected = options
                        .iter()
                        .position(|(value, _)| value == current)
                        .map_or(-1, |i| i as i32);
                    field.options = model(
                        options
                            .iter()
                            .map(|(_, label)| label.clone().into())
                            .collect(),
                    );
                    fields.push(field);
                }
            }
        }
    }
    pub fn edit(&mut self, path: &str, text: &str) -> Result<(), String> {
        self.invalid_field = path.into();
        self.buffers.insert(path.into(), text.into());
        let schema = self.schema_at(path).clone();
        let kind = contracts::kind(&schema);
        let value = if text.trim().is_empty() && contracts::nullable(&schema) {
            Value::Null
        } else {
            match kind {
                "boolean" => json!(text == "true"),
                "integer" => json!(text.trim().parse::<i64>().map_err(|_| format!(
                    "{}请输入整数。",
                    labels::label(path.rsplit('.').next().unwrap_or(path))
                ))?),
                "number" => serde_json::to_value(export_doc_engine::invoice::parse_number(text)?)
                    .map_err(|e| e.to_string())?,
                _ => json!(text),
            }
        };
        set(&mut self.value, path, value)?;
        self.error.clear();
        self.invalid_field.clear();
        Ok(())
    }
    pub fn choose(&mut self, path: &str, index: usize) -> Result<(), String> {
        let schema = self.schema_at(path);
        let key = path.rsplit('.').next().unwrap_or(path);
        let options = self.choices(key, schema);
        let value = options
            .get(index)
            .ok_or("选项已变化，请重新选择。")?
            .0
            .clone();
        self.buffers.remove(path);
        set(&mut self.value, path, value)
    }
    pub fn array_action(&mut self, path: &str, action: &str) -> Result<(), String> {
        let schema = contracts::resolve(self.schema_at(path)).clone();
        let current = get(&self.value, path);
        let mut array = current.as_array().cloned().unwrap_or_default();
        match action {
            "add" if array.len() < 5000 => array.push(contracts::initial(&schema["items"])),
            "remove" => {
                array.pop();
            }
            _ => return Err("列表已达到容量上限。".into()),
        }
        self.buffers
            .retain(|key, _| !key.starts_with(&format!("{path}.")));
        set(&mut self.value, path, json!(array))
    }
    pub fn validate(&mut self) -> Result<(), String> {
        for (path, value) in self.buffers.clone() {
            self.edit(&path, &value)?;
        }
        Ok(())
    }
    fn schema_at(&self, path: &str) -> &Value {
        path.split('.').fold(&self.schema, |schema, key| {
            let schema = contracts::resolve(schema);
            if contracts::kind(schema) == "array" {
                &schema["items"]
            } else {
                &schema["properties"][key]
            }
        })
    }
    fn choices(&self, key: &str, schema: &Value) -> Vec<(Value, String)> {
        if let Some(options) = self.lookups.get(key) {
            let mut choices = vec![(
                if contracts::kind(schema) == "string" {
                    json!("")
                } else {
                    Value::Null
                },
                "未选择".into(),
            )];
            choices.extend(options.clone());
            return choices;
        }
        let options: &[(&str, &str)] = match key {
            "currency" => &[
                ("USD", "USD 美元"),
                ("CNY", "CNY 人民币"),
                ("EUR", "EUR 欧元"),
                ("GBP", "GBP 英镑"),
                ("JPY", "JPY 日元"),
                ("HKD", "HKD 港币"),
            ],
            "role" => &[
                ("User", "普通用户"),
                ("Admin", "管理员"),
                ("Sales", "业务员"),
                ("Document", "单证员"),
                ("Administration", "行政"),
            ],
            "notifyPartyMode" => &[
                ("None", "无通知人"),
                ("SameAsConsignee", "同收货人"),
                ("Separate", "单独填写"),
            ],
            "shippingMarksType" => &[("Text", "文字唛头"), ("Image", "图片唛头")],
            "employmentType" => &[
                ("FullTime", "全职"),
                ("PartTime", "兼职"),
                ("Intern", "实习"),
                ("Contractor", "合同工"),
            ],
            "targetStatus" => &[
                ("Verified", "已核对"),
                ("Shipped", "已出运"),
                ("Completed", "已结汇"),
                ("Cancelled", "已作废"),
            ],
            "nextStage" => &[
                ("线索", "线索"),
                ("需求确认", "需求确认"),
                ("已报价", "已报价"),
                ("谈判中", "谈判中"),
                ("已成交", "已成交"),
                ("已失单", "已失单"),
            ],
            "dataScope" => &[
                ("Own", "本人"),
                ("Department", "本部门"),
                ("Company", "本公司"),
                ("All", "全部"),
            ],
            "priceCalculationMode" => &[
                ("UnitPriceDriven", "按单价计算金额"),
                ("LineAmountDriven", "按金额反算单价"),
            ],
            _ => &[],
        };
        if !options.is_empty() {
            return options
                .iter()
                .map(|(key, label)| (json!(key), (*label).into()))
                .collect();
        }
        contracts::resolve(schema)["enum"]
            .as_array()
            .map(|values| values.iter().map(|v| (v.clone(), display(v))).collect())
            .unwrap_or_default()
    }
}
pub fn subset(schema: &Value, keys: &[&str]) -> Value {
    json!({"type":"object","properties":keys.iter().filter_map(|key|contracts::properties(schema)?.get(*key).map(|v|((*key).to_owned(),v.clone()))).collect::<serde_json::Map<_,_>>()})
}
pub fn get<'a>(value: &'a Value, path: &str) -> &'a Value {
    path.split('.').fold(value, |v, key| {
        if v.is_array() {
            key.parse::<usize>()
                .ok()
                .and_then(|i| v.get(i))
                .unwrap_or(&Value::Null)
        } else {
            &v[key]
        }
    })
}
pub fn set(value: &mut Value, path: &str, next: Value) -> Result<(), String> {
    let keys: Vec<_> = path.split('.').collect();
    let mut current = value;
    for (index, key) in keys.iter().enumerate() {
        if current.is_null() {
            *current = json!({});
        }
        if index + 1 == keys.len() {
            if let Some(array) = current.as_array_mut() {
                let i = key.parse::<usize>().map_err(|_| "列表索引无效。")?;
                *array.get_mut(i).ok_or("列表项已变化。")? = next;
            } else {
                current
                    .as_object_mut()
                    .ok_or("字段结构不匹配。")?
                    .insert((*key).into(), next);
            }
            return Ok(());
        }
        current = if current.is_array() {
            current
                .get_mut(key.parse::<usize>().map_err(|_| "列表索引无效。")?)
                .ok_or("列表项已变化。")?
        } else {
            current
                .as_object_mut()
                .ok_or("字段结构不匹配。")?
                .entry((*key).to_string())
                .or_insert(Value::Null)
        };
    }
    Err("字段路径不能为空。".into())
}
