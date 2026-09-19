use super::*;
use crate::{Access, Audit};
use export_doc_engine::generated_api::*;
use serde_json::json;

#[derive(Default)]
pub struct AdministrationSmoke {
    stage: usize,
    pub checks: Vec<String>,
}
impl AdministrationSmoke {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let app = ui.global::<App>();
        let access = ui.global::<Access>();
        let audit = ui.global::<Audit>();
        match self.stage {
            0 => state.borrow_mut().navigate_now("users"),
            1 => app.invoke_open_tab("permission-templates".into()),
            2 => {
                if app.get_page() != "access" || access.get_access_templates().row_count() == 0 {
                    return Err("权限方案未进入专用工作区。".into());
                }
                access.invoke_access_action("new".into(),"".into());
                access.invoke_access_field("code".into(),"NATIVE-QA".into());
                access.invoke_access_field("name".into(),"原生权限验收".into());
                access.invoke_access_grant("document.invoices|view".into(),3);
                access.invoke_access_action("search".into(),"客户".into());
                access.invoke_access_action("preset:view".into(),"sales.customers".into());
            }
            3 => {
                let desktop = state.borrow();
                if desktop.access.direct().get(&("document.invoices".into(),"view".into())).map(String::as_str) != Some("company") {
                    return Err("筛选权限模块丢失了隐藏模块的草稿。".into());
                }
                drop(desktop);
                snapshot(ui,&output.join("36-native-permission-editor.png"))?;
                access.invoke_access_action("save".into(),"".into());
            }
            4 => {
                if !state.borrow().access.templates.iter().any(|row| row["code"] == "NATIVE-QA") {
                    return Err("权限方案没有保存回读。".into());
                }
                access.invoke_access_action("search".into(),"".into());
                access.invoke_access_action("copy".into(),"".into());
                if access.get_access_id() != 0 { return Err("复制方案没有建立独立草稿。".into()); }
                // No edit was made to the newly copied draft.
                self.checks.push("native-permission-presets-filter-preservation-save-copy".into());
                state.borrow_mut().navigate_now("audit");
            }
            5 => {
                let index = export_doc_engine::engine::audit::ENTITIES.iter().position(|(_,name,_)| *name == "CrmCustomer").ok_or("CRM 客户审计筛选不存在")?;
                audit.set_entity_index(index as i32+1);
                audit.invoke_action("search".into());
            }
            6 => {
                if app.get_records().row_count() == 0 { return Err("客户审计记录未显示。".into()); }
                app.invoke_select_record(app.get_records().row_data(0).unwrap().id);
                audit.set_maintenance(true);
                snapshot(ui,&output.join("37-native-audit-search-details.png"))?;
                state.borrow_mut().request(SAVE_AUDIT_LOGS_TO_PATH,0,vec![],
                    Some(json!({"entityName":"CrmCustomer","destinationPath":output.join("native-audit.xlsx")})),"audit-exported");
            }
            7 => {
                if !std::fs::read(output.join("native-audit.xlsx")).map_err(|error| error.to_string())?.starts_with(b"PK") {
                    return Err("原生审计导出不是有效工作簿。".into());
                }
                audit.invoke_action("delete".into());
                if !app.get_confirm_open() { return Err("审计删除未要求确认。".into()); }
                snapshot(ui,&output.join("38-native-audit-delete-confirmation.png"))?;
                app.invoke_confirm(false);
            }
            8 => {
                if app.get_records().row_count() == 0 { return Err("取消确认仍删除了审计。".into()); }
                audit.invoke_action("delete".into());
                app.invoke_confirm(true);
            }
            9 => {
                if app.get_records().row_count() != 0 { return Err("确认后未清理选定的客户审计。".into()); }
                self.checks.push("native-audit-filter-details-excel-and-confirmed-cleanup".into());
                std::fs::write(output.join("retry-source.xlsx"),b"invalid workbook").map_err(|error| error.to_string())?;
                state.borrow_mut().navigate_now("jobs");
            }
            10 => state.borrow_mut().request(START_BOOKING_SHEET_CONVERT_SAVE_TO_PATH_JOB,0,vec![],
                Some(json!({"sourcePath":output.join("retry-source.xlsx"),"destinationPath":output.join("native-retried-booking.xlsx")})),"task-action"),
            11 => {
                app.set_job_status_index(4);
                app.invoke_refresh();
            }
            12 => {
                if app.get_records().row_count() == 0 { app.invoke_refresh(); return Ok(false); }
                app.invoke_select_record(app.get_records().row_data(0).unwrap().id);
                if !app.get_job_can_retry() { return Err("失败任务未提供重试入口。".into()); }
                if app.get_job_detail().is_empty() { return Err("失败任务未显示错误详情。".into()); }
                snapshot(ui,&output.join("39-native-failed-task-retry.png"))?;
                let source = state.borrow().paths.app_root.join("Resources/ExcelTemplates/invoice-import-template.xlsx");
                std::fs::copy(source,output.join("retry-source.xlsx")).map_err(|error| error.to_string())?;
                app.invoke_file_action("retry".into());
            }
            13 => {
                let destination = output.join("native-retried-booking.xlsx");
                if !destination.is_file() { return Ok(false); }
                if !std::fs::read(destination).map_err(|error| error.to_string())?.starts_with(b"PK") {
                    return Err("任务重试没有生成实际的 Excel 输出。".into());
                }
                app.set_job_status_index(3);
                app.invoke_refresh();
            }
            14 => {
                if app.get_records().row_count() == 0 { return Err("任务状态筛选未显示成功结果。".into()); }
                snapshot(ui,&output.join("40-native-successful-tasks.png"))?;
                self.checks.push("native-task-failure-filter-retry-and-real-excel-output".into());
                return Ok(true);
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
