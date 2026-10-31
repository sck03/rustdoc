//! Stable personnel identifiers and effective periods, without storage or hosts.
use chrono::NaiveDate;
use serde::{Deserialize, Deserializer, Serialize, de::Error};
use std::collections::HashSet;

#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord, Hash, Serialize)]
#[serde(transparent)]
pub struct StableId(String);
impl StableId {
    pub fn parse(value: &str) -> Result<Self, String> {
        if value.starts_with('0')
            || !value.bytes().all(|v| v.is_ascii_digit())
            || value.parse::<i64>().ok().is_none_or(|v| v <= 0)
        {
            return Err("编号须为不带前导零的正整数十进制字符串。".into());
        }
        Ok(Self(value.into()))
    }
    pub fn number(&self) -> i64 {
        self.0.parse().expect("validated stable ID")
    }
    pub fn as_str(&self) -> &str {
        &self.0
    }
}
impl<'de> Deserialize<'de> for StableId {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        Self::parse(&String::deserialize(d)?).map_err(D::Error::custom)
    }
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct EffectivePeriod {
    pub starts_on: String,
    #[serde(default)]
    pub ends_on: Option<String>,
}
impl EffectivePeriod {
    pub fn bounds(&self) -> Result<(NaiveDate, Option<NaiveDate>), String> {
        let start = crate::oa::date(&self.starts_on)?;
        let end = self.ends_on.as_deref().map(crate::oa::date).transpose()?;
        if end.is_some_and(|end| end <= start) {
            return Err("有效区间终点须晚于起点；终点当天已失效。".into());
        }
        Ok((start, end))
    }
    pub fn contains(&self, day: NaiveDate) -> Result<bool, String> {
        let (start, end) = self.bounds()?;
        Ok(start <= day && end.is_none_or(|end| day < end))
    }
    pub fn overlaps(&self, other: &Self) -> Result<bool, String> {
        let (a, b) = self.bounds()?;
        let (c, d) = other.bounds()?;
        Ok(b.is_none_or(|b| c < b) && d.is_none_or(|d| a < d))
    }
}
pub fn label(value: &str, maximum: usize) -> Result<(), String> {
    if value.trim().is_empty() || value.chars().count() > maximum {
        return Err("名称或编号为空或超过容量。".into());
    }
    Ok(())
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Assignment {
    #[serde(default)]
    pub id: Option<StableId>,
    pub appointment_id: String,
    #[serde(default)]
    pub project_id: Option<StableId>,
    #[serde(default)]
    pub role: String,
    pub period: EffectivePeriod,
}
pub fn assignments(values: &[Assignment]) -> Result<(), String> {
    if values.len() > 200 {
        return Err("职责或项目成员最多 200 项。".into());
    }
    let mut ids = HashSet::new();
    for (i, value) in values.iter().enumerate() {
        label(&value.appointment_id, 64)?;
        value.period.bounds()?;
        if value.id.as_ref().is_some_and(|id| !ids.insert(id)) {
            return Err("成员编号重复。".into());
        }
        for other in &values[..i] {
            if value.appointment_id == other.appointment_id
                && value.project_id == other.project_id
                && value.period.overlaps(&other.period)?
            {
                return Err("同一任职的成员有效期不能重叠。".into());
            }
        }
    }
    Ok(())
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Position {
    #[serde(default)]
    pub id: Option<StableId>,
    pub unit_id: StableId,
    #[serde(default)]
    pub department_id: Option<StableId>,
    pub code: String,
    pub name: String,
    pub is_active: bool,
    pub expected_version: i64,
    pub note: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Employment {
    #[serde(default)]
    pub id: Option<StableId>,
    pub unit_id: StableId,
    pub person_id: StableId,
    pub employee_number: String,
    pub stage: String,
    pub period: EffectivePeriod,
    pub expected_version: i64,
    pub note: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Responsibility {
    #[serde(default)]
    pub id: Option<StableId>,
    pub unit_id: StableId,
    #[serde(default)]
    pub department_id: Option<StableId>,
    pub code: String,
    pub name: String,
    pub workflow_kinds: Vec<String>,
    pub assignments: Vec<Assignment>,
    pub is_active: bool,
    pub expected_version: i64,
    pub note: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Project {
    #[serde(default)]
    pub id: Option<StableId>,
    pub unit_id: StableId,
    pub code: String,
    pub name: String,
    pub owner_appointment_id: String,
    pub members: Vec<Assignment>,
    pub is_active: bool,
    pub expected_version: i64,
    pub note: String,
}
