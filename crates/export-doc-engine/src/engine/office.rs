use super::{
    auth,
    error::{Result, conflict, invalid},
    records::{parent, positive, required, text},
    store::{self, Actor, Store},
};
use crate::{contracts, invoice::valid_date};
use chrono::{DateTime, FixedOffset};
use export_doc_storage::Connection;
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
    let employee = if connection.provider() == "SQLite" {
        parent(
            connection,
            actor,
            "people",
            positive(value, "employeeId", "在职人员")?,
        )?
    } else {
        let owner = value["ownerUserId"]
            .as_i64()
            .filter(|id| *id > 0)
            .unwrap_or(actor.id);
        store::all(connection, "people")?
            .into_iter()
            .find(|person| {
                person["account"]["id"] == owner && person["companyScope"] == actor.company
            })
            .ok_or_else(|| invalid("团队申请需要关联本公司的在职人员档案。"))?
    };
    if employee["companyScope"] != actor.company {
        return Err(super::error::error(403, "不能为其他公司的人员办理登记。"));
    }
    if employee["status"] == "Departed" {
        return Err(invalid("不能为离职人员办理登记。"));
    }
    Ok(employee)
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
    match kind {
        "people" => {
            required(value, "profile.fullName", "姓名", 100)?;
            if id == 0 {
                required(value, "employeeNumber", "工号", 50)?;
                if !valid_date(&text(value, "hireDate")) {
                    return Err(invalid("入职日期无效。"));
                }
                value["status"] = json!(if value["onProbation"] == true {
                    "Probation"
                } else {
                    "Active"
                });
                value["lastEffectiveDate"] = value["hireDate"].clone();
            } else {
                if previous["status"] == "Departed" {
                    return Err(conflict("离职档案不能直接修改，请办理重新入职。"));
                }
                value["status"] = previous["status"].clone();
                for field in ["employeeNumber", "departmentId", "jobTitle", "hireDate"] {
                    value[field] = previous[field].clone();
                }
                if let Some(registration) = value
                    .get("registration")
                    .filter(|registration| registration.is_object())
                    .cloned()
                {
                    if !can_correct(connection, id)? {
                        return Err(conflict("已有任职变动或业务引用，不能修正初始登记。"));
                    }
                    for field in [
                        "employeeNumber",
                        "departmentId",
                        "jobTitle",
                        "hireDate",
                        "onProbation",
                    ] {
                        if let Some(corrected) = registration.get(field) {
                            value[field] = corrected.clone();
                        }
                    }
                    required(value, "employeeNumber", "工号", 50)?;
                    if !valid_date(&text(value, "hireDate")) {
                        return Err(invalid("入职日期无效。"));
                    }
                    value["status"] = json!(if value["onProbation"] == true {
                        "Probation"
                    } else {
                        "Active"
                    });
                    value["lastEffectiveDate"] = value["hireDate"].clone();
                }
            }
            validate_department(connection, actor, &text(value, "departmentId"))?;
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
            value["employee"] = employee;
            value["canEdit"] = json!(true);
            value["canTransition"] = json!(true);
            value["canLinkAccount"] = json!(true);
            let correct = id == 0 || can_correct(connection, id)?;
            value["canDelete"] = json!(correct);
            value["canCorrectRegistration"] = json!(correct);
        }
        "rooms" => {
            required(value, "name", "会议室名称", 120)?;
            integer(value, "capacity", 1, 10_000, "容量")?;
            integer(value, "maximumBookingHours", 1, 24, "最长预约小时")?;
            integer(value, "advanceBookingDays", 1, 365, "提前预约天数")?;
            if id > 0
                && value["isActive"] == false
                && store::all(connection, "bookings")?.iter().any(|booking| {
                    booking["meetingRoomId"] == id
                        && ["Approved", "InUse", "Pending"]
                            .contains(&text(booking, "status").as_str())
                })
            {
                return Err(conflict("会议室还有未结束预约，不能停用。"));
            }
        }
        "bookings" => {
            required(value, "title", "会议主题", 200)?;
            if id > 0 && value["meetingRoomId"] != previous["meetingRoomId"] {
                return Err(invalid("已登记预约不能更换会议室，请取消后重新登记。"));
            }
            if id > 0 && !["Approved", "Pending"].contains(&text(previous, "status").as_str()) {
                return Err(conflict("已交接或结束的预约不能修改。"));
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
            for booking in store::all(connection, "bookings")? {
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
            value["roomName"] = room["name"].clone();
            value["location"] = room["location"].clone();
            value["requiresKey"] = room["requiresKey"].clone();
            value["status"] = json!(if connection.provider() == "SQLite" {
                "Approved"
            } else {
                "Pending"
            });
        }
        "supplies" => {
            required(value, "name", "物品名称", 120)?;
            required(value, "unit", "计量单位", 20)?;
            integer(value, "minimumStock", 0, 1_000_000, "最低库存")?;
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
            if id > 0 && !["Approved", "Pending"].contains(&text(previous, "status").as_str()) {
                return Err(conflict("已发放或结束的领用申请不能修改。"));
            }
            let supply_id = positive(value, "officeSupplyId", "物品")?;
            let mut supply = parent(connection, actor, "supplies", supply_id)?;
            if supply["companyScope"] != actor.company {
                return Err(super::error::error(403, "不能领用其他公司的物品。"));
            }
            if supply["isActive"] != true {
                return Err(invalid("物品已停用。"));
            }
            if id > 0 && previous["officeSupplyId"] != supply_id {
                return Err(invalid("已登记申请不能更换物品，请取消后重新登记。"));
            }
            let quantity = integer(value, "quantity", 1, 1_000_000, "领用数量")?;
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
            let previous_quantity = if id > 0 && previous["status"] == "Approved" {
                previous["quantity"].as_i64().unwrap_or(0)
            } else {
                0
            };
            let registered = connection.provider() == "SQLite";
            let delta = if registered { quantity } else { 0 } - previous_quantity;
            let stock = supply["stockQuantity"].as_i64().unwrap_or(0);
            let reserved = supply["reservedQuantity"].as_i64().unwrap_or(0);
            if stock - reserved < delta {
                return Err(conflict("可用库存不足。"));
            }
            let employee = person(connection, actor, value)?;
            value["employeeId"] = employee["id"].clone();
            value["departmentId"] = employee["departmentId"].clone();
            value["applicantUserId"] = value["ownerUserId"].clone();
            value["applicantName"] = employee["profile"]["fullName"].clone();
            value["supplyName"] = supply["name"].clone();
            value["unit"] = supply["unit"].clone();
            value["isReturnable"] = supply["isReturnable"].clone();
            value["status"] = json!(if registered { "Approved" } else { "Pending" });
            value["returnedQuantity"] = json!(0);
            supply["reservedQuantity"] = json!(reserved + delta);
            supply_totals(&mut supply);
            store::save(
                connection,
                "supplies",
                supply_id,
                supply.clone(),
                Some(text(&supply, "name")),
                actor,
                "reserve",
            )?;
        }
        _ => {}
    }
    Ok(())
}
pub(super) fn supply_totals(supply: &mut Value) {
    let available = supply["stockQuantity"].as_i64().unwrap_or(0)
        - supply["reservedQuantity"].as_i64().unwrap_or(0);
    supply["availableQuantity"] = json!(available);
    supply["lowStock"] = json!(available < supply["minimumStock"].as_i64().unwrap_or(0));
}
fn validate_department(connection: &Connection, actor: &Actor, code: &str) -> Result<()> {
    if store::all(connection, "departments")?
        .iter()
        .any(|department| {
            department["code"] == code
                && department["isActive"] == true
                && (actor.admin || department["companyCode"] == actor.company)
        })
    {
        Ok(())
    } else {
        Err(invalid("请选择本公司启用的部门。"))
    }
}
pub fn can_correct(connection: &Connection, id: i64) -> Result<bool> {
    let person = store::get(connection, "people", id)?;
    if super::oa::references(
        connection,
        &text(&person, "companyScope"),
        Some(id),
        None,
        false,
    )? > 0
    {
        return Ok(false);
    }
    if person["account"].is_object() {
        return Ok(false);
    }
    if store::history(connection, "people", id)?
        .iter()
        .any(|event| {
            !["create", "edit", "image-upload", "image-delete"]
                .contains(&text(event, "action").as_str())
        })
    {
        return Ok(false);
    }
    for kind in ["bookings", "supply-requests"] {
        if store::all(connection, kind)?
            .iter()
            .any(|item| item["employeeId"] == id)
        {
            return Ok(false);
        }
    }
    Ok(!store::all(connection, "departments")?
        .iter()
        .any(|department| department["managerEmployeeId"] == id))
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

pub fn action(
    store: &Store,
    actor: &Actor,
    operation: &str,
    id: i64,
    body: Value,
    business_date: chrono::NaiveDate,
) -> Result<Value> {
    if ![
        "ConfirmPersonnel",
        "TransferPersonnel",
        "DepartPersonnel",
        "RehirePersonnel",
    ]
    .contains(&operation)
    {
        return super::office_workflows::action(store, actor, operation, id, body, business_date);
    }
    auth::authorize(actor, "office.people", "transition")?;
    store.transaction(|tx| {
        let mut value = store::get(tx, "people", id)?;
        if !auth::visible(actor, "office.people", "transition", &value) {
            return Err(super::error::error(403, "没有办理此人员档案的权限。"));
        }
        store::check_version(&value, store::expected(&body))?;
        let state = text(&value, "status");
        let effective = text(&body, "effectiveDate");
        if !valid_date(&effective) || effective < text(&value, "lastEffectiveDate") {
            return Err(invalid("任职生效日期不能早于上一项变动。"));
        }
        required(&body, "note", "任职变动说明", 500)?;
        if matches!(operation, "TransferPersonnel" | "DepartPersonnel") {
            let clearance = super::office_queries::clearance(tx, &value)?;
            if clearance["isClear"] != true {
                return Err(conflict("仍有未结清的行政或人事申请，请先完成交接。"));
            }
            if operation == "DepartPersonnel" && clearance["canDepart"] != true {
                return Err(conflict("请先移交部门负责人职责。"));
            }
        }
        match operation {
            "ConfirmPersonnel" if state == "Probation" => {
                value["status"] = json!("Active");
                value["confirmedOn"] = json!(effective);
            }
            "TransferPersonnel" if state != "Departed" => {
                validate_department(tx, actor, &text(&body, "departmentId"))?;
                value["departmentId"] = body["departmentId"].clone();
                value["jobTitle"] = body["jobTitle"].clone();
            }
            "DepartPersonnel" if state != "Departed" => {
                value["status"] = json!("Departed");
                value["departedOn"] = json!(effective);
            }
            "RehirePersonnel" if state == "Departed" => {
                validate_department(tx, actor, &text(&body, "departmentId"))?;
                value["departmentId"] = body["departmentId"].clone();
                value["jobTitle"] = body["jobTitle"].clone();
                value["status"] = json!(if body["onProbation"] == true {
                    "Probation"
                } else {
                    "Active"
                });
                value["hireDate"] = json!(effective);
                value["confirmedOn"] = Value::Null;
                value["departedOn"] = Value::Null;
            }
            _ => return Err(conflict("当前任职状态不能办理此项变动。")),
        }
        value["lastEffectiveDate"] = json!(effective);
        value["employee"]["status"] = value["status"].clone();
        value["employee"]["departmentId"] = value["departmentId"].clone();
        value["employee"]["jobTitle"] = value["jobTitle"].clone();
        super::personnel::save(tx, actor, id, value, operation, &text(&body, "note"))
    })
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
