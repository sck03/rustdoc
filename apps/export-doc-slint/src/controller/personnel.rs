use super::*;
use crate::{
    DataRow, PageTab, PersonRow,
    personnel_model::{self, IMAGE_KINDS},
};
use export_doc_engine::{contracts, workspace};
use serde_json::{Value, json};

impl Desktop {
    pub fn refresh_personnel(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        if app.get_page() == "personnel" {
            if let Some(record) = &self.personnel.record {
                self.request(
                    GET_PERSONNEL,
                    record.employee.id,
                    vec![],
                    None,
                    "personnel-record",
                );
            }
            return;
        }
        let department = app
            .get_people_department_index()
            .checked_sub(1)
            .and_then(|index| self.personnel.departments.get(index as usize))
            .map(|department| department.code.clone())
            .unwrap_or_default();
        let status = if self.workspace.root == "directory" {
            ""
        } else {
            match app.get_people_status_index() {
                1 => "Probation",
                2 => "Active",
                3 => "Departed",
                _ => "",
            }
        };
        self.start(Work::PersonnelDirectory {
            query: vec![
                ("pageNumber", self.workspace.page.max(1).to_string()),
                ("pageSize", "24".into()),
                ("keyword", app.get_search().to_string()),
                ("departmentId", department),
                ("status", status.into()),
                (
                    "attentionOnly",
                    (self.workspace.root != "directory" && app.get_people_attention()).to_string(),
                ),
            ],
        });
    }
    pub fn personnel_directory(&mut self, value: Value) {
        let options = match serde_json::from_value::<PersonnelOptions>(value["options"].clone()) {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        let page = match serde_json::from_value::<PagedResultOfPersonnelDirectoryRecord>(
            value["page"].clone(),
        ) {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        self.personnel.departments = options.departments;
        self.personnel.directory = page.items;
        self.workspace.payload = Some(value["page"].clone());
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_people_departments(model(
                std::iter::once("全部部门".into())
                    .chain(
                        self.personnel
                            .departments
                            .iter()
                            .map(|department| department.name.clone().into()),
                    )
                    .collect(),
            ));
            app.set_people(model(
                self.personnel
                    .directory
                    .iter()
                    .map(|person| PersonRow {
                        id: person.id as i32,
                        name: person.full_name.clone().into(),
                        number: person.employee_number.clone().into(),
                        department: person.department_name.clone().into(),
                        job_title: person.job_title.clone().into(),
                        email: person.work_email.clone().into(),
                        phone: person.work_phone.clone().into(),
                        location: person.work_location.clone().into(),
                        state: workspace::display(&json!(person.status)).into(),
                        can_open: person.can_view_details && self.workspace.root != "directory",
                    })
                    .collect(),
            ));
            app.set_can_create(options.can_create && self.workspace.root != "directory");
            app.set_people_can_view_details(self.allowed(GET_PERSONNEL));
            app.set_list_page(page.page_number as i32);
            app.set_total_pages(page.total_pages as i32);
            app.set_total_records(page.total_count as i32);
        }
        if let Some((resource, id)) = self.pending_open.take() {
            if resource == "people" {
                self.request(GET_PERSONNEL, id, vec![], None, "personnel-record");
            }
        }
    }
    pub fn open_personnel(&mut self, value: Value) {
        let record = match serde_json::from_value::<PersonnelRecord>(value) {
            Ok(value) => value,
            Err(cause) => {
                self.error(format!("人员档案格式无效：{cause}"));
                return;
            }
        };
        let changed = self
            .personnel
            .record
            .as_ref()
            .is_none_or(|current| current.employee.id != record.employee.id);
        if changed {
            self.personnel.tab = 0;
            self.personnel.image_kind = 0;
            self.personnel.show_identity = false;
            self.personnel.history_page = 1;
        }
        self.personnel.record = Some(record);
        self.cancel_form();
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_page("personnel".into());
        }
        self.sync_personnel();
        if !changed {
            self.load_personnel_tab();
        }
    }
    pub fn sync_personnel(&mut self) {
        let (Some(record), Some(ui)) = (&self.personnel.record, self.ui.upgrade()) else {
            return;
        };
        let app = ui.global::<App>();
        app.set_person_title(format!("{} · 人员档案", record.employee.full_name).into());
        app.set_person_subtitle(
            format!(
                "{} · {} · {}",
                record.employee.employee_number,
                record.employee.department_name,
                record.employee.job_title
            )
            .into(),
        );
        app.set_person_state(workspace::display(&json!(record.employee.status)).into());
        app.set_person_tab(self.personnel.tab);
        app.set_person_can_edit(record.can_edit && self.can(UPDATE_PERSONNEL));
        app.set_person_can_delete(record.can_delete && self.can(DELETE_PERSONNEL));
        app.set_person_restriction(record.delete_restriction.clone().into());
        app.set_person_identity_visible(self.personnel.show_identity);
        app.set_person_sections(model(personnel_model::facts(
            record,
            self.personnel.show_identity,
            &mut self.disclosure_state,
        )));
        app.set_person_reminder(
            personnel_model::reminder(
                record,
                self.user
                    .as_ref()
                    .map(|user| user.business_date.as_str())
                    .unwrap_or(""),
            )
            .into(),
        );
        let actions = if record.can_transition {
            workspace::actions("people", &json!({"status":record.employee.status}))
        } else {
            vec![]
        };
        app.set_person_actions(model(
            actions
                .into_iter()
                .filter(|(operation, _)| self.can(*operation))
                .map(|(operation, label)| PageTab {
                    key: operation.id.into(),
                    label: label.into(),
                })
                .collect(),
        ));
        app.set_person_image_kind(self.personnel.image_kind as i32);
        app.set_person_has_image(
            record
                .images
                .iter()
                .any(|image| image.kind == IMAGE_KINDS[self.personnel.image_kind]),
        );
    }
    pub fn new_personnel(&mut self) {
        self.edit_personnel(true);
    }
    fn edit_personnel(&mut self, new: bool) {
        let record = if new {
            None
        } else {
            self.personnel.record.as_ref()
        };
        if !new && record.is_none_or(|record| !record.can_edit) {
            self.error("当前档案不能编辑。");
            return;
        }
        let date = self
            .user
            .as_ref()
            .map(|user| user.business_date.as_str())
            .unwrap_or("");
        let form = match personnel_model::form(
            record,
            date,
            &self.personnel.departments,
            self.lookups.clone(),
        ) {
            Ok(form) => form,
            Err(cause) => {
                self.error(cause);
                return;
            }
        };
        self.form_id = record.map(|record| record.employee.id).unwrap_or(0);
        self.form_operation = Some(if new {
            CREATE_PERSONNEL
        } else {
            UPDATE_PERSONNEL
        });
        self.form_reply = "personnel-saved".into();
        self.form = Some(form);
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(
                if new {
                    "入职登记"
                } else {
                    "编辑人员档案"
                }
                .into(),
            );
            app.set_form_open(true);
        }
        self.sync_form();
    }
    pub fn personnel_action(&mut self, action: &str, value: &str) {
        if self.task.is_some() {
            return;
        }
        match action {
            "filter" => {
                self.workspace.page = 1;
                self.refresh_personnel();
            }
            "back" => {
                self.personnel.record = None;
                if let Some(ui) = self.ui.upgrade() {
                    ui.global::<App>().set_page("people".into());
                    ui.global::<App>().set_person_image(Default::default());
                }
                self.refresh_personnel();
            }
            "open" => {
                if let Ok(id) = value.parse::<i64>() {
                    self.request(GET_PERSONNEL, id, vec![], None, "personnel-record");
                }
            }
            "edit" => self.edit_personnel(false),
            "tab" => {
                self.personnel.tab = value.parse::<i32>().unwrap_or(0).clamp(0, 3);
                self.sync_personnel();
                self.load_personnel_tab();
            }
            "identity" => {
                self.personnel.show_identity = !self.personnel.show_identity;
                self.sync_personnel();
            }
            "image-kind" => {
                self.personnel.image_kind = value.parse::<usize>().unwrap_or(0).min(2);
                self.sync_personnel();
                self.load_personnel_image();
            }
            "upload-image" => self.upload_personnel_image(),
            "delete-image" => self.confirm(
                Pending::PersonnelImage(IMAGE_KINDS[self.personnel.image_kind].into()),
                "确认删除当前人员的这张图片？删除后可以重新上传。",
            ),
            "delete" => {
                if let Some(record) = &self.personnel.record {
                    let mut selected = json!(record);
                    selected["id"] = json!(record.employee.id);
                    self.workspace.selected = Some(selected);
                    self.delete_record();
                }
            }
            "history-previous" | "history-next" => {
                self.personnel.history_page = (self.personnel.history_page
                    + if action == "history-next" { 1 } else { -1 })
                .max(1);
                self.load_personnel_tab();
            }
            "clearance-open" => {
                if let Ok(index) = value.parse::<usize>() {
                    if let Some(item) = self.personnel.clearance.get(index) {
                        let target = if item.kind == "rooms" {
                            "bookings"
                        } else {
                            "supply-requests"
                        };
                        self.pending_open = Some((target, item.request_id));
                        self.navigate_now(target);
                    }
                }
            }
            _ => self.personnel_workflow(action),
        }
    }
    fn personnel_workflow(&mut self, action: &str) {
        let Some(operation) = [
            CONFIRM_PERSONNEL,
            TRANSFER_PERSONNEL,
            DEPART_PERSONNEL,
            REHIRE_PERSONNEL,
        ]
        .into_iter()
        .find(|operation| operation.id == action && self.can(*operation)) else {
            return;
        };
        let Some(record) = &self.personnel.record else {
            return;
        };
        if !record.can_transition {
            return;
        }
        let mut value = contracts::object(operation.id, true);
        value["expectedVersion"] = json!(record.version_number);
        value["effectiveDate"] = json!(
            self.user
                .as_ref()
                .map(|user| user.business_date.as_str())
                .unwrap_or("")
        );
        let mut schema = contracts::resolve(contracts::request(operation.id)).clone();
        if operation == TRANSFER_PERSONNEL || operation == REHIRE_PERSONNEL {
            value["departmentId"] = json!(record.employee.department_id);
            value["jobTitle"] = json!(record.employee.job_title);
            value["onProbation"] = json!(operation == REHIRE_PERSONNEL);
        } else {
            for key in ["departmentId", "jobTitle", "onProbation"] {
                schema["properties"][key]["readOnly"] = json!(true);
            }
        }
        if operation != REHIRE_PERSONNEL {
            schema["properties"]["onProbation"]["readOnly"] = json!(true);
        }
        let mut lookups = self.lookups.clone();
        lookups.insert(
            "departmentId".into(),
            self.personnel
                .departments
                .iter()
                .filter(|department| department.is_active)
                .map(|department| (json!(department.code), department.name.clone()))
                .collect(),
        );
        self.form = Some(FormModel::new(&schema, value, lookups));
        self.form_id = record.employee.id;
        self.form_operation = Some(operation);
        self.form_reply = "personnel-saved".into();
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(
                match operation {
                    CONFIRM_PERSONNEL => "办理转正",
                    TRANSFER_PERSONNEL => "部门 / 岗位调动",
                    DEPART_PERSONNEL => "办理离职",
                    _ => "办理返聘",
                }
                .into(),
            );
            app.set_form_open(true);
        }
        self.sync_form();
    }
    fn load_personnel_tab(&mut self) {
        let Some(record) = &self.personnel.record else {
            return;
        };
        let id = record.employee.id;
        match self.personnel.tab {
            1 => self.load_personnel_image(),
            2 => self.request(
                GET_PERSONNEL_HISTORY,
                id,
                vec![
                    ("pageNumber", self.personnel.history_page.max(1).to_string()),
                    ("pageSize", "30".into()),
                ],
                None,
                "personnel-history",
            ),
            3 => self.request(
                GET_PERSONNEL_CLEARANCE,
                id,
                vec![],
                None,
                "personnel-clearance",
            ),
            _ => {}
        }
    }
    fn load_personnel_image(&mut self) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<App>().set_person_image(Default::default());
        }
        let Some(record) = &self.personnel.record else {
            return;
        };
        let kind = IMAGE_KINDS[self.personnel.image_kind];
        if record.images.iter().any(|image| image.kind == kind) {
            self.start(Work::Image {
                operation: GET_PERSONNEL_IMAGE,
                parameters: vec![
                    ("id", record.employee.id.to_string()),
                    ("kind", kind.into()),
                ],
                reply: "personnel".into(),
            });
        }
    }
    fn upload_personnel_image(&mut self) {
        let (Some(record), Some(ui)) = (&self.personnel.record, self.ui.upgrade()) else {
            return;
        };
        if !record.can_edit || !self.can(UPLOAD_PERSONNEL_IMAGE) {
            return;
        }
        let Some(source) =
            self.platform
                .choose_source(ui.window(), "照片或身份证图片", &["png", "jpg", "jpeg"])
        else {
            return;
        };
        self.start(Work::Upload {
            operation: UPLOAD_PERSONNEL_IMAGE,
            parameters: vec![
                ("id", record.employee.id.to_string()),
                ("kind", IMAGE_KINDS[self.personnel.image_kind].into()),
            ],
            metadata: json!({"expectedVersion":record.version_number}),
            source,
            limit: 5 * 1024 * 1024,
            reply: "personnel-image-saved".into(),
        });
    }
    pub fn delete_personnel_image(&mut self, kind: &str) {
        let Some(record) = &self.personnel.record else {
            return;
        };
        if !record.can_edit || !self.can(DELETE_PERSONNEL_IMAGE) {
            return;
        }
        self.start(Work::Request {
            operation: DELETE_PERSONNEL_IMAGE,
            parameters: vec![
                ("id", record.employee.id.to_string()),
                ("kind", kind.into()),
            ],
            query: vec![("expectedVersion", record.version_number.to_string())],
            body: None,
            reply: "personnel-image-saved".into(),
        });
    }
    pub fn personnel_history(&self, value: Value) {
        let page = match serde_json::from_value::<PagedResultOfPersonnelEventRecord>(value) {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_columns(model(
                ["生效日期", "业务操作", "办理人", "任职说明", "备注"]
                    .map(Into::into)
                    .to_vec(),
            ));
            app.set_records(model(
                page.items
                    .into_iter()
                    .map(|event| DataRow {
                        id: event.id as i32,
                        cells: model(vec![
                            event.effective_date.into(),
                            personnel_event_label(&event.action).into(),
                            event.actor_name.into(),
                            event.summary.into(),
                            event.note.into(),
                        ]),
                    })
                    .collect(),
            ));
            app.set_list_page(page.page_number as i32);
            app.set_total_pages(page.total_pages as i32);
            app.set_total_records(page.total_count as i32);
        }
    }
    pub fn personnel_clearance(&mut self, value: Value) {
        let clearance = match serde_json::from_value::<PersonnelClearance>(value) {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        self.personnel.clearance = clearance.items;
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_person_clearance_summary(
                format!(
                    "{} 项会议室交接 · {} 项物品交接{}",
                    clearance.meeting_count,
                    clearance.supply_count,
                    if clearance.managed_departments.is_empty() {
                        String::new()
                    } else {
                        format!(
                            " · 负责部门：{}",
                            clearance
                                .managed_departments
                                .iter()
                                .map(|department| department.name.as_str())
                                .collect::<Vec<_>>()
                                .join("、")
                        )
                    }
                )
                .into(),
            );
            app.set_columns(model(
                ["类型", "资源", "状态", "未归还数量"]
                    .map(Into::into)
                    .to_vec(),
            ));
            app.set_records(model(
                self.personnel
                    .clearance
                    .iter()
                    .enumerate()
                    .map(|(index, item)| DataRow {
                        id: index as i32,
                        cells: model(vec![
                            if item.kind == "rooms" {
                                "会议室"
                            } else {
                                "物品"
                            }
                            .into(),
                            item.resource_name.clone().into(),
                            workspace::display(&json!(item.status)).into(),
                            item.outstanding_quantity.to_string().into(),
                        ]),
                    })
                    .collect(),
            ));
        }
    }
}
fn personnel_event_label(action: &str) -> &str {
    match action {
        "Hire" => "入职登记",
        "Edit" => "档案更新",
        "Image" => "照片与证件更新",
        "LinkAccount" => "关联账号",
        "Confirm" => "转正",
        "Transfer" => "调岗",
        "Depart" => "离职归档",
        "Rehire" => "返聘入职",
        _ => action,
    }
}
