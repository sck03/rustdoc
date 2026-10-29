//! Immutable submitted content and original attachment references. Generic audit
//! logs remain redacted; these business archives follow the request's access.
use super::*;
pub(super) const KIND: &str = "oa-submission";
pub(super) const PDF: &str = "oa-approval-pdf";

pub(super) fn begin(tx: &Connection, actor: &Actor, row: &mut Value) -> Result<()> {
    let number = row["submissionNumber"].as_i64().unwrap_or(0) + 1;
    let mut request = row.clone();
    request["status"] = json!("Pending");
    request["versionNumber"] = json!(store::expected(row) + 1);
    let attachments = children(tx, row, "oa-attachment", 0, 20)?.1;
    for attachment in &attachments {
        let mut retained = attachment.clone();
        retained["retained"] = json!(true);
        retained["sourceRequestId"] = row["id"].clone();
        store::save_in_scope(
            tx,
            "oa-attachment",
            records::positive(attachment, "id", "附件")?,
            retained,
            Some(format!("{}:{}", row["id"], text(attachment, "digest"))),
            actor,
            "retain-submission",
            Some(row),
        )?;
    }
    request["attachments"] = json!(attachments);
    let person = if row["personnelChange"]["action"] == "Hire" {
        row["personnelChange"]["registration"].clone()
    } else {
        store::get(
            tx,
            "people",
            records::positive(row, "employeeId", "申请人员")?,
        )?
    };
    let company = tx
        .find_identity("companies", &store::normalize(&actor.company))?
        .ok_or_else(|| super::super::error::unavailable("申请公司资料缺失。"))?;
    let department = tx
        .find_identity("departments", &store::normalize(&text(row, "departmentId")))?
        .ok_or_else(|| super::super::error::unavailable("申请部门资料缺失。"))?;
    let settings = settings::load(tx, &actor.company)?;
    let template = sheet_templates::select(&settings, row)?;
    let value = json!({"requestId":row["id"],"submissionNumber":number,"requestVersion":request["versionNumber"],
        "submittedAt":store::timestamp(),"status":"Pending","request":request,
        "companyName":text(&company,"name"),"departmentName":text(&department,"name"),"applicantJobTitle":text(&person,"jobTitle"),
        "formName":template["name"],"template":template,"templateVersion":settings["versionNumber"]});
    let saved = store::save_in_scope(
        tx,
        KIND,
        0,
        value,
        Some(format!("{}:{number}", row["id"])),
        actor,
        "submit-snapshot",
        Some(row),
    )?;
    row["submissionId"] = saved["id"].clone();
    row["submissionNumber"] = json!(number);
    Ok(())
}
pub(super) fn after_save(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    action: &str,
    note: &str,
) -> Result<()> {
    if !matches!(
        action,
        "approve"
            | "approve-step"
            | "reject"
            | "return"
            | "withdraw"
            | "void"
            | "approval-reassign"
    ) {
        return Ok(());
    }
    let Some(id) = row["submissionId"].as_i64().filter(|id| *id > 0) else {
        return Ok(());
    };
    let mut snapshot = store::get(tx, KIND, id)?;
    snapshot["status"] = json!(match action {
        "withdraw" => "Withdrawn",
        "void" => "Voided",
        _ if action == "approve" => "Approved",
        _ => row["status"].as_str().unwrap_or("Pending"),
    });
    // The submitted content is immutable. Approval outcomes are a separate seal.
    snapshot["approvalPlan"] = row["approvalPlan"].clone();
    if matches!(action, "approve" | "approve-step" | "reject") {
        if snapshot.get("decisions").is_none() {
            snapshot["decisions"] = json!([]);
        }
        snapshot["decisions"].as_array_mut().ok_or_else(||super::super::error::unavailable("审批决定记录损坏。"))?.push(json!({"actorId":actor.id,"actorName":actor.name,"action":action,"note":note,"occurredAt":store::timestamp()}));
    }
    if action == "approve" {
        snapshot["approvedAt"] = json!(store::timestamp());
    }
    store::save_in_scope(
        tx,
        KIND,
        id,
        snapshot,
        Some(format!("{}:{}", row["id"], row["submissionNumber"])),
        actor,
        action,
        Some(row),
    )?;
    Ok(())
}
pub(super) fn checked(tx: &Connection, row: &Value, id: i64) -> Result<Value> {
    let snapshot = store::get(tx, KIND, id)?;
    if snapshot["requestId"] != row["id"] || snapshot["companyScope"] != row["companyScope"] {
        return Err(error(404, "提交版本不存在。"));
    }
    Ok(snapshot)
}
pub(super) fn project(tx: &Connection, mut snapshot: Value, detail: bool) -> Result<Value> {
    let id = records::positive(&snapshot, "id", "提交版本")?;
    let blob = tx.blob_info(id, PDF)?;
    let job = if let Some(id) = snapshot["documentJobId"]
        .as_str()
        .filter(|id| !id.is_empty())
    {
        tx.find_identity("background-jobs", id)?
    } else {
        None
    };
    let state = if snapshot["status"] == "Voided" {
        "Voided"
    } else if snapshot["status"] != "Approved" {
        "NotApproved"
    } else if blob.is_some() {
        "Ready"
    } else if job.as_ref().is_some_and(|job| {
        matches!(
            job["status"].as_str(),
            Some("Running" | "Queued" | "Pending")
        )
    }) {
        "Running"
    } else {
        "Failed"
    };
    snapshot["documentStatus"] = json!(state);
    snapshot["documentDigest"] = json!(blob.map(|blob| blob.digest).unwrap_or_default());
    snapshot["documentError"] = json!(if state == "Failed" {
        job.as_ref()
            .map(|job| text(job, "errorMessage"))
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| {
                if text(&snapshot, "documentError").is_empty() {
                    "审批单尚未生成或上次生成中断，请重试。".into()
                } else {
                    text(&snapshot, "documentError")
                }
            })
    } else {
        String::new()
    });
    if detail {
        if snapshot["approvalPlan"].is_object() {
            snapshot["request"]["approvalPlan"] = snapshot["approvalPlan"].clone();
        }
    } else {
        snapshot.as_object_mut().unwrap().remove("request");
    }
    Ok(contracts::dto(contracts::schema("OaSubmission"), snapshot))
}
pub(super) fn id(parameters: &[(&str, String)]) -> Result<i64> {
    parameters
        .iter()
        .find(|(key, _)| *key == "submissionId")
        .and_then(|(_, value)| value.parse().ok())
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("提交版本编号无效。"))
}
pub(super) fn list(tx: &Connection, row: &Value, query: &[(&str, String)]) -> Result<Value> {
    let (page, size) = paging(query)?;
    let (count, items) = children(tx, row, KIND, (page - 1) * size, size)?;
    let items = items
        .into_iter()
        .map(|item| project(tx, item, false))
        .collect::<Result<Vec<_>>>()?;
    Ok(json!({"items":items,"pageNumber":page,"pageSize":size,"totalCount":count}))
}
