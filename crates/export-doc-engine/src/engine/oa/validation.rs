use super::*;
use export_doc_domain::oa as rules;
use rust_decimal::Decimal;

pub(super) fn employee(
    tx: &Connection,
    actor: &Actor,
    body: &Value,
    existing: bool,
) -> Result<Value> {
    let person = if tx.provider() == "SQLite" || existing {
        store::get(tx, "people", records::positive(body, "employeeId", "人员")?)?
    } else {
        if body["employeeId"].as_i64().is_some_and(|id| id > 0) {
            return Err(invalid("团队申请由本人发起，不能代选其他员工。"));
        }
        store::all(tx, "people")?
            .into_iter()
            .find(|p| p["account"]["id"] == actor.id && p["companyScope"] == actor.company)
            .ok_or_else(|| invalid("请先由人事将账号关联到本公司在职人员。"))?
    };
    if person["companyScope"] != actor.company {
        return Err(error(403, "只能为本公司员工办理申请。"));
    }
    if !["Active", "Probation"].contains(&text(&person, "status").as_str()) {
        return Err(conflict("员工已离职或状态不可用。"));
    }
    if tx.provider() != "SQLite" && existing && person["account"]["id"] != body["ownerUserId"] {
        return Err(conflict("申请人与人员账号关联已变化。"));
    }
    if existing && person["departmentId"] != body["departmentId"] {
        return Err(conflict("员工部门已变化，请取消旧申请后重新申请。"));
    }
    Ok(person)
}

pub(super) fn fields(meta: &Value, body: &Value, today: chrono::NaiveDate) -> Result<Value> {
    required(body, "title", "申请标题", 150)?;
    required(body, "reason", "申请说明", 2000)?;
    let mut row = json!({"title":text(body,"title"),"reason":text(body,"reason"),"totalAmount":"","durationDays":"","durationHours":""});
    match text(meta, "kind").as_str() {
        "leave" => {
            let (start, end) = rules::leave_span(&body["leave"]).map_err(invalid)?;
            row["leave"] = body["leave"].clone();
            row["durationDays"] =
                json!((Decimal::from(end - start) / Decimal::from(2)).to_string());
        }
        "expense" => {
            let currency = text(body, "currency");
            row["totalAmount"] = json!(
                rules::expense_total(&body["lines"], &currency, today)
                    .map_err(invalid)?
                    .to_string()
            );
            row["lines"] = body["lines"].clone();
            row["currency"] = json!(currency);
        }
        "purchase" => {
            let currency = text(body, "currency");
            row["totalAmount"] = json!(
                rules::purchase_total(&body["purchaseLines"], &currency)
                    .map_err(invalid)?
                    .to_string()
            );
            row["purchaseLines"] = body["purchaseLines"].clone();
            row["currency"] = json!(currency);
        }
        "travel" => {
            required(body, "travel.destination", "出差地点", 200)?;
            let start = rules::date(&text(body, "travel.startsOn")).map_err(invalid)?;
            let end = rules::date(&text(body, "travel.endsOn")).map_err(invalid)?;
            let days = (end - start).num_days() + 1;
            if !(1..=366).contains(&days) {
                return Err(invalid("出差日期须为 1–366 个自然日。"));
            }
            row["travel"] = body["travel"].clone();
            row["durationDays"] = json!(days.to_string());
        }
        "overtime" => {
            required(body, "overtime.location", "加班地点", 200)?;
            let (start, end) = rules::overtime_span(&body["overtime"]).map_err(invalid)?;
            row["overtime"] = body["overtime"].clone();
            row["durationHours"] = json!(
                (Decimal::from((end - start).num_minutes()) / Decimal::from(60))
                    .round_dp(2)
                    .to_string()
            );
        }
        "general" => {
            let category = text(body, "category");
            if !["Seal", "Certificate", "IT", "Repair", "Other"].contains(&category.as_str()) {
                return Err(invalid("请选择通用申请类别。"));
            }
            row["category"] = json!(category);
        }
        _ => return Err(invalid("未知申请类型。")),
    }
    Ok(row)
}

pub(super) fn submission(tx: &Connection, row: &Value) -> Result<()> {
    if row["kind"] == "expense" && children(tx, row, "oa-attachment", 0, 1)?.0 == 0 {
        return Err(invalid("请先上传至少一份 PDF 或图片报销凭证。"));
    }
    if !["leave", "travel", "overtime"].contains(&text(row, "kind").as_str()) {
        return Ok(());
    }
    let kinds = if row["kind"] == "overtime" {
        vec!["oa-overtime"]
    } else {
        vec!["oa-leave", "oa-travel"]
    };
    for kind in kinds {
        for status in ["Pending", "Approved"] {
            let mut offset = 0;
            loop {
                crate::operation::check()?;
                let (count, rows) = tx.query_records(&RecordQuery {
                    kind,
                    company: row["companyScope"].as_str().unwrap_or(""),
                    employee: row["employeeId"].as_i64(),
                    status: Some(status),
                    offset,
                    limit: 100,
                    ..Default::default()
                })?;
                for other in rows {
                    if other["id"] == row["id"] {
                        continue;
                    }
                    let overlap = if kind == "oa-overtime" {
                        let (a, b) = rules::overtime_span(&row["overtime"]).map_err(invalid)?;
                        let (c, d) = rules::overtime_span(&other["overtime"]).map_err(invalid)?;
                        a < d && c < b
                    } else {
                        let span = |value: &Value| {
                            let leave = if value["kind"] == "leave" {
                                value["leave"].clone()
                            } else {
                                json!({"category":"Other","startsOn":value["travel"]["startsOn"],"endsOn":value["travel"]["endsOn"],"startPeriod":"AM","endPeriod":"PM"})
                            };
                            rules::leave_span(&leave).map_err(invalid)
                        };
                        let (a, b) = span(row)?;
                        let (c, d) = span(&other)?;
                        a < d && c < b
                    };
                    if overlap {
                        return Err(conflict(
                            "该员工已有重叠的待审批或已批准申请，请先核对原申请。",
                        ));
                    }
                }
                offset += 100;
                if offset >= count {
                    break;
                }
            }
        }
    }
    Ok(())
}

pub(super) fn completion(row: &Value, today: chrono::NaiveDate) -> Result<()> {
    let end = match text(row, "kind").as_str() {
        "leave" => Some(text(row, "leave.endsOn")),
        "travel" => Some(text(row, "travel.endsOn")),
        _ => None,
    };
    if end.is_some_and(|end| end > today.to_string()) {
        return Err(conflict("申请时段尚未结束，不能登记完成。"));
    }
    if row["kind"] == "overtime"
        && rules::overtime_span(&row["overtime"]).map_err(invalid)?.1 > chrono::Utc::now()
    {
        return Err(conflict("加班时段尚未结束。"));
    }
    Ok(())
}
