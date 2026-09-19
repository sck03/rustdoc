mod canvas;
mod designer_ui;
mod forms;
mod invoice_fields;
mod invoice_ui;
mod labels;
mod navigation_ui;
mod pdf_ui;
mod smoke;
mod theme;
mod worker;
mod workspace_ui;

use eframe::egui::{self, Color32, RichText};
use export_doc_native::{
    api::ApiClient,
    designer::{Design, Field},
    generated_api::*,
    history::History,
    invoice::InvoiceDraft,
    paths::RuntimePaths,
    template,
};
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::Arc,
};
use worker::{Event, Task, Work};

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum View {
    Invoice,
    Items,
    Designer,
    Pdf,
    Workspace(&'static str),
}
impl View {
    pub fn title(self) -> &'static str {
        match self {
            Self::Invoice => "发票编辑",
            Self::Items => "商品明细",
            Self::Designer => "报表设计",
            Self::Pdf => "PDF 输出",
            Self::Workspace(key) => export_doc_native::workspace::navigation(key)
                .map(|(_, item)| item.label)
                .unwrap_or("业务资料"),
        }
    }
}
pub struct Desktop {
    client: ApiClient,
    paths: RuntimePaths,
    user: Option<ApiUserDto>,
    username: String,
    password: String,
    view: View,
    task: Option<Task>,
    status: String,
    progress: f32,
    error: Option<String>,
    invoice: InvoiceDraft,
    invoice_checkpoint: InvoiceDraft,
    invoice_saved: InvoiceDraft,
    invoice_history: History<InvoiceDraft>,
    invoice_dirty: bool,
    design: Design,
    design_checkpoint: Design,
    design_saved: Option<Design>,
    design_history: History<Design>,
    design_dirty: bool,
    template_name: String,
    template_record: Option<ApiUserReportTemplateDto>,
    fields: Vec<Field>,
    templates: Vec<ApiReportTemplateDto>,
    active_cell: Option<(usize, usize)>,
    focus_cell: Option<(usize, usize)>,
    show_spares: bool,
    invoice_tab: usize,
    selected: BTreeSet<String>,
    active_layer: usize,
    field_filter: String,
    zoom: Option<f32>,
    drag: Option<canvas::Drag>,
    pdf_bytes: Option<Arc<Vec<u8>>>,
    pdf_texture: Option<egui::TextureHandle>,
    pdf_page_count: u32,
    pdf_page: u32,
    pdf_template: String,
    pdf_saved_path: Option<std::path::PathBuf>,
    show_records: bool,
    records: Option<ApiPagedResponseOfApiInvoiceListItemDto>,
    record_search: String,
    record_page: i64,
    replace: Option<Replace>,
    close_requested: bool,
    allow_close: bool,
    ime_composing: bool,
    pub probes: BTreeMap<String, egui::Rect>,
    smoke: Option<smoke::Smoke>,
    invoice_field_buffers: BTreeMap<String, String>,
    invoice_fields_error: Option<String>,
    item_editor: Option<(usize, export_doc_native::workspace::Editor)>,
    workspace: export_doc_native::workspace::Workspace,
    navigation_search: String,
    expanded_navigation: BTreeSet<&'static str>,
    navigation_collapsed: bool,
    pending_navigation: Option<&'static str>,
    compact: bool,
}
enum Replace {
    Blank,
    Demo,
    Load(i64),
}
impl Desktop {
    pub fn new(client: ApiClient, paths: RuntimePaths) -> Self {
        let invoice = InvoiceDraft::new("");
        let design = Design::invoice();
        Self {
            client,
            paths,
            user: None,
            username: "admin".into(),
            password: String::new(),
            view: View::Invoice,
            task: None,
            status: "后端已就绪".into(),
            progress: 0.,
            error: None,
            invoice: invoice.clone(),
            invoice_checkpoint: invoice.clone(),
            invoice_saved: invoice,
            invoice_history: History::new(40),
            invoice_dirty: false,
            design: design.clone(),
            design_checkpoint: design,
            design_saved: None,
            design_history: History::new(60),
            design_dirty: true,
            template_name: "原生发票模板".into(),
            template_record: None,
            fields: vec![],
            templates: vec![],
            active_cell: None,
            focus_cell: None,
            show_spares: false,
            invoice_tab: 0,
            selected: BTreeSet::new(),
            active_layer: 0,
            field_filter: String::new(),
            zoom: None,
            drag: None,
            pdf_bytes: None,
            pdf_texture: None,
            pdf_page_count: 0,
            pdf_page: 0,
            pdf_template: String::new(),
            pdf_saved_path: None,
            show_records: false,
            records: None,
            record_search: String::new(),
            record_page: 1,
            replace: None,
            close_requested: false,
            allow_close: false,
            ime_composing: false,
            probes: BTreeMap::new(),
            smoke: None,
            invoice_field_buffers: BTreeMap::new(),
            invoice_fields_error: None,
            item_editor: None,
            workspace: Default::default(),
            navigation_search: String::new(),
            expanded_navigation: BTreeSet::from(["documents"]),
            navigation_collapsed: false,
            pending_navigation: None,
            compact: false,
        }
    }
    fn busy(&self) -> bool {
        self.task.is_some()
    }
    fn probe(&mut self, name: impl Into<String>, response: &egui::Response) {
        self.probes.insert(name.into(), response.rect);
    }
    fn start(&mut self, ctx: &egui::Context, work: Work, label: &str) {
        if self.busy() {
            return;
        }
        self.error = None;
        self.status = label.into();
        self.progress = 0.;
        self.task = Some(Task::spawn(
            self.client.clone(),
            work,
            ctx.clone(),
            self.paths.pdfium_path(),
        ));
    }
    fn poll(&mut self, ctx: &egui::Context) {
        let events: Vec<_> = self
            .task
            .as_ref()
            .map(|task| task.events.try_iter().collect())
            .unwrap_or_default();
        for event in events {
            match event {
                Event::Login {
                    client,
                    user,
                    fields,
                    templates,
                } => {
                    self.client = client;
                    self.invoice = InvoiceDraft::new(&user.business_date);
                    self.invoice_saved = self.invoice.clone();
                    self.invoice_checkpoint = self.invoice.clone();
                    self.user = Some(user);
                    self.fields = fields;
                    self.templates = templates;
                    self.status = "已连接本机数据".into();
                }
                Event::Invoices(page) => self.records = Some(page),
                Event::Invoice(invoice) => {
                    self.invoice_field_buffers.clear();
                    self.invoice_fields_error = None;
                    self.invoice = InvoiceDraft::from_dto(*invoice);
                    self.invoice_checkpoint = self.invoice.clone();
                    self.invoice_saved = self.invoice.clone();
                    self.invoice_history.clear();
                    self.invoice_dirty = false;
                    self.status = "发票已读取并保存草稿基线".into();
                }
                Event::Template(record) => {
                    match Design::from_html(&record.content_html) {
                        Ok(design) => {
                            self.design = design.clone();
                            self.design_checkpoint = design.clone();
                            self.design_saved = Some(design);
                            self.design_dirty = false;
                            self.design_history.clear();
                            self.template_name = record.name.clone();
                        }
                        Err(error) => self.error = Some(error),
                    }
                    self.template_record = Some(record);
                }
                Event::Pdf(bytes) => {
                    self.pdf_bytes = Some(bytes);
                    self.pdf_texture = None;
                    self.pdf_page = 0;
                    self.pdf_saved_path = None;
                }
                Event::Page(page) => {
                    match image::load_from_memory_with_format(&page.png, image::ImageFormat::Png) {
                        Ok(image) => {
                            let rgba = image.to_rgba8();
                            let image = egui::ColorImage::from_rgba_unmultiplied(
                                [page.width as usize, page.height as usize],
                                rgba.as_raw(),
                            );
                            self.pdf_texture = Some(ctx.load_texture(
                                "pdf-page",
                                image,
                                egui::TextureOptions::LINEAR,
                            ));
                            self.pdf_page_count = page.page_count;
                            self.pdf_page = page.page_index;
                        }
                        Err(error) => self.error = Some(format!("PDF 图片读取失败：{error}")),
                    }
                }
                Event::PreviewError(error) => self.error = Some(error),
                Event::Data { value, reply } => {
                    use worker::Reply;
                    match reply {
                        Reply::Data(key) if self.workspace.resource == key => {
                            self.workspace.payload = Some(value)
                        }
                        Reply::Saved(key) | Reply::Action(key)
                            if self.workspace.resource == key =>
                        {
                            self.workspace.editor = None;
                            self.workspace.action = None;
                            self.workspace.loaded = false;
                            self.workspace.lookup_loaded = false;
                        }
                        Reply::Edit(key) if self.workspace.resource == key => {
                            if let Err(error) = self.workspace.edit(Some(value)) {
                                self.error = Some(error);
                            }
                            self.workspace.lookup_loaded = false;
                        }
                        _ => {}
                    }
                }
                Event::Lookups(lookups) => {
                    self.workspace.lookups = lookups;
                    self.workspace.lookup_loaded = true;
                }
                Event::Progress(status, progress) => {
                    self.status = status;
                    self.progress = progress;
                }
                Event::Done(result) => {
                    self.task = None;
                    self.progress = 0.;
                    match result {
                        Ok(()) => self.status = "操作完成".into(),
                        Err(error) => {
                            self.status = "操作未完成，草稿已保留".into();
                            self.error = Some(error);
                        }
                    }
                }
            }
        }
    }
    fn invoice_changed(&mut self, ctx: &egui::Context) {
        let group = ctx
            .memory(|memory| memory.focused())
            .map(|id| format!("{id:?}"));
        self.invoice_history.record(
            std::mem::replace(&mut self.invoice_checkpoint, self.invoice.clone()),
            &self.invoice,
            group,
        );
        self.invoice_dirty = self.invoice != self.invoice_saved;
    }
    fn design_changed(&mut self, group: Option<String>) {
        self.design_history.record(
            std::mem::replace(&mut self.design_checkpoint, self.design.clone()),
            &self.design,
            group,
        );
        self.design_dirty = self.design_saved.as_ref() != Some(&self.design);
    }
    fn save_invoice(&mut self, ctx: &egui::Context) {
        if let Some(error) = &self.invoice_fields_error {
            self.error = Some(error.clone());
            return;
        }
        match self.invoice.build() {
            Ok(invoice) => self.start(ctx, Work::SaveInvoice(Box::new(invoice)), "正在保存发票"),
            Err(error) => {
                self.error = Some(error);
            }
        }
    }
    fn save_design(&mut self, ctx: &egui::Context) {
        if self.template_name.trim().is_empty() {
            self.error = Some("请填写模板名称。".into());
            return;
        }
        match template::export(&self.design, &self.fields) {
            Ok(html) => self.start(
                ctx,
                Work::SaveTemplate {
                    name: self.template_name.clone(),
                    html,
                    previous: self.template_record.clone(),
                },
                "正在保存并应用模板",
            ),
            Err(error) => self.error = Some(error),
        }
    }
    fn apply_replace(&mut self, replace: Replace, ctx: &egui::Context) {
        let date = self
            .user
            .as_ref()
            .map(|user| user.business_date.as_str())
            .unwrap_or("");
        self.invoice = match replace {
            Replace::Blank => InvoiceDraft::new(date),
            Replace::Demo => {
                let suffix = export_doc_native::paths::nonce().unwrap_or_else(|_| "sample".into());
                InvoiceDraft::demo(date, &format!("NATIVE-{}", &suffix[..6]))
            }
            Replace::Load(id) => {
                self.start(ctx, Work::LoadInvoice(id), "正在读取发票");
                return;
            }
        };
        self.invoice_field_buffers.clear();
        self.invoice_fields_error = None;
        self.invoice_history.clear();
        self.invoice_checkpoint = self.invoice.clone();
        self.invoice_dirty = matches!(replace, Replace::Demo);
        self.invoice_saved = InvoiceDraft::new(date);
        self.active_cell = None;
    }
    fn request_replace(&mut self, replace: Replace, ctx: &egui::Context) {
        if self.invoice_dirty {
            self.replace = Some(replace);
        } else {
            self.apply_replace(replace, ctx);
        }
    }
    fn login_ui(&mut self, ui: &mut egui::Ui) {
        ui.add_space(45.);
        ui.horizontal(|ui| {
            ui.add_space((ui.available_width() - 480.).max(0.) / 2.);
            ui.vertical(|ui| {
                ui.set_width(440.);
                theme::card().show(ui, |ui| {
                    theme::title(ui, "登录系统");
                    ui.label(RichText::new("选择账号后进入业务工作区。").color(theme::MUTED));
                    ui.add_space(12.);
                    theme::field(ui, "账号", &mut self.username, "username");
                    ui.label("密码");
                    ui.add_sized(
                        [ui.available_width(), 34.],
                        egui::TextEdit::singleline(&mut self.password)
                            .password(true)
                            .id_salt("password"),
                    );
                    ui.label(
                        RichText::new("首次登录账号为 admin，密码留空。")
                            .size(12.)
                            .color(theme::MUTED),
                    );
                    ui.add_space(12.);
                    let login = ui.add_enabled(
                        !self.busy(),
                        theme::primary("登录").min_size(egui::vec2(ui.available_width(), 40.)),
                    );
                    self.probe("login", &login);
                    if login.clicked() {
                        let password = std::mem::take(&mut self.password);
                        self.start(
                            ui.ctx(),
                            Work::Login(self.username.clone(), password),
                            "正在登录",
                        );
                    }
                });
            });
        });
    }
    fn dialogs(&mut self, ctx: &egui::Context) {
        if self.replace.is_some() {
            egui::Window::new("保留未保存的发票")
                .collapsible(false)
                .resizable(false)
                .anchor(egui::Align2::CENTER_CENTER, [0., 0.])
                .show(ctx, |ui| {
                    ui.label("当前发票有未保存修改。继续会替换本地草稿。");
                    ui.horizontal(|ui| {
                        if ui.button("保留草稿，返回编辑").clicked() {
                            self.replace = None;
                        }
                        if ui.button("放弃修改并继续").clicked() {
                            if let Some(replace) = self.replace.take() {
                                self.apply_replace(replace, ctx);
                            }
                        }
                    });
                });
        }
        if self.close_requested {
            egui::Window::new("退出系统")
                .collapsible(false)
                .resizable(false)
                .anchor(egui::Align2::CENTER_CENTER, [0., 0.])
                .show(ctx, |ui| {
                    if self.busy() {
                        ui.label("当前操作仍在执行，请完成或取消后再退出。");
                        if let Some(task) = &self.task {
                            if task.cancellable && ui.button("取消 PDF 任务").clicked() {
                                task.cancel();
                            }
                        }
                    } else {
                        ui.label("未保存的发票或报表修改将被放弃。");
                        if ui.button("放弃未保存修改并退出").clicked() {
                            self.allow_close = true;
                            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                        }
                    }
                    if ui.button("返回工作区").clicked() {
                        self.close_requested = false;
                    }
                });
        }
        self.records_ui(ctx);
        self.workspace_dialogs(ctx);
        self.item_dialog(ctx);
    }
}
impl eframe::App for Desktop {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        self.poll(&ctx);
        self.probes.clear();
        if self.smoke.is_some() {
            ctx.request_repaint_after(std::time::Duration::from_millis(30));
        }
        if ctx.input(|input| input.viewport().close_requested())
            && !self.allow_close
            && (self.invoice_dirty
                || self.design_dirty
                || self.busy()
                || self
                    .workspace
                    .editor
                    .as_ref()
                    .is_some_and(|editor| editor.record != editor.baseline))
        {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            self.close_requested = true;
        }
        if !self.ime_composing
            && !self.busy()
            && self.user.is_some()
            && ctx.input_mut(|input| input.consume_key(egui::Modifiers::COMMAND, egui::Key::S))
        {
            if self.view == View::Designer {
                self.save_design(&ctx);
            } else if matches!(self.view, View::Workspace(_)) {
                self.save_workspace(&ctx);
            } else {
                self.save_invoice(&ctx);
            }
        }
        self.sidebar(ui);
        self.header(ui);
        egui::Panel::bottom("status")
            .exact_size(32.)
            .frame(
                egui::Frame::new()
                    .fill(Color32::WHITE)
                    .inner_margin(egui::Margin::symmetric(18, 6)),
            )
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    if self.busy() {
                        ui.spinner();
                    }
                    ui.label(RichText::new(&self.status).size(12.).color(theme::MUTED));
                    if self.busy() && self.progress > 0. {
                        ui.add(egui::ProgressBar::new(self.progress).desired_width(180.));
                    }
                    if let Some(task) = &self.task {
                        if task.cancellable && ui.small_button("取消").clicked() {
                            task.cancel();
                        }
                    }
                });
            });
        egui::CentralPanel::default()
            .frame(egui::Frame::new().fill(theme::BACKGROUND).inner_margin(26))
            .show(ui, |ui| {
                if let Some(error) = self.error.clone() {
                    egui::Frame::new()
                        .fill(Color32::from_rgb(255, 243, 224))
                        .corner_radius(6)
                        .inner_margin(10)
                        .show(ui, |ui| {
                            ui.horizontal_wrapped(|ui| {
                                ui.label(
                                    RichText::new(error).color(Color32::from_rgb(130, 70, 10)),
                                );
                                if ui.small_button("关闭提示").clicked() {
                                    self.error = None;
                                }
                            });
                        });
                    ui.add_space(8.);
                }
                if self.user.is_none() {
                    self.login_ui(ui);
                } else {
                    match self.view {
                        View::Invoice => self.invoice_ui(ui),
                        View::Items => self.items_ui(ui),
                        View::Designer => self.designer_ui(ui),
                        View::Pdf => self.pdf_ui(ui),
                        View::Workspace(_) => self.workspace_ui(ui),
                    }
                }
            });
        self.dialogs(&ctx);
    }
    fn raw_input_hook(&mut self, ctx: &egui::Context, input: &mut egui::RawInput) {
        if let Some(mut smoke) = self.smoke.take() {
            smoke.advance(self, ctx, input);
            self.smoke = Some(smoke);
        }
        for event in &input.events {
            if let egui::Event::Ime(event) = event {
                match event {
                    egui::ImeEvent::Preedit { text, .. } => self.ime_composing = !text.is_empty(),
                    egui::ImeEvent::Commit(_) => self.ime_composing = false,
                    _ => {}
                }
            }
        }
    }
}

pub fn run(
    client: ApiClient,
    paths: RuntimePaths,
    smoke_output: Option<std::path::PathBuf>,
) -> Result<(), String> {
    if let Some(output) = &smoke_output {
        export_doc_native::paths::ensure_safe_absolute(output)?;
        std::fs::create_dir_all(output).map_err(|error| error.to_string())?;
    }
    let result = Arc::new(std::sync::Mutex::new(None));
    let test_result = result.clone();
    let testing = smoke_output.is_some();
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("外贸业务综合管理系统")
            .with_inner_size([1366., 768.])
            .with_min_inner_size([1000., 680.]),
        renderer: eframe::Renderer::Glow,
        persist_window: false,
        centered: true,
        ..Default::default()
    };
    eframe::run_native(
        "ExportDocManager.Native",
        options,
        Box::new(move |context| {
            theme::configure(&context.egui_ctx, &paths.font_path)?;
            let mut app = Desktop::new(client, paths);
            if let Some(output) = smoke_output {
                app.smoke = Some(smoke::Smoke::new(output, test_result));
            }
            Ok(Box::new(app))
        }),
    )
    .map_err(|error| error.to_string())?;
    if testing {
        result
            .lock()
            .unwrap()
            .take()
            .unwrap_or_else(|| Err("原生 UI 验证未完成。".into()))
    } else {
        Ok(())
    }
}
