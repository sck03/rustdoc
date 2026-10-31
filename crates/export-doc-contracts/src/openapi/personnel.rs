use serde_json::{Value, json};
#[path = "personnel_organization.rs"]
mod organization;

#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "PersonnelRecord",
        &[
            "attachments",
            "approvalRequiredActions",
            "organization",
            "companyScope",
        ],
    ),
    (
        "PersonnelOptions",
        &["approvalRequiredActions", "positions"],
    ),
    ("PersonnelCreateRequest", &["organization"]),
    ("PersonnelRegistrationCorrection", &["organization"]),
    ("ApiUserDto", &["effectiveDepartmentIds"]),
    (
        "PersonnelClearance",
        &[
            "reportingCount",
            "isCompanyExecutive",
            "businessDataCount",
            "invoiceCount",
            "paymentCount",
            "otherBusinessDataCount",
        ],
    ),
    (
        "ApiOrganizationCompanyDto",
        &[
            "executiveEmployeeId",
            "executiveAppointmentId",
            "executiveName",
            "positions",
            "additionalLeaders",
            "additionalLeaderNames",
        ],
    ),
    (
        "ApiOrganizationCompanySaveRequest",
        &[
            "executiveEmployeeId",
            "executiveAppointmentId",
            "positions",
            "additionalLeaders",
        ],
    ),
    ("ApiUserAccountDto", &["oaAssignedOnly"]),
    ("ApiUserSaveRequest", &["oaAssignedOnly"]),
    ("ApiPermissionTemplateDto", &["oaAssignedOnly"]),
    ("ApiPermissionTemplateOptionDto", &["oaAssignedOnly"]),
    ("ApiPermissionTemplateSaveRequest", &["oaAssignedOnly"]),
    ("ApiUserCapabilitiesDto", &["oaAssignedOnly"]),
    ("PersonnelUpdateRequest", &["correctionNote"]),
    (
        "ApiOrganizationDepartmentDto",
        &[
            "managerEmployeeNumber",
            "managerAppointmentId",
            "additionalLeaders",
            "additionalLeaderNames",
        ],
    ),
    (
        "ApiOrganizationDepartmentSaveRequest",
        &["additionalLeaders", "managerAppointmentId"],
    ),
    (
        "OfficeSupplyRequestRecord",
        &["employeeNumber", "employeeId"],
    ),
    ("MeetingBookingRecord", &["employeeNumber", "employeeId"]),
];

pub(super) fn extend(doc: &mut Value) {
    organization::extend(doc);
    let mut unlink = doc["paths"]["/api/office/people/{id}/account"]["post"].clone();
    unlink["operationId"] = json!("UnlinkPersonnelAccount");
    unlink["requestBody"]["content"]["application/json"]["schema"] =
        json!({"$ref":"#/components/schemas/PersonnelUnlinkAccountRequest"});
    doc["paths"]["/api/office/people/{id}/account/unlink"] = json!({"post":unlink});
    let mut request = doc["components"]["schemas"]["PersonnelAccountRequest"].clone();
    request["properties"]["reason"] = json!({"type":"string","minLength":1,"maxLength":500});
    request["required"]
        .as_array_mut()
        .unwrap()
        .push(json!("reason"));
    doc["components"]["schemas"]["PersonnelUnlinkAccountRequest"] = request;
    doc["paths"]["/api/office/people/options"]["get"]["parameters"] = json!([{
        "name":"employeeId", "in":"query", "required":false,
        "schema":{"type":"integer","format":"int64","minimum":1},
        "description":"Read departments for an authorized employee; omitted for registration in the current company."
    }]);
    let actions = json!({"type":"array","readOnly":true,"items":doc["components"]["schemas"]["OaPersonnelChange"]["properties"]["action"]});
    for schema in ["PersonnelRecord", "PersonnelOptions"] {
        doc["components"]["schemas"][schema]["properties"]["approvalRequiredActions"] =
            actions.clone();
    }
    doc["components"]["schemas"]["PersonnelUpdateRequest"]["properties"]["correctionNote"] =
        json!({"type":"string","maxLength":500});
    for schema in ["OfficeSupplyRequestRecord", "MeetingBookingRecord"] {
        doc["components"]["schemas"][schema]["properties"]["employeeId"] = json!({
            "type":"integer", "format":"int64", "readOnly":true,
            "description":"Stable employee reference, independent of the applicant name."
        });
    }
    for (schema, field) in [
        ("ApiOrganizationDepartmentDto", "managerEmployeeNumber"),
        ("OfficeSupplyRequestRecord", "employeeNumber"),
        ("MeetingBookingRecord", "employeeNumber"),
        ("OaRequest", "employeeNumber"),
    ] {
        doc["components"]["schemas"][schema]["properties"][field] = json!({
            "type":"string", "readOnly":true,
            "description":"Employee number for disambiguation; relationships and authorization use the stable employee ID. Request records retain the number captured when created."
        });
    }
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unlink_retains_account_assignment_security_and_errors() {
        let doc = crate::openapi::document();
        let link = &doc["paths"]["/api/office/people/{id}/account"]["post"];
        let unlink = &doc["paths"]["/api/office/people/{id}/account/unlink"]["post"];
        for field in ["security", "x-exportdoc-policy", "responses", "parameters"] {
            assert_eq!(unlink[field], link[field]);
        }
        assert_eq!(unlink["operationId"], "UnlinkPersonnelAccount");
        let body = &doc["components"]["schemas"]["PersonnelUnlinkAccountRequest"];
        assert_eq!(
            body["required"],
            json!([
                "expectedVersion",
                "userId",
                "expectedAccountVersion",
                "reason"
            ])
        );
        assert_eq!(body["properties"]["reason"]["maxLength"], 500);
    }
}
