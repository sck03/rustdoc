//! Office page presentation and drafts; database and UI event handling stay outside this model.
use crate::{
    FormSection, OfficeCard, PageTab,
    form_model::{FormModel, Lookups},
    form_sections::{self, DisclosureState, Section},
    model,
};
use export_doc_engine::{clock::BusinessClock, contracts, generated_api::*, workspace};
use serde_json::{Value, json};

#[derive(Default)]
pub struct OfficeModel {
    pub rows: Vec<Value>,
    pub room: Option<Value>,
    pub history: Option<(Operation, i64)>,
    pub history_page: i64,
    pub focus_request: Option<i64>,
    pub statuses: Vec<&'static str>,
}

pub fn allowed(user: &ApiUserDto, kind: &str, action: &str, row: Option<&Value>) -> bool {
    let resource = if matches!(kind, "bookings" | "rooms") {
        "office.rooms"
    } else {
        "office.supplies"
    };
    user.capabilities.permissions.iter().any(|grant| {
        grant.resource_key == resource
            && grant.action == action
            && match grant.data_scope.as_str() {
                "all" | "company" => true,
                "department" => row.is_none_or(|row| {
                    !user.department_id.is_empty() && row["departmentId"] == user.department_id
                }),
                "own" => row.is_none_or(|row| row["ownerUserId"] == user.id),
                _ => false,
            }
    })
}

pub fn states(meeting: bool, register: bool) -> Vec<&'static str> {
    let all = if meeting {
        vec![
            "",
            "Pending",
            "Approved",
            "InUse",
            "Completed",
            "Rejected",
            "Cancelled",
        ]
    } else {
        vec![
            "",
            "Pending",
            "Approved",
            "Issued",
            "Returned",
            "Rejected",
            "Cancelled",
        ]
    };
    all.into_iter()
        .filter(|state| !register || !matches!(*state, "Pending" | "Rejected"))
        .collect()
}

pub fn state_label(state: &str, meeting: bool) -> String {
    match state {
        "" => "全部状态".into(),
        "Pending" => "待审批".into(),
        "Approved" => if meeting {
            "待使用／领钥匙"
        } else {
            "待领用"
        }
        .into(),
        "Rejected" => "已驳回".into(),
        _ => workspace::display(&json!(state)),
    }
}

fn text(row: &Value, field: &str) -> String {
    workspace::display(&row[field])
}

pub fn cards(rows: &[Value], kind: &str, user: &ApiUserDto) -> Result<Vec<OfficeCard>, String> {
    let clock = BusinessClock::new(&user.business_time_zone)?;
    rows.iter()
        .map(|row| {
            let mut actions = vec![];
            let mut add = |action: &str, label: &str, permission: &str, scoped: bool| {
                if allowed(user, kind, permission, scoped.then_some(row)) {
                    actions.push(PageTab {
                        key: action.into(),
                        label: label.into(),
                    });
                }
            };
            let directory = matches!(kind, "rooms" | "supplies");
            let (title, state, details, description) = if kind == "rooms" {
                add("schedule", "查看日程与预约", "view", false);
                add("edit", "编辑", "manage", false);
                add("delete", "删除", "manage", false);
                (
                    text(row, "name"),
                    if row["isActive"] != true {
                        "已停用"
                    } else if row["inUse"] == true {
                        "使用中"
                    } else {
                        "可预约"
                    }
                    .into(),
                    format!(
                        "{} · {} 人 · {}",
                        text(row, "location"),
                        text(row, "capacity"),
                        if row["requiresKey"] == true {
                            "需领还钥匙"
                        } else {
                            "无需钥匙"
                        }
                    ),
                    text(row, "equipment"),
                )
            } else if kind == "supplies" {
                if row["isActive"] == true {
                    add(
                        "request",
                        if row["isReturnable"] == true {
                            "登记借用"
                        } else {
                            "登记领用"
                        },
                        "create",
                        false,
                    );
                }
                add(RESTOCK_OFFICE_SUPPLY.id, "补充库存", "restock", false);
                add("history", "库存流水", "restock", false);
                add(STOCKTAKE_OFFICE_SUPPLY.id, "盘点", "manage", false);
                add("edit", "编辑", "manage", false);
                add("delete", "删除", "manage", false);
                (
                    text(row, "name"),
                    if row["isActive"] != true {
                        "已停用"
                    } else if row["lowStock"] == true {
                        "低库存"
                    } else if row["isReturnable"] == true {
                        "可借用"
                    } else {
                        "消耗品"
                    }
                    .into(),
                    format!(
                        "可用 {} {} · 在库 {} · 已预留 {} · 最低可用 {}",
                        text(row, "availableQuantity"),
                        text(row, "unit"),
                        text(row, "stockQuantity"),
                        text(row, "reservedQuantity"),
                        text(row, "minimumStock")
                    ),
                    format!("{}\n{}", text(row, "location"), text(row, "description")),
                )
            } else {
                let meeting = kind == "bookings";
                let state = row["status"].as_str().unwrap_or("");
                if state == "Pending" && row["ownerUserId"] != user.id {
                    add(
                        if meeting {
                            APPROVE_MEETING_BOOKING.id
                        } else {
                            APPROVE_OFFICE_SUPPLY_REQUEST.id
                        },
                        "批准",
                        "approve",
                        true,
                    );
                    add(
                        if meeting {
                            REJECT_MEETING_BOOKING.id
                        } else {
                            REJECT_OFFICE_SUPPLY_REQUEST.id
                        },
                        "驳回",
                        "approve",
                        true,
                    );
                }
                if matches!(state, "Pending" | "Approved") {
                    add("edit", "修改", "edit", true);
                    add(
                        if meeting {
                            CANCEL_MEETING_BOOKING.id
                        } else {
                            CANCEL_OFFICE_SUPPLY_REQUEST.id
                        },
                        "取消登记",
                        "cancel",
                        true,
                    );
                }
                if state == "Approved" {
                    add(
                        if meeting {
                            ISSUE_MEETING_ROOM_KEY.id
                        } else {
                            ISSUE_OFFICE_SUPPLY.id
                        },
                        if meeting {
                            if row["requiresKey"] == true {
                                "发放钥匙"
                            } else {
                                "登记使用"
                            }
                        } else {
                            "确认发放"
                        },
                        "issue",
                        true,
                    );
                }
                if state == "InUse" || (state == "Issued" && row["isReturnable"] == true) {
                    add(
                        if meeting {
                            RETURN_MEETING_ROOM_KEY.id
                        } else {
                            RETURN_OFFICE_SUPPLY.id
                        },
                        if meeting {
                            if row["requiresKey"] == true {
                                "归还钥匙"
                            } else {
                                "结束使用"
                            }
                        } else {
                            "登记归还"
                        },
                        "return",
                        true,
                    );
                }
                add("history", "处理记录", "view", true);
                let mut label = state_label(state, meeting);
                if !meeting && state == "Issued" && row["isReturnable"] == true {
                    label = if row["returnDueDate"]
                        .as_str()
                        .is_some_and(|day| day < user.business_date.as_str())
                    {
                        "逾期未归还"
                    } else if row["returnedQuantity"].as_i64().unwrap_or(0) > 0 {
                        "部分归还"
                    } else {
                        "借用中"
                    }
                    .into();
                }
                if meeting
                    && matches!(state, "Pending" | "Approved" | "InUse")
                    && chrono::DateTime::parse_from_rfc3339(row["endsAt"].as_str().unwrap_or(""))
                        .map_err(|_| "预约结束时间无效。")?
                        <= chrono::Utc::now()
                {
                    label = if state == "InUse" {
                        "超时未归还"
                    } else {
                        "已过期（未使用）"
                    }
                    .into();
                }
                let details = if meeting {
                    format!(
                        "{} · {} 人\n{} 至 {}",
                        text(row, "roomName"),
                        text(row, "attendeeCount"),
                        clock.local_input(row["startsAt"].as_str().unwrap_or(""))?,
                        clock.local_input(row["endsAt"].as_str().unwrap_or(""))?
                    )
                } else {
                    format!(
                        "申请 {} {} · 已归还 {} {}\n{}{}",
                        text(row, "quantity"),
                        text(row, "unit"),
                        text(row, "returnedQuantity"),
                        text(row, "unit"),
                        text(row, "purpose"),
                        row["returnDueDate"]
                            .as_str()
                            .map(|date| format!(" · 应还 {date}"))
                            .unwrap_or_default()
                    )
                };
                (
                    text(row, if meeting { "title" } else { "supplyName" }),
                    label,
                    details,
                    format!(
                        "登记人员：{} · 登记于 {}",
                        text(row, "applicantName"),
                        clock.local_input(row["createdAt"].as_str().unwrap_or(""))?
                    ),
                )
            };
            Ok(OfficeCard {
                id: row["id"].as_i64().unwrap_or(0) as i32,
                title: title.into(),
                state: state.into(),
                details: details.into(),
                description: description.into(),
                actions: model(actions),
                directory,
            })
        })
        .collect()
}

pub fn draft(
    operation: Operation,
    row: Option<&Value>,
    user: &ApiUserDto,
    lookups: Lookups,
    appointment_day: Option<&str>,
) -> Result<FormModel, String> {
    let mut schema = contracts::resolve(contracts::request(operation.id)).clone();
    let mut value = contracts::object(operation.id, true);
    if let Some(row) = row {
        value = contracts::overlay(value, row);
        value["expectedVersion"] = row["versionNumber"].clone();
    }
    let clock = BusinessClock::new(&user.business_time_zone)?;
    if matches!(
        operation,
        CREATE_MEETING_BOOKING | CREATE_OFFICE_SUPPLY_REQUEST
    ) {
        value["requestKey"] = json!(export_doc_engine::paths::nonce()?);
        let field = if operation == CREATE_MEETING_BOOKING {
            "meetingRoomId"
        } else {
            "officeSupplyId"
        };
        value[field] = row.ok_or("请先选择会议室或物品。")?["id"].clone();
        schema["properties"][field]["readOnly"] = json!(true);
        value["employeeId"] = Value::Null;
        value["quantity"] = json!(1);
        value["attendeeCount"] = json!(1);
        if operation == CREATE_MEETING_BOOKING {
            let now = chrono::Utc::now().timestamp();
            let start = chrono::DateTime::from_timestamp(((now + 899) / 900 + 1) * 900, 0)
                .ok_or("预约时间超出范围。")?;
            let start = if let Some(day) = appointment_day.filter(|day| !day.is_empty()) {
                let date = chrono::NaiveDate::parse_from_str(day, "%Y-%m-%d")
                    .map_err(|_| "日程日期无效。")?;
                let today = clock.now()?.today;
                if date < today {
                    return Err("请选择今天或之后的预约日期。".into());
                }
                if date == today {
                    start
                } else {
                    clock.parse_local_input(&format!("{date} 09:00"))?
                }
            } else {
                start
            };
            value["startsAt"] = json!(clock.local_input(&start.to_rfc3339())?);
            value["endsAt"] =
                json!(clock.local_input(&(start + chrono::Duration::hours(1)).to_rfc3339())?);
        }
    } else if operation == UPDATE_MEETING_BOOKING {
        for key in ["startsAt", "endsAt"] {
            value[key] = json!(clock.local_input(value[key].as_str().unwrap_or(""))?);
        }
    }
    if operation == RETURN_OFFICE_SUPPLY {
        value["quantity"] = json!(
            value["quantity"].as_i64().unwrap_or(0)
                - value["returnedQuantity"].as_i64().unwrap_or(0)
        );
    }
    if matches!(
        operation,
        CREATE_OFFICE_SUPPLY_REQUEST | UPDATE_OFFICE_SUPPLY_REQUEST
    ) {
        if row.is_some_and(|row| row["isReturnable"] == true) {
            if operation == CREATE_OFFICE_SUPPLY_REQUEST {
                value["returnDueDate"] = json!(clock.now()?.today.to_string());
            }
        } else {
            value["returnDueDate"] = Value::Null;
            schema["properties"]["returnDueDate"]["readOnly"] = json!(true);
        }
    }
    if matches!(operation, RESTOCK_OFFICE_SUPPLY | STOCKTAKE_OFFICE_SUPPLY) {
        value["operationId"] = json!(export_doc_engine::paths::nonce()?);
        value["quantity"] = if operation == STOCKTAKE_OFFICE_SUPPLY {
            value["stockQuantity"].clone()
        } else {
            json!(1)
        };
    }
    if row.is_none() {
        for (key, default) in [
            ("isActive", json!(true)),
            ("requiresKey", json!(true)),
            ("capacity", json!(10)),
            ("maximumBookingHours", json!(8)),
            ("advanceBookingDays", json!(90)),
            ("unit", json!("件")),
        ] {
            if value.get(key).is_some() {
                value[key] = default;
            }
        }
    }
    // Retain only the generated request fields so resource IDs cannot leak into a new request's identity.
    let keys = contracts::properties(&schema).ok_or("行政表单契约无效。")?;
    value
        .as_object_mut()
        .ok_or("行政表单内容无效。")?
        .retain(|key, _| keys.contains_key(key));
    Ok(FormModel::new(&schema, value, lookups))
}

pub fn sections(
    form: &FormModel,
    operation: Operation,
    state: &mut DisclosureState,
) -> Vec<FormSection> {
    let fields: &[&str] = match operation {
        CREATE_MEETING_ROOM | UPDATE_MEETING_ROOM => &[
            "name",
            "location",
            "capacity",
            "maximumBookingHours",
            "advanceBookingDays",
            "equipment",
            "requiresKey",
            "isActive",
        ],
        CREATE_OFFICE_SUPPLY | UPDATE_OFFICE_SUPPLY => &[
            "name",
            "unit",
            "location",
            "description",
            "isReturnable",
            "minimumStock",
            "isActive",
        ],
        CREATE_MEETING_BOOKING | UPDATE_MEETING_BOOKING => {
            &["startsAt", "endsAt", "employeeId", "title", "attendeeCount"]
        }
        CREATE_OFFICE_SUPPLY_REQUEST | UPDATE_OFFICE_SUPPLY_REQUEST => {
            &["employeeId", "quantity", "purpose", "returnDueDate"]
        }
        _ => &["quantity", "note"],
    };
    form_sections::build(
        form,
        &[Section::new("office.main", "登记资料", fields, true)],
        state,
    )
}

pub fn request_body(
    form: &FormModel,
    operation: Operation,
    user: &ApiUserDto,
) -> Result<Value, String> {
    let mut value = form.value.clone();
    if matches!(operation, CREATE_MEETING_BOOKING | UPDATE_MEETING_BOOKING) {
        let clock = BusinessClock::new(&user.business_time_zone)?;
        for key in ["startsAt", "endsAt"] {
            value[key] = json!(
                clock
                    .parse_local_input(value[key].as_str().unwrap_or(""))?
                    .to_rfc3339()
            );
        }
    }
    Ok(value)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn selected_schedule_day_and_zone_reach_the_booking_request() {
        let clock = BusinessClock::new("Pacific/Auckland").unwrap();
        let day = (clock.now().unwrap().today + chrono::Duration::days(2)).to_string();
        let user = ApiUserDto {
            business_time_zone: "Pacific/Auckland".into(),
            ..Default::default()
        };
        let form = draft(
            CREATE_MEETING_BOOKING,
            Some(&json!({"id":42,"name":"会议室"})),
            &user,
            Lookups::new(),
            Some(&day),
        )
        .unwrap();
        let body = request_body(&form, CREATE_MEETING_BOOKING, &user).unwrap();
        assert_eq!(body["meetingRoomId"], 42);
        assert!(body.get("id").is_none());
        assert_eq!(
            body["startsAt"],
            clock
                .parse_local_input(&format!("{day} 09:00"))
                .unwrap()
                .to_rfc3339()
        );
        assert_eq!(
            body["endsAt"],
            clock
                .parse_local_input(&format!("{day} 10:00"))
                .unwrap()
                .to_rfc3339()
        );
    }
}
