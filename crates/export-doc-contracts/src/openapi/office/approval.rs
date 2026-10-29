use super::{endpoint, object, reference};
use serde_json::{Value, json};
#[path = "handling.rs"]
mod handling;
pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    let ids = json!({"type":"array","items":int,"maxItems":50,"uniqueItems":true});
    let decision = json!({"type":"string","enum":["All","Any"]});
    let keys = json!({"type":"array","items":text,"maxItems":32,"uniqueItems":true});
    s["OaApprovalCondition"] = object(
        json!({
            "field":{"type":"string","enum":["Amount","Currency","Duration","Department","Category","PersonnelAction"]},
            "operator":{"type":"string","enum":["Equal","NotEqual","Greater","GreaterOrEqual","Less","LessOrEqual"]},"value":text
        }),
        &["field", "operator", "value"],
    );
    s["OaApprovalStage"] = object(
        json!({
            "key":text,"name":text,"dependsOn":keys,
            "source":{"type":"string","enum":["Named","DepartmentManager","LineManager","FunctionalManagers","CompanyExecutive"]},
            "approverUserIds":ids,"decision":decision,"selfApproval":{"type":"string","enum":["Skip","Reject"]},
            "repeatedApprover":{"type":"string","enum":["Allow","Reject"]},
            "conditions":{"type":"array","items":reference("OaApprovalCondition"),"maxItems":16},"dueHours":{"type":"integer","minimum":1,"maximum":8760}
        }),
        &[
            "key",
            "name",
            "dependsOn",
            "source",
            "approverUserIds",
            "decision",
            "selfApproval",
            "repeatedApprover",
            "conditions",
            "dueHours",
        ],
    );
    s["OaApprovalNode"] = object(
        json!({"key":text,"name":text,"dependsOn":keys,"decision":decision,"dueHours":int,"independentReview":{"type":"boolean"},"skipped":{"type":"boolean"},"startedAt":text,"dueAt":text}),
        &[
            "key",
            "name",
            "dependsOn",
            "decision",
            "dueHours",
            "independentReview",
            "skipped",
            "startedAt",
            "dueAt",
        ],
    );
    s["OaApprovalStep"] = object(
        json!({"taskKey":text,"stage":{"type":"integer","minimum":0,"maximum":31},"decision":decision,
            "approverUserId":int,"approverName":text,"status":{"type":"string","enum":["Waiting","Approved","Rejected","Returned","Skipped","Cancelled"]},
            "actedByUserId":{"type":["integer","null"],"format":"int64"},"actedByName":text,"actedAt":text,"note":text,"delegationKey":text
        }),
        &[
            "taskKey",
            "stage",
            "decision",
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
        json!({"definitionKey":text,"policyVersion":int,"nodes":{"type":"array","items":reference("OaApprovalNode"),"maxItems":32},
        "steps":{"type":"array","items":reference("OaApprovalStep"),"maxItems":200},"notifyUserIds":ids}),
        &[
            "definitionKey",
            "policyVersion",
            "nodes",
            "steps",
            "notifyUserIds",
        ],
    );
    s["OaApprovalRule"] = object(
        json!({"key":text,"kind":{"type":"string","enum":["leave","overtime","expense","travel","purchase","general","personnel","room","supply"]},
        "variant":text,"name":text,"stages":{"type":"array","items":reference("OaApprovalStage"),"minItems":1,"maxItems":32},"notifyUserIds":ids}),
        &["key", "kind", "variant", "name", "stages", "notifyUserIds"],
    );
    s["OaPublication"] = object(
        json!({"key":text,"version":int,"enabled":{"type":"boolean"}}),
        &["key", "version", "enabled"],
    );
    s["OaApprovalDelegation"] = object(
        json!({"key":text,"principalUserId":int,"delegateUserId":int,"startsAt":{"type":"string","format":"date-time"},
        "endsAt":{"type":"string","format":"date-time"},"isActive":{"type":"boolean"}}),
        &[
            "key",
            "principalUserId",
            "delegateUserId",
            "startsAt",
            "endsAt",
            "isActive",
        ],
    );
    let props = json!({"expectedVersion":int,"personnelApprovalActions":{"type":"array","items":{"type":"string","enum":["Hire","Confirm","Transfer","Depart","Rehire"]},"maxItems":5,"uniqueItems":true},
        "rules":{"type":"array","items":reference("OaApprovalRule"),"minItems":13,"maxItems":13},
        "delegations":{"type":"array","items":reference("OaApprovalDelegation"),"maxItems":50}});
    s["OaApprovalSettingsSave"] =
        object(props.clone(), &["expectedVersion", "rules", "delegations"]);
    let mut props = props;
    props["versionNumber"] = int.clone();
    props["publications"] = json!({"type":"array","items":reference("OaPublication")});
    s["OaApprovalSettings"] = object(
        props,
        &["versionNumber", "rules", "delegations", "publications"],
    );
    s["OaDefinitionAction"] = object(
        json!({"expectedVersion":int,"definitionKey":text}),
        &["expectedVersion", "definitionKey"],
    );
    s["OaAction"]["properties"]["taskKey"] = text.clone();
    s["OfficeDecisionRequest"]["properties"]["taskKey"] = text.clone();
    for name in ["MeetingBookingRecord", "OfficeSupplyRequestRecord"] {
        s[name]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
        s[name]["properties"]["canReview"] = json!({"type":"boolean"});
        s[name]["properties"]["reviewTaskKeys"] = json!({"type":"array","items":text});
    }
    s["OfficeRequestEventRecord"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    s["OaRequest"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    s["OaRequest"]["properties"]["canReview"] = json!({"type":"boolean"});
    s["OaRequest"]["properties"]["reviewTaskKeys"] = json!({"type":"array","items":text});
    s["OaRequest"]["properties"]["approvalRoutingNotes"] = json!({"type":"array","items":text});
    s["OaRequest"]["properties"]["lastRemindedAt"] = text.clone();
    s["OaEvent"]["properties"]["approvalPlan"] = reference("OaApprovalPlan");
    if let Some(states) = s["OaRequest"]["properties"]["status"]["enum"].as_array_mut() {
        states.push(json!("Returned"));
    }
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
    for (path, id, action) in [
        ("publish", "PublishOaDefinition", "settings-publish"),
        ("disable", "DisableOaDefinition", "settings-disable"),
    ] {
        endpoint(
            doc,
            &format!("/api/office/approval-settings/{path}"),
            "post",
            id,
            "system.users",
            "manage",
            Some("OaDefinitionAction"),
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
    s["OaApprovalReassign"]["properties"]["taskKey"] =
        json!({"type":"string","minLength":1,"maxLength":180});
    s["OaApprovalReassign"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("taskKey"));
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
