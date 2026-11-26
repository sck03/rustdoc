//! Explicit validity replaces sentinel dates and counts; authorization metadata is unchanged.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    let schema = &mut baseline["components"]["schemas"]["ApiLicenseStatusResponse"];
    schema["properties"]["licenseState"] = json!({
        "type": "string",
        "enum": ["Trial", "TrialExpired", "Lifetime", "FixedTerm", "Expired", "Invalid"]
    });
    schema["required"]
        .as_array_mut()
        .unwrap()
        .insert(0, json!("licenseState"));
    schema["properties"]["daysRemaining"]["type"] = json!(["null", "integer", "string"]);
    schema["properties"]["expireDate"]["type"] = json!(["null", "string"]);
}
