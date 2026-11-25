//! Reviewed removal of unused APIs and addition of bounded image preview.
use serde_json::{Value, json};

pub fn apply(baseline: &mut Value) {
    for kind in [
        "personnel",
        "leave",
        "overtime",
        "expense",
        "travel",
        "purchase",
        "general",
    ] {
        assert!(
            baseline["paths"]
                .as_object_mut()
                .unwrap()
                .remove(&format!(
                    "/api/office/{kind}-requests/{{id}}/approval-reassign"
                ))
                .is_some()
        );
    }
    let schemas = &mut baseline["components"]["schemas"];
    assert!(
        schemas
            .as_object_mut()
            .unwrap()
            .remove("OaApprovalReassign")
            .is_some()
    );
    for (name, fields) in [
        ("ApiSettingsValidationMessageDto", &["isAutoFixable"][..]),
        (
            "ApiSettingsValidationResponse",
            &["hasWarnings", "canAutoFix", "normalizedSettings"][..],
        ),
    ] {
        for field in fields {
            assert!(
                schemas[name]["properties"]
                    .as_object_mut()
                    .unwrap()
                    .remove(*field)
                    .is_some()
            );
            schemas[name]["required"]
                .as_array_mut()
                .unwrap()
                .retain(|value| value != field);
        }
    }
    schemas["ApiSettingsValidationMessageDto"]["properties"]["level"]["enum"] =
        json!(["error", "warning", "info"]);
    let path = &mut baseline["paths"]["/api/tools/ocr/preview-image"];
    path["get"]["x-exportdoc-processing-slot"] = json!(true);
    path["get"]["x-exportdoc-timeout-seconds"] = json!(30);
    let mut upload = path["get"].clone();
    upload["operationId"] = json!("PreviewUploadedOcrImage");
    upload.as_object_mut().unwrap().remove("parameters");
    upload["responses"].as_object_mut().unwrap().remove("206");
    upload["responses"].as_object_mut().unwrap().remove("416");
    upload["requestBody"] = json!({"required":true,"content":{"application/octet-stream":{"schema":{"type":"string","format":"byte"}}},"x-exportdoc-max-bytes":25 * 1024 * 1024});
    path["post"] = upload;
}
