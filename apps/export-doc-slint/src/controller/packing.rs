use super::*;
use crate::{DataRow, Packing};
use serde_json::{Value, json};
use std::time::{Duration, Instant};

impl Desktop {
    pub fn setup_packing(&mut self) {
        self.packing = Default::default();
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Packing>();
            view.set_tab(0);
            view.set_project_index(-1);
            view.set_ready(false);
            view.set_preview(Default::default());
        }
        self.sync_packing();
        self.request(
            LIST_CONTAINER_PACKING_CONTAINER_TYPES,
            0,
            vec![],
            None,
            "packing:types",
        );
    }
    pub fn sync_packing(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Packing>();
        view.set_can_edit(
            self.can(SAVE_CONTAINER_PACKING_PROJECT) && self.can(ANALYZE_CONTAINER_PACKING),
        );
        view.set_can_manage(self.can(SAVE_CONTAINER_PACKING_CONTAINER_TYPE));
        view.set_can_delete(self.can(DELETE_CONTAINER_PACKING_PROJECT));
        view.set_can_export(self.can(DOWNLOAD_CONTAINER_PACKING_PDF));
        view.set_name(
            self.packing.form.value["name"]
                .as_str()
                .unwrap_or("")
                .into(),
        );
        view.set_type_name(
            self.packing.form.value["containerType"]
                .as_str()
                .unwrap_or("")
                .into(),
        );
        view.set_dirty(self.packing.dirty());
        view.set_saved(self.packing.id() > 0);
        view.set_can_undo(self.packing.history.can_undo());
        view.set_can_redo(self.packing.history.can_redo());
        view.set_form_error(self.packing.form.error.clone().into());
        view.set_invalid_field(self.packing.form.invalid_field.clone().into());
        view.set_cargo(model(self.packing.rows()));
        view.set_sections(model(self.packing.sections(&mut self.disclosure_state)));
        view.set_projects(model(
            self.packing
                .projects
                .iter()
                .map(|row| {
                    format!(
                        "{} · {}",
                        row["name"].as_str().unwrap_or(""),
                        row["containerType"].as_str().unwrap_or("")
                    )
                    .into()
                })
                .collect(),
        ));
        view.set_types(model(
            self.packing
                .types
                .iter()
                .map(|row| row["name"].as_str().unwrap_or("").into())
                .collect(),
        ));
        view.set_type_index(
            self.packing
                .types
                .iter()
                .position(|row| row["name"] == self.packing.form.value["containerType"])
                .map_or(-1, |i| i as i32),
        );
        view.set_ready(self.packing.analysis.is_some());
        if let Some(analysis) = &self.packing.analysis {
            view.set_summary(format!("已装 {} / {} 件 · 未装 {} 件 · {} 托 · {:.3} m³ / {:.2} kg\n容积利用率 {:.2}% · 载重利用率 {:.2}% · 重心偏差 {:.2}% / {:.2}% · {}",
                analysis.packed_packages,analysis.total_packages,analysis.unpacked_packages,analysis.packed_pallets,analysis.packed_volume,analysis.packed_weight,
                analysis.volume_utilization_percent,analysis.weight_utilization_percent,analysis.center_of_gravity_length_deviation_percent,analysis.center_of_gravity_width_deviation_percent,
                if analysis.is_center_of_gravity_within_tolerance{"重心在容差内"}else{"重心超出容差"}).into());
            view.set_blocks(model(
                analysis
                    .packed_items
                    .iter()
                    .enumerate()
                    .map(|(index, row)| DataRow {
                        id: index as i32 + 1,
                        cells: model(vec![
                            row.name.clone().into(),
                            row.display_text.clone().into(),
                            format!(
                                "{}, {}, {}",
                                row.x.normalize(),
                                row.y.normalize(),
                                row.base_height.normalize()
                            )
                            .into(),
                            format!(
                                "{} × {} × {}",
                                row.width.normalize(),
                                row.height.normalize(),
                                row.occupied_height.normalize()
                            )
                            .into(),
                            row.total_weight.normalize().to_string().into(),
                            row.priority_group.clone().into(),
                        ]),
                    })
                    .collect(),
            ));
            view.set_status("分析已完成；预览视角不会改变实际装载位置。".into());
        } else {
            view.set_preview(Default::default());
            view.set_blocks(model(vec![]));
            view.set_summary("请先完成装箱分析。".into());
            view.set_status("录入货物与规则后开始分析；方案保存后可再次读取。".into());
        }
    }
    pub fn packing_edit(&mut self, key: &str, value: &str, choice: Option<i32>) {
        if self.task.is_some() || !self.can(SAVE_CONTAINER_PACKING_PROJECT) {
            return;
        }
        let before = self.packing.snapshot();
        let result = match choice {
            Some(index) => self.packing.form.choose(key, index.max(0) as usize),
            None => self.packing.form.edit(key, value),
        };
        if let Err(cause) = result {
            self.packing.form.error = cause;
        }
        self.packing.change(before, Some(key.into()));
        self.sync_packing();
    }
    pub fn packing_action(&mut self, action: &str, index: i32) {
        if action == "view" {
            self.packing.view_due = Some(Instant::now() + Duration::from_millis(120));
            return;
        }
        if self.task.is_some() {
            return;
        }
        if action.starts_with("packing-") {
            let expanded = self
                .disclosure_state
                .get(action)
                .copied()
                .unwrap_or(action == "packing-dimensions");
            self.disclosure_state.insert(action.into(), !expanded);
            self.sync_packing();
            return;
        }
        if ["new", "load"].contains(&action) && self.packing.dirty() {
            self.confirm(
                Pending::PackingAction(action.into(), index),
                "当前装柜方案尚未保存，确认放弃修改？",
            );
            return;
        }
        if ["delete", "delete-type", "clear"].contains(&action) {
            self.confirm(
                Pending::PackingAction(action.into(), index),
                "确认执行此删除或清空操作？",
            );
            return;
        }
        self.packing_confirmed(action, index);
    }
    pub fn packing_confirmed(&mut self, action: &str, index: i32) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Packing>();
        if action == "refresh" {
            self.request(
                LIST_CONTAINER_PACKING_PROJECTS,
                0,
                vec![],
                None,
                "packing:projects",
            );
            return;
        }
        if action == "load" {
            if let Some(id) = self
                .packing
                .projects
                .get(index.max(0) as usize)
                .and_then(|row| row["id"].as_i64())
            {
                self.request(
                    GET_CONTAINER_PACKING_PROJECT,
                    id,
                    vec![],
                    None,
                    "packing:loaded",
                );
            }
            return;
        }
        if action == "delete" {
            if !self.can(DELETE_CONTAINER_PACKING_PROJECT) {
                return;
            }
            if let Some(id) = self
                .packing
                .projects
                .get(index.max(0) as usize)
                .and_then(|row| row["id"].as_i64())
            {
                self.packing.removing_id = Some(id);
                self.request(
                    DELETE_CONTAINER_PACKING_PROJECT,
                    id,
                    vec![],
                    None,
                    "packing:deleted",
                );
            }
            return;
        }
        if action == "delete-type" {
            if !self.can(DELETE_CONTAINER_PACKING_CONTAINER_TYPE) {
                return;
            }
            if let Some(row) = self.packing.types.get(index.max(0) as usize) {
                if row["isSystemDefault"] == true {
                    self.error("系统默认柜型不能删除。");
                    return;
                }
                if let Some(id) = row["id"].as_i64() {
                    self.request(
                        DELETE_CONTAINER_PACKING_CONTAINER_TYPE,
                        id,
                        vec![],
                        None,
                        "packing:type-saved",
                    );
                }
            }
            return;
        }
        if action == "pdf" {
            if !self.can(SAVE_CONTAINER_PACKING_PDF_TO_PATH) {
                return;
            }
            let Some(analysis) = &self.packing.analysis else {
                return;
            };
            let name = export_doc_engine::paths::suggested_pdf_name(&view.get_name());
            let Some(path) = self
                .platform
                .choose_destination(ui.window(), &name, &["pdf"])
            else {
                return;
            };
            let body = json!({"projectName":self.packing.form.value["name"],"containerType":self.packing.form.value["containerType"],"container":self.packing.form.value["container"],"analysis":analysis,"destinationPath":path});
            self.request(
                SAVE_CONTAINER_PACKING_PDF_TO_PATH,
                0,
                vec![],
                Some(body),
                "packing:pdf",
            );
            return;
        }
        if !self.can(SAVE_CONTAINER_PACKING_PROJECT) {
            return;
        }
        if action == "analyze" || action == "save" {
            let body = match self.packing.body() {
                Ok(body) => body,
                Err(cause) => {
                    self.packing.form.error = cause;
                    self.sync_packing();
                    return;
                }
            };
            self.packing.auto_due = None;
            self.request(
                if action == "analyze" {
                    ANALYZE_CONTAINER_PACKING
                } else {
                    SAVE_CONTAINER_PACKING_PROJECT
                },
                0,
                vec![],
                Some(body),
                if action == "analyze" {
                    "packing:analysis"
                } else {
                    "packing:saved"
                },
            );
            return;
        }
        if action == "save-type" {
            if !self.can(SAVE_CONTAINER_PACKING_CONTAINER_TYPE) {
                return;
            }
            if let Err(cause) = self.packing.form.validate() {
                self.packing.form.error = cause;
                self.sync_packing();
                return;
            }
            let c = &self.packing.form.value["container"];
            let type_id = self
                .packing
                .types
                .iter()
                .find(|row| row["name"] == self.packing.form.value["containerType"])
                .and_then(|row| row["id"].as_i64())
                .unwrap_or(0);
            let body = json!({"id":type_id,"name":self.packing.form.value["containerType"],"length":c["length"],"width":c["width"],"height":c["height"],"maxVolume":c["volume"],"maxWeight":c["maxWeight"]});
            self.request(
                SAVE_CONTAINER_PACKING_CONTAINER_TYPE,
                0,
                vec![],
                Some(body),
                "packing:type-saved",
            );
            return;
        }
        let before = self.packing.snapshot();
        match action {
            "new" => self.packing.reset(),
            "undo" | "redo" => self.packing.undo(action == "redo"),
            "type" => {
                if let Some(row) = self.packing.types.get(index.max(0) as usize) {
                    self.packing.form.value["containerType"] = row["name"].clone();
                    self.packing.form.value["container"] = json!({"length":row["length"],"width":row["width"],"height":row["height"],"volume":row["maxVolume"],"maxWeight":row["maxWeight"]});
                    self.packing
                        .form
                        .buffers
                        .retain(|key, _| !key.starts_with("container.") && key != "containerType");
                }
            }
            "add" | "duplicate" => {
                if self.packing.form.value["cargoItems"]
                    .as_array()
                    .is_some_and(|r| r.len() >= 200)
                {
                    self.error("最多 200 行货物。");
                    return;
                }
                if action == "duplicate" {
                    if let Err(cause) = self.packing.form.validate() {
                        self.error(cause);
                        return;
                    }
                }
                let row = if action == "duplicate" {
                    self.packing.form.value["cargoItems"]
                        .get(index.max(0) as usize)
                        .cloned()
                        .unwrap_or_else(crate::packing_model::cargo)
                } else {
                    crate::packing_model::cargo()
                };
                if let Some(rows) = self.packing.form.value["cargoItems"].as_array_mut() {
                    rows.push(row);
                }
                view.set_tab(0);
            }
            "remove" => self.packing.remove_cargo(index.max(0) as usize),
            "clear" => {
                self.packing.form.value["cargoItems"] = json!([]);
                self.packing
                    .form
                    .buffers
                    .retain(|key, _| !key.starts_with("cargoItems."));
            }
            "color" => {
                if let Some(row) =
                    self.packing.form.value["cargoItems"].get_mut(index.max(0) as usize)
                {
                    let colors = [
                        0xff4287f5_u32,
                        0xff3aa981,
                        0xffe7a845,
                        0xffa477cb,
                        0xffda7265,
                    ];
                    let color = row["colorArgb"].as_i64().unwrap_or(0) as u32;
                    let next =
                        (colors.iter().position(|c| *c == color).unwrap_or(0) + 1) % colors.len();
                    row["colorArgb"] = json!(colors[next] as i32);
                }
            }
            _ => return,
        }
        if !["undo", "redo", "new"].contains(&action) {
            self.packing.change(before, None);
        }
        self.sync_packing();
    }
    pub fn packing_loaded(&mut self, reply: &str, value: Value) {
        match reply {
            "packing:types" => {
                self.packing.types = value["containerTypes"]
                    .as_array()
                    .cloned()
                    .unwrap_or_default();
                self.sync_packing();
                self.request(
                    LIST_CONTAINER_PACKING_PROJECTS,
                    0,
                    vec![],
                    None,
                    "packing:projects",
                );
                return;
            }
            "packing:projects" => {
                self.packing.projects = value["projects"].as_array().cloned().unwrap_or_default()
            }
            "packing:loaded" | "packing:saved" => {
                self.packing.open(value["project"].clone());
                self.sync_packing();
                self.request(
                    LIST_CONTAINER_PACKING_PROJECTS,
                    0,
                    vec![],
                    None,
                    "packing:projects",
                );
                return;
            }
            "packing:analysis" => match serde_json::from_value(value["analysis"].clone()) {
                Ok(analysis) => {
                    self.packing.analysis = Some(analysis);
                    self.packing.view_due = Some(Instant::now());
                    if let Some(ui) = self.ui.upgrade() {
                        ui.global::<Packing>().set_tab(1);
                    }
                }
                Err(cause) => self.error(cause.to_string()),
            },
            "packing:view" => {
                if let Some(svg) = value.as_str() {
                    match slint::Image::load_from_svg_data(svg.as_bytes()) {
                        Ok(image) => {
                            if let Some(ui) = self.ui.upgrade() {
                                ui.global::<Packing>().set_preview(image);
                            }
                        }
                        Err(cause) => self.error(cause.to_string()),
                    }
                }
                return;
            }
            "packing:deleted" => {
                if self.packing.removing_id.take() == Some(self.packing.id()) {
                    self.packing.reset();
                }
                self.request(
                    LIST_CONTAINER_PACKING_PROJECTS,
                    0,
                    vec![],
                    None,
                    "packing:projects",
                );
            }
            "packing:type-saved" => self.request(
                LIST_CONTAINER_PACKING_CONTAINER_TYPES,
                0,
                vec![],
                None,
                "packing:types",
            ),
            "packing:pdf" => self.status(value["message"].as_str().unwrap_or("PDF 已保存")),
            _ => {}
        }
        self.sync_packing();
    }
    pub fn poll_packing(&mut self) {
        if self.task.is_some() || self.workspace.root != "container-projects" {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Packing>();
        if self
            .packing
            .auto_due
            .is_some_and(|due| Instant::now() >= due)
        {
            self.packing.auto_due = None;
            if view.get_auto_refresh() && self.packing.body().is_ok() {
                self.packing_confirmed("analyze", 0);
                return;
            }
        }
        if self
            .packing
            .view_due
            .is_some_and(|due| Instant::now() >= due)
        {
            self.packing.view_due = None;
            if let Some(analysis) = self
                .packing
                .analysis
                .as_ref()
                .filter(|a| !a.packed_items.is_empty())
            {
                let container =
                    match serde_json::from_value(self.packing.form.value["container"].clone()) {
                        Ok(c) => c,
                        Err(e) => {
                            self.error(e.to_string());
                            return;
                        }
                    };
                self.start(Work::PackingView {
                    container,
                    analysis: Box::new(analysis.clone()),
                    projection: view.get_projection(),
                    angle: view.get_angle() as f64,
                    filled: view.get_filled(),
                    selected: view.get_selected_block() - 1,
                });
            }
        }
    }
}
