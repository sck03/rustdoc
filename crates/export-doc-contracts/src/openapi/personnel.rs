use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    let mut attachment = doc["components"]["schemas"]["OaAttachment"].clone();
    attachment["properties"]["id"] = json!({"type":"string"});
    doc["components"]["schemas"]["PersonnelAttachment"] = attachment;
    doc["components"]["schemas"]["PersonnelRecord"]["properties"]["attachments"] =
        json!({"type":"array","items":{"$ref":"#/components/schemas/PersonnelAttachment"}});
    for (suffix, method, id, action) in [
        ("", "post", "UploadPersonnelAttachment", "edit"),
        (
            "/{attachmentId}",
            "get",
            "DownloadPersonnelAttachment",
            "view-details",
        ),
        (
            "/{attachmentId}",
            "delete",
            "DeletePersonnelAttachment",
            "edit",
        ),
    ] {
        let source = format!("/api/office/expense-requests/{{id}}/attachments{suffix}");
        let mut op = doc["paths"][&source][method].clone();
        op.as_object_mut().unwrap().remove("x-exportdoc-office");
        op["operationId"] = json!(id);
        op["tags"] = json!(["Personnel"]);
        op["x-exportdoc-policy"]["requirements"] =
            json!([{"resourceKey":"office.people","action":action}]);
        for parameter in op["parameters"].as_array_mut().unwrap() {
            if parameter["name"] == "attachmentId" {
                parameter["schema"] = json!({"type":"string"});
            }
        }
        if method != "get" {
            op["responses"]["200"]["content"]["application/json"]["schema"] =
                json!({"$ref":"#/components/schemas/PersonnelRecord"});
        }
        let target = format!("/api/office/people/{{id}}/attachments{suffix}");
        if doc["paths"].get(&target).is_none() {
            doc["paths"][&target] = json!({});
        }
        doc["paths"][&target][method] = op;
    }
}
