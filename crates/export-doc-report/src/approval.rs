//! Pure projection of frozen OA facts to editable approval-sheet fields.
use crate::{ReportData, Result};
use serde_json::{Value, json};
fn text(value: &Value, key: &str) -> String {
    key.split('.')
        .fold(value, |v, key| &v[key])
        .as_str()
        .unwrap_or("")
        .to_owned()
}
fn time(value: &str) -> String {
    chrono::DateTime::parse_from_rfc3339(value)
        .map(|time| time.format("%Y-%m-%d %H:%M:%S %:z").to_string())
        .unwrap_or_else(|_| value.to_owned())
}
pub fn data(snapshot: &Value, generated_at: &str) -> Result<ReportData> {
    let request = &snapshot["request"];
    let mut summary = vec![];
    let money = text(request, "totalAmount");
    if !money.is_empty() {
        summary.push(format!("{} {}", text(request, "currency"), money));
        if request["currency"] == "CNY" {
            summary.push(crate::chinese_money(
                money
                    .parse()
                    .map_err(|_| crate::error::invalid("批准金额无效。"))?,
            )?);
        }
    }
    for (key, label) in [
        ("leave.category", "请假类型"),
        ("leave.startsOn", "开始日期"),
        ("leave.startPeriod", "开始时段"),
        ("leave.endsOn", "结束日期"),
        ("leave.endPeriod", "结束时段"),
        ("durationDays", "天数"),
        ("overtime.startsAt", "开始时间"),
        ("overtime.endsAt", "结束时间"),
        ("overtime.location", "加班地点"),
        ("durationHours", "小时"),
        ("travel.destination", "出差地点"),
        ("travel.startsOn", "出差开始"),
        ("travel.endsOn", "出差结束"),
        ("personnelChange.effectiveDate", "生效日期"),
        ("personnelChange.registration.hireDate", "入职日期"),
        ("personnelChange.registration.departmentId", "入职部门"),
        ("personnelChange.registration.jobTitle", "入职职位"),
        ("personnelChange.departmentId", "任职部门"),
        ("personnelChange.jobTitle", "职位"),
    ] {
        let value = text(request, key);
        let value = match (key, value.as_str()) {
            ("leave.startPeriod" | "leave.endPeriod", "AM") => "上午".into(),
            ("leave.startPeriod" | "leave.endPeriod", "PM") => "下午".into(),
            ("leave.category", "Annual") => "年假".into(),
            ("leave.category", "Sick") => "病假".into(),
            ("leave.category", "Personal") => "事假".into(),
            ("leave.category", "Other") => "其他".into(),
            ("overtime.startsAt" | "overtime.endsAt", _) => time(&value),
            _ => value,
        };
        if !value.is_empty() {
            summary.push(format!("{label}：{value}"));
        }
    }
    if request["kind"] == "expense" {
        summary.push("详细费用见提交的报销单及凭证附件；金额以本次批准记录为准。".into());
        for line in request["lines"].as_array().into_iter().flatten() {
            summary.push(format!(
                "{} · {} · {} {}",
                text(line, "spentOn"),
                text(line, "description"),
                text(request, "currency"),
                text(line, "amount")
            ));
        }
    }
    if let Some(lines) = request["purchaseLines"].as_array() {
        for line in lines {
            summary.push(format!(
                "{} {} · {} {} × {}",
                text(line, "name"),
                text(line, "specification"),
                text(line, "quantity"),
                text(line, "unit"),
                text(line, "unitPrice")
            ));
        }
    }
    let attachments = snapshot["request"]["attachments"]
        .as_array()
        .map(|items| {
            items
                .iter()
                .enumerate()
                .map(|(index, item)| {
                    format!(
                        "{}. {} · {}",
                        index + 1,
                        text(item, "fileName"),
                        match text(item, "purpose").as_str() {
                            "ReimbursementForm" => "报销单",
                            "Supporting" => "补充资料",
                            _ => "发票/凭证",
                        }
                    )
                })
                .collect::<Vec<_>>()
                .join("\n")
        })
        .unwrap_or_default();
    let mut decisions = vec![];
    let mut previous_stage = None;
    if let Some(steps) = snapshot["approvalPlan"]["steps"].as_array() {
        for (index, step) in steps.iter().enumerate() {
            let stage = step["stage"].as_u64().unwrap_or(index as u64) as usize;
            let label = request["approvalStageLabels"][stage]
                .as_str()
                .map(str::to_owned)
                .unwrap_or_else(|| "审批".into());
            if previous_stage != Some(stage) {
                let rule = if step["decision"] == "Any" {
                    "或签：任一人同意"
                } else {
                    "会签：全部同意"
                };
                decisions.push(format!("节点 {} · {label} · {rule}", stage + 1));
                previous_stage = Some(stage);
            }
            let record = if step["status"] == "Skipped" {
                format!(
                    "候选审批人：{}（账号 #{}）\n或签节点已由其他审批人通过，本人未签署。",
                    text(step, "approverName"),
                    step["approverUserId"]
                )
            } else if step["status"] == "Approved" {
                format!(
                    "审批人：{}（账号 #{}） · 结果：同意\n{}审批意见：{}\n审批时间：{}",
                    text(step, "approverName"),
                    step["approverUserId"],
                    if text(step, "delegationKey").is_empty() {
                        String::new()
                    } else {
                        format!(
                            "代理审批：{}（账号 #{}）\n",
                            text(step, "actedByName"),
                            step["actedByUserId"]
                        )
                    },
                    text(step, "note"),
                    time(&text(step, "actedAt"))
                )
            } else {
                return Err(crate::error::invalid("审批单包含未批准的审批记录。"));
            };
            decisions.push(record);
        }
    }
    if decisions.is_empty() {
        decisions.push("节点 1 · 单步审批".into());
        for decision in snapshot["decisions"]
            .as_array()
            .into_iter()
            .flatten()
            .filter(|decision| decision["action"] == "approve")
        {
            decisions.push(format!(
                "{}（账号 #{}） · {}\n{}",
                text(decision, "actorName"),
                decision["actorId"],
                text(decision, "occurredAt"),
                text(decision, "note")
            ));
        }
        if decisions.len() == 1 {
            return Err(crate::error::invalid("审批单缺少实际批准记录。"));
        }
    }
    Ok(ReportData {
        report_type: "OaApproval",
        root: json!({"Oa":{
            "CompanyName":text(snapshot,"companyName"),"FormName":text(snapshot,"formName"),
            "ApplicantName":text(request,"employeeName"),"DepartmentName":text(snapshot,"departmentName"),"EmployeeNumber":text(request,"employeeNumber"),"JobTitle":text(snapshot,"applicantJobTitle"),"SubmittedAt":time(&text(snapshot,"submittedAt")),
            "ApprovalResult":std::iter::once(if request["kind"] == "expense" {
                "本次提交已批准。财务请在费用报销的财务接收中，按申请编号及提交版本核对；纸单不代表当前仍有效或已付款。"
            } else { "本次提交版本已审批通过，请按公司制度办理后续事项。" }.to_owned())
                .chain(request["approvalRoutingNotes"].as_array().into_iter().flatten().filter_map(|v| v.as_str().map(str::to_owned)))
                .collect::<Vec<_>>().join("\n"),
            "Reference":format!("申请 #{} · 提交版本 {} · 已批准",snapshot["requestId"],snapshot["submissionNumber"]),
            "Applicant":format!("{} · 工号 {} · {}\n提交时间：{}",text(request,"employeeName"),text(request,"employeeNumber"),text(snapshot,"departmentName"),text(snapshot,"submittedAt")),
            "Title":text(request,"title"),"Summary":summary.join("\n"),"Reason":text(request,"reason"),
            "Attachments":if attachments.is_empty(){"无附件".into()}else{attachments},"Approvals":decisions.join("\n\n"),"GeneratedAt":format!("生成时间：{}",time(generated_at))
        }}),
        images: Default::default(),
    })
}
