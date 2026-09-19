use super::*;
use crate::{SingleWindow, worker::Work};
use export_doc_engine::generated_api::*;
use serde_json::json;

#[derive(Default)]
pub struct SingleWindowSmoke {
    stage: usize,
    pub checks: Vec<String>,
}
impl SingleWindowSmoke {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
        invoice_id: i64,
    ) -> Result<bool, String> {
        self.step(ui, state, output, invoice_id)
            .map_err(|e| format!("单一窗口阶段 {}：{e}", self.stage))
    }
    fn step(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
        invoice_id: i64,
    ) -> Result<bool, String> {
        let view = ui.global::<SingleWindow>();
        match self.stage {
            0 => state.borrow_mut().navigate_now("single-window"),
            1 => {
                state.borrow_mut().single_window.invoice_id = invoice_id;
                view.invoke_action("tab".into(), 1);
            }
            2 => {
                if !view.get_has_document() {
                    return Err("来源发票没有加载到原产地证。".into());
                }
                view.invoke_edited("applName".into(), "原生申报员".into());
                view.invoke_action("save-document".into(), 0);
            }
            3 => {
                snapshot(ui, &output.join("80-single-window-coo.png"))?;
                view.invoke_action("document-tab".into(), 1);
            }
            4 => {
                if view.get_goods().row_count() == 0 {
                    return Err("原产地证商品明细为空。".into());
                }
                view.invoke_edited(
                    "items.0.goodsNameE".into(),
                    "NATIVE DECLARATION GOODS".into(),
                );
                view.invoke_action("save-document".into(), 0);
            }
            5 => {
                snapshot(ui, &output.join("81-single-window-goods.png"))?;
                view.invoke_action("document-tab".into(), 3);
                view.invoke_action("review".into(), 0);
            }
            6 => {
                snapshot(ui, &output.join("82-single-window-review.png"))?;
                view.invoke_action("tab".into(), 2);
            }
            7 => {
                for (key, value) in [
                    ("copCusCode", "1234567890"),
                    ("operType", "1"),
                    ("gName", "T SHIRTS"),
                    ("codeTS", "6109100000"),
                    ("declTotal", "100.00"),
                    ("ieDate", "20260917"),
                    ("tradeMode", "0110"),
                    ("oriCountry", "142"),
                    ("tradeCode", "1234567890"),
                    ("agentCode", "0987654321"),
                    ("curr", "502"),
                    ("qtyOrWeight", "10"),
                    ("packingCondition", "CARTONS"),
                    ("consignTele", "12345678901"),
                ] {
                    view.invoke_edited(key.into(), value.into());
                }
                view.invoke_action("save-document".into(), 0);
            }
            8 => {
                snapshot(ui, &output.join("83-single-window-acd.png"))?;
                view.invoke_action("tab".into(), 3);
            }
            9 => view.invoke_action("new-profile".into(), 0),
            10 => {
                for (key, value) in [
                    ("profile.profileName", "联调持卡机"),
                    ("profile.companyScope", "DEFAULT"),
                    ("profile.cardIdentifier", "NATIVE-TEST-CARD"),
                ] {
                    view.invoke_edited(key.into(), value.into());
                }
                view.invoke_action("save-profile".into(), 0);
            }
            11 => {
                if view.get_profiles().row_count() != 1 {
                    return Err("持卡机档案没有保存回读。".into());
                }
                snapshot(ui, &output.join("84-single-window-station.png"))?;
                state.borrow_mut().start(Work::Request{operation:SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,parameters:vec![("invoiceId",invoice_id.to_string())],query:vec![],body:Some(json!({"packagePath":output.join("single-window-submit.zip"),"stationAssignmentCode":""})),reply:"sw:package".into()});
            }
            12 => {
                if !output.join("single-window-submit.zip").is_file() {
                    return Err("申报提交包没有生成。".into());
                }
                if view.get_batches().row_count() == 0 {
                    return Ok(false);
                }
                view.invoke_action("batch".into(), 1);
            }
            13 => {
                if view.get_packages().row_count() != 1 {
                    return Err("操作中心交接历史未回读。".into());
                }
                snapshot(ui, &output.join("85-single-window-center.png"))?;
                view.invoke_action("tab".into(), 4);
            }
            14 => {
                if view.get_dictionary_rows().row_count() == 0 {
                    return Err("内置申报词典没有加载。".into());
                }
                view.invoke_action("dictionary-row".into(), 1);
            }
            15 => {
                snapshot(ui, &output.join("86-single-window-dictionary.png"))?;
                self.checks.push("单一窗口原生五页签、中文草稿、商品锁定、代理委托、持卡机档案、真实申报 ZIP 与操作中心回读".into());
                return Ok(true);
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
