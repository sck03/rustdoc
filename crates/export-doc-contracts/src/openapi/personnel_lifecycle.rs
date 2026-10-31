//! Formal personnel changes run only through published OA definitions.
use serde_json::Value;

const RETIRED: &[&str] = &["confirm", "transfer", "depart", "rehire"];

pub(super) fn extend(doc: &mut Value) {
    let paths = doc["paths"].as_object_mut().unwrap();
    for action in RETIRED {
        paths
            .remove(&format!("/api/office/people/{{id}}/{action}"))
            .expect("frozen direct personnel transition");
    }
    doc["components"]["schemas"]
        .as_object_mut()
        .unwrap()
        .remove("PersonnelTransitionRequest")
        .expect("direct transition DTO");
    doc["components"]["schemas"]["OaPersonnelChange"]["properties"]["primaryAppointmentId"] = serde_json::json!({"type":"string","maxLength":64,"description":"Existing appointment to promote through an approved Transfer; empty keeps the current primary appointment."});
}

#[cfg(test)]
pub(super) fn compare(current: &mut Value, baseline: &Value) {
    for action in RETIRED {
        let path = format!("/api/office/people/{{id}}/{action}");
        assert!(
            current["paths"].get(&path).is_none(),
            "retired operation {path}"
        );
        current["paths"][&path] = baseline["paths"][&path].clone();
    }
    assert!(
        current["components"]["schemas"]
            .get("PersonnelTransitionRequest")
            .is_none()
    );
    current["components"]["schemas"]["PersonnelTransitionRequest"] =
        baseline["components"]["schemas"]["PersonnelTransitionRequest"].clone();
}
