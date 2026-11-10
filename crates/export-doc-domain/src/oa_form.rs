//! Bounded, versioned application forms. Values are data, never executable expressions.
use rust_decimal::Decimal;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::{BTreeMap, HashSet},
    str::FromStr,
};

pub type FormValues = BTreeMap<String, Value>;

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum FieldKind {
    Text,
    Multiline,
    Decimal,
    Money,
    Date,
    Select,
    Person,
    Department,
    Table,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Visibility {
    pub field: String,
    pub equals: String,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct FormField {
    pub key: String,
    pub label: String,
    pub kind: FieldKind,
    #[serde(default)]
    pub required: bool,
    #[serde(default)]
    pub options: Vec<String>,
    #[serde(default)]
    pub currency: String,
    #[serde(default)]
    pub columns: Vec<FormField>,
    #[serde(default)]
    pub visible_when: Option<Visibility>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct FormDefinition {
    pub category: String,
    #[serde(default)]
    pub department_ids: Vec<String>,
    #[serde(default)]
    pub attachment_required: bool,
    pub fields: Vec<FormField>,
}

pub fn field_key(key: &str) -> bool {
    !key.is_empty()
        && key.len() <= 40
        && key.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_')
}

impl FormDefinition {
    pub fn validate(&self) -> Result<(), String> {
        if self.category.trim().is_empty()
            || self.category.chars().count() > 50
            || self.department_ids.len() > 100
        {
            return Err("事项分类须为 1–50 字，适用部门最多 100 个。".into());
        }
        let mut departments = HashSet::new();
        if self
            .department_ids
            .iter()
            .any(|id| id.is_empty() || id.len() > 50 || !departments.insert(id))
        {
            return Err("事项适用部门无效或重复。".into());
        }
        validate_fields(&self.fields, false)
    }
    pub fn validate_values(&self, values: &FormValues, submitting: bool) -> Result<(), String> {
        self.validate()?;
        if serde_json::to_vec(values)
            .map_err(|_| "表单编码失败。")?
            .len()
            > 256 * 1024
        {
            return Err("表单数据不能超过 256 KiB。".into());
        }
        validate_values(&self.fields, values, submitting)
    }
}

fn validate_fields(fields: &[FormField], nested: bool) -> Result<(), String> {
    if fields.is_empty() || fields.len() > if nested { 10 } else { 40 } {
        return Err("表单须有 1–40 个字段，明细须有 1–10 列。".into());
    }
    let mut keys = HashSet::new();
    for field in fields {
        if !field_key(&field.key)
            || !keys.insert(&field.key)
            || field.label.trim().is_empty()
            || field.label.chars().count() > 100
        {
            return Err("字段编号须唯一，名称须为 1–100 字。".into());
        }
        if let Some(condition) = &field.visible_when {
            if nested
                || condition.field == field.key
                || !fields.iter().take_while(|f| f.key != field.key).any(|f| {
                    f.key == condition.field
                        && f.visible_when.is_none()
                        && f.kind != FieldKind::Table
                })
                || condition.equals.len() > 100
            {
                return Err("显示条件只能引用前面的无条件普通字段。".into());
            }
        }
        if field.kind == FieldKind::Select {
            let unique: HashSet<_> = field.options.iter().collect();
            if field.options.is_empty()
                || field.options.len() > 50
                || unique.len() != field.options.len()
                || field
                    .options
                    .iter()
                    .any(|v| v.trim().is_empty() || v.chars().count() > 100)
            {
                return Err("选项须有 1–50 个不重复的有效值。".into());
            }
        } else if !field.options.is_empty() {
            return Err("只有选择字段可以配置选项。".into());
        }
        if field.kind == FieldKind::Money {
            if !["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].contains(&field.currency.as_str()) {
                return Err("金额字段须明确币种。".into());
            }
        } else if !field.currency.is_empty() {
            return Err("只有金额字段可以配置币种。".into());
        }
        if field.kind == FieldKind::Table {
            if nested {
                return Err("明细表不能嵌套。".into());
            }
            validate_fields(&field.columns, true)?;
        } else if !field.columns.is_empty() {
            return Err("普通字段不能包含明细列。".into());
        }
    }
    Ok(())
}
fn visible(field: &FormField, values: &FormValues) -> bool {
    field
        .visible_when
        .as_ref()
        .is_none_or(|c| values.get(&c.field).and_then(Value::as_str) == Some(c.equals.as_str()))
}
fn validate_values(
    fields: &[FormField],
    values: &FormValues,
    submitting: bool,
) -> Result<(), String> {
    if values
        .keys()
        .any(|key| !fields.iter().any(|f| &f.key == key))
    {
        return Err("申请包含未定义的字段。".into());
    }
    for field in fields {
        let empty = if field.kind == FieldKind::Table {
            Value::Array(vec![])
        } else {
            Value::String(String::new())
        };
        let value = values.get(&field.key).unwrap_or(&empty);
        if (field.kind == FieldKind::Table && !value.is_array())
            || (field.kind != FieldKind::Table && !value.is_string())
        {
            return Err(format!("{}的数据类型无效。", field.label));
        }
        let absent = value.as_str().is_some_and(|v| v.trim().is_empty())
            || value.as_array().is_some_and(Vec::is_empty);
        if !visible(field, values) {
            if !absent {
                return Err(format!("{}当前不可填写。", field.label));
            }
            continue;
        }
        if absent {
            if submitting && field.required {
                return Err(format!("请填写{}。", field.label));
            }
            continue;
        }
        if field.kind == FieldKind::Table {
            let rows = value
                .as_array()
                .filter(|r| r.len() <= 100)
                .ok_or("明细最多 100 行。")?;
            for row in rows {
                let row: FormValues =
                    serde_json::from_value(row.clone()).map_err(|_| "明细行格式无效。")?;
                validate_values(&field.columns, &row, submitting)?;
            }
            continue;
        }
        let value = value
            .as_str()
            .ok_or_else(|| format!("{}必须使用文字值。", field.label))?;
        if value.chars().count()
            > if field.kind == FieldKind::Multiline {
                2000
            } else {
                200
            }
        {
            return Err(format!("{}超过长度限制。", field.label));
        }
        match field.kind {
            FieldKind::Decimal | FieldKind::Money => {
                let number = Decimal::from_str(value)
                    .map_err(|_| format!("{}须为精确十进制数。", field.label))?;
                if number < Decimal::ZERO
                    || number.scale() > if field.kind == FieldKind::Money { 2 } else { 6 }
                {
                    return Err(format!("{}不能为负或超过小数位限制。", field.label));
                }
            }
            FieldKind::Date => {
                crate::oa::date(value)?;
            }
            FieldKind::Select if !field.options.iter().any(|v| v == value) => {
                return Err(format!("{}选项无效。", field.label));
            }
            FieldKind::Person if value.parse::<i64>().ok().is_none_or(|id| id <= 0) => {
                return Err("人员编号无效。".into());
            }
            FieldKind::Department if value.len() > 50 => return Err("部门编号无效。".into()),
            _ => {}
        }
    }
    Ok(())
}
