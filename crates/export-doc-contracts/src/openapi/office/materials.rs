use super::{object, reference};
use serde_json::{Value, json};

// Response-only projections retain declared types while permitting absent protected fields.
// Request schemas remain complete and are never weakened by material visibility.
fn partial(schemas: &mut Value, mut schema: Value) -> Value {
    if let Some(source) = schema["$ref"].as_str().and_then(|s| s.rsplit('/').next()) {
        let source = source.to_owned();
        if !schemas[&source]["properties"].is_object() {
            return schema;
        }
        let target = format!("OaMaterial{source}");
        if schemas.get(&target).is_none() {
            let original = schemas[&source].clone();
            schemas[&target] = json!({});
            schemas[&target] = partial(schemas, original);
        }
        return reference(&target);
    }
    if let Some(properties) = schema.get_mut("properties").and_then(Value::as_object_mut) {
        for property in properties.values_mut() {
            *property = partial(schemas, property.clone());
        }
        schema["required"] = json!([]);
    }
    if let Some(items) = schema.get_mut("items") {
        *items = partial(schemas, items.clone());
    }
    schema
}

const IDENTITY_FIELDS: &[&str] = &[
    "id",
    "kind",
    "status",
    "versionNumber",
    "rowVersion",
    "ownerUserId",
    "companyScope",
    "departmentId",
    "employeeId",
    "employeeName",
    "employeeNumber",
    "applicantAccountId",
    "applicantName",
    "meetingRoomId",
    "officeSupplyId",
    "createdAt",
    "updatedAt",
    "requestKey",
    "submissionId",
    "submissionNumber",
    "approvalPlan",
    "approvalDocument",
    "canReview",
    "reviewTaskKeys",
    "workflowActions",
    "personnelExecution",
    "canHandle",
    "handlerNames",
    "approvalStageLabels",
    "approvalRoutingNotes",
    "attachments",
    "materialAccess",
];
pub(in crate::openapi) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    s["OaVerificationField"] = object(
        json!({"key":{"type":"string","minLength":1,"maxLength":40},"label":{"type":"string","minLength":1,"maxLength":120},"kind":{"type":"string","enum":["Text","Decimal","Date","Boolean"]},"required":{"type":"boolean"},"restricted":{"type":"boolean","default":false}}),
        &["key", "label", "kind", "required"],
    );
    let fields = json!({"type":"array","items":{"type":"string","minLength":1,"maxLength":160},"maxItems":100,"uniqueItems":true,"default":[]});
    s["OaMaterialPolicy"] = object(
        json!({"visibleFields":fields,"hiddenFields":fields,"verificationFields":{"type":"array","items":reference("OaVerificationField"),"maxItems":20,"default":[]},"requireReviewPoints":{"type":"boolean","default":false},"allowRestrictedAttachments":{"type":"boolean","default":false}}),
        &[],
    );
    s["OaMaterialAccess"] = object(
        json!({"isPartial":{"type":"boolean"},"omittedFields":{"type":"array","items":{"type":"string"}},"canDownloadArchive":{"type":"boolean"}}),
        &["isPartial", "omittedFields", "canDownloadArchive"],
    );
    for name in ["OaApprovalNode", "OaApprovalStage"] {
        s[name]["properties"]["materialPolicy"] = reference("OaMaterialPolicy");
    }
    let values = json!({"type":"object","additionalProperties":{"oneOf":[{"type":"string","maxLength":1000},{"type":"boolean"}]},"maxProperties":20,"default":{}});
    let points = json!({"type":"array","items":{"type":"integer","minimum":0,"maximum":11},"maxItems":12,"uniqueItems":true,"default":[]});
    for name in ["OaAction", "OaCommandAction", "OfficeDecisionRequest"] {
        s[name]["properties"]["verificationValues"] = values.clone();
        s[name]["properties"]["confirmedReviewPoints"] = points.clone();
    }
    s["OaApprovalStep"]["properties"]["verificationValues"] = values;
    s["OaApprovalStep"]["properties"]["confirmedReviewPoints"] = points;
    for key in ["approverPersonId", "actedByPersonId"] {
        s["OaApprovalStep"]["properties"][key] =
            json!({"type":["integer","null"],"format":"int64","readOnly":true});
    }
    for name in [
        "OaRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
        "OaSubmission",
        "ResourceSubmission",
    ] {
        s[name]["properties"]["materialAccess"] = reference("OaMaterialAccess");
    }
    s["OaAttachment"]["properties"]["classification"] =
        json!({"type":"string","enum":["Internal","Restricted"],"default":"Internal"});
    let mut registration = s["PersonnelCreateRequest"].clone();
    let profile_name = registration["properties"]["profile"]["$ref"]
        .as_str()
        .unwrap()
        .rsplit('/')
        .next()
        .unwrap()
        .to_owned();
    let mut profile = s[&profile_name].clone();
    profile["required"] = json!(["fullName"]);
    s["OaPersonnelProfileProjection"] = profile;
    registration["properties"]["profile"] = reference("OaPersonnelProfileProjection");
    s["OaPersonnelRegistrationProjection"] = registration;
    let mut change = s["OaPersonnelChange"].clone();
    change["properties"]["registration"] = reference("OaPersonnelRegistrationProjection");
    s["OaPersonnelChangeProjection"] = change;
    s["OaRequest"]["properties"]["personnelChange"] = reference("OaPersonnelChangeProjection");
    for name in [
        "OaRequest",
        "OaSubmittedRequest",
        "ResourceSubmittedRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
    ] {
        let mut schema = s[name].clone();
        for (key, property) in schema["properties"].as_object_mut().unwrap() {
            if !IDENTITY_FIELDS.contains(&key.as_str()) {
                *property = partial(s, property.clone());
            }
        }
        schema["required"]
            .as_array_mut()
            .unwrap()
            .retain(|key| IDENTITY_FIELDS.contains(&key.as_str().unwrap()));
        s[name] = schema;
    }
    for methods in doc["paths"].as_object_mut().unwrap().values_mut() {
        for endpoint in methods
            .as_object_mut()
            .into_iter()
            .flat_map(|m| m.values_mut())
        {
            if endpoint["x-exportdoc-office"]["action"] == "upload" {
                endpoint["requestBody"]["content"]["multipart/form-data"]["schema"]["properties"]
                    ["classification"] =
                    json!({"type":"string","enum":["Internal","Restricted"],"default":"Internal"});
            }
        }
    }
}
