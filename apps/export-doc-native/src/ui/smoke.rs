use super::Desktop;
use eframe::egui::{self, Event, Key, Modifiers, PointerButton, Pos2};
use export_doc_native::paths::{atomic_write, save_pdf};
use serde_json::json;
use std::{
    collections::VecDeque,
    path::PathBuf,
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

#[derive(Debug)]
enum Action {
    Click(&'static str),
    Events(Vec<Event>),
    Idle,
    Wait(Condition),
    Capture(&'static str),
    Drag,
    CheckDrag,
    UndoCheck,
    Scale(f32),
    Finish,
}
#[derive(Debug)]
enum Condition {
    Login,
    Sample,
    SavedInvoice,
    Template,
    Pdf,
    Ready,
}
pub(super) struct Smoke {
    actions: VecDeque<Action>,
    deadline: Instant,
    capture_pending: bool,
    output: PathBuf,
    pub result: Arc<Mutex<Option<Result<(), String>>>>,
    before_drag: i32,
    screenshots: Vec<String>,
    steps: usize,
    last_action: String,
    input_clock: f64,
}
impl Smoke {
    pub fn new(output: PathBuf, result: Arc<Mutex<Option<Result<(), String>>>>) -> Self {
        let mut actions=VecDeque::from([
            Action::Click("login"),Action::Wait(Condition::Login),Action::Click("demo"),Action::Wait(Condition::Sample),
            Action::Click("invoice-no"),Action::Events(vec![key(Key::A,Modifiers::COMMAND),Event::Text("NATIVE-UI-验证".into())]),
            Action::Click("save-invoice"),Action::Wait(Condition::SavedInvoice),Action::Capture("01-invoice.png"),
            Action::Click("tab-Items"),Action::Idle,Action::Click("cell-0-0"),
            Action::Events(vec![Event::Paste("PO-NATIVE\tNATIVE-001\tCOTTON SHIRT\t中文输入与表格验证\t100% COTTON\tBRIDGE\t6109100000\t1500\tPCS\t4.25".into())]),
            Action::Click("save-invoice"),Action::Wait(Condition::SavedInvoice),Action::Capture("02-items.png"),
            Action::Click("group-documents"),Action::Click("group-resources"),Action::Click("nav-report-templates"),Action::Wait(Condition::Ready),Action::Click("record-new"),Action::Idle,Action::Drag,Action::CheckDrag,
            Action::Click("design-undo"),Action::UndoCheck,Action::Click("design-redo"),Action::CheckDrag,
            Action::Click("save-design"),Action::Wait(Condition::Template),Action::Capture("03-designer.png"),
            Action::Click("design-preview"),Action::Idle,Action::Click("render-pdf"),Action::Wait(Condition::Pdf),Action::Capture("04-pdf.png"),
        ]);
        actions.extend([
            Action::Scale(1.25),
            Action::Idle,
            Action::Click("tab-Invoice"),
            Action::Idle,
            Action::Capture("05-invoice-125.png"),
            Action::Scale(1.0),
            Action::Idle,
            Action::Finish,
        ]);
        Self {
            actions,
            deadline: Instant::now() + Duration::from_secs(150),
            capture_pending: false,
            output,
            result,
            before_drag: 0,
            screenshots: vec![],
            steps: 0,
            last_action: String::new(),
            input_clock: 0.,
        }
    }
    pub fn advance(&mut self, app: &mut Desktop, ctx: &egui::Context, input: &mut egui::RawInput) {
        if self.result.lock().unwrap().is_some() {
            return;
        }
        // Test-owned input must not mix with the user's mouse or focus changes
        // in another desktop window while this acceptance run is active.
        input
            .events
            .retain(|event| matches!(event, Event::Screenshot { .. }));
        input.focused = true;
        self.input_clock += 1. / 60.;
        input.time = Some(self.input_clock);
        if let Some(viewport) = input.viewports.get_mut(&egui::ViewportId::ROOT) {
            viewport.focused = Some(true);
        }
        for event in &input.events {
            if let Event::Screenshot {
                user_data, image, ..
            } = event
            {
                if let Some(name) = user_data
                    .data
                    .as_ref()
                    .and_then(|data| data.downcast_ref::<String>())
                {
                    let bytes: Vec<_> = image
                        .pixels
                        .iter()
                        .flat_map(|pixel| pixel.to_array())
                        .collect();
                    let result = image::save_buffer_with_format(
                        self.output.join(name),
                        &bytes,
                        image.size[0] as u32,
                        image.size[1] as u32,
                        image::ColorType::Rgba8,
                        image::ImageFormat::Png,
                    )
                    .map_err(|error| error.to_string());
                    if let Err(error) = result {
                        self.fail(app, ctx, error);
                        return;
                    }
                    self.screenshots.push(name.clone());
                    self.capture_pending = false;
                }
            }
        }
        if Instant::now() > self.deadline {
            self.fail(
                app,
                ctx,
                format!("原生 UI 验证超时，已完成 {} 个步骤。", self.steps),
            );
            return;
        }
        if let Some(error) = app.error.clone() {
            self.fail(app, ctx, error);
            return;
        }
        if self.capture_pending {
            return;
        }
        let Some(action) = self.actions.pop_front() else {
            return;
        };
        self.steps += 1;
        let description = format!("{action:?}");
        if description != self.last_action {
            eprintln!(
                "UI {description}: view={:?}, invoice={}, dirty={}, busy={}",
                app.view,
                app.invoice.header.id,
                app.invoice_dirty,
                app.busy()
            );
            self.last_action = description;
        }
        match action {
            Action::Click(name) => {
                if let Some(rect) = app.probes.get(name) {
                    let pos = rect.center();
                    input.events.push(Event::PointerMoved(pos));
                    input.events.push(pointer(pos, true));
                    self.actions.push_front(Action::Events(vec![
                        Event::PointerMoved(pos),
                        pointer(pos, false),
                    ]));
                    self.actions.push_front(Action::Idle);
                } else {
                    self.actions.push_front(Action::Click(name));
                }
            }
            Action::Events(events) => input.events.extend(events),
            Action::Idle => {}
            Action::Wait(condition) => {
                let satisfied = match condition {
                    Condition::Login => app.user.is_some() && !app.busy(),
                    Condition::Sample => app.invoice.rows.len() == 3,
                    Condition::SavedInvoice => {
                        app.invoice.header.id > 0 && !app.invoice_dirty && !app.busy()
                    }
                    Condition::Template => {
                        app.template_record
                            .as_ref()
                            .is_some_and(|template| template.status == "Published")
                            && !app.busy()
                    }
                    Condition::Pdf => app.pdf_texture.is_some() && !app.busy(),
                    Condition::Ready => !app.busy() && app.workspace.loaded,
                };
                if !satisfied {
                    self.actions.push_front(Action::Wait(condition));
                }
            }
            Action::Capture(name) => {
                self.capture_pending = true;
                ctx.send_viewport_cmd(egui::ViewportCommand::Screenshot(egui::UserData::new(
                    name.to_owned(),
                )));
            }
            Action::Drag => {
                self.before_drag = app.design.element("title").unwrap().x_hundredth_mm;
                let Some(rect) = app.probes.get("element-title").copied() else {
                    self.fail(app, ctx, "设计画布没有标题组件。".into());
                    return;
                };
                let start = rect.center();
                let end = start + egui::vec2(9., 0.);
                input
                    .events
                    .extend([Event::PointerMoved(start), pointer(start, true)]);
                for action in [
                    Action::Idle,
                    Action::Events(vec![Event::PointerMoved(start + egui::vec2(5., 0.))]),
                    Action::Events(vec![Event::PointerMoved(end)]),
                    Action::Events(vec![pointer(end, false)]),
                    Action::Idle,
                ]
                .into_iter()
                .rev()
                {
                    self.actions.push_front(action);
                }
            }
            Action::CheckDrag => {
                if app.design.element("title").unwrap().x_hundredth_mm == self.before_drag {
                    self.fail(app, ctx, "画布拖动/重做未改变真实坐标。".into());
                }
            }
            Action::UndoCheck => {
                if app.design.element("title").unwrap().x_hundredth_mm != self.before_drag {
                    self.fail(app, ctx, "撤销没有恢复拖动前坐标。".into());
                }
            }
            Action::Scale(scale) => ctx.set_zoom_factor(scale),
            Action::Finish => {
                let result = (|| {
                    let bytes = app.pdf_bytes.as_ref().ok_or("原生 UI 未产生 PDF。")?;
                    save_pdf(&self.output.join("ui-flow.pdf"), bytes)?;
                    if app.invoice.rows[0].cells[3] != "中文输入与表格验证"
                        || app.invoice.rows[0].cells[7] != "1500"
                    {
                        return Err("UI 表格粘贴未保存到后台。".into());
                    }
                    let report = json!({"passed":true,"invoiceId":app.invoice.header.id,"invoiceNo":app.invoice.header.invoice_no,"templateId":app.template_record.as_ref().map(|record|record.id),"pdfPages":app.pdf_page_count,"screenshots":self.screenshots,"steps":self.steps,"nativePixelsPerPoint":ctx.pixels_per_point(),"guiZoomFactors":[1.0,1.25],"manualPending":["real Windows IME candidate window","Windows display DPI change","native save dialog on target machines"]});
                    atomic_write(
                        &self.output.join("ui-acceptance.json"),
                        serde_json::to_vec_pretty(&report).unwrap().as_slice(),
                    )?;
                    Ok(())
                })();
                *self.result.lock().unwrap() = Some(result);
                app.allow_close = true;
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
        }
    }
    fn fail(&mut self, app: &mut Desktop, ctx: &egui::Context, error: String) {
        let _ = atomic_write(&self.output.join("ui-failure.txt"), error.as_bytes());
        *self.result.lock().unwrap() = Some(Err(error));
        app.allow_close = true;
        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
    }
}
fn pointer(pos: Pos2, pressed: bool) -> Event {
    Event::PointerButton {
        pos,
        button: PointerButton::Primary,
        pressed,
        modifiers: Modifiers::NONE,
    }
}
fn key(key: Key, modifiers: Modifiers) -> Event {
    Event::Key {
        key,
        physical_key: None,
        pressed: true,
        repeat: false,
        modifiers,
    }
}
