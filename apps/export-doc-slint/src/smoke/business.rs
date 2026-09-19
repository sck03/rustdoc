use super::*;
use crate::{Crm, PartyFiles, Sales, SupplierOverview, worker::Work};
use export_doc_engine::generated_api::*;
use serde_json::json;

#[derive(Default)]
pub struct BusinessSmoke {
    stage: usize,
    customer: i64,
    follow_up: i64,
    supplier: i64,
    assessment: i64,
    pub checks: Vec<String>,
}
fn named(state: &Rc<RefCell<Desktop>>, name: &str) -> Result<i64, String> {
    state
        .borrow()
        .workspace
        .items()
        .iter()
        .find(|row| row["name"] == name)
        .and_then(|row| row["id"].as_i64())
        .ok_or_else(|| format!("未回读 {name}。"))
}
impl BusinessSmoke {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        self.step(ui, state, output)
            .map_err(|cause| format!("销售与供应链界面阶段 {}：{cause}", self.stage))
    }
    fn step(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let app = ui.global::<App>();
        let sales = ui.global::<Sales>();
        match self.stage {
            0 => state.borrow_mut().navigate_now("crm-customers"),
            1 => app.invoke_new_record(),
            2 => {
                app.invoke_field_edited("name".into(), "业务联调客户".into());
                app.invoke_field_edited("countryRegion".into(), "中国".into());
                app.invoke_field_edited("source".into(), "业务联调".into());
                app.invoke_save_form();
            }
            3 => {
                self.customer = named(state, "业务联调客户")?;
                app.invoke_open_record(self.customer as i32);
            }
            4 => app.invoke_business_action("tab".into(), "crm-follow-ups".into()),
            5 => app.invoke_new_record(),
            6 => {
                app.invoke_field_edited("summary".into(), "确认报价规格".into());
                app.invoke_field_edited("nextAction".into(), "发送样品报价".into());
                app.invoke_save_form();
            }
            7 => {
                self.follow_up = state
                    .borrow()
                    .workspace
                    .items()
                    .iter()
                    .find(|row| row["summary"] == "确认报价规格")
                    .and_then(|row| row["id"].as_i64())
                    .ok_or("未回读跟进记录。")?;
                app.invoke_open_record(self.follow_up as i32);
            }
            8 => {
                let local = state
                    .borrow()
                    .form
                    .as_ref()
                    .ok_or("跟进编辑器未打开。")?
                    .value["followedUpAt"]
                    .as_str()
                    .unwrap_or("")
                    .to_owned();
                if local.len() != 16 || !local.contains(' ') {
                    return Err("跟进时间没有按业务时区显示为本地输入。".into());
                }
                snapshot(ui, &output.join("50-follow-up-editor.png"))?;
                app.invoke_cancel_form();
                app.invoke_select_record(self.follow_up as i32);
                app.invoke_record_action(COMPLETE_CRM_FOLLOW_UP.id.into());
            }
            9 => app.invoke_save_form(),
            10 => {
                if !state.borrow().workspace.items().is_empty() {
                    return Err("完成的跟进仍显示为待办。".into());
                }
                ui.global::<Crm>().set_include_completed(true);
                app.invoke_refresh();
            }
            11 => {
                if state
                    .borrow()
                    .workspace
                    .items()
                    .iter()
                    .all(|row| row["id"] != self.follow_up || row["isCompleted"] != true)
                {
                    return Err("已完成筛选未回读完成状态。".into());
                }
                self.checks
                    .push("crm-follow-up-local-time-save-complete-filter".into());
                state.borrow_mut().navigate_now("opportunities");
            }
            12 => app.invoke_new_record(),
            13 => {
                let field = sales
                    .get_details()
                    .iter()
                    .flat_map(|section| section.fields.iter().collect::<Vec<_>>())
                    .find(|field| field.key == "crmCustomerId")
                    .ok_or("商机缺少客户选择。")?;
                let index = field
                    .options
                    .iter()
                    .position(|name| name == "业务联调客户")
                    .ok_or("客户选择未包含新增客户。")?;
                sales.invoke_selected("crmCustomerId".into(), index as i32);
                sales.invoke_edited("title".into(), "原生报价联调".into());
                sales.invoke_edited("quotationNo".into(), "NATIVE-UI-QUOTE".into());
                sales.invoke_edited("estimatedAmount".into(), "12345.67".into());
                sales.invoke_edited("probabilityPercent".into(), "40".into());
                sales.invoke_action("save".into(), "".into());
            }
            14 => {
                if !sales.get_saved() || sales.get_history().row_count() != 1 {
                    return Err("商机保存或首版历史未完成。".into());
                }
                snapshot(ui, &output.join("51-opportunity-editor.png"))?;
                sales.invoke_action("history".into(), "".into());
            }
            15 => {
                sales.set_transition_note("客户确认需求".into());
                sales.invoke_action("transition".into(), "需求确认".into());
            }
            16 => app.invoke_confirm(true),
            17 => {
                if sales.get_stage() != "需求确认" || sales.get_history().row_count() != 2 {
                    return Err("阶段变更及历史没有同步回读。".into());
                }
                snapshot(ui, &output.join("52-opportunity-history.png"))?;
                self.checks
                    .push("opportunity-quote-save-transition-history".into());
                state.borrow_mut().navigate_now("sales-dashboard");
            }
            18 => {
                if ui.global::<Crm>().get_currencies().row_count() == 0 {
                    return Err("销售概览未汇总商机金额。".into());
                }
                snapshot(ui, &output.join("53-sales-dashboard.png"))?;
                state.borrow_mut().navigate_now("suppliers");
            }
            19 => {
                let source = output.join("supplier-import.csv");
                paths::atomic_write(&source,"供应商名称,分类,主要产品,联系人,邮箱\n业务联调供应商,服装,棉外套,李经理,li@example.test".as_bytes())?;
                state.borrow_mut().start(Work::Upload {
                    operation: PREVIEW_SUPPLIER_IMPORT,
                    parameters: vec![],
                    metadata: json!({}),
                    source,
                    limit: 25 * 1024 * 1024,
                    reply: "party:preview".into(),
                });
            }
            20 => {
                if app.get_page() != "party-import" || !ui.global::<PartyFiles>().get_can_confirm()
                {
                    return Err("未进入可确认的原生导入预检。".into());
                }
                snapshot(ui, &output.join("54-supplier-import-preview.png"))?;
                ui.global::<PartyFiles>().invoke_action("confirm".into(), 0);
            }
            21 => {
                self.supplier = named(state, "业务联调供应商")?;
                app.invoke_open_record(self.supplier as i32);
            }
            22 => app.invoke_business_action("tab".into(), "supplier-contacts".into()),
            23 => {
                if state
                    .borrow()
                    .workspace
                    .items()
                    .iter()
                    .all(|row| row["name"] != "李经理" || row["isPrimary"] != true)
                {
                    return Err("导入联系人未关联供应商。".into());
                }
                snapshot(ui, &output.join("55-supplier-contact.png"))?;
                self.checks
                    .push("party-import-preview-confirm-primary-contact".into());
                app.invoke_business_action("tab".into(), "supplier-assessments".into());
            }
            24 => app.invoke_new_record(),
            25 => {
                app.invoke_field_edited("notes".into(), "交期与样品的原生评价联调".into());
                app.invoke_save_form();
            }
            26 => {
                self.assessment = state
                    .borrow()
                    .workspace
                    .items()
                    .first()
                    .and_then(|row| row["id"].as_i64())
                    .ok_or("未回读供应商评价。")?;
                app.invoke_select_record(self.assessment as i32);
                app.invoke_record_action(CONFIRM_SUPPLIER_ASSESSMENT.id.into());
            }
            27 => app.invoke_save_form(),
            28 => {
                if state
                    .borrow()
                    .workspace
                    .items()
                    .iter()
                    .all(|row| row["id"] != self.assessment || row["status"] != "Confirmed")
                {
                    return Err("评价确认未携带当前版本或未回读。".into());
                }
                state.borrow_mut().navigate_now("suppliers");
            }
            29 => app.invoke_open_tab("supplier-overview".into()),
            30 => {
                if app.get_page() != "supplier-overview"
                    || ui.global::<SupplierOverview>().get_rows().row_count() == 0
                {
                    return Err("评价分析没有展示已确认评价。".into());
                }
                snapshot(ui, &output.join("56-supplier-assessment-overview.png"))?;
                self.checks
                    .push("supplier-assessment-confirm-query-version-overview".into());
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
