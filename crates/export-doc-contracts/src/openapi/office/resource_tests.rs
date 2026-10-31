//! Verify each intentional resource extension, then compare every other frozen contract.
use super::reference;
use serde_json::{Value, json};
pub(in crate::openapi) fn verify_and_strip(current: &mut Value, baseline: &Value) {
    for (root, kind, name, resource, record) in [
        (
            "/api/office/bookings",
            "room",
            "MeetingBooking",
            "office.rooms",
            "MeetingBookingRecord",
        ),
        (
            "/api/office/supply-requests",
            "supply",
            "OfficeSupplyRequest",
            "office.supplies",
            "OfficeSupplyRequestRecord",
        ),
    ] {
        let path = format!("{root}/{{id}}");
        assert!(
            baseline["paths"][&path].get("get").is_none(),
            "resource detail is an additive endpoint"
        );
        let detail = current["paths"][&path]
            .as_object_mut()
            .unwrap()
            .remove("get")
            .unwrap();
        assert_eq!(detail["operationId"], format!("Get{name}"));
        assert_eq!(
            detail["x-exportdoc-policy"],
            baseline["paths"][root]["get"]["x-exportdoc-policy"]
        );
        assert_eq!(
            detail["security"],
            baseline["paths"][root]["get"]["security"]
        );
        assert_eq!(
            detail["responses"]["200"]["content"]["application/json"]["schema"],
            reference(record)
        );
        assert_eq!(
            detail["x-exportdoc-office"],
            json!({"kind":kind,"action":"get","resource":resource,"permission":"view"})
        );
    }
    for path in ["/api/office/bookings", "/api/office/supply-requests"] {
        let parameters = current["paths"][path]["get"]["parameters"]
            .as_array_mut()
            .unwrap();
        let index = parameters
            .iter()
            .position(|p| p["name"] == "approvalsOnly")
            .expect("resource inbox filter");
        assert_eq!(
            parameters.remove(index),
            json!({"name":"approvalsOnly","in":"query","schema":{"type":"boolean"}})
        );
    }
    for name in ["MeetingBookingStatus", "SupplyRequestStatus"] {
        let states = current["components"]["schemas"][name]["enum"]
            .as_array_mut()
            .unwrap();
        assert_eq!(
            states.split_off(states.len() - 3),
            vec![json!("Draft"), json!("Suspended"), json!("Terminated")]
        );
    }
    for name in ["MeetingBookingCreateRequest", "SupplyRequestCreateRequest"] {
        let props = current["components"]["schemas"][name]["properties"]
            .as_object_mut()
            .unwrap();
        assert_eq!(
            props.remove("submitImmediately"),
            Some(json!({"type":"boolean","default":true}))
        );
        assert_eq!(
            props.remove("departmentId"),
            Some(json!({"type":"string","maxLength":50}))
        );
    }
    for name in ["MeetingBookingRecord", "OfficeSupplyRequestRecord"] {
        let schema = &mut current["components"]["schemas"][name];
        let mut original_required = baseline["components"]["schemas"][name]["required"]
            .as_array()
            .unwrap()
            .clone();
        original_required.push(json!("companyScope"));
        let identity = [
            "id",
            "meetingRoomId",
            "officeSupplyId",
            "ownerUserId",
            "applicantName",
            "departmentId",
            "status",
            "createdAt",
            "versionNumber",
            "companyScope",
        ];
        assert_eq!(
            schema["required"],
            json!(
                original_required
                    .iter()
                    .filter(|key| identity.contains(&key.as_str().unwrap()))
                    .collect::<Vec<_>>()
            ),
            "only protected material values may be omitted"
        );
        schema["required"] = json!(original_required);
        let props = schema["properties"].as_object_mut().unwrap();
        assert_eq!(
            props.remove("workflowActions"),
            Some(
                json!({"type":"array","items":{"type":"string","enum":["AddBefore","AddAfter","AddParallel","RemoveSigner","ReturnToNode","Suspend","Resume","Terminate"]},"uniqueItems":true})
            )
        );
        assert_eq!(
            props.remove("materialAccess"),
            Some(reference("OaMaterialAccess"))
        );
        if name == "OfficeSupplyRequestRecord" {
            assert_eq!(
                props.remove("reservationState"),
                Some(
                    json!({"type":"string","enum":["Unreserved","Reserved","Insufficient","Consumed"],"readOnly":true})
                )
            );
        }
        assert_eq!(
            props.remove("attachments"),
            Some(json!({"type":"array","items":reference("OaAttachment"),"readOnly":true}))
        );
        for key in ["submissionId", "submissionNumber"] {
            assert_eq!(
                props.remove(key),
                Some(json!({"type":"integer","format":"int64","readOnly":true}))
            );
        }
        assert_eq!(
            props.remove("approvalDocument"),
            Some(reference("ResourceSubmission"))
        );
        assert_eq!(
            props.remove("companyScope"),
            Some(json!({"type":"string","readOnly":true}))
        );
        assert_eq!(
            schema["required"].as_array_mut().unwrap().pop(),
            Some(json!("companyScope"))
        );
    }
    for key in ["canRemove", "removalReason"] {
        let field = current["components"]["schemas"]["PersonnelAssignment"]["properties"]
            .as_object_mut()
            .unwrap()
            .remove(key)
            .unwrap();
        assert_eq!(
            field,
            json!({"type":if key=="canRemove" {"boolean"}else{"string"},"readOnly":true})
        );
    }
    for name in ["OfficeDecisionRequest", "OfficeReturnRequest"] {
        let schema = &mut current["components"]["schemas"][name];
        assert_eq!(
            schema["properties"]
                .as_object_mut()
                .unwrap()
                .remove("commandKey"),
            Some(
                json!({"type":"string","minLength":8,"maxLength":128,"pattern":"^[A-Za-z0-9_:-]+$"})
            )
        );
        assert_eq!(
            schema["required"].as_array_mut().unwrap().pop(),
            Some(json!("commandKey"))
        );
    }
    let props = current["components"]["schemas"]["OfficeDecisionRequest"]["properties"]
        .as_object_mut()
        .unwrap();
    assert_eq!(
        props.remove("verificationValues"),
        Some(
            json!({"type":"object","additionalProperties":{"oneOf":[{"type":"string","maxLength":1000},{"type":"boolean"}]},"maxProperties":20,"default":{}})
        )
    );
    assert_eq!(
        props.remove("confirmedReviewPoints"),
        Some(
            json!({"type":"array","items":{"type":"integer","minimum":0,"maximum":11},"maxItems":12,"uniqueItems":true,"default":[]})
        )
    );
}
