use super::*;
use crate::{DataRow, PageTab, office_model};
use export_doc_engine::{clock::BusinessClock, contracts, engine::catalog, workspace};
use serde_json::Value;

impl Desktop {
    pub fn is_office(&self) -> bool {
        matches!(self.workspace.root, "bookings" | "supply-requests")
    }
    fn office_resource(&self, requests: bool) -> &'static str {
        match (self.workspace.root == "bookings", requests) {
            (true, false) => "rooms",
            (true, true) => "bookings",
            (false, false) => "supplies",
            (false, true) => "supply-requests",
        }
    }
    pub fn refresh_office(&mut self) {
        let (Some(ui), Some(user)) = (self.ui.upgrade(), &self.user) else {
            return;
        };
        let app = ui.global::<App>();
        if self.office.history.is_some() {
            self.load_office_history();
            return;
        }
        if app.get_office_schedule() {
            self.load_office_schedule();
            return;
        }
        self.workspace.resource = self.office_resource(app.get_office_requests());
        app.set_resource(self.workspace.resource.into());
        self.office.statuses = office_model::states(
            self.workspace.root == "bookings",
            user.capabilities.uses_office_register,
        );
        app.set_office_statuses(model(
            self.office
                .statuses
                .iter()
                .map(|state| {
                    office_model::state_label(state, self.workspace.root == "bookings").into()
                })
                .collect(),
        ));
        app.set_office_can_manage(office_model::allowed(
            user,
            self.workspace.resource,
            "manage",
            None,
        ));
        app.set_can_create(office_model::allowed(
            user,
            self.workspace.resource,
            "create",
            None,
        ));
        app.set_office_register(user.capabilities.uses_office_register);
        app.set_office_focused(self.office.focus_request.is_some());
        let mut query = vec![
            ("pageNumber", self.workspace.page.max(1).to_string()),
            ("pageSize", "24".into()),
        ];
        if app.get_office_requests() {
            query.push((
                "status",
                self.office
                    .statuses
                    .get(app.get_office_status_index().max(0) as usize)
                    .unwrap_or(&"")
                    .to_string(),
            ));
            query.push(("mineOnly", app.get_office_mine().to_string()));
            if let Some(id) = self.office.focus_request {
                query.push(("requestId", id.to_string()));
            }
        } else {
            query.extend([
                ("keyword", app.get_search().to_string()),
                ("includeInactive", app.get_office_inactive().to_string()),
                ("lowStockOnly", app.get_office_low_stock().to_string()),
            ]);
        }
        self.request(
            catalog::resource(self.workspace.resource).unwrap().list,
            0,
            query,
            None,
            "office-list",
        );
    }
    pub fn office_action(&mut self, action: &str, id: i32) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        match action {
            "resources" | "requests" | "all" => {
                app.set_office_requests(action != "resources");
                app.set_office_schedule(false);
                app.set_office_history_open(false);
                self.office.history = None;
                self.office.focus_request = None;
                self.workspace.page = 1;
                self.refresh_office();
                return;
            }
            "filter" => {
                self.workspace.page = 1;
                self.refresh_office();
                return;
            }
            "previous" | "next" => {
                if self.office.history.is_some() {
                    self.office.history_page =
                        (self.office.history_page + if action == "next" { 1 } else { -1 }).max(1);
                } else {
                    self.workspace.page =
                        (self.workspace.page + if action == "next" { 1 } else { -1 }).max(1);
                }
                self.refresh_office();
                return;
            }
            "day" => {
                self.load_office_schedule();
                return;
            }
            "day-previous" | "day-next" => {
                match chrono::NaiveDate::parse_from_str(app.get_office_day().as_str(), "%Y-%m-%d") {
                    Ok(day) => {
                        if let Some(day) = day.checked_add_signed(chrono::Duration::days(
                            if action == "day-next" { 1 } else { -1 },
                        )) {
                            app.set_office_day(day.to_string().into());
                            self.load_office_schedule();
                        }
                    }
                    Err(_) => self.error("请输入有效日期。"),
                }
                return;
            }
            "book" => {
                if let Some(room) = self.office.room.clone() {
                    self.office_form(CREATE_MEETING_BOOKING, Some(room), "登记会议室预约");
                }
                return;
            }
            "new" => {
                let resource = catalog::resource(self.office_resource(false)).unwrap();
                self.office_form(resource.create, None, &format!("添加{}", resource.title));
                return;
            }
            _ => {}
        }
        let Some(row) = self.office.rows.iter().find(|row| row["id"] == id).cloned() else {
            self.error("记录已变化，请刷新后选择。");
            return;
        };
        self.workspace.selected = Some(row.clone());
        match action {
            "schedule" => {
                self.office.room = Some(row.clone());
                app.set_office_schedule(true);
                app.set_office_room_title(
                    format!("{} · 日程与预约", workspace::display(&row["name"])).into(),
                );
                app.set_office_room_description(
                    format!(
                        "{} · 最多 {} 人 · 单次最多 {} 小时",
                        workspace::display(&row["location"]),
                        row["capacity"],
                        row["maximumBookingHours"]
                    )
                    .into(),
                );
                app.set_office_can_book(
                    row["isActive"] == true && self.can(CREATE_MEETING_BOOKING),
                );
                if let Some(user) = &self.user {
                    app.set_office_day(user.business_date.clone().into());
                }
                self.load_office_schedule();
            }
            "request" => {
                self.office_form(CREATE_OFFICE_SUPPLY_REQUEST, Some(row), "登记办公物品领用")
            }
            "edit" => {
                let resource = catalog::resource(self.workspace.resource).unwrap();
                self.office_form(
                    resource.update,
                    Some(row),
                    &format!("修改{}", resource.title),
                );
            }
            "delete" => self.delete_record(),
            "history" => {
                self.office.history = Some((
                    match self.workspace.resource {
                        "supplies" => GET_OFFICE_STOCK_HISTORY,
                        "bookings" => GET_MEETING_BOOKING_HISTORY,
                        _ => GET_OFFICE_SUPPLY_REQUEST_HISTORY,
                    },
                    i64::from(id),
                ));
                self.office.history_page = 1;
                app.set_office_history_open(true);
                app.set_office_history_title(
                    if self.workspace.resource == "supplies" {
                        "库存流水"
                    } else {
                        "登记与交接处理记录"
                    }
                    .into(),
                );
                self.load_office_history();
            }
            _ => {
                if let Some(operation) = catalog::operation(action) {
                    let title = app
                        .get_office_cards()
                        .iter()
                        .find(|card| card.id == id)
                        .and_then(|card| card.actions.iter().find(|entry| entry.key == action))
                        .map(|entry| entry.label.to_string())
                        .unwrap_or_else(|| "行政办理".into());
                    self.office_form(operation, Some(row), &title);
                }
            }
        }
    }
    fn load_office_schedule(&mut self) {
        let (Some(ui), Some(user), Some(room)) = (self.ui.upgrade(), &self.user, &self.office.room)
        else {
            return;
        };
        let range = BusinessClock::new(&user.business_time_zone)
            .and_then(|clock| clock.day_range(ui.global::<App>().get_office_day().as_str()));
        match range {
            Ok((from, to)) => self.request(
                GET_MEETING_ROOM_AVAILABILITY,
                room["id"].as_i64().unwrap_or(0),
                vec![("from", from), ("to", to)],
                None,
                "office-slots",
            ),
            Err(cause) => self.error(cause),
        }
    }
    fn load_office_history(&mut self) {
        if let Some((operation, id)) = self.office.history {
            self.request(
                operation,
                id,
                vec![
                    ("pageNumber", self.office.history_page.max(1).to_string()),
                    ("pageSize", "30".into()),
                ],
                None,
                "office-history",
            );
        }
    }
    fn office_form(&mut self, operation: Operation, row: Option<Value>, title: &str) {
        if !self.can(operation) {
            self.error("当前账号没有执行此操作的权限。");
            return;
        }
        let Some(user) = &self.user else {
            return;
        };
        let day = self
            .ui
            .upgrade()
            .map(|ui| ui.global::<App>().get_office_day().to_string());
        match office_model::draft(
            operation,
            row.as_ref(),
            user,
            self.lookups.clone(),
            day.as_deref(),
        ) {
            Ok(form) => {
                self.form_id = if operation.method == "POST"
                    && matches!(
                        operation,
                        CREATE_MEETING_BOOKING
                            | CREATE_OFFICE_SUPPLY_REQUEST
                            | CREATE_MEETING_ROOM
                            | CREATE_OFFICE_SUPPLY
                    ) {
                    0
                } else {
                    row.as_ref().and_then(|row| row["id"].as_i64()).unwrap_or(0)
                };
                self.form_operation = Some(operation);
                self.form_reply = "office-saved".into();
                self.form = Some(form);
                if let Some(ui) = self.ui.upgrade() {
                    let app = ui.global::<App>();
                    app.set_form_title(title.into());
                    app.set_form_open(true);
                }
                self.sync_form();
            }
            Err(cause) => self.error(cause),
        }
    }
    pub fn office_data(&mut self, reply: &str, value: &Value) -> bool {
        if !reply.starts_with("office-") {
            return false;
        }
        let result = self.apply_office_data(reply, value);
        if let Err(cause) = result {
            self.error(cause);
        }
        true
    }
    fn apply_office_data(&mut self, reply: &str, value: &Value) -> Result<(), String> {
        let (Some(ui), Some(user)) = (self.ui.upgrade(), &self.user) else {
            return Ok(());
        };
        let app = ui.global::<App>();
        match reply {
            "office-list" => {
                export_doc_contracts::validation::structure(
                    contracts::response(
                        catalog::resource(self.workspace.resource).unwrap().list.id,
                    ),
                    value,
                )
                .map_err(|cause| cause.to_string())?;
                self.office.rows = value["items"]
                    .as_array()
                    .ok_or("行政目录格式无效。")?
                    .clone();
                app.set_office_cards(model(office_model::cards(
                    &self.office.rows,
                    self.workspace.resource,
                    user,
                )?));
                self.workspace.payload = Some(value.clone());
                self.workspace.loaded = true;
            }
            "office-saved" => {
                let operation = self.form_operation;
                self.cancel_form();
                self.load_lookups = true;
                if operation.is_some_and(|operation| {
                    matches!(
                        operation,
                        CREATE_MEETING_BOOKING
                            | CREATE_OFFICE_SUPPLY_REQUEST
                            | UPDATE_MEETING_BOOKING
                            | UPDATE_OFFICE_SUPPLY_REQUEST
                    )
                }) {
                    app.set_office_schedule(false);
                    app.set_office_requests(true);
                    self.office.focus_request = value["id"].as_i64();
                    self.workspace.page = 1;
                }
                self.refresh_office();
                self.status("登记与处理记录已保存");
                return Ok(());
            }
            "office-slots" => {
                let clock = BusinessClock::new(&user.business_time_zone)?;
                let slots = value.as_array().ok_or("会议日程格式无效。")?;
                let rows = slots
                    .iter()
                    .map(|slot| {
                        Ok(PageTab {
                            key: office_model::state_label(
                                slot["status"].as_str().unwrap_or(""),
                                true,
                            )
                            .into(),
                            label: format!(
                                "{} 至 {}",
                                clock.local_input(slot["startsAt"].as_str().unwrap_or(""))?,
                                clock.local_input(slot["endsAt"].as_str().unwrap_or(""))?
                            )
                            .into(),
                        })
                    })
                    .collect::<Result<Vec<_>, String>>()?;
                app.set_office_slots(model(rows));
                return Ok(());
            }
            "office-history" => {
                let stock = self
                    .office
                    .history
                    .is_some_and(|(operation, _)| operation == GET_OFFICE_STOCK_HISTORY);
                let clock = BusinessClock::new(&user.business_time_zone)?;
                let keys = if stock {
                    ["kind", "quantityDelta", "stockAfter", "actorName", "note"]
                } else {
                    ["action", "quantity", "actorName", "note", ""]
                };
                app.set_columns(model(
                    if stock {
                        vec!["时间", "操作", "数量变化", "变动后库存", "经办人", "备注"]
                    } else {
                        vec!["时间", "操作", "数量", "经办人", "备注"]
                    }
                    .into_iter()
                    .map(Into::into)
                    .collect(),
                ));
                let rows = value["items"]
                    .as_array()
                    .ok_or("处理记录分页格式无效。")?
                    .iter()
                    .map(|event| {
                        let mut cells = vec![
                            clock
                                .local_input(event["createdAt"].as_str().unwrap_or(""))?
                                .into(),
                        ];
                        for key in keys.iter().filter(|key| !key.is_empty()) {
                            let value = workspace::display(&event[*key]);
                            cells.push(
                                match value.as_str() {
                                    "Register" => "登记",
                                    "Submit" => "提交申请",
                                    "Approve" => "批准",
                                    "Reject" => "驳回",
                                    "Cancel" => "取消",
                                    "Issue" => "发放／交接",
                                    "Return" => "归还",
                                    "Restock" => "补充入库",
                                    "Stocktake" => "盘点",
                                    "Edit" => "修改",
                                    _ => value.as_str(),
                                }
                                .into(),
                            );
                        }
                        Ok(DataRow {
                            id: event["id"].as_i64().unwrap_or(0) as i32,
                            cells: model(cells),
                        })
                    })
                    .collect::<Result<Vec<_>, String>>()?;
                app.set_records(model(rows));
            }
            _ => return Err("未知行政页面响应。".into()),
        }
        app.set_list_page(value["pageNumber"].as_i64().unwrap_or(1) as i32);
        app.set_total_pages(value["totalPages"].as_i64().unwrap_or(1) as i32);
        app.set_total_records(value["totalCount"].as_i64().unwrap_or(0) as i32);
        Ok(())
    }
}
