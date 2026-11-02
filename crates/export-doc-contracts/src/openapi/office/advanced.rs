//! Complete workflow definitions and recovery use one generated API contract.
use super::{endpoint, object, reference};
use serde_json::{Value, json};
pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    let stable = json!({"type":["string","null"],"pattern":"^[1-9][0-9]*$"});
    let nullable_int = json!({"type":["integer","null"],"format":"int64","minimum":1});
    let ids = json!({"type":"array","items":int,"maxItems":50,"uniqueItems":true});
    let phase = json!({"type":"string","enum":["Decision","Fulfillment"],"default":"Decision"});
    let state =
        json!({"type":"string","enum":["Pending","Active","Completed","Blocked","Cancelled"]});
    let kind = json!({"type":"string","enum":["Start","Approval","Exclusive","Fork","Join","Copy","Handling","System","Subflow","End"],"default":"Approval"});
    let source = json!({"type":"string","enum":["Named","DepartmentManager","DepartmentLeaders","LineManager","FunctionalManagers","CompanyExecutive","CompanyLeaders","Position","Responsibility","ProjectLeader","Handlers","Requester"]});
    let handling = json!({"type":"string","enum":["Complete","Receive","Issue","Return","Acceptance"],"default":"Complete"});
    let system = json!({"type":"string","enum":["None","ApplyPersonnel","ReserveSupply","GenerateApprovalDocument"],"default":"None"});
    let logic = json!({"type":"string","enum":["All","Any"],"default":"All"});
    let values = json!({"type":"object","additionalProperties":true,"maxProperties":32});
    s["OaApprovalCondition"]["properties"]["field"]["enum"]
        .as_array_mut()
        .unwrap()
        .push(json!("Returnable"));
    s["OaConditionGroup"] = object(
        json!({"logic":logic,"conditions":{"type":"array","items":reference("OaApprovalCondition"),"maxItems":16},"groups":{"type":"array","items":reference("OaConditionGroup"),"maxItems":16}}),
        &[],
    );
    s["OaExclusiveBranch"] = object(
        json!({"target":text,"condition":reference("OaConditionGroup"),"isDefault":{"type":"boolean","default":false}}),
        &["target"],
    );
    s["OaActorSelector"] = object(
        json!({"source":source,"sourceId":stable,"userIds":ids}),
        &["source"],
    );
    s["OaEscalation"] = object(
        json!({"afterHours":{"type":"integer","minimum":1,"maximum":8760},"action":{"type":"string","enum":["Remind","NotifySupervisor","Reassign","Exception"]},"target":{"anyOf":[reference("OaActorSelector"),{"type":"null"}]}}),
        &["afterHours", "action"],
    );
    s["OaAssignmentOrigin"] = object(
        json!({"source":{"anyOf":[source.clone(),{"type":"null"}]},"sourceId":stable,"unitCode":text,"appointmentId":text}),
        &[],
    );
    s["OaAssignmentOrigin"]["properties"]["departmentId"] = stable.clone();
    s["OaAssignmentOrigin"]["properties"]["subjectPersonId"] = nullable_int.clone();
    s["OaAssignmentOrigin"]["properties"]["subjectAppointmentId"] = text.clone();
    s["OaActorBinding"] = object(
        json!({"userId":int,"personId":nullable_int,"name":text,"context":text,"administration":{"type":"boolean"},"origin":reference("OaAssignmentOrigin")}),
        &["userId", "personId", "name", "context"],
    );
    s["OaNodeExecution"] = object(
        json!({"userId":int,"personId":nullable_int,"context":text,"occurredAt":{"type":"string","format":"date-time"},"note":text,"verificationValues":values}),
        &["userId", "personId", "context", "occurredAt", "note"],
    );
    for name in ["OaApprovalStage", "OaApprovalNode"] {
        let p = &mut s[name]["properties"];
        p["kind"] = kind.clone();
        p["pairKey"] = text.clone();
        p["phase"] = phase.clone();
        p["handlingAction"] = handling.clone();
        p["systemAction"] = system.clone();
        p["escalations"] = json!({"type":"array","items":reference("OaEscalation"),"maxItems":4});
        p["allowAdministration"] = json!({"type":"boolean","default":false});
    }
    let p = &mut s["OaApprovalStage"]["properties"];
    p["source"] = source;
    p["sourceId"] = stable;
    p["conditionLogic"] = logic;
    p["conditionGroups"] =
        json!({"type":"array","items":reference("OaConditionGroup"),"maxItems":16});
    p["branches"] = json!({"type":"array","items":reference("OaExclusiveBranch"),"maxItems":16});
    let p = &mut s["OaApprovalNode"]["properties"];
    p["state"] = state.clone();
    p["actors"] = json!({"type":"array","items":reference("OaActorBinding"),"maxItems":200});
    p["escalationTargets"] = json!({"type":"array","items":{"type":"array","items":reference("OaActorBinding"),"maxItems":50},"maxItems":4});
    p["execution"] = json!({"anyOf":[reference("OaNodeExecution"),{"type":"null"}]});
    p["errorCode"] = text.clone();
    p["escalationCursor"] = json!({"type":"integer","minimum":0,"maximum":4});
    p["nextEscalationAt"] = text.clone();
    s["OaApprovalStep"]["properties"]["origin"] = reference("OaAssignmentOrigin");
    s["OaApprovalStep"]["properties"]["mandatory"] = json!({"type":"boolean","default":false});
    s["OaApprovalStep"]["properties"]["actedByPersonId"] = nullable_int.clone();
    s["OaApprovalStep"]["properties"]["approverPersonId"] = nullable_int;
    s["OaSubflowInput"] = object(
        json!({"sourcePath":{"type":"string","enum":["totalAmount","currency","durationDays","durationHours","departmentId","category","personnelChange.action","isReturnable"]},"target":{"type":"string","enum":["Amount","Currency","Duration","Department","Category","PersonnelAction","Returnable"]}}),
        &["sourcePath", "target"],
    );
    s["OaSubflowOutput"] = object(
        json!({"nodeKey":text,"verificationKey":text,"target":text,"aggregation":{"type":"string","enum":["AllEqual","Sum","AllTrue","AnyTrue"],"default":"AllEqual"},"required":{"type":"boolean","default":false}}),
        &["nodeKey", "verificationKey", "target"],
    );
    let propagation =
        json!({"type":"string","enum":["Propagate","Block","Ignore"],"default":"Propagate"});
    s["OaSubflowPolicy"] = object(
        json!({"rejected":propagation,"returned":propagation,"cancelled":propagation,"exception":propagation,"compensation":{"type":"string","enum":["None","ManualReview","ReturnResource","CancelPendingPersonnel"],"default":"None"}}),
        &[],
    );
    for name in ["OaSubflowReference", "OaSubflowInstance"] {
        s[name]["properties"]["inputs"] =
            json!({"type":"array","items":reference("OaSubflowInput"),"maxItems":32});
        s[name]["properties"]["outputs"] =
            json!({"type":"array","items":reference("OaSubflowOutput"),"maxItems":32});
        s[name]["properties"]["policy"] = reference("OaSubflowPolicy");
    }
    let p = &mut s["OaSubflowInstance"]["properties"];
    p["inputSnapshot"] = values.clone();
    p["parentKey"] = text.clone();
    p["phase"] = phase.clone();
    p["boundaryKey"] = text.clone();
    s["OaSubflowProgress"] = object(
        json!({"state":state,"outputs":values,"errorCode":text,"outcome":text,"controlOutcome":text,"compensationCompleted":{"type":"boolean"}}),
        &[],
    );
    s["OaApprovalPlan"]["properties"]["subflowProgress"] =
        json!({"type":"object","additionalProperties":reference("OaSubflowProgress")});
    s["OaFulfillmentTask"] = object(
        json!({"nodeKey":text,"taskKey":{"type":["string","null"]},"name":text,"kind":kind,"state":state,"action":{"type":"string","enum":["complete","issue","return"]},"canExecute":{"type":"boolean"},"dueAt":text,"errorCode":text,"executedBy":{"type":["integer","null"]},"executedAt":{"type":["string","null"]},"materialPolicy":reference("OaMaterialPolicy"),"reviewPoints":{"type":"array","items":text}}),
        &[
            "nodeKey",
            "name",
            "kind",
            "state",
            "action",
            "canExecute",
            "dueAt",
            "errorCode",
        ],
    );
    for name in [
        "OaRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
    ] {
        s[name]["properties"]["workflowTasks"] =
            json!({"type":"array","items":reference("OaFulfillmentTask"),"maxItems":32});
        s[name]["properties"]["fulfillmentState"] = json!({"type":"string","enum":["Pending","Active","Completed","Blocked","Suspended","Cancelled"]});
    }
    for name in [
        "OaAction",
        "OaCommandAction",
        "OfficeDecisionRequest",
        "OfficeReturnRequest",
    ] {
        s[name]["properties"]["nodeKey"] = text.clone();
        s[name]["properties"]["decision"] =
            json!({"type":"string","enum":["Approve","Reject","Return"],"default":"Approve"});
    }
    for name in [
        "OaWorkflowCommandSave",
        "OaRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
    ] {
        let enumeration = if name == "OaWorkflowCommandSave" {
            &mut s[name]["properties"]["command"]["enum"]
        } else {
            &mut s[name]["properties"]["workflowActions"]["items"]["enum"]
        };
        enumeration.as_array_mut().unwrap().extend(
            [
                "RetryNode",
                "CancelSubflow",
                "RetrySubflow",
                "CompensateSubflow",
            ]
            .map(|v| json!(v)),
        );
    }
    for name in ["OaEvent", "OfficeRequestEventRecord"] {
        s[name]["properties"]
            .as_object_mut()
            .unwrap()
            .remove("approvalPlan");
        s[name]["properties"]["approvalGeneration"] = int.clone();
        s[name]["properties"]["definitionVersion"] = int.clone();
        s[name]["properties"]["definitionKey"] = text.clone();
    }
    s["OaWorkflowCommandSave"]["properties"]["subflowKey"] = text.clone();
    s["OaWorkflowCommandSave"]["properties"]["quantity"] =
        json!({"type":"integer","minimum":1,"maximum":1000000});
    s["OaSimulationReporting"] = object(
        json!({"managerEmployeeId":{"type":["integer","null"],"format":"int64"},"managerAssignmentId":{"type":["string","null"]},"functionalManagerEmployeeIds":{"type":"array","items":int,"maxItems":10},"functionalManagerAssignmentIds":{"type":"array","items":text,"maxItems":10}}),
        &[],
    );
    s["OaSimulationSample"] = object(
        json!({"employeeId":int,"appointmentId":text,"amount":text,"currency":text,"duration":text,"category":text,"isReturnable":{"type":"boolean"},"projectId":{"type":["string","null"],"pattern":"^[1-9][0-9]*$"}}),
        &["employeeId", "appointmentId"],
    );
    s["OaSimulationSample"]["properties"]["reporting"] = reference("OaSimulationReporting");
    for name in [
        "OaRequestSave",
        "OaRequest",
        "OaSubmittedRequest",
        "ResourceSubmittedRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
        "MeetingBookingCreateRequest",
        "SupplyRequestCreateRequest",
        "MeetingBookingUpdateRequest",
        "SupplyRequestUpdateRequest",
    ] {
        s[name]["properties"]["projectId"] =
            json!({"type":["string","null"],"pattern":"^[1-9][0-9]*$"});
    }
    for name in [
        "OaRequest",
        "OaSubmittedRequest",
        "ResourceSubmittedRequest",
        "MeetingBookingRecord",
        "OfficeSupplyRequestRecord",
    ] {
        s[name]["properties"]["projectName"] = json!({"type":"string","readOnly":true});
        s[name]["properties"]["projectCode"] = json!({"type":"string","readOnly":true});
    }
    s["OaPublishAction"] = s["OaDefinitionAction"].clone();
    s["OaPublishAction"]["properties"]["samples"] =
        json!({"type":"array","items":reference("OaSimulationSample"),"minItems":1,"maxItems":20});
    s["OaPublishAction"]["required"]
        .as_array_mut()
        .unwrap()
        .extend([json!("samples"), json!("commandKey")]);
    s["OaPublishAction"]["properties"]["commandKey"] =
        json!({"type":"string","minLength":8,"maxLength":128});
    s["OaScheduleAction"] = s["OaPublishAction"].clone();
    s["OaActivationCancel"] = s["OaDefinitionAction"].clone();
    s["OaActivationCancel"]["properties"]["commandKey"] =
        s["OaPublishAction"]["properties"]["commandKey"].clone();
    s["OaActivationCancel"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("commandKey"));
    s["OaScheduleAction"]["properties"]["activateAt"] =
        json!({"type":"string","format":"date-time"});
    s["OaScheduleAction"]["properties"]["commandKey"] =
        json!({"type":"string","minLength":8,"maxLength":128});
    s["OaScheduleAction"]["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("activateAt"));
    s["OaActivation"] = object(
        json!({"id":text,"companyCode":text,"definitionKey":text,"definitionRecordId":int,"definitionVersion":int,"expectedActiveVersion":int,"actorId":int,"contextId":text,"activateAt":{"type":"string","format":"date-time"},"state":{"type":"string","enum":["Pending","Applied","Blocked","Cancelled"]},"errorCode":text,"versionNumber":int}),
        &[
            "id",
            "definitionKey",
            "definitionVersion",
            "activateAt",
            "state",
            "errorCode",
            "versionNumber",
        ],
    );
    s["OaApprovalSettings"]["properties"]["activations"] =
        json!({"type":"array","items":reference("OaActivation"),"maxItems":100});
    s["OaActivation"]["properties"]["expectedActiveEnabled"] = json!({"type":"boolean"});
    s["OaDefinitionVersion"] = object(
        json!({"id":int,"version":int,"definition":reference("OaApprovalRule"),"publishedAt":text,"publishedBy":int}),
        &["id", "version", "definition", "publishedAt", "publishedBy"],
    );
    s["OaDefinitionVersions"] = object(
        json!({"items":{"type":"array","items":reference("OaDefinitionVersion"),"maxItems":100},"totalCount":int}),
        &["items", "totalCount"],
    );
    s["OaDefinitionDifference"] = object(
        json!({"path":text,"before":{},"after":{}}),
        &["path", "before", "after"],
    );
    s["OaDefinitionDiff"] = object(
        json!({"definitionKey":text,"fromVersion":int,"toVersion":int,"changes":{"type":"array","items":reference("OaDefinitionDifference")}}),
        &["definitionKey", "fromVersion", "toVersion", "changes"],
    );
    s["OaRoutingSource"] = object(
        json!({"id":text,"unitId":text,"code":text,"name":text}),
        &["id", "unitId", "code", "name"],
    );
    s["OaRoutingSources"] = object(
        json!({"items":{"type":"array","items":reference("OaRoutingSource"),"maxItems":100},"totalCount":int}),
        &["items", "totalCount"],
    );
    endpoint(
        doc,
        "/api/office/approval-settings/routing-sources",
        "get",
        "GetOaRoutingSources",
        "system.users",
        "manage",
        None,
        "OaRoutingSources",
        "approval-settings",
        "settings-routing-sources",
    );
    doc["paths"]["/api/office/approval-settings/routing-sources"]["get"]["parameters"] = json!([
        {"name":"source","in":"query","required":true,"schema":{"type":"string","enum":["Position","Responsibility","ProjectLeader"]}},
        {"name":"unitId","in":"query","schema":{"type":"integer","format":"int64","minimum":1}},
        {"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},
        {"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}
    ]);
    for (path, method, id, action, body, response) in [
        (
            "schedule",
            "post",
            "ScheduleOaDefinition",
            "settings-schedule",
            Some("OaScheduleAction"),
            "OaApprovalSettings",
        ),
        (
            "versions",
            "get",
            "ListOaDefinitionVersions",
            "settings-versions",
            None,
            "OaDefinitionVersions",
        ),
        (
            "diff",
            "get",
            "DiffOaDefinitions",
            "settings-diff",
            None,
            "OaDefinitionDiff",
        ),
        (
            "activations/{activationId}/cancel",
            "post",
            "CancelOaActivation",
            "settings-cancel-activation",
            Some("OaActivationCancel"),
            "OaApprovalSettings",
        ),
    ] {
        endpoint(
            doc,
            &format!("/api/office/approval-settings/{path}"),
            method,
            id,
            "system.users",
            "manage",
            body,
            response,
            "approval-settings",
            action,
        );
    }
    endpoint(
        doc,
        "/api/office/request-projects",
        "get",
        "GetOaRequestProjects",
        "office.approval-hub",
        "view",
        None,
        "OaRoutingSources",
        "request-projects",
        "project-options",
    );
    doc["paths"]["/api/office/request-projects"]["get"]["parameters"] = json!([
        {"name":"kind","in":"query","required":true,"schema":{"type":"string","enum":["personnel","leave","overtime","expense","travel","purchase","general","room","supply"]}},
        {"name":"employeeId","in":"query","schema":{"type":"integer","format":"int64","minimum":1}},
        {"name":"appointmentId","in":"query","schema":{"type":"string","maxLength":100}},
        {"name":"departmentId","in":"query","schema":{"type":"string","maxLength":50}},
        {"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1,"default":1}},
        {"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100,"default":20}}
    ]);
    doc["paths"]["/api/office/approval-settings/publish"]["post"]["requestBody"]["content"]["application/json"]
        ["schema"] = reference("OaPublishAction");
    doc["paths"]["/api/office/approval-settings/activations/{activationId}/cancel"]["post"]["parameters"] = json!([{"name":"activationId","in":"path","required":true,"schema":{"type":"string","pattern":"^[1-9][0-9]*$"}}]);
    let s = &mut doc["components"]["schemas"];
    s["OaDecision"] = object(
        json!({"generation":{"type":"integer","minimum":1,"maximum":1000},"taskKey":text,"nodeKey":text,"signature":reference("OaApprovalStep")}),
        &["generation", "taskKey", "nodeKey", "signature"],
    );
    s["OaDecisionPage"] = object(
        json!({"items":{"type":"array","items":reference("OaDecision"),"maxItems":100},"totalCount":int,"pageNumber":int,"pageSize":int}),
        &["items", "totalCount", "pageNumber", "pageSize"],
    );
    for (path, name, kind, response, resource) in [
        (
            "personnel-requests",
            "PersonnelChangeRequest",
            "personnel",
            "OaRequest",
            "office.personnel-changes",
        ),
        (
            "leave-requests",
            "LeaveRequest",
            "leave",
            "OaRequest",
            "office.leave",
        ),
        (
            "overtime-requests",
            "OvertimeRequest",
            "overtime",
            "OaRequest",
            "office.overtime",
        ),
        (
            "expense-requests",
            "ExpenseRequest",
            "expense",
            "OaRequest",
            "office.expenses",
        ),
        (
            "travel-requests",
            "TravelRequest",
            "travel",
            "OaRequest",
            "office.travel",
        ),
        (
            "purchase-requests",
            "PurchaseRequest",
            "purchase",
            "OaRequest",
            "office.purchase",
        ),
        (
            "general-requests",
            "GeneralRequest",
            "general",
            "OaRequest",
            "office.general",
        ),
        (
            "bookings",
            "MeetingBooking",
            "room",
            "MeetingBookingRecord",
            "office.rooms",
        ),
        (
            "supply-requests",
            "OfficeSupplyRequest",
            "supply",
            "OfficeSupplyRequestRecord",
            "office.supplies",
        ),
    ] {
        endpoint(
            doc,
            &format!("/api/office/{path}/{{id}}/fulfillment"),
            "post",
            &format!("Execute{name}Fulfillment"),
            resource,
            "view",
            Some("OaCommandAction"),
            response,
            kind,
            "fulfillment",
        );
        let decisions = format!("/api/office/{path}/{{id}}/decisions");
        endpoint(
            doc,
            &decisions,
            "get",
            &format!("ListDecisionsOf{name}"),
            resource,
            "view",
            None,
            "OaDecisionPage",
            kind,
            "decisions",
        );
        for key in ["pageNumber", "pageSize", "submissionNumber", "generation"] {
            doc["paths"][&decisions]["get"]["parameters"]
                .as_array_mut()
                .unwrap()
                .push(json!({"name":key,"in":"query","schema":{"type":"integer","minimum":1}}));
        }
    }
    let s = &mut doc["components"]["schemas"];
    s["OaWorkflowException"] = object(
        json!({"requestId":int,"requestKind":text,"versionNumber":int,"nodeKey":text,"nodeName":text,"nodeKind":kind,"phase":phase,"state":state,"errorCode":text,"dueAt":text}),
        &[
            "requestId",
            "requestKind",
            "versionNumber",
            "nodeKey",
            "nodeName",
            "nodeKind",
            "phase",
            "state",
            "errorCode",
            "dueAt",
        ],
    );
    s["OaWorkflowExceptions"] = object(
        json!({"items":{"type":"array","items":reference("OaWorkflowException"),"maxItems":100},"totalCount":int}),
        &["items", "totalCount"],
    );
    s["OaSystemRetryRequest"] = object(
        json!({"requestId":int,"requestKind":text,"nodeKey":text,"expectedVersion":int,"commandKey":{"type":"string","minLength":8,"maxLength":128},"note":{"type":"string","minLength":1,"maxLength":500}}),
        &[
            "requestId",
            "requestKind",
            "nodeKey",
            "expectedVersion",
            "commandKey",
            "note",
        ],
    );
    s["OaSystemRetryResult"] = object(
        json!({"requestId":int,"versionNumber":int,"state":{"anyOf":[state.clone(),{"type":"null"}]}}),
        &["requestId", "versionNumber", "state"],
    );
    endpoint(
        doc,
        "/api/office/approval-settings/exceptions",
        "get",
        "ListOaWorkflowExceptions",
        "system.users",
        "manage",
        None,
        "OaWorkflowExceptions",
        "approval-settings",
        "settings-exceptions",
    );
    doc["paths"]["/api/office/approval-settings/exceptions"]["get"]["parameters"] = json!([{"name":"pageNumber","in":"query","schema":{"type":"integer","minimum":1}},{"name":"pageSize","in":"query","schema":{"type":"integer","minimum":1,"maximum":100}}]);
    endpoint(
        doc,
        "/api/office/approval-settings/retry-system",
        "post",
        "RetryOaSystemNode",
        "system.users",
        "manage",
        Some("OaSystemRetryRequest"),
        "OaSystemRetryResult",
        "approval-settings",
        "settings-retry-system",
    );
    for path in ["versions", "diff"] {
        let parameters =
            &mut doc["paths"][format!("/api/office/approval-settings/{path}")]["get"]["parameters"];
        if !parameters.is_array() {
            *parameters = json!([]);
        }
        parameters.as_array_mut().unwrap().push(json!({"name":"definitionKey","in":"query","required":true,"schema":{"type":"string","minLength":1,"maxLength":80}}));
        for name in if path == "diff" {
            vec!["fromVersion", "toVersion"]
        } else {
            vec!["pageNumber", "pageSize"]
        } {
            parameters.as_array_mut().unwrap().push(json!({"name":name,"in":"query","required":path=="diff","schema":{"type":"integer","minimum":1}}));
        }
    }
}
