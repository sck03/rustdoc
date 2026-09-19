use super::*;
use crate::worker::Work;
use export_doc_engine::{contracts, generated_api::*};
use serde_json::json;

#[derive(Default)]
pub struct BackupSmoke {
    stage: usize,
    pub checks: Vec<String>,
}
impl BackupSmoke {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let app = ui.global::<App>();
        match self.stage {
            0 => state.borrow_mut().navigate_now("settings"),
            1 => app.invoke_open_tab("backup".into()),
            2 => {
                if app.get_page() != "backup" {
                    return Err("系统设置未进入原生备份工作区。".into());
                }
                app.invoke_maintenance_action("create".into());
            }
            3 => {
                if app.get_backup_rows().row_count() == 0 {
                    // The create request and the subsequent list refresh are
                    // asynchronous; wait for the published response instead of
                    // asserting on the same timer tick.
                    return Ok(false);
                }
                snapshot(ui, &output.join("34-backup-workspace.png"))?;
                let name = state.borrow().backups[0].file_name.clone();
                state.borrow_mut().start(Work::SaveBackupCopy {
                    file_name: name,
                    destination: output.join("native-backup-copy.sqlite3"),
                });
            }
            4 => {
                let backup = std::fs::read(output.join("native-backup-copy.sqlite3"))
                    .map_err(|cause| cause.to_string())?;
                if !backup.starts_with(b"SQLite format 3") {
                    return Err("保存的备份副本不是 SQLite 数据库。".into());
                }
                state.borrow_mut().start(Work::Request {
                    operation: CREATE_CUSTOMER,
                    parameters: vec![],
                    query: vec![],
                    body: Some(contracts::overlay(
                        contracts::object(CREATE_CUSTOMER.id, true),
                        &json!({"customerNameEN":"RESTORE-REMOVES-THIS-CANARY"}),
                    )),
                    reply: "backup-canary".into(),
                });
            }
            5 => {
                app.set_backup_confirmation("RESTORE".into());
                app.invoke_maintenance_action("restore".into());
            }
            6 => {
                if !app.get_confirm_open() {
                    return Err("数据库还原缺少最终确认。".into());
                }
                snapshot(ui, &output.join("35-backup-restore-confirmation.png"))?;
                app.invoke_confirm(true);
            }
            7 => {
                if app.get_logged_in() {
                    return Err("数据库还原没有撤销会话。".into());
                }
                app.invoke_login("admin".into(), "".into());
            }
            8 => {
                if state
                    .borrow()
                    .lookups
                    .get("customerId")
                    .is_some_and(|rows| {
                        rows.iter()
                            .any(|(_, label)| label == "RESTORE-REMOVES-THIS-CANARY")
                    })
                {
                    return Err("数据库还原没有回到备份时点。".into());
                }
                self.checks
                    .push("native-backup-create-copy-confirm-restore-session-reset".into());
                let recovery = ui.global::<crate::Recovery>();
                recovery.set_password("native-disaster-password".into());
                recovery.set_password_confirm("native-disaster-password".into());
                recovery.invoke_action("create".into());
            }
            9 => {
                let recovery = ui.global::<crate::Recovery>();
                let root = state.borrow().paths.data_root.join("DisasterRecovery");
                let package = std::fs::read_dir(&root)
                    .map_err(|cause| cause.to_string())?
                    .filter_map(Result::ok)
                    .map(|entry| entry.path())
                    .find(|path| path.extension().is_some_and(|value| value == "edmrecovery"));
                let Some(package) = package else {
                    return Ok(false);
                };
                let bytes = std::fs::read(&package).map_err(|cause| cause.to_string())?;
                if !bytes.starts_with(b"EDM-DISASTER-RECOVERY-1") {
                    return Err("原生灾备包缺少受控包头。".into());
                }
                recovery.set_package_path(package.to_string_lossy().into_owned().into());
                recovery.set_restore_password("native-disaster-password".into());
                recovery.set_restore_confirmation("RECOVER".into());
                recovery.invoke_action("restore".into());
            }
            10 => {
                if !app.get_confirm_open() {
                    return Err("灾备恢复没有进入最终确认。".into());
                }
                snapshot(ui, &output.join("41-native-disaster-recovery.png"))?;
                app.invoke_confirm(false);
            }
            11 => {
                self.checks
                    .push("native-disaster-package-create-and-restore-confirmation".into());
                return Ok(true);
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
