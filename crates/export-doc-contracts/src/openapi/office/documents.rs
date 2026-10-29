//! Submission archives and approval sheets use the OA authorization domain.
use super::{endpoint, object, reference};
use serde_json::{Value, json};
pub const DETAIL_FIELDS: &[(&str, &str)] = &[
    ("ApplicantName", "申请人姓名"),
    ("DepartmentName", "申请部门名称"),
    ("EmployeeNumber", "申请人工号"),
    ("JobTitle", "申请人职位"),
    ("SubmittedAt", "提交时间"),
];
pub const SHEET_FIELDS: &[(&str, &str)] = &[
    ("CompanyName", "公司名称"),
    ("FormName", "表单名称"),
    ("Reference", "申请编号与提交版本"),
    ("Applicant", "申请人、工号、部门和提交时间"),
    ("Title", "申请标题"),
    ("Summary", "申请摘要"),
    ("Reason", "申请说明"),
    ("Attachments", "本次提交的附件清单"),
    ("Approvals", "实际审批记录"),
    ("ApprovalResult", "审批结果与流程说明"),
    ("GeneratedAt", "PDF 生成时间"),
];

pub(super) fn extend(doc: &mut Value) {
    let text = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    let s = &mut doc["components"]["schemas"];
    s["OaSubmittedRequest"] = s["OaRequest"].clone();
    s["OaSubmission"] = object(
        json!({
            "id":int,"requestId":int,"submissionNumber":int,"requestVersion":int,
            "submittedAt":text,"approvedAt":text,"companyName":text,"departmentName":text,"formName":text,
            "status":text,"documentStatus":{"type":"string","enum":["NotApproved","Pending","Running","Ready","Failed","Voided"]},
            "documentError":text,"documentDigest":text,"request":reference("OaSubmittedRequest"),
            "applicantJobTitle":text,"appointment":reference("PersonnelAssignment")
        }),
        &[
            "id",
            "requestId",
            "submissionNumber",
            "requestVersion",
            "submittedAt",
            "companyName",
            "departmentName",
            "formName",
            "status",
            "documentStatus",
            "documentError",
            "documentDigest",
        ],
    );
    s["OaSubmissionPage"] = object(
        json!({"items":{"type":"array","items":reference("OaSubmission")},"pageNumber":int,"pageSize":int,"totalCount":int}),
        &["items", "pageNumber", "pageSize", "totalCount"],
    );
    s["OaRequest"]["properties"]["submissionId"] = int.clone();
    s["OaRequest"]["properties"]["submissionNumber"] = int.clone();
    s["OaRequest"]["properties"]["approvalDocument"] = reference("OaSubmission");
    s["OaEvent"]["properties"]["submissionId"] = int.clone();
    s["OaAttachment"]["properties"]["purpose"] =
        json!({"type":"string","enum":["ReimbursementForm","Voucher","Supporting"]});
    s["OaAttachment"]["properties"]["digest"] = text.clone();
    s["OaSheetTemplate"] = object(
        json!({"key":text,"name":text,"content":text}),
        &["key", "name", "content"],
    );
    let mut fields = json!({});
    for (name, title) in SHEET_FIELDS {
        fields[*name] = json!({"type":"string","title":title});
    }
    for (name, title) in DETAIL_FIELDS {
        fields[*name] = json!({"type":"string","title":title});
    }
    s["OaSheetFields"] = object(fields, &[]);
    for schema in ["OaApprovalSettings", "OaApprovalSettingsSave"] {
        s[schema]["properties"]["sheetTemplates"] =
            json!({"type":"array","items":reference("OaSheetTemplate"),"maxItems":16});
    }
    s["OaApprovalSettings"]["properties"]["sheetFieldCatalog"] =
        reference("ApiReportTemplateFieldCatalogResponse");
    s["OaApprovalSettings"]["properties"]["sheetDefaultContent"] = text.clone();
    s["OaSheetPreview"] = object(json!({"html":text}), &["html"]);
    s["OaApprovalPreview"] = object(
        json!({"approvalPlan":reference("OaApprovalPlan"),"approvalRoutingNotes":{"type":"array","items":text}}),
        &["approvalPlan", "approvalRoutingNotes"],
    );
    s["OaApprovalVerification"] = object(
        json!({
            "requestId":int,"submissionNumber":int,"currentSubmissionNumber":int,
            "submissionStatus":text,"requestStatus":text,"documentStatus":text,
            "isCurrent":{"type":"boolean"},"isValid":{"type":"boolean"},"canOpenRequest":{"type":"boolean"},
            "checkedAt":text,"approvedAt":text,"companyName":text,"departmentName":text,
            "employeeName":text,"employeeNumber":text,"title":text,"currency":text,"totalAmount":text
        }),
        &[
            "requestId",
            "submissionNumber",
            "currentSubmissionNumber",
            "submissionStatus",
            "requestStatus",
            "documentStatus",
            "isCurrent",
            "isValid",
            "canOpenRequest",
            "checkedAt",
        ],
    );
    endpoint(
        doc,
        "/api/office/expense-requests/{id}/verification",
        "get",
        "VerifyExpenseApproval",
        "office.expenses",
        "complete",
        None,
        "OaApprovalVerification",
        "expense",
        "verification",
    );
    doc["paths"]["/api/office/expense-requests/{id}/verification"]["get"]["parameters"].as_array_mut().unwrap().push(json!({"name":"submissionNumber","in":"query","required":true,"schema":{"type":"integer","format":"int64","minimum":1}}));
    endpoint(
        doc,
        "/api/office/approval-settings/sheet-preview",
        "post",
        "PreviewOaApprovalSheet",
        "system.users",
        "manage",
        Some("OaSheetTemplate"),
        "OaSheetPreview",
        "approval-settings",
        "sheet-preview",
    );
    for (kind, name, resource) in [
        ("personnel", "PersonnelChange", "office.personnel-changes"),
        ("leave", "Leave", "office.leave"),
        ("overtime", "Overtime", "office.overtime"),
        ("expense", "Expense", "office.expenses"),
        ("travel", "Travel", "office.travel"),
        ("purchase", "Purchase", "office.purchase"),
        ("general", "General", "office.general"),
    ] {
        let root = format!("/api/office/{kind}-requests");
        for (suffix, method, verb, action, response) in [
            (
                "/{id}/approval-preview",
                "get",
                "PreviewApprovalOf",
                "approval-preview",
                "OaApprovalPreview",
            ),
            (
                "/{id}/submissions",
                "get",
                "ListSubmissionsOf",
                "submissions",
                "OaSubmissionPage",
            ),
            (
                "/{id}/submissions/{submissionId}",
                "get",
                "GetSubmissionOf",
                "submission",
                "OaSubmission",
            ),
            (
                "/{id}/submissions/{submissionId}/pdf",
                "get",
                "DownloadApprovalOf",
                "document-download",
                "OaSubmission",
            ),
            (
                "/{id}/submissions/{submissionId}/bundle",
                "get",
                "DownloadMaterialsOf",
                "bundle-download",
                "OaSubmission",
            ),
            (
                "/{id}/submissions/{submissionId}/generate",
                "post",
                "GenerateApprovalOf",
                "document-generate",
                "OaSubmission",
            ),
        ] {
            let path = format!("{root}{suffix}");
            endpoint(
                doc,
                &path,
                method,
                &format!("{verb}{name}Request"),
                resource,
                if action == "approval-preview" {
                    "edit"
                } else {
                    "view"
                },
                None,
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
            if action.ends_with("download") {
                op["responses"]["200"]["content"] = json!({"application/octet-stream":{"schema":{"type":"string","format":"binary"}}});
            }
        }
        doc["paths"][format!("{root}/{{id}}/attachments")]["post"]["requestBody"]["content"]["multipart/form-data"]
            ["schema"]["properties"]["purpose"] =
            json!({"type":"string","enum":["ReimbursementForm","Voucher","Supporting"]});
    }
}
