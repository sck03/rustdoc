use serde_json::Value;

pub(super) fn apply(baseline: &mut Value) {
    // Explicit frozen delta; keep every existing profile in the independent comparison.
    let roles = baseline["x-exportdoc-permissions"]["roles"]
        .as_array_mut()
        .unwrap();
    assert_eq!(roles.len(), 8);
    let presets: Vec<Value> = serde_json::from_str(include_str!("approval-presets.json")).unwrap();
    roles.extend(presets);
}
