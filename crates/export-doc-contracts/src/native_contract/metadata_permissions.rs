//! Native declarations. Change these typed definitions, then regenerate both clients.
use super::model::*;

pub(super) fn value() -> serde_json::Value {
    j_object(vec![
        ("resources", super::catalogue_resources::value()),
        ("modules", super::catalogue_modules::value()),
        ("roles", super::catalogue_roles::value()),
        ("dependencies", super::catalogue_dependencies::value()),
        ("editions", super::catalogue_editions::value()),
    ])
}
