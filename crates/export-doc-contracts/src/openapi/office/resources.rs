//! Resource drafts use the existing attachment, submission and document contracts.
use super::{endpoint, object, reference};
use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    for name in ["MeetingBookingStatus", "SupplyRequestStatus"] {
        s[name]["enum"].as_array_mut().unwrap().push(json!("Draft"));
    }
    let mut request = object(
        json!({}),
        &[
            "id",
            "kind",
            "companyScope",
            "ownerUserId",
            "departmentId",
            "employeeId",
            "employeeName",
            "employeeNumber",
            "title",
            "reason",
            "attachments",
            "approvalPlan",
            "versionNumber",
        ],
    );
    for key in [
        "id",
        "kind",
        "companyScope",
        "ownerUserId",
        "departmentId",
        "employeeId",
        "employeeName",
        "employeeNumber",
        "title",
        "reason",
        "attachments",
        "approvalPlan",
        "versionNumber",
    ] {
        request["properties"][key] = s["OaSubmittedRequest"]["properties"][key].clone();
    }
    for name in ["MeetingBookingRecord", "OfficeSupplyRequestRecord"] {
        for (key, value) in s[name]["properties"].as_object().unwrap() {
            request["properties"][key] = value.clone();
        }
        s[name]["properties"]["attachments"] =
            json!({"type":"array","items":reference("OaAttachment"),"readOnly":true});
        s[name]["properties"]["submissionId"] =
            json!({"type":"integer","format":"int64","readOnly":true});
        s[name]["properties"]["submissionNumber"] =
            json!({"type":"integer","format":"int64","readOnly":true});
        s[name]["properties"]["approvalDocument"] = reference("ResourceSubmission");
        s[name]["properties"]["companyScope"] = json!({"type":"string","readOnly":true});
        s[name]["required"]
            .as_array_mut()
            .unwrap()
            .push(json!("companyScope"));
    }
    request["properties"]["kind"] = json!({"type":"string","enum":["room","supply"]});
    s["ResourceSubmittedRequest"] = request;
    s["ResourceSubmission"] = s["OaSubmission"].clone();
    s["ResourceSubmission"]["properties"]["request"] = reference("ResourceSubmittedRequest");
    s["ResourceSubmissionPage"] = s["OaSubmissionPage"].clone();
    s["ResourceSubmissionPage"]["properties"]["items"]["items"] = reference("ResourceSubmission");
    for name in ["MeetingBookingCreateRequest", "SupplyRequestCreateRequest"] {
        assert!(s[name].is_object(), "Resource create schema {name}");
        s[name]["properties"]["submitImmediately"] = json!({"type":"boolean","default":true});
        s[name]["properties"]["departmentId"] = json!({"type":"string","maxLength":50});
    }
    for schema in ["OaApprovalSettings", "OaApprovalSettingsSave"] {
        s[schema]["properties"]["sheetTemplates"]["maxItems"] = json!(18);
    }
    for (kind, root, name, resource, record, update) in [
        (
            "room",
            "/api/office/bookings",
            "MeetingBooking",
            "office.rooms",
            "MeetingBookingRecord",
            "MeetingBookingUpdateRequest",
        ),
        (
            "supply",
            "/api/office/supply-requests",
            "OfficeSupplyRequest",
            "office.supplies",
            "OfficeSupplyRequestRecord",
            "SupplyRequestUpdateRequest",
        ),
    ] {
        for (suffix, method, verb, action, permission, body, response) in [
            ("/{id}", "get", "Get", "get", "view", None, record),
            (
                "/{id}/draft",
                "put",
                "UpdateDraftOf",
                "resource-update",
                "create",
                Some(update),
                record,
            ),
            (
                "/{id}/submit",
                "post",
                "Submit",
                "resource-submit",
                "create",
                Some("OaAction"),
                record,
            ),
            (
                "/{id}/attachments",
                "post",
                "UploadAttachmentTo",
                "upload",
                "create",
                None,
                record,
            ),
            (
                "/{id}/attachments/{attachmentId}",
                "get",
                "DownloadAttachmentOf",
                "download",
                "view",
                None,
                "OaAttachment",
            ),
            (
                "/{id}/attachments/{attachmentId}",
                "delete",
                "DeleteAttachmentOf",
                "delete-attachment",
                "create",
                Some("OaAction"),
                record,
            ),
            (
                "/{id}/submissions",
                "get",
                "ListSubmissionsOf",
                "submissions",
                "view",
                None,
                "ResourceSubmissionPage",
            ),
            (
                "/{id}/submissions/{submissionId}",
                "get",
                "GetSubmissionOf",
                "submission",
                "view",
                None,
                "ResourceSubmission",
            ),
            (
                "/{id}/submissions/{submissionId}/pdf",
                "get",
                "DownloadApprovalOf",
                "document-download",
                "view",
                None,
                "ResourceSubmission",
            ),
            (
                "/{id}/submissions/{submissionId}/bundle",
                "get",
                "DownloadMaterialsOf",
                "bundle-download",
                "view",
                None,
                "ResourceSubmission",
            ),
            (
                "/{id}/submissions/{submissionId}/generate",
                "post",
                "GenerateApprovalOf",
                "document-generate",
                "view",
                None,
                "ResourceSubmission",
            ),
        ] {
            let path = format!("{root}{suffix}");
            endpoint(
                doc,
                &path,
                method,
                &format!("{verb}{name}"),
                resource,
                permission,
                body,
                response,
                kind,
                action,
            );
            let op = &mut doc["paths"][&path][method];
            if suffix.contains("{submissionId}") {
                op["parameters"].as_array_mut().unwrap().push(json!({"name":"submissionId","in":"path","required":true,"schema":{"type":"integer","format":"int64","minimum":1}}));
            }
            if action == "submissions" {
                for name in ["pageNumber", "pageSize"] {
                    op["parameters"].as_array_mut().unwrap().push(
                        json!({"name":name,"in":"query","schema":{"type":"integer","minimum":1}}),
                    );
                }
            }
            if action.ends_with("download") || action == "download" {
                op["responses"]["200"]["content"] = json!({"application/octet-stream":{"schema":{"type":"string","format":"binary"}}});
            }
            if action == "upload" {
                op["requestBody"] = json!({"required":true,"content":{"multipart/form-data":{"schema":object(json!({"file":{"type":"string","format":"binary"},"expectedVersion":{"type":"integer","format":"int64"},"purpose":{"type":"string","enum":["Supporting"]}}), &["file","expectedVersion"])}}});
            }
        }
    }
}
