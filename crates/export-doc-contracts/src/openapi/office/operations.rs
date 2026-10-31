//! Exceptional commands and durable acknowledgements use the same office policy.
use super::{object, reference};
use serde_json::{Value, json};
pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    s["OaPersonnelExecution"] = object(
        json!({"state":{"type":"string","enum":["Pending","Applied","Blocked","Cancelled"]},"effectiveAt":{"type":"string","format":"date-time"},"errorCode":{"type":"string"}}),
        &["state", "effectiveAt", "errorCode"],
    );
    s["OaRequest"]["properties"]["personnelExecution"] =
        json!({"anyOf":[reference("OaPersonnelExecution"),{"type":"null"}]});
    s["OaWorkflowCandidate"] = object(json!({"id":int,"fullName":text}), &["id", "fullName"]);
    s["OaWorkflowCandidates"] = object(
        json!({"users":{"type":"array","items":reference("OaWorkflowCandidate"),"maxItems":100},"nextCursor":{"type":["integer","null"],"format":"int64"}}),
        &["users", "nextCursor"],
    );
    let commands = json!({"type":"string","enum":["AddBefore","AddAfter","AddParallel","RemoveSigner","ReturnToNode","Suspend","Resume","Terminate"]});
    for name in ["MeetingBookingStatus", "SupplyRequestStatus"] {
        s[name]["enum"]
            .as_array_mut()
            .unwrap()
            .extend([json!("Suspended"), json!("Terminated")]);
    }
    if let Some(states) = s["OaSubmission"]["properties"]["status"]["enum"].as_array_mut() {
        states.extend([json!("Suspended"), json!("Terminated")]);
    }
    s["OaRevisionPolicy"] = object(
        json!({"allowAddSigners":{"type":"boolean","default":false},"allowRemoveSigners":{"type":"boolean","default":false},"allowReturnToNode":{"type":"boolean","default":false},"minimumSigners":{"type":"integer","minimum":1,"maximum":200,"default":1},"pauseStopsClock":{"type":"boolean","default":true},"protectedUserIds":{"type":"array","items":{"type":"integer","format":"int64","minimum":1},"maxItems":50,"uniqueItems":true}}),
        &[],
    );
    for name in ["OaApprovalNode", "OaApprovalStage"] {
        s[name]["properties"]["revisionPolicy"] = reference("OaRevisionPolicy");
    }
    s["OaApprovalPlan"]["properties"]["generation"] =
        json!({"type":"integer","minimum":1,"maximum":1000});
    s["OaApprovalPlan"]["properties"]["suspendedAt"] = text.clone();
    s["OaApprovalPlan"]["properties"]["pausedSeconds"] = int.clone();
    s["OaApprovalStep"]["properties"]["sourceGeneration"] =
        json!({"type":["integer","null"],"minimum":1,"maximum":999});
    let key = json!({"type":"string","minLength":8,"maxLength":128,"pattern":"^[A-Za-z0-9_:-]+$"});
    for name in [
        "OaAction",
        "OfficeDecisionRequest",
        "OfficeReturnRequest",
        "OaApprovalReassign",
    ] {
        s[name]["properties"]["commandKey"] = key.clone();
        if name == "OaAction" {
            continue;
        }
        let required = s[name]["required"].as_array_mut().unwrap();
        if !required.iter().any(|k| k == "commandKey") {
            required.push(json!("commandKey"));
        }
    }
    s["OaCommandAction"] = s["OaAction"].clone();
    s["OfficeSupplyRequestRecord"]["properties"]["reservationState"] = json!({"type":"string","enum":["Unreserved","Reserved","Insufficient","Consumed"],"readOnly":true});
    s["OaCommandAction"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("commandKey"));
    for name in [
        "OaRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
    ] {
        s[name]["properties"]["workflowActions"] =
            json!({"type":"array","items":commands,"uniqueItems":true});
        if let Some(states) = s[name]["properties"]["status"]
            .get_mut("enum")
            .and_then(Value::as_array_mut)
        {
            states.extend([json!("Suspended"), json!("Terminated")]);
        }
    }
    s["OaWorkflowCommandSave"] = object(
        json!({"commandKey":key,"expectedVersion":int,"command":commands,"note":{"type":"string","minLength":1,"maxLength":500},"targetNodeKey":{"type":"string","maxLength":180},"userIds":{"type":"array","items":{"type":"integer","format":"int64","minimum":1},"maxItems":50,"uniqueItems":true}}),
        &["commandKey", "expectedVersion", "command", "note"],
    );
    s["OaCommandResult"] = object(
        json!({"requestId":int,"commandKey":text,"command":text,"committedAt":{"type":"string","format":"date-time"},"committedVersion":int,"currentVersion":int}),
        &[
            "requestId",
            "commandKey",
            "command",
            "committedAt",
            "committedVersion",
            "currentVersion",
        ],
    );
    for (path, name) in [
        ("personnel-requests", "PersonnelChangeRequest"),
        ("leave-requests", "LeaveRequest"),
        ("overtime-requests", "OvertimeRequest"),
        ("expense-requests", "ExpenseRequest"),
        ("travel-requests", "TravelRequest"),
        ("purchase-requests", "PurchaseRequest"),
        ("general-requests", "GeneralRequest"),
        ("bookings", "MeetingBooking"),
        ("supply-requests", "OfficeSupplyRequest"),
    ] {
        let base = format!("/api/office/{path}/{{id}}");
        let source = doc["paths"][format!("{base}/approve")]["post"].clone();
        assert!(source.is_object(), "Missing office approval source {base}");
        let mut post = source;
        if name == "MeetingBooking" || name == "OfficeSupplyRequest" {
            post = doc["paths"]["/api/office/leave-requests/{id}/approve"]["post"].clone();
            let room = name == "MeetingBooking";
            post["x-exportdoc-office"] = json!({"kind":if room{"room"}else{"supply"},"resource":if room{"office.rooms"}else{"office.supplies"},"permission":"view","action":"workflow-command"});
            post["responses"]["200"]["content"]["application/json"]["schema"] =
                reference(if room {
                    "MeetingBookingRecord"
                } else {
                    "OfficeSupplyRequestRecord"
                });
        }
        post["operationId"] = json!(format!("Execute{name}Workflow"));
        post["requestBody"]["content"]["application/json"]["schema"] =
            reference("OaWorkflowCommandSave");
        post["x-exportdoc-office"]["action"] = json!("workflow-command");
        post["x-exportdoc-office"]["permission"] = json!("view");
        let resource = post["x-exportdoc-office"]["resource"].clone();
        post["x-exportdoc-policy"]["requirements"] =
            json!([{"resourceKey":resource,"action":"view"}]);
        let mut get = post.clone();
        get.as_object_mut().unwrap().remove("requestBody");
        let mut candidates = get.clone();
        candidates["operationId"] = json!(format!("List{name}WorkflowCandidates"));
        candidates["x-exportdoc-office"]["action"] = json!("workflow-candidates");
        candidates["parameters"].as_array_mut().unwrap().push(json!({"name":"beforeId","in":"query","required":false,"schema":{"type":"integer","format":"int64","minimum":1}}));
        candidates["responses"]["200"]["content"]["application/json"]["schema"] =
            reference("OaWorkflowCandidates");
        doc["paths"][format!("{base}/workflow-candidates")] = json!({"get":candidates});
        get["operationId"] = json!(format!("Get{name}CommandResult"));
        get["x-exportdoc-office"]["action"] = json!("command-result");
        get["responses"]["200"]["content"]["application/json"]["schema"] =
            reference("OaCommandResult");
        get["parameters"].as_array_mut().unwrap().push(json!({"name":"commandKey","in":"path","required":true,"schema":{"type":"string","minLength":8,"maxLength":128}}));
        doc["paths"][format!("{base}/workflow-commands")] = json!({"post":post});
        doc["paths"][format!("{base}/commands/{{commandKey}}")] = json!({"get":get});
    }
    for (verb, action, permission, suffix) in [
        ("Execute", "personnel-execute", "approve", "execution"),
        ("Cancel", "personnel-cancel", "cancel", "cancel-execution"),
    ] {
        super::endpoint(
            doc,
            &format!("/api/office/personnel-requests/{{id}}/{suffix}"),
            "post",
            &format!("{verb}ApprovedPersonnelChange"),
            "office.personnel-changes",
            permission,
            Some("OaCommandAction"),
            "OaRequest",
            "personnel",
            action,
        );
    }
    for item in doc["paths"].as_object_mut().unwrap().values_mut() {
        for operation in item
            .as_object_mut()
            .unwrap()
            .values_mut()
            .filter(|op| op.is_object())
        {
            if matches!(
                operation["x-exportdoc-office"]["action"].as_str(),
                Some(
                    "submit"
                        | "approve"
                        | "reject"
                        | "return"
                        | "withdraw"
                        | "cancel"
                        | "void"
                        | "complete"
                        | "resource-submit"
                )
            ) && operation["requestBody"]["content"]["application/json"]["schema"]
                == reference("OaAction")
            {
                operation["requestBody"]["content"]["application/json"]["schema"] =
                    reference("OaCommandAction");
            }
        }
    }
}
