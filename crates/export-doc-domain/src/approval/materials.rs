//! Frozen node material policy and typed verification results.
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::{BTreeMap, HashSet};
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct MaterialPolicy {
    #[serde(default)]
    pub visible_fields: Vec<String>,
    #[serde(default)]
    pub hidden_fields: Vec<String>,
    #[serde(default)]
    pub verification_fields: Vec<VerificationField>,
    #[serde(default)]
    pub require_review_points: bool,
    #[serde(default)]
    pub allow_restricted_attachments: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct VerificationField {
    pub key: String,
    pub label: String,
    pub kind: VerificationKind,
    pub required: bool,
    #[serde(default)]
    pub restricted: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
pub enum VerificationKind {
    Text,
    Decimal,
    Date,
    Boolean,
}
impl MaterialPolicy {
    pub fn validate(&self) -> Result<(), String> {
        for paths in [&self.visible_fields, &self.hidden_fields] {
            let mut unique = HashSet::new();
            if paths.len() > 100
                || paths.iter().any(|p| {
                    p.is_empty()
                        || p.len() > 160
                        || !p
                            .bytes()
                            .all(|b| b.is_ascii_alphanumeric() || b"._-*".contains(&b))
                        || !unique.insert(p)
                })
            {
                return Err("材料字段标识为空、重复、无效或超过容量。".into());
            }
        }
        let mut unique = HashSet::new();
        if self.verification_fields.len() > 20
            || self.verification_fields.iter().any(|f| {
                f.key.is_empty()
                    || f.key.len() > 40
                    || !f
                        .key
                        .bytes()
                        .all(|b| b.is_ascii_alphanumeric() || b"_-".contains(&b))
                    || f.label.trim().is_empty()
                    || f.label.chars().count() > 120
                    || !unique.insert(&f.key)
            })
        {
            return Err("核验字段配置无效，最多 20 项且标识不能重复。".into());
        }
        Ok(())
    }
    pub fn verify(
        &self,
        values: &BTreeMap<String, Value>,
        points: &[u16],
        point_count: usize,
    ) -> Result<(), String> {
        self.validate()?;
        if values
            .keys()
            .any(|key| !self.verification_fields.iter().any(|f| &f.key == key))
        {
            return Err("提交了本节点未声明的核验字段。".into());
        }
        for field in &self.verification_fields {
            let value = values.get(&field.key);
            if value.is_none_or(|v| v.is_null() || v.as_str().is_some_and(|s| s.trim().is_empty()))
            {
                if field.required {
                    return Err(format!("请填写核验项：{}。", field.label));
                }
                continue;
            }
            let value = value.expect("checked value");
            let valid = match field.kind {
                VerificationKind::Boolean => value.is_boolean(),
                VerificationKind::Text => value.as_str().is_some_and(|v| v.chars().count() <= 1000),
                VerificationKind::Decimal => value
                    .as_str()
                    .is_some_and(|v| v.parse::<rust_decimal::Decimal>().is_ok()),
                VerificationKind::Date => {
                    value.as_str().is_some_and(|v| crate::oa::date(v).is_ok())
                }
            };
            if !valid {
                return Err(format!("核验项类型无效：{}。", field.label));
            }
        }
        let unique = points.iter().copied().collect::<HashSet<_>>();
        if unique.len() != points.len()
            || points.iter().any(|p| usize::from(*p) >= point_count)
            || (self.require_review_points && unique.len() != point_count)
        {
            return Err("请逐项确认本节点的审查要点，不能遗漏或重复。".into());
        }
        Ok(())
    }
}
