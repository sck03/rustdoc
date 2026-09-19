use export_doc_engine::{paths::RuntimePaths, runtime::Runtime};
use slint::{ComponentHandle, ModelRc, VecModel};
use std::{path::PathBuf, rc::Rc, time::Duration};
mod access_model;
mod attachment_model;
mod business_model;
mod controller;
mod document_package_model;
mod form_model;
mod form_sections;
mod hs_model;
mod labels;
mod license_model;
mod mail_model;
mod ocr_model;
mod office_model;
mod organization_model;
mod packing_model;
mod payment_model;
mod personnel_model;
mod platform;
mod recovery_model;
mod sales_model;
mod settings_model;
mod single_window_model;
mod single_window_sections;
mod smoke;
mod template_file_model;
mod worker;
slint::include_modules!();
pub fn model<T: Clone + 'static>(rows: Vec<T>) -> ModelRc<T> {
    Rc::new(VecModel::from(rows)).into()
}

fn main() {
    if let Err(error) = run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
fn run() -> Result<(), String> {
    let args: Vec<String> = std::env::args().collect();
    let option = |name: &str| {
        args.iter()
            .position(|a| a == name)
            .and_then(|i| args.get(i + 1))
            .map(PathBuf::from)
    };
    if let Some(output) = option("--capability-report") {
        export_doc_engine::paths::ensure_safe_absolute(&output)?;
        let operations:Vec<_>=export_doc_engine::generated_api::ALL_OPERATIONS.iter().map(|operation|serde_json::json!({
            "id":operation.id,"method":operation.method,"path":operation.path,
            "implementedDispatch":export_doc_engine::engine::NativeService::supports(*operation)
        })).collect();
        return export_doc_engine::paths::atomic_write(&output,&serde_json::to_vec_pretty(&serde_json::json!({
            "schemaVersion":1,"scope":"Rust service dispatch only; UI and business parity require separate acceptance",
            "operations":operations
        })).map_err(|e|e.to_string())?);
    }
    if args.iter().any(|a| a == "--ai-review-worker") {
        return export_doc_engine::ai_worker();
    }
    if let Some(result) = export_doc_engine::pdf::worker_args(&args) {
        return result;
    }
    let root = option("--app-root").unwrap_or(
        std::env::current_exe()
            .map_err(|e| e.to_string())?
            .parent()
            .ok_or("无法定位程序目录。")?
            .to_path_buf(),
    );
    let validation = args.iter().any(|a| a == "--validation");
    let smoke_output = option("--ui-smoke");
    if smoke_output.is_some() && !validation {
        return Err("窗口验收必须使用 --validation 独立数据目录。".into());
    }
    let retention = export_doc_engine::engine::tasks::retention::Retention::from_lookup(|key| {
        std::env::var(key).ok()
    })
    .map_err(|error| error.to_string())?;
    let mut runtime =
        Runtime::start_with_retention(RuntimePaths::open(&root, validation)?, retention)?;
    slint::BackendSelector::new()
        .backend_name("winit".into())
        .renderer_name("software".into())
        .select()
        .map_err(|e| e.to_string())?;
    let ui = AppWindow::new().map_err(|e| e.to_string())?;
    let desktop = controller::Desktop::new(&ui, runtime.client.clone(), runtime.paths.clone());
    let timer = slint::Timer::default();
    let state = desktop.clone();
    timer.start(
        slint::TimerMode::Repeated,
        Duration::from_millis(25),
        move || state.borrow_mut().poll(),
    );
    let smoke_timer = slint::Timer::default();
    let smoke_error = Rc::new(std::cell::RefCell::new(None));
    if let Some(output) = smoke_output {
        let mut smoke = smoke::Smoke::new(output)?;
        let weak = ui.as_weak();
        let state = desktop.clone();
        let error = smoke_error.clone();
        smoke_timer.start(
            slint::TimerMode::Repeated,
            Duration::from_millis(500),
            move || {
                if let Some(ui) = weak.upgrade() {
                    match smoke.tick(&ui, &state) {
                        Ok(false) => {}
                        Ok(true) => {
                            let _ = slint::quit_event_loop();
                        }
                        Err(e) => {
                            *error.borrow_mut() = Some(e);
                            let _ = slint::quit_event_loop();
                        }
                    }
                }
            },
        );
    }
    let result = ui.run().map_err(|e| e.to_string());
    timer.stop();
    smoke_timer.stop();
    drop(desktop);
    let result = result.and(runtime.shutdown());
    if let Some(error) = smoke_error.borrow_mut().take() {
        return Err(error);
    }
    result
}
