//! Finance checks the printed submission number against its immutable archive.
//! The caller has already checked current company, complete scope and assignment.
use super::*;

pub(super) fn verify(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, "office.expenses", "view")?;
    let number = query
        .iter()
        .find(|(key, _)| *key == "submissionNumber")
        .and_then(|(_, value)| value.parse::<i64>().ok())
        .filter(|value| *value > 0)
        .ok_or_else(|| invalid("请填写审批单上的提交版本。"))?;
    let snapshot = tx
        .find_identity(submissions::KIND, &format!("{}:{number}", row["id"]))?
        .filter(|value| {
            value["requestId"] == row["id"] && value["companyScope"] == row["companyScope"]
        })
        .ok_or_else(|| error(404, "未找到该编号和提交版本的审批档案，请核对纸单。"))?;
    let is_current = snapshot["id"] == row["submissionId"];
    let approved = !text(&snapshot, "approvedAt").is_empty();
    let is_valid = approved
        && is_current
        && snapshot["status"] == "Approved"
        && FINANCE_STATUSES.contains(&text(row, "status").as_str());
    let mut result = json!({
        "requestId":row["id"],"submissionNumber":number,"currentSubmissionNumber":row["submissionNumber"],
        "submissionStatus":snapshot["status"],"requestStatus":row["status"],
        "isCurrent":is_current,"isValid":is_valid,
        "canOpenRequest":viewable(tx, actor, "office.expenses", row)?,"checkedAt":store::timestamp()
    });
    // A reference to a draft/rejected submission must not expose its private content.
    if approved {
        for key in ["approvedAt", "companyName", "departmentName"] {
            result[key] = snapshot[key].clone();
        }
        for key in [
            "employeeName",
            "employeeNumber",
            "title",
            "currency",
            "totalAmount",
        ] {
            result[key] = snapshot["request"][key].clone();
        }
    }
    result["documentStatus"] = submissions::project(tx, snapshot, false)?["documentStatus"].clone();
    Ok(contracts::dto(
        contracts::schema("OaApprovalVerification"),
        result,
    ))
}
