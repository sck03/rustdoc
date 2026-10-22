use super::{endpoint, object, reference};
use serde_json::{Value, json};
#[path = "handling.rs"]
mod handling;

pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    let mode = json!({"type":"string","enum":["Single","DepartmentChain","Named","Configured"]});
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
        json!({"mode":mode,"policyVersion":int,"matchedBranch":text,"steps":{"type":"array","items":reference("OaApprovalStep"),"maxItems":50}}),
        &["mode", "policyVersion", "steps"],
    );
    s["OaApprovalStep"]["properties"]["stage"] = json!({"type":["integer","null"],"minimum":0,"maximum":9,"description":"Zero-based countersign stage; omitted in serial plans, where each array position is its own stage."});
    s["OaApprovalStage"] = object(
        json!({
            "source":{"type":"string","enum":["Named","DepartmentManager","LineManager","FunctionalManagers","CompanyExecutive"]},
            "approverUserIds":{"type":"array","items":int,"maxItems":10,"uniqueItems":true}
        }),
        &["source", "approverUserIds"],
    );
    let stages = json!({"type":"array","items":reference("OaApprovalStage"),"maxItems":10});
    s["OaApprovalAmountBranch"] = object(
        json!({
            "key":{"type":"string","maxLength":80},"name":{"type":"string","maxLength":100},
            "currency":{"type":"string","pattern":"^[A-Z]{3}$"},"minimum":text,
            "maximum":{"type":["string","null"]},"stages":stages
        }),
        &["key", "name", "currency", "minimum", "stages"],
    );
    s["OaRequest"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    s["OaRequest"]["properties"]["canReview"] = json!({"type":"boolean"});
    s["OaRequest"]["properties"]["lastRemindedAt"] = text.clone();
    s["OaEvent"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    s["OaApprovalRule"] = object(
        json!({"kind":{"type":"string","enum":["leave","overtime","expense","travel","purchase","general","personnel"]},"mode":mode,"approverUserIds":{"type":"array","items":int,"maxItems":10},"stages":stages,"amountBranches":{"type":"array","items":reference("OaApprovalAmountBranch"),"maxItems":10}}),
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
    let properties = json!({"expectedVersion":int,"personnelApprovalActions":{"type":"array","items":{"type":"string","enum":["Hire","Confirm","Transfer","Depart","Rehire"]},"maxItems":5,"uniqueItems":true},"rules":{"type":"array","items":reference("OaApprovalRule"),"minItems":7,"maxItems":7},"delegations":{"type":"array","items":reference("OaApprovalDelegation"),"maxItems":50}});
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
    handling::extend(doc);
    let s = &mut doc["components"]["schemas"];
    s["OaApprovalReassign"] = object(
        json!({"expectedVersion":{"type":"integer","format":"int64"},"fromUserId":{"type":"integer","format":"int64","minimum":1},"toUserId":{"type":"integer","format":"int64","minimum":1},"note":{"type":"string","minLength":1,"maxLength":500}}),
        &["expectedVersion", "fromUserId", "toUserId", "note"],
    );
    for (kind, path, name) in [
        ("personnel", "personnel-requests", "PersonnelChangeRequest"),
        ("leave", "leave-requests", "LeaveRequest"),
        ("overtime", "overtime-requests", "OvertimeRequest"),
        ("expense", "expense-requests", "ExpenseRequest"),
        ("travel", "travel-requests", "TravelRequest"),
        ("purchase", "purchase-requests", "PurchaseRequest"),
        ("general", "general-requests", "GeneralRequest"),
    ] {
        let source = format!("/api/office/{path}/{{id}}/approve");
        let mut operation = doc["paths"][&source]["post"].clone();
        assert!(operation.is_object(), "Missing approval route: {source}");
        operation["operationId"] = json!(format!("Reassign{name}Approval"));
        operation["requestBody"]["content"]["application/json"]["schema"] =
            reference("OaApprovalReassign");
        operation["x-exportdoc-policy"]["requirements"] =
            json!([{"resourceKey":"system.users","action":"manage"}]);
        operation["x-exportdoc-office"]["action"] = json!("approval-reassign");
        operation["x-exportdoc-office"]["permission"] = json!("view");
        operation["x-exportdoc-office"]["kind"] = json!(kind);
        doc["paths"][format!("/api/office/{path}/{{id}}/approval-reassign")] =
            json!({"post":operation});
    }
}
