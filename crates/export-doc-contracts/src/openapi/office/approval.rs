use super::{endpoint, object, reference};
use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    let mode = json!({"type":"string","enum":["Single","DepartmentChain","Named"]});
    s["OaApprovalStep"] = object(
        json!({"approverUserId":int,"approverName":text,"status":{"type":"string","enum":["Waiting","Approved","Rejected"]},"actedByUserId":{"type":["integer","null"],"format":"int64"},"actedByName":text,"actedAt":text,"note":text,"delegationKey":text}),
        &[
            "approverUserId",
            "approverName",
            "status",
            "actedByName",
            "actedAt",
            "note",
            "delegationKey",
        ],
    );
    s["OaApprovalPlan"] = object(
        json!({"mode":mode,"policyVersion":int,"steps":{"type":"array","items":reference("OaApprovalStep"),"maxItems":10}}),
        &["mode", "policyVersion", "steps"],
    );
    s["OaRequest"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    s["OaRequest"]["properties"]["canReview"] = json!({"type":"boolean"});
    s["OaRequest"]["properties"]["lastRemindedAt"] = text.clone();
    s["OaEvent"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    s["OaApprovalRule"] = object(
        json!({"kind":{"type":"string","enum":["leave","overtime","expense","travel","purchase","general"]},"mode":mode,"approverUserIds":{"type":"array","items":int,"maxItems":10}}),
        &["kind", "mode", "approverUserIds"],
    );
    s["OaApprovalDelegation"] = object(
        json!({"key":text,"principalUserId":int,"delegateUserId":int,"startsAt":{"type":"string","format":"date-time"},"endsAt":{"type":"string","format":"date-time"},"isActive":{"type":"boolean"}}),
        &[
            "key",
            "principalUserId",
            "delegateUserId",
            "startsAt",
            "endsAt",
            "isActive",
        ],
    );
    let properties = json!({"expectedVersion":int,"rules":{"type":"array","items":reference("OaApprovalRule"),"minItems":6,"maxItems":6},"delegations":{"type":"array","items":reference("OaApprovalDelegation"),"maxItems":50}});
    s["OaApprovalSettingsSave"] = object(
        properties.clone(),
        &["expectedVersion", "rules", "delegations"],
    );
    let mut properties = properties;
    properties["versionNumber"] = int;
    s["OaApprovalSettings"] = object(properties, &["versionNumber", "rules", "delegations"]);
    for (method, id, action, body) in [
        ("get", "GetOaApprovalSettings", "settings-get", None),
        (
            "put",
            "SaveOaApprovalSettings",
            "settings-save",
            Some("OaApprovalSettingsSave"),
        ),
    ] {
        endpoint(
            doc,
            "/api/office/approval-settings",
            method,
            id,
            "system.users",
            "manage",
            body,
            "OaApprovalSettings",
            "approval-settings",
            action,
        );
    }
}
