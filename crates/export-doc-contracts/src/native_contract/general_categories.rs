//! Stable category identities and display names; all other layers consume this catalogue.
use super::model::*;

pub const CATEGORIES: &[(&str, &str)] = &[
    ("Seal", "用印"),
    ("Certificate", "证明开具"),
    ("IT", "IT 支持"),
    ("Repair", "维修"),
    ("Other", "其他事项"),
];

pub fn label(id: &str) -> Option<&'static str> {
    CATEGORIES
        .iter()
        .find(|(key, _)| *key == id)
        .map(|(_, name)| *name)
}

pub(super) fn category() -> Schema {
    s().enumeration(Enumeration::Values(
        CATEGORIES
            .iter()
            .map(|(id, _)| serde_json::json!(id))
            .collect(),
    ))
}

pub(super) fn requirements() -> Schema {
    o(CATEGORIES
        .iter()
        .map(|(id, _)| {
            (
                *id,
                s().enumeration(Enumeration::Values(vec![
                    serde_json::json!("Required"),
                    serde_json::json!("ApprovalOnly"),
                ])),
            )
        })
        .collect())
    .required(CATEGORIES.iter().map(|(id, _)| *id).collect())
    .additional(Additional::Allowed(false))
}
