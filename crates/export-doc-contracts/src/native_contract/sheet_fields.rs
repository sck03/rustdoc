use super::model::{Schema, o, s};
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
pub(super) fn schema() -> Schema {
    o(SHEET_FIELDS
        .iter()
        .chain(DETAIL_FIELDS)
        .map(|(name, title)| (*name, s().title(title)))
        .collect())
    .required(vec![])
}
