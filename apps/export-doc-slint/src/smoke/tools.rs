use super::*;
use crate::{Hs, Mail, Packing, worker::Work};
use export_doc_engine::generated_api::*;
use serde_json::json;

#[derive(Default)]
pub struct ToolsSmoke {
    stage: usize,
    pub checks: Vec<String>,
}
impl ToolsSmoke {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        self.step(ui, state, output)
            .map_err(|e| format!("装柜、HS 与邮件阶段 {}：{e}", self.stage))
    }
    fn step(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let app = ui.global::<App>();
        let packing = ui.global::<Packing>();
        let hs = ui.global::<Hs>();
        let mail = ui.global::<Mail>();
        match self.stage {
            0 => state.borrow_mut().navigate_now("container-projects"),
            1 => {
                packing.invoke_edited("name".into(), "原生装柜联调".into());
                packing.invoke_action("add".into(), 0);
                packing.invoke_edited("cargoItems.0.quantity".into(), "8".into());
                packing.invoke_action("save".into(), 0);
            }
            2 => {
                if state.borrow().packing.id() <= 0 {
                    return Err("装柜方案未保存回读。".into());
                }
                snapshot(ui, &output.join("70-packing-cargo.png"))?;
                packing.invoke_action("analyze".into(), 0);
            }
            3 => {
                if !packing.get_ready() {
                    return Err("装柜分析没有回到界面。".into());
                }
                if packing.get_preview().size().width == 0 {
                    return Ok(false);
                }
                if state
                    .borrow()
                    .packing
                    .analysis
                    .as_ref()
                    .is_none_or(|a| a.packed_packages != 8)
                {
                    return Err("装载数量错误。".into());
                }
                snapshot(ui, &output.join("71-packing-preview.png"))?;
                packing.invoke_edited("cargoItems.0.quantity".into(), "9".into());
                if packing.get_ready() {
                    return Err("修改输入未使旧分析失效。".into());
                }
                packing.invoke_action("undo".into(), 0);
                self.checks
                    .push("装柜方案保存回读、约束分析、原生视角和撤销失效".into());
            }
            4 => state.borrow_mut().navigate_now("hs-codes"),
            5 => hs.invoke_action("tab".into(), 1),
            6 => hs.invoke_action("new".into(), 0),
            7 => {
                hs.invoke_edited("code".into(), "6109100000".into());
                hs.invoke_edited("name".into(), "棉制针织T恤衫".into());
                hs.invoke_edited("unit".into(), "件".into());
                hs.invoke_action("save".into(), 0);
            }
            8 => {
                if state
                    .borrow()
                    .hs
                    .rows
                    .first()
                    .is_none_or(|r| r["status"] != "ReferenceOnly")
                {
                    return Err("手工资料未保持参考状态。".into());
                }
                hs.invoke_action("tab".into(), 5);
            }
            9 => {
                let source = output.join("hs-ui-tariffs.csv");
                std::fs::write(&source,"HS编码,商品名称,法定第一单位,退税率,申报要素\n6109100000,棉制针织T恤衫,011,13%,棉制 针织\n").map_err(|e|e.to_string())?;
                state.borrow_mut().start(Work::Upload{operation:PREVIEW_HS_CODES_IMPORT_UPLOAD,parameters:vec![],metadata:json!({"sourceName":"原生联调税则","effectiveYear":2026,"mode":"Incremental"}),source,limit:25*1024*1024,reply:"hs:tariff-preview".into()});
            }
            10 => {
                if !hs.get_preview_ready() {
                    return Err("税则预检没有打开。".into());
                }
                snapshot(ui, &output.join("72-hs-preview.png"))?;
                hs.invoke_action("commit-tariff".into(), 0);
            }
            11 => hs.invoke_action("tab".into(), 0),
            12 => {
                hs.set_query("610910".into());
                hs.invoke_action("search".into(), 0);
            }
            13 => {
                if !hs.get_can_use() {
                    return Err("导入税则没有进入可用知识候选。".into());
                }
                hs.invoke_action("accept".into(), 0);
            }
            14 => {
                snapshot(ui, &output.join("73-hs-knowledge.png"))?;
                hs.invoke_action("tab".into(), 2);
            }
            15 => hs.invoke_action("new".into(), 0),
            16 => {
                for (key, value) in [
                    ("rawReportedHsCode", "6109100000"),
                    ("resolvedCurrentHsCode", "6109100000"),
                    ("productName", "棉制针织T恤衫"),
                    ("specification", "100%棉 针织"),
                    ("isManuallyVerified", "true"),
                ] {
                    hs.invoke_edited(key.into(), value.into());
                }
                hs.invoke_action("save".into(), 0);
            }
            17 => {
                if state.borrow().hs.rows.is_empty() {
                    return Err("申报案例没有回读。".into());
                }
                self.checks
                    .push("HS 参考主档、年度预检确认、知识检索反馈和案例维护".into());
                state.borrow_mut().navigate_now("email");
            }
            18 => mail.invoke_action("tab".into(), 2),
            19 => mail.invoke_action("new-template".into(), 0),
            20 => {
                for (key, value) in [
                    ("template-name", "原生报价邮件"),
                    ("template-category", "报价"),
                    ("template-subject", "Hello {{CustomerName}}"),
                    (
                        "template-body",
                        "<p>Dear <b>{{ContactName}}</b></p><p>报价已经准备。</p><script>bad()</script>",
                    ),
                ] {
                    mail.invoke_edited(key.into(), value.into());
                }
                mail.invoke_action("save-template".into(), 0);
            }
            21 => {
                if state.borrow().mail.id() <= 0
                    || state.borrow().mail.template.html.contains("script")
                {
                    return Err("邮件模板保存或净化失败。".into());
                }
                mail.invoke_action("publish".into(), 0);
            }
            22 => {
                mail.set_share_scope(2);
                mail.invoke_action("share".into(), 0);
            }
            23 => {
                if state
                    .borrow()
                    .mail
                    .current
                    .as_ref()
                    .is_none_or(|r| r["shareScope"] != "Company")
                {
                    return Err("邮件模板共享未保存。".into());
                }
                snapshot(ui, &output.join("74-mail-template.png"))?;
                mail.invoke_action("template-tab".into(), 2);
            }
            24 => {
                mail.invoke_edited("variable:CustomerName".into(), "联调客户".into());
                mail.invoke_edited("variable:ContactName".into(), "张先生".into());
                mail.invoke_action("preview".into(), 0);
            }
            25 => {
                if mail.get_preview_subject() != "Hello 联调客户" {
                    return Err("邮件变量预览未生效。".into());
                }
                mail.invoke_action("compose-preview".into(), 0);
            }
            26 => {
                if !mail.get_rich_body()
                    || !state.borrow().mail.compose.body().contains("<b>张先生</b>")
                {
                    return Err("模板填写到邮件时丢失格式。".into());
                }
                snapshot(ui, &output.join("75-mail-compose.png"))?;
                mail.invoke_action("clear".into(), 0);
                app.invoke_confirm(true);
            }
            27 => {
                mail.invoke_action("tab".into(), 2);
            }
            28 => {
                mail.invoke_action("template-tab".into(), 4);
            }
            29 => {
                if state.borrow().mail.versions.len() < 3 {
                    return Err("邮件模板历史不完整。".into());
                }
                snapshot(ui, &output.join("76-mail-history.png"))?;
                self.checks
                    .push("邮件模板净化、发布共享、变量预览、富文本草稿保留和版本历史".into());
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
