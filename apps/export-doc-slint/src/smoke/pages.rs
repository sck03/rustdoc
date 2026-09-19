use super::*;
use crate::worker::Work;
use export_doc_engine::generated_api::*;
use serde_json::json;

#[derive(Default)]
pub struct Pages {
    stage: usize,
    pub checks: Vec<String>,
}
impl Pages {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let result = self.step(ui, state, output);
        result.map_err(|cause| format!("业务页面阶段 {}：{cause}", self.stage))
    }
    fn step(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let app = ui.global::<App>();
        match self.stage {
            0 => state.borrow_mut().navigate_now("companies"),
            1 => {
                if app.get_page() != "organization" {
                    return Err("未进入原生组织目录。".into());
                }
                app.invoke_organization_action("new-department".into(), "GENERAL".into());
            }
            2 => {
                app.invoke_field_edited("code".into(), "NATIVE-SALES".into());
                app.invoke_field_edited("name".into(), "国际业务部".into());
                app.invoke_save_form();
            }
            3 => {
                if app.get_org_selected() != "NATIVE-SALES" {
                    return Err("保存部门后没有定位新部门。".into());
                }
                app.invoke_organization_search("国际".into());
            }
            4 => {
                if app.get_org_rows().row_count() != 2 {
                    return Err("组织搜索没有保留上级链。".into());
                }
                snapshot(ui, &output.join("19-organization-tree.png"))?;
                self.checks.push("organization-create-search-reveal".into());
                state.borrow_mut().navigate_now("people");
            }
            5 => app.invoke_new_record(),
            6 => {
                app.invoke_field_edited("employeeNumber".into(), "UI-HR-001".into());
                app.invoke_field_edited("profile.fullName".into(), "张明（原生验收）".into());
                app.invoke_field_edited("jobTitle".into(), "外贸业务员".into());
                app.invoke_field_edited(
                    "profile.workEmail".into(),
                    "native@example.invalid".into(),
                );
                app.invoke_field_edited(
                    "profile.personalPhone".into(),
                    "private-test-value".into(),
                );
                let field = app
                    .get_fields()
                    .iter()
                    .find(|field| field.key == "departmentId")
                    .ok_or("入职表单没有部门选项。")?;
                let index = field
                    .options
                    .iter()
                    .position(|label| label == "国际业务部")
                    .ok_or("新增部门没有进入人员选项。")?;
                app.invoke_field_selected("departmentId".into(), index as i32);
                app.invoke_save_form();
            }
            7 => {
                if app.get_page() != "personnel" || !app.get_person_title().contains("张明") {
                    return Err("登记后未进入人员档案。".into());
                }
                if app
                    .get_person_sections()
                    .iter()
                    .any(|section| section.key == "person.private" && section.expanded)
                {
                    return Err("人员低频私密资料应默认折叠。".into());
                }
                snapshot(ui, &output.join("20-personnel-profile.png"))?;
                app.invoke_personnel_action("ConfirmPersonnel".into(), "".into());
            }
            8 => {
                app.invoke_field_edited("note".into(), "原生界面转正验收".into());
                app.invoke_save_form();
            }
            9 => {
                if state
                    .borrow()
                    .personnel
                    .record
                    .as_ref()
                    .is_none_or(|record| record.employee.status != "Active")
                {
                    return Err("转正未保存任职状态。".into());
                }
                app.invoke_personnel_action("tab".into(), "2".into());
            }
            10 => {
                if app.get_records().row_count() != 2 {
                    return Err("入职与转正记录不完整。".into());
                }
                snapshot(ui, &output.join("21-personnel-history.png"))?;
                app.invoke_personnel_action("tab".into(), "3".into());
            }
            11 => {
                if !app.get_person_clearance_summary().contains("0 项会议室") {
                    return Err("人员交接查询未回读。".into());
                }
                snapshot(ui, &output.join("22-personnel-clearance.png"))?;
                self.checks
                    .push("personnel-create-transition-history-clearance".into());
                state.borrow_mut().navigate_now("crm-customers");
            }
            12 => {
                let row = app
                    .get_records()
                    .iter()
                    .find(|row| row.cells.iter().any(|cell| cell.contains("Slint 验收客户")))
                    .ok_or("没有找到已保存的客户。")?;
                app.invoke_open_record(row.id);
            }
            13 => {
                if app.get_page() != "business" {
                    return Err("没有打开专用客户资料页。".into());
                }
                app.invoke_business_action("tab".into(), "crm-contacts".into());
            }
            14 => app.invoke_new_record(),
            15 => {
                app.invoke_field_edited("name".into(), "李梅（联系人验收）".into());
                app.invoke_field_edited("email".into(), "contact@example.invalid".into());
                app.invoke_save_form();
            }
            16 => {
                if app.get_records().row_count() != 1 {
                    return Err("客户联系人没有按当前客户回读。".into());
                }
                snapshot(ui, &output.join("23-customer-contacts.png"))?;
                self.checks.push("customer-related-contact-workflow".into());
                state.borrow_mut().navigate_now("worklist");
            }
            17 => {
                let index = state
                    .borrow()
                    .worklist_sources
                    .iter()
                    .position(|source| source == "invoice-review")
                    .ok_or("待办缺少单据来源。")?;
                app.set_worklist_source_index(index as i32 + 1);
                app.invoke_refresh();
            }
            18 => {
                if app.get_worklist_rows().row_count() == 0 {
                    return Err("单据待办为空。".into());
                }
                snapshot(ui, &output.join("24-worklist.png"))?;
                app.invoke_worklist_open(0);
            }
            19 => {
                if app.get_page() != "invoice-edit" {
                    return Err("待办没有打开对应发票。".into());
                }
                self.checks
                    .push("worklist-source-filter-open-invoice".into());
                app.invoke_invoice_action("attachments".into());
            }
            20 => {
                if app.get_attachment_invoice_index() == 0 {
                    return Err("业务资料未保留所属发票。".into());
                }
                app.invoke_attachment_action("category".into(), "".into());
            }
            21 => {
                app.invoke_field_edited("name".into(), "原生验收资料".into());
                app.invoke_save_form();
            }
            22 => {
                let source = output.join("native-attachment.txt");
                paths::atomic_write(&source, "原生附件回读验证".as_bytes())?;
                let mut desktop = state.borrow_mut();
                let invoice = desktop.attachments.invoice_id.ok_or("缺少资料发票。")?;
                let category = desktop
                    .attachments
                    .categories
                    .iter()
                    .find(|category| category.name == "原生验收资料")
                    .ok_or("分类未保存。")?
                    .id;
                desktop.start(Work::Upload{operation:UPLOAD_BUSINESS_ATTACHMENT,parameters:vec![("invoiceId",invoice.to_string())],metadata:json!({"uploadKey":paths::nonce()?,"title":"客户装运确认","categoryId":category,"poNumber":"PO-NATIVE","styleNo":"","note":"实际文件上传验收"}),source,limit:16*1024*1024,reply:"attachment-uploaded".into()});
            }
            23 => {
                if app.get_attachment_versions().row_count() != 1 {
                    return Err("附件未进入版本列表。".into());
                }
                app.set_attachment_note("已核对原始资料".into());
                app.invoke_attachment_action("confirm".into(), "1".into());
            }
            24 => {
                if !app.get_confirm_open() {
                    return Err("确认有效版本缺少确认对话框。".into());
                }
                app.invoke_confirm(true);
            }
            25 => {
                if !app
                    .get_attachment_versions()
                    .row_data(0)
                    .is_some_and(|version| version.current)
                {
                    return Err("附件未标记有效版本。".into());
                }
                snapshot(ui, &output.join("25-attachment-versions.png"))?;
                app.invoke_attachment_action("preview".into(), "1".into());
            }
            26 => {
                if app.get_attachment_text() != "原生附件回读验证" {
                    return Err("原文件内容未正确回读。".into());
                }
                snapshot(ui, &output.join("26-attachment-preview.png"))?;
                self.checks
                    .push("attachment-upload-confirm-native-text-preview".into());
                app.invoke_attachment_action("close-preview".into(), "".into());
                state.borrow_mut().navigate_now("dashboard");
            }
            27 => {
                if app.get_metrics().row_count() != 6 {
                    return Err("概览没有六项业务指标。".into());
                }
                snapshot(ui, &output.join("27-dashboard.png"))?;
                self.checks
                    .push("dashboard-six-metrics-and-recent-invoices".into());
                return Ok(true);
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
