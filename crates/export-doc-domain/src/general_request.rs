//! General requests keep approval and fulfillment as distinct business decisions.
use export_doc_contracts::native_contract::general_categories::CATEGORIES;
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub enum HandlingRequirement {
    Required,
    ApprovalOnly,
}

pub fn default_requirements() -> BTreeMap<String, HandlingRequirement> {
    CATEGORIES
        .iter()
        .map(|(id, _)| ((*id).into(), HandlingRequirement::Required))
        .collect()
}

pub fn validate_requirements(values: &BTreeMap<String, HandlingRequirement>) -> Result<(), String> {
    if values.len() != CATEGORIES.len()
        || CATEGORIES.iter().any(|(id, _)| !values.contains_key(*id))
    {
        return Err("每个通用申请类别都须明确办理要求。".into());
    }
    if CATEGORIES
        .iter()
        .any(|(id, _)| *id != "Other" && values.get(*id) != Some(&HandlingRequirement::Required))
    {
        return Err("用印、证明、IT 支持和维修必须办理；仅其他事项可以配置为仅审批。".into());
    }
    Ok(())
}
