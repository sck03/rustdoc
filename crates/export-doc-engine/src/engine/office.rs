use super::{
    error::{Result, conflict, invalid},
    records::{parent, positive, required, text},
    store::{self, Actor},
};
use crate::contracts;
use crate::engine::store::Connection;
use chrono::{DateTime, FixedOffset};
use export_doc_domain::business_date::is_valid as valid_date;
use serde_json::{Value, json};

pub(super) fn integer(body: &Value, key: &str, min: i64, max: i64, label: &str) -> Result<i64> {
    body[key]
        .as_i64()
        .filter(|value| (*value >= min) && (*value <= max))
        .ok_or_else(|| invalid(format!("{label}须在 {min}–{max} 之间。")))
}
pub(super) fn instant(body: &Value, key: &str) -> Result<DateTime<FixedOffset>> {
    DateTime::parse_from_rfc3339(&text(body, key))
        .map_err(|_| invalid("预约时间须包含日期、时分和时区。"))
}
fn person(connection: &Connection, actor: &Actor, value: &Value) -> Result<Value> {
    super::oa::request_employee(connection, actor, value, false)
}
pub fn validate(
    connection: &Connection,
    actor: &Actor,
    kind: &str,
    id: i64,
    previous: &Value,
    value: &mut Value,
    business_date: chrono::NaiveDate,
) -> Result<()> {
    let submit = value["submitImmediately"].as_bool().unwrap_or(true);
    match kind {
        "people" => {
            required(value, "profile.fullName", "姓名", 100)?;
            if id == 0 {
                required(value, "employeeNumber", "工号", 50)?;
                required(value, "jobTitle", "职位", 120)?;
                if !valid_date(&text(value, "hireDate"))
                    || text(value, "hireDate") > business_date.to_string()
                {
                    return Err(invalid("入职日期无效。"));
                }
                value["status"] = json!(if value["onProbation"] == true {
                    "Probation"
                } else {
                    "Active"
                });
                value["lastEffectiveDate"] = value["hireDate"].clone();
            } else {
                if previous["status"] == "Departed" && !actor.admin {
                    return Err(conflict("离职档案不能直接修改，请办理重新入职。"));
                }
                value["status"] = previous["status"].clone();
                value["organization"] = previous.get("organization").cloned().unwrap_or(json!({}));
                for field in ["employeeNumber", "departmentId", "jobTitle", "hireDate"] {
                    value[field] = previous[field].clone();
                }
                if let Some(registration) = value
                    .get("registration")
                    .filter(|registration| registration.is_object())
                    .cloned()
                {
                    let unused = can_correct(connection, id)?;
                    if !unused {
                        return Err(conflict(
                            "已有账号、业务或正式任职记录，请通过人员变动申请办理。",
                        ));
                    }
                    required(value, "correctionNote", "登记纠错原因", 500)?;
                    let organization = registration.get("organization").filter(|v| !v.is_null());
                    for field in ["employeeNumber", "departmentId", "jobTitle", "hireDate"] {
                        value[field] = registration[field].clone();
                    }
                    if let Some(organization) = organization {
                        super::personnel_organization::validate(
                            connection,
                            actor,
                            value,
                            organization,
                            "edit",
                        )?;
                    }
                    let changing_job = value["departmentId"] != previous["departmentId"]
                        || value["jobTitle"] != previous["jobTitle"]
                        || super::personnel_organization::organization(value)?
                            != super::personnel_organization::organization(previous)?;
                    if changing_job
                        && super::office_queries::clearance(connection, previous)?["isClear"]
                            != true
                    {
                        return Err(conflict(
                            "仍有未结申请或办理职责，请完成交接后再纠正任职信息。",
                        ));
                    }
                    required(value, "employeeNumber", "工号", 50)?;
                    required(value, "jobTitle", "职位", 120)?;
                    let hire = text(value, "hireDate");
                    if !valid_date(&hire)
                        || hire > business_date.to_string()
                        || (!unused && hire > text(previous, "lastEffectiveDate"))
                        || ["confirmedOn", "departedOn"].iter().any(|key| {
                            previous[*key]
                                .as_str()
                                .is_some_and(|date| hire.as_str() > date)
                        })
                    {
                        return Err(invalid(
                            "入职日期须有效且不能晚于已有任职变动或公司业务日期。",
                        ));
                    }
                    if unused {
                        value["lastEffectiveDate"] = json!(hire);
                    }
                    if previous["status"] != "Departed" {
                        value["status"] = json!(if registration["onProbation"] == true {
                            "Probation"
                        } else {
                            "Active"
                        });
                        if value["status"] != previous["status"] {
                            value["confirmedOn"] = Value::Null;
                        }
                    }
                }
            }
            validate_department(
                connection,
                &text(value, "companyScope"),
                &text(value, "departmentId"),
            )?;
            if id == 0 {
                let organization = value
                    .get("organization")
                    .filter(|v| !v.is_null())
                    .cloned()
                    .unwrap_or(json!({}));
                super::personnel_organization::validate(
                    connection,
                    actor,
                    value,
                    &organization,
                    "create",
                )?;
            } else {
                super::personnel_organization::organization(value)?
                    .validate(
                        id,
                        &text(value, "departmentId"),
                        &text(value, "companyScope"),
                    )
                    .map_err(invalid)?;
            }
            if !["FullTime", "PartTime", "Intern", "Contractor"]
                .contains(&text(value, "employmentType").as_str())
            {
                return Err(invalid("用工类型无效。"));
            }
            for field in ["probationEndsOn", "contractEndsOn"] {
                let date = text(value, field);
                if !date.is_empty() && (!valid_date(&date) || date < text(value, "hireDate")) {
                    return Err(invalid("试用和合同截止日期不能早于入职日期。"));
                }
            }
            validate_identity(&value["profile"])?;
            value["fullName"] = value["profile"]["fullName"].clone();
            let mut employee = contracts::initial(contracts::schema("PersonnelDirectoryRecord"));
            for key in [
                "id",
                "employeeNumber",
                "departmentId",
                "jobTitle",
                "status",
                "fullName",
            ] {
                employee[key] = value[key].clone();
            }
            for key in ["workEmail", "workPhone", "workLocation"] {
                employee[key] = value["profile"][key].clone();
            }
            employee["canViewDetails"] = json!(true);
            employee["avatarHash"] = previous["employee"]["avatarHash"].clone();
            value["employee"] = employee;
        }
        "rooms" => {
            required(value, "name", "会议室名称", 120)?;
            integer(value, "capacity", 1, 10_000, "容量")?;
            integer(value, "maximumBookingHours", 1, 24, "最长预约小时")?;
            integer(value, "advanceBookingDays", 1, 365, "提前预约天数")?;
            super::handling::validate_resource(connection, actor, kind, id, previous, value)?;
        }
        "bookings" => {
            required(value, "title", "会议主题", 200)?;
            if id > 0 && value["meetingRoomId"] != previous["meetingRoomId"] {
                return Err(invalid("已登记预约不能更换会议室，请取消后重新登记。"));
            }
            if id > 0 {
                return Err(conflict("已提交的申请内容不可修改，请取消后重新申请。"));
            }
            let room = parent(
                connection,
                actor,
                "rooms",
                positive(value, "meetingRoomId", "会议室")?,
            )?;
            if room["companyScope"] != actor.company {
                return Err(super::error::error(403, "不能预约其他公司的会议室。"));
            }
            if room["isActive"] != true {
                return Err(invalid("会议室已停用。"));
            }
            integer(
                value,
                "attendeeCount",
                1,
                room["capacity"].as_i64().unwrap_or(0),
                "参会人数",
            )?;
            let start = instant(value, "startsAt")?;
            let end = instant(value, "endsAt")?;
            if end - start < chrono::Duration::minutes(15)
                || end - start
                    > chrono::Duration::hours(room["maximumBookingHours"].as_i64().unwrap_or(8))
            {
                return Err(invalid(
                    "预约时长须至少 15 分钟，且不超过会议室的最长预约时长。",
                ));
            }
            let now = chrono::Utc::now();
            if start < now
                || start
                    > now
                        + chrono::Duration::days(room["advanceBookingDays"].as_i64().unwrap_or(90))
            {
                return Err(invalid(
                    "请选择当前时间之后、会议室允许提前预约天数以内的时段。",
                ));
            }
            for booking in store::matching(
                connection,
                "bookings",
                &[("meetingRoomId", value["meetingRoomId"].to_string())],
                &[],
            )? {
                if booking["id"] != id
                    && booking["meetingRoomId"] == value["meetingRoomId"]
                    && ["Approved", "Pending", "InUse"].contains(&text(&booking, "status").as_str())
                    && start < instant(&booking, "endsAt")?
                    && end > instant(&booking, "startsAt")?
                {
                    return Err(conflict("该会议室在所选时间已被预约。"));
                }
            }
            let employee = person(connection, actor, value)?;
            value["employeeId"] = employee["id"].clone();
            value["departmentId"] = employee["departmentId"].clone();
            value["applicantUserId"] = value["ownerUserId"].clone();
            value["applicantName"] = employee["profile"]["fullName"].clone();
            value["employeeNumber"] = employee["employeeNumber"].clone();
            value["roomName"] = room["name"].clone();
            value["location"] = room["location"].clone();
            value["requiresKey"] = room["requiresKey"].clone();
            super::handling::snapshot(
                connection,
                actor,
                value,
                if id > 0 { previous } else { &room },
                "Room",
                id == 0,
            )?;
            value["status"] = json!(if submit { "Pending" } else { "Draft" });
            value["kind"] = json!("room");
            value["employeeName"] = value["applicantName"].clone();
            value["reason"] = value["title"].clone();
            value["_requesterContext"] = json!(actor.context);
            value["submissionNumber"] = json!(if submit { 1 } else { 0 });
            if submit {
                super::oa::approval::submit(connection, actor, value)?;
            } else {
                value.as_object_mut().unwrap().remove("approvalPlan");
            }
        }
        "supplies" => {
            super::handling::validate_resource(connection, actor, kind, id, previous, value)?;
            required(value, "name", "物品名称", 120)?;
            required(value, "unit", "计量单位", 20)?;
            integer(value, "minimumStock", 0, 1_000_000, "最低库存")?;
            if id > 0
                && (value["unit"] != previous["unit"]
                    || value["isReturnable"] != previous["isReturnable"])
                && store::all(connection, "supply-requests")?
                    .iter()
                    .any(|request| request["officeSupplyId"] == id)
            {
                return Err(conflict(
                    "已有领用记录，不能更改计量单位或归还类型，请另建物品。",
                ));
            }
            for key in ["stockQuantity", "reservedQuantity"] {
                value[key] = if id == 0 {
                    json!(0)
                } else {
                    previous[key].clone()
                };
            }
            supply_totals(value);
        }
        "supply-requests" => {
            required(value, "purpose", "领用用途", 500)?;
            if id > 0 {
                return Err(conflict("已提交的申请内容不可修改，请取消后重新申请。"));
            }
            let supply_id = positive(value, "officeSupplyId", "物品")?;
            let supply = parent(connection, actor, "supplies", supply_id)?;
            if supply["companyScope"] != actor.company {
                return Err(super::error::error(403, "不能领用其他公司的物品。"));
            }
            if supply["isActive"] != true {
                return Err(invalid("物品已停用。"));
            }

            integer(value, "quantity", 1, 1_000_000, "领用数量")?;
            let due = value["returnDueDate"]
                .as_str()
                .filter(|date| valid_date(date))
                .and_then(|date| chrono::NaiveDate::parse_from_str(date, "%Y-%m-%d").ok());
            if supply["isReturnable"] == true {
                if due.is_none_or(|date| {
                    date < business_date || date > business_date + chrono::Duration::days(365)
                }) {
                    return Err(invalid("借用物品须填写今天至未来一年内的预计归还日期。"));
                }
            } else if !value["returnDueDate"].is_null() {
                return Err(invalid("消耗品不需要归还日期。"));
            }
            let employee = person(connection, actor, value)?;
            value["employeeId"] = employee["id"].clone();
            value["departmentId"] = employee["departmentId"].clone();
            value["applicantUserId"] = value["ownerUserId"].clone();
            value["applicantName"] = employee["profile"]["fullName"].clone();
            value["employeeNumber"] = employee["employeeNumber"].clone();
            value["supplyName"] = supply["name"].clone();
            super::handling::snapshot(
                connection,
                actor,
                value,
                if id > 0 { previous } else { &supply },
                "Supply",
                id == 0,
            )?;
            value["unit"] = supply["unit"].clone();
            value["isReturnable"] = supply["isReturnable"].clone();
            value["status"] = json!(if submit { "Pending" } else { "Draft" });
            value["kind"] = json!("supply");
            value["employeeName"] = value["applicantName"].clone();
            value["title"] = json!(format!("{}领用", text(value, "supplyName")));
            value["reason"] = value["purpose"].clone();
            value["_requesterContext"] = json!(actor.context);
            value["submissionNumber"] = json!(if submit { 1 } else { 0 });
            if submit {
                super::oa::approval::submit(connection, actor, value)?;
            } else {
                value.as_object_mut().unwrap().remove("approvalPlan");
            }
            value["returnedQuantity"] = json!(0);
        }
        _ => {}
    }
    if matches!(kind, "bookings" | "supply-requests") {
        value.as_object_mut().unwrap().remove("submitImmediately");
    }
    Ok(())
}
pub(super) fn supply_totals(supply: &mut Value) {
    let available = supply["stockQuantity"].as_i64().unwrap_or(0)
        - supply["reservedQuantity"].as_i64().unwrap_or(0);
    supply["availableQuantity"] = json!(available);
    supply["lowStock"] = json!(available < supply["minimumStock"].as_i64().unwrap_or(0));
}
fn validate_department(connection: &Connection, company: &str, code: &str) -> Result<()> {
    if store::all(connection, "departments")?
        .iter()
        .any(|department| {
            department["code"] == code
                && department["isActive"] == true
                && department["companyCode"] == company
        })
    {
        Ok(())
    } else {
        Err(invalid("请选择本公司启用的部门。"))
    }
}
pub fn can_correct(connection: &Connection, id: i64) -> Result<bool> {
    Ok(deletion_restriction(connection, id)?.is_none())
}

pub fn deletion_restriction(connection: &Connection, id: i64) -> Result<Option<&'static str>> {
    let person = store::get(connection, "people", id)?;
    if person["account"].is_object() {
        return Ok(Some(
            "此档案已关联登录账号，不能作为误录档案删除。请在档案信息中核对关联账号；停止任职请办理离职。",
        ));
    }
    if super::oa::references(
        connection,
        &text(&person, "companyScope"),
        Some(id),
        None,
        false,
    )? > 0
    {
        return Ok(Some(
            "此人员已有行政或人员变动申请记录，需要保留历史。请通过调岗、离职流程处理。",
        ));
    }
    if connection.has_history_except(
        "people",
        id,
        &[
            "create",
            "edit",
            "image-upload",
            "image-delete",
            "attachment-upload",
            "attachment-delete",
        ],
    )? {
        return Ok(Some(
            "此档案已有正式任职变动或账号关联历史，需要保留追溯。可在“任职与操作记录”中查看；请通过调岗、离职流程处理。",
        ));
    }
    for kind in ["bookings", "supply-requests"] {
        if has_reference(connection, kind, "employeeId", id)? {
            return Ok(Some(
                "此人员已有会议室预约或物品领用记录，需要保留业务历史。请通过离职流程处理。",
            ));
        }
    }
    if super::organization::leadership_referenced(connection, &text(&person, "companyScope"), id)? {
        return Ok(Some(
            "此人员仍承担公司或部门主负责人、分管负责人职责，请先在组织架构中交接，再删除误录档案。",
        ));
    }
    let person = store::get(connection, "people", id)?;
    if super::personnel_organization::references(
        connection,
        &text(&person, "companyScope"),
        id,
        false,
    )? > 0
    {
        return Ok(Some(
            "此人员仍有公司领导审批负责人或任职汇报关系引用，请先办理负责人交接；历史引用须保留。",
        ));
    }
    Ok(None)
}

fn has_reference(connection: &Connection, kind: &str, field: &str, id: i64) -> Result<bool> {
    Ok(connection
        .query_generic(&export_doc_storage::GenericQuery {
            kind,
            scope: export_doc_storage::DataScope {
                all: true,
                ..Default::default()
            },
            filters: &[(field, id.to_string())],
            keyword: "",
            search_root: None,
            fields: &[("id", "id")],
            offset: 0,
            limit: 1,
        })?
        .0
        > 0)
}

fn validate_identity(profile: &Value) -> Result<()> {
    let number = text(profile, "identityNumber");
    if !number.is_empty() {
        let bytes = number.as_bytes();
        let weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
        let check = b"10X98765432";
        if bytes.len() != 18 || !bytes[..17].iter().all(u8::is_ascii_digit) {
            return Err(invalid("身份证号须为有效的 18 位号码。"));
        }
        let sum: usize = bytes[..17]
            .iter()
            .zip(weights)
            .map(|(byte, weight)| (byte - b'0') as usize * weight)
            .sum();
        if bytes[17].to_ascii_uppercase() != check[sum % 11] {
            return Err(invalid("身份证号码校验位无效。"));
        }
        let birth = format!("{}-{}-{}", &number[6..10], &number[10..12], &number[12..14]);
        if !valid_date(&birth) {
            return Err(invalid("身份证出生日期无效。"));
        }
    }
    let from = text(profile, "identityValidFrom");
    let until = text(profile, "identityValidUntil");
    if (!from.is_empty() && !valid_date(&from))
        || (!until.is_empty() && !valid_date(&until))
        || (!from.is_empty() && !until.is_empty() && until < from)
    {
        return Err(invalid("身份证有效期无效。"));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn personnel_identity_checksum_and_validity_are_checked() {
        assert!(validate_identity(&json!({"identityNumber":"11010519491231002X"})).is_ok());
        assert!(validate_identity(&json!({"identityNumber":"110105194912310021"})).is_err());
        assert!(
            validate_identity(
                &json!({"identityValidFrom":"2026-10-01","identityValidUntil":"2026-09-01"})
            )
            .is_err()
        );
    }
}
