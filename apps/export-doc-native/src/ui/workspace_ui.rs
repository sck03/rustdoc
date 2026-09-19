use super::{
    Desktop, Replace, View, forms, theme,
    worker::{Reply, Work},
};
use eframe::egui::{self, RichText};
use egui_extras::{Column, TableBuilder};
use export_doc_native::{
    contracts,
    engine::catalog,
    workspace::{self, ActionEditor, display},
};
use serde_json::{Value, json};

impl Desktop {
    pub(super) fn navigate(&mut self, key: &'static str, ctx: &egui::Context) {
        if self
            .workspace
            .editor
            .as_ref()
            .is_some_and(|editor| editor.record != editor.baseline)
        {
            self.pending_navigation = Some(key);
            return;
        }
        self.workspace.open(key);
        self.view = View::Workspace(key);
        if let Some((group, _)) = workspace::navigation(key) {
            self.expanded_navigation.insert(group.key);
        }
        self.refresh_workspace(ctx);
    }
    pub(super) fn refresh_workspace(&mut self, ctx: &egui::Context) {
        if self.busy() {
            return;
        }
        let key = self.workspace.resource;
        if let Some(operation) = workspace::read_operation(key) {
            if !self.client.supports(operation) {
                self.workspace.loaded = true;
                return;
            }
            self.workspace.loaded = true;
            self.start(
                ctx,
                Work::Request {
                    operation,
                    parameters: vec![],
                    query: vec![
                        ("pageNumber", self.workspace.page.max(1).to_string()),
                        ("pageSize", "30".into()),
                        ("keyword", self.workspace.search.clone()),
                    ],
                    body: None,
                    reply: Reply::Data(key),
                },
                "正在读取资料",
            );
        } else {
            self.workspace.loaded = true;
        }
    }
    fn open_record(&mut self, record: Value, ctx: &egui::Context) {
        if self.workspace.resource == "invoices" || self.workspace.resource == "query" {
            if let Some(id) = record["id"].as_i64() {
                self.view = View::Invoice;
                self.request_replace(Replace::Load(id), ctx);
            }
            return;
        }
        if self.workspace.resource == "report-templates" {
            if let Some(id) = record["id"].as_i64() {
                self.view = View::Designer;
                self.start(ctx, Work::LoadTemplate(id), "正在读取报表模板");
            }
            return;
        }
        if let Some(resource) = catalog::resource(self.workspace.resource) {
            if let Some(operation) = resource.get {
                self.start(
                    ctx,
                    Work::Request {
                        operation,
                        parameters: vec![("id", record["id"].to_string())],
                        query: vec![],
                        body: None,
                        reply: Reply::Edit(resource.key),
                    },
                    "正在读取详情",
                );
            } else if let Err(error) = self.workspace.edit(Some(record)) {
                self.error = Some(error);
            }
        }
    }
    fn new_record(&mut self, ctx: &egui::Context) {
        if self.workspace.resource == "invoices" {
            self.view = View::Invoice;
            self.request_replace(Replace::Blank, ctx);
            return;
        }
        if self.workspace.resource == "report-templates" {
            self.view = View::Designer;
            return;
        }
        if let Err(error) = self.workspace.edit(None) {
            self.error = Some(error);
            return;
        }
        if !self.busy() {
            self.workspace.lookup_loaded = false;
            self.start(ctx, Work::Lookups, "正在读取选项");
        }
    }
    pub(super) fn workspace_ui(&mut self, ui: &mut egui::Ui) {
        if !self.workspace.loaded && !self.busy() {
            self.refresh_workspace(ui.ctx());
        }
        let root = self.workspace.root;
        let tabs = workspace::tabs(root);
        if !tabs.is_empty() {
            ui.horizontal_wrapped(|ui| {
                for (key, label) in tabs {
                    let response = ui.selectable_label(self.workspace.resource == key, label);
                    self.probe(format!("section-{key}"), &response);
                    if response.clicked() {
                        self.workspace.resource = key;
                        self.workspace.loaded = false;
                        self.workspace.payload = None;
                        self.workspace.page = 1;
                        self.workspace.search.clear();
                    }
                }
            });
            ui.add_space(12.);
        }
        match self.workspace.resource {
            "dashboard" => {
                self.dashboard_ui(ui);
                return;
            }
            "about" => {
                self.about_ui(ui);
                return;
            }
            "settings" => {
                self.settings_ui(ui);
                return;
            }
            _ => {}
        }
        let key = self.workspace.resource;
        if workspace::read_operation(key).is_some_and(|operation| !self.client.supports(operation))
            || workspace::read_operation(key).is_none()
        {
            theme::card().show(ui, |ui| {
                theme::title(
                    ui,
                    workspace::navigation(root)
                        .map(|(_, item)| item.label)
                        .unwrap_or("业务工具"),
                );
                ui.label("此版本暂未提供这项功能。");
                ui.label(
                    RichText::new("已有资料与原程序仍保留在原工作目录中。").color(theme::MUTED),
                );
            });
            return;
        }
        let spec = catalog::resource(key);
        let busy = self.busy();
        theme::card().show(ui, |ui| {
            ui.horizontal(|ui| {
                let response = ui.add(
                    egui::TextEdit::singleline(&mut self.workspace.search)
                        .hint_text("搜索名称、编号或业务资料")
                        .desired_width(260.),
                );
                self.probe("record-search", &response);
                let search = ui.add_enabled(!busy, egui::Button::new("查询"));
                self.probe("record-query", &search);
                if search.clicked() {
                    self.workspace.page = 1;
                    self.workspace.loaded = false;
                }
                if ui.add_enabled(!busy, egui::Button::new("刷新")).clicked() {
                    self.workspace.loaded = false;
                }
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if let Some(spec) = spec {
                        let add = ui.add_enabled(
                            !busy && self.client.supports(spec.create),
                            theme::primary("＋ 新增"),
                        );
                        self.probe("record-new", &add);
                        if add.clicked() {
                            self.new_record(ui.ctx());
                        }
                    }
                });
            });
            ui.add_space(12.);
            let rows = self.workspace.items();
            let columns = spec
                .map(|resource| resource.columns)
                .unwrap_or_else(|| match key {
                    "query" => &[
                        ("invoiceNo", "发票号"),
                        ("invoiceDate", "日期"),
                        ("customerName", "客户"),
                        ("totalAmount", "金额"),
                        ("status", "状态"),
                    ],
                    "directory" => &[
                        ("employeeNumber", "工号"),
                        ("fullName", "姓名"),
                        ("departmentId", "部门"),
                        ("jobTitle", "岗位"),
                        ("workPhone", "电话"),
                        ("workEmail", "邮箱"),
                    ],
                    "worklist" => &[
                        ("title", "事项"),
                        ("source", "来源"),
                        ("status", "状态"),
                        ("nextAction", "下一步"),
                    ],
                    "jobs" => &[
                        ("title", "任务"),
                        ("statusText", "状态"),
                        ("progressPercent", "进度"),
                        ("createdAt", "创建时间"),
                    ],
                    "audit" => &[
                        ("timestamp", "操作时间"),
                        ("module", "业务模块"),
                        ("action", "动作"),
                        ("recordId", "记录编号"),
                        ("description", "说明"),
                    ],
                    _ => &[("name", "名称"), ("status", "状态")],
                });
            let height = (ui.available_height() - 60.).max(160.);
            let mut open = None;
            let mut action = None;
            egui::ScrollArea::horizontal()
                .id_salt(("records-horizontal", key))
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    ui.set_min_width(
                        (columns.len() as f32 * 130. + 160.).max(ui.available_width()),
                    );
                    let mut table = TableBuilder::new(ui)
                        .id_salt(("records-table", key))
                        .striped(true)
                        .resizable(true)
                        .cell_layout(egui::Layout::left_to_right(egui::Align::Center))
                        .column(Column::exact(138.))
                        .max_scroll_height(height)
                        .min_scrolled_height(height);
                    for _ in columns {
                        table = table.column(Column::remainder().at_least(110.).clip(true));
                    }
                    table
                        .header(40., |mut header| {
                            header.col(|ui| {
                                ui.strong("操作");
                            });
                            for (_, label) in columns {
                                header.col(|ui| {
                                    ui.label(RichText::new(*label).color(theme::MUTED).strong());
                                });
                            }
                        })
                        .body(|body| {
                            body.rows(46., rows.len(), |mut row| {
                                let index = row.index();
                                let record = &rows[index];
                                row.col(|ui| {
                                    if key != "audit" && key != "directory" {
                                        let response = ui.add_enabled(
                                            !busy,
                                            egui::Button::new(if key == "worklist" {
                                                "办理"
                                            } else {
                                                "打开"
                                            }),
                                        );
                                        self.probes
                                            .insert(format!("record-open-{index}"), response.rect);
                                        if response.clicked() {
                                            open = Some(record.clone());
                                        }
                                    }
                                    if let Some(spec) = spec {
                                        ui.menu_button("更多", |ui| {
                                            for (operation, label) in
                                                workspace::actions(key, record)
                                            {
                                                if ui
                                                    .add_enabled(
                                                        !busy && self.client.supports(operation),
                                                        egui::Button::new(label),
                                                    )
                                                    .clicked()
                                                {
                                                    action = Some((
                                                        operation,
                                                        label.to_owned(),
                                                        record.clone(),
                                                    ));
                                                    ui.close();
                                                }
                                            }
                                            if let Some(operation) = spec.delete {
                                                ui.separator();
                                                if ui
                                                    .add_enabled(
                                                        !busy,
                                                        egui::Button::new(
                                                            RichText::new("删除")
                                                                .color(theme::ERROR),
                                                        ),
                                                    )
                                                    .clicked()
                                                {
                                                    action = Some((
                                                        operation,
                                                        "删除记录".into(),
                                                        record.clone(),
                                                    ));
                                                    ui.close();
                                                }
                                            }
                                        });
                                    }
                                });
                                for (field, _) in columns {
                                    row.col(|ui| {
                                        let value = record
                                            .get(*field)
                                            .filter(|value| !value.is_null())
                                            .or_else(|| record["employee"].get(*field))
                                            .unwrap_or(&Value::Null);
                                        let text = display(value);
                                        ui.add(egui::Label::new(&text).truncate())
                                            .on_hover_text(text);
                                    });
                                }
                            });
                        });
                });
            if rows.is_empty() && !busy {
                ui.label(RichText::new("暂无记录，可通过“新增”录入资料。").color(theme::MUTED));
            }
            ui.add_space(8.);
            let payload = self.workspace.payload.as_ref();
            let total = payload
                .and_then(|value| value["totalCount"].as_i64())
                .unwrap_or(rows.len() as i64);
            ui.horizontal(|ui| {
                ui.label(
                    RichText::new(format!(
                        "共 {total} 条 · 第 {} 页",
                        self.workspace.page.max(1)
                    ))
                    .size(12.)
                    .color(theme::MUTED),
                );
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui
                        .add_enabled(
                            !busy && payload.is_some_and(|value| value["hasNextPage"] == true),
                            egui::Button::new("下一页"),
                        )
                        .clicked()
                    {
                        self.workspace.page += 1;
                        self.workspace.loaded = false;
                    }
                    if ui
                        .add_enabled(
                            !busy && self.workspace.page > 1,
                            egui::Button::new("上一页"),
                        )
                        .clicked()
                    {
                        self.workspace.page -= 1;
                        self.workspace.loaded = false;
                    }
                });
            });
            if let Some(record) = open {
                if key == "worklist" {
                    let source = record["source"].as_str().unwrap_or("");
                    if let Some(resource) = catalog::resource(source) {
                        self.navigate(resource.key, ui.ctx());
                        self.open_record(record["record"].clone(), ui.ctx());
                    }
                } else {
                    self.open_record(record, ui.ctx());
                }
            }
            if let Some((operation, title, record)) = action {
                let mut request = contracts::object(operation.id, true);
                if !request.is_object() {
                    request = json!({});
                }
                request["expectedVersion"] = record["versionNumber"].clone();
                request["rowVersion"] = record["rowVersion"].clone();
                if request.get("effectiveDate").is_some() {
                    request["effectiveDate"] = json!(chrono::Local::now().date_naive().to_string());
                }
                if request.get("operationId").is_some() {
                    request["operationId"] =
                        json!(export_doc_native::paths::nonce().unwrap_or_default());
                }
                if request.get("quantity").is_some() {
                    request["quantity"] = record.get("quantity").cloned().unwrap_or(json!(1));
                }
                self.workspace.action = Some(ActionEditor {
                    operation,
                    title,
                    request,
                    buffers: Default::default(),
                    record,
                    invalid: None,
                });
            }
        });
    }
    pub(super) fn save_workspace(&mut self, ctx: &egui::Context) {
        let Some(editor) = self.workspace.editor.as_ref() else {
            return;
        };
        if let Some(error) = &editor.invalid {
            self.error = Some(error.clone());
            return;
        }
        let Some(resource) = catalog::resource(self.workspace.resource) else {
            return;
        };
        let operation = if editor.id > 0 {
            resource.update
        } else {
            resource.create
        };
        self.start(
            ctx,
            Work::Request {
                operation,
                parameters: if editor.id > 0 {
                    vec![("id", editor.id.to_string())]
                } else {
                    vec![]
                },
                query: vec![],
                body: Some(editor.record.clone()),
                reply: Reply::Saved(resource.key),
            },
            "正在保存资料",
        );
    }
    pub(super) fn workspace_dialogs(&mut self, ctx: &egui::Context) {
        let editor = if self.workspace.resource != "settings" {
            self.workspace.editor.take()
        } else {
            None
        };
        if let Some(mut editor) = editor {
            let title = format!(
                "{}{}",
                if editor.id > 0 { "编辑" } else { "新增" },
                catalog::resource(self.workspace.resource)
                    .map(|resource| resource.title)
                    .unwrap_or("资料")
            );
            let mut save = false;
            let mut close = false;
            egui::Window::new(title)
                .id(egui::Id::new("record-editor"))
                .collapsible(false)
                .resizable(true)
                .default_width(850.)
                .max_height(ctx.content_rect().height() - 80.)
                .anchor(egui::Align2::CENTER_CENTER, [0., 0.])
                .show(ctx, |ui| {
                    ui.add_enabled_ui(!self.busy(), |ui| {
                        egui::ScrollArea::vertical()
                            .id_salt("record-fields")
                            .max_height((ctx.content_rect().height() - 240.).max(200.))
                            .show(ui, |ui| {
                                editor.invalid = forms::form(
                                    ui,
                                    editor.schema,
                                    &mut editor.record,
                                    &mut editor.buffers,
                                    &self.workspace.lookups,
                                    "record",
                                    &mut self.probes,
                                );
                            });
                        ui.separator();
                        if let Some(error) = &editor.invalid {
                            ui.colored_label(theme::ERROR, error);
                        }
                        ui.horizontal(|ui| {
                            let response = ui.add(theme::primary("保存"));
                            self.probe("record-save", &response);
                            save = response.clicked();
                            let cancel = ui.button(if editor.record == editor.baseline {
                                "关闭"
                            } else {
                                "放弃修改并关闭"
                            });
                            self.probe("record-cancel", &cancel);
                            close = cancel.clicked();
                        });
                    });
                });
            if !close {
                self.workspace.editor = Some(editor);
            }
            if save {
                self.save_workspace(ctx);
            }
        }
        if let Some(mut action) = self.workspace.action.take() {
            let mut confirm = false;
            let mut cancel = false;
            egui::Window::new(&action.title)
                .id(egui::Id::new("record-action"))
                .collapsible(false)
                .default_width(650.)
                .anchor(egui::Align2::CENTER_CENTER, [0., 0.])
                .show(ctx, |ui| {
                    if action.operation.method == "DELETE" {
                        ui.label("删除后无法恢复此记录。已有业务引用的资料会保留。");
                    }
                    ui.add_enabled_ui(!self.busy(), |ui| {
                        action.invalid = forms::form(
                            ui,
                            contracts::request(action.operation.id),
                            &mut action.request,
                            &mut action.buffers,
                            &self.workspace.lookups,
                            "action",
                            &mut self.probes,
                        );
                        ui.add_space(12.);
                        ui.horizontal(|ui| {
                            confirm = ui.add(theme::primary("确认办理")).clicked();
                            cancel = ui.button("返回").clicked();
                        });
                    });
                });
            if confirm && action.invalid.is_none() {
                let parameters = vec![("id", action.record["id"].to_string())];
                let query = if action.operation.method == "DELETE" {
                    vec![
                        (
                            "rowVersion",
                            action.record["rowVersion"]
                                .as_str()
                                .unwrap_or("")
                                .to_owned(),
                        ),
                        (
                            "expectedVersion",
                            action.record["versionNumber"].to_string(),
                        ),
                    ]
                } else {
                    vec![]
                };
                self.start(
                    ctx,
                    Work::Request {
                        operation: action.operation,
                        parameters,
                        query,
                        body: Some(action.request.clone()),
                        reply: Reply::Action(self.workspace.resource),
                    },
                    "正在办理",
                );
            }
            if !cancel {
                self.workspace.action = Some(action);
            }
        }
        if let Some(key) = self.pending_navigation {
            egui::Window::new("保留未保存的资料")
                .collapsible(false)
                .anchor(egui::Align2::CENTER_CENTER, [0., 0.])
                .show(ctx, |ui| {
                    ui.label("当前资料有未保存修改。请先保存，或确认放弃修改。");
                    ui.horizontal(|ui| {
                        if ui.button("返回编辑").clicked() {
                            self.pending_navigation = None;
                        }
                        if ui.button("放弃修改并继续").clicked() {
                            self.workspace.editor = None;
                            self.pending_navigation = None;
                            self.navigate(key, ctx);
                        }
                    });
                });
        }
    }
    fn dashboard_ui(&mut self, ui: &mut egui::Ui) {
        let value = self.workspace.payload.clone().unwrap_or(json!({}));
        ui.columns(4, |columns| {
            for (index, (field, label)) in [
                ("monthlyExportAmount", "本月单据金额"),
                ("monthlyInvoiceCount", "本月发票"),
                ("draftCount", "待核对"),
                ("shippedCount", "已出运"),
            ]
            .iter()
            .enumerate()
            {
                theme::card().show(&mut columns[index], |ui| {
                    ui.label(RichText::new(*label).color(theme::MUTED));
                    ui.label(RichText::new(display(&value[*field])).size(28.).strong());
                });
            }
        });
        ui.add_space(18.);
        theme::card().show(ui, |ui| {
            theme::title(ui, "近期发票");
            if let Some(records) = value["recentInvoices"].as_array() {
                for record in records {
                    ui.horizontal(|ui| {
                        if ui.link(display(&record["invoiceNo"])).clicked() {
                            self.view = View::Invoice;
                            self.request_replace(
                                Replace::Load(record["id"].as_i64().unwrap_or(0)),
                                ui.ctx(),
                            );
                        }
                        ui.label(display(&record["customerNameEN"]));
                        ui.label(display(&record["invoiceDate"]));
                        ui.label(display(&record["status"]));
                    });
                    ui.separator();
                }
            }
        });
    }
    fn about_ui(&mut self, ui: &mut egui::Ui) {
        theme::card().show(ui, |ui| {
            theme::title(ui, "外贸业务综合管理系统");
            ui.label("Rust 原生桌面 · 开发分支");
            ui.add_space(12.);
            ui.label("业务资料保存到当前程序的数据目录。此版本处于完整功能迁移和验收阶段。");
            ui.label(format!("程序目录：{}", self.paths.app_root.display()));
            ui.label(format!("数据目录：{}", self.paths.data_root.display()));
            ui.add_space(12.);
            ui.label("界面和业务操作使用原生程序处理，无需 WebView2。");
        });
    }
    fn settings_ui(&mut self, ui: &mut egui::Ui) {
        if self.workspace.editor.is_none() {
            if let Some(value) = self.workspace.payload.as_ref() {
                let value = value["settings"].clone();
                self.workspace.editor = Some(export_doc_native::workspace::Editor {
                    record: value.clone(),
                    baseline: value,
                    buffers: Default::default(),
                    schema: contracts::schema("AppSettings"),
                    id: 0,
                    invalid: None,
                });
            }
        }
        if let Some(mut editor) = self.workspace.editor.take() {
            theme::card().show(ui, |ui| {
                theme::title(ui, "系统设置");
                egui::ScrollArea::vertical()
                    .id_salt("settings-fields")
                    .max_height((ui.available_height() - 60.).max(180.))
                    .show(ui, |ui| {
                        editor.invalid = forms::form(
                            ui,
                            editor.schema,
                            &mut editor.record,
                            &mut editor.buffers,
                            &self.workspace.lookups,
                            "settings",
                            &mut self.probes,
                        );
                    });
                if ui
                    .add_enabled(
                        !self.busy() && editor.invalid.is_none(),
                        theme::primary("保存设置"),
                    )
                    .clicked()
                {
                    self.start(
                        ui.ctx(),
                        Work::Request {
                            operation: export_doc_native::generated_api::UPDATE_SETTINGS,
                            parameters: vec![],
                            query: vec![],
                            body: Some(editor.record.clone()),
                            reply: Reply::Saved("settings"),
                        },
                        "正在保存设置",
                    );
                }
            });
            self.workspace.editor = Some(editor);
        }
    }
}
