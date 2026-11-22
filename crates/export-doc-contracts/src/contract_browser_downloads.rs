//! Reviewed browser attachment tickets preserve the existing file authorization.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    let source =
        &baseline["paths"]["/api/business-attachments/{id}/revisions/{revision}/content"]["get"];
    let mut create = source.clone();
    create["operationId"] = json!("CreateBusinessAttachmentDownloadTicket");
    create["x-exportdoc-timeout-seconds"] = json!(30);
    create["x-exportdoc-json-response-limit-bytes"] = json!(65536);
    let responses = create["responses"].as_object_mut().unwrap();
    assert!(responses.remove("206").is_some());
    assert!(responses.remove("416").is_some());
    responses.insert(
        "200".into(),
        json!({"description":"OK","content":{
            "application/json":{"schema":{"$ref":"#/components/schemas/ApiDownloadTicket"}}
        }}),
    );
    let parameters = create["parameters"].as_array_mut().unwrap();
    parameters.retain(|parameter| parameter["in"] == "path");
    for parameter in parameters {
        parameter["schema"]
            .as_object_mut()
            .unwrap()
            .remove("pattern");
    }
    let mut download = source.clone();
    download["operationId"] = json!("DownloadBusinessAttachmentWithTicket");
    download["x-exportdoc-policy"]["requiresAuthentication"] = json!(false);
    download["x-exportdoc-policy"]["requiresLicense"] = json!(false);
    let existing = &baseline["paths"]["/downloads/jobs/{token}"]["get"];
    download["parameters"] = existing["parameters"].clone();
    download["security"] = existing["security"].clone();
    baseline["paths"]["/api/business-attachments/{id}/revisions/{revision}/download-ticket"] =
        json!({"post":create});
    baseline["paths"]["/downloads/business-attachments/{token}"] = json!({"get":download});
}
