use super::*;
use export_doc_engine::generated_api::*;
#[derive(Default)]
pub struct OfficeSmoke {
    stage: usize,
    pub checks: Vec<String>,
    selected: i32,
}
impl OfficeSmoke {
    pub fn tick(
        &mut self,
        ui: &AppWindow,
        state: &Rc<RefCell<Desktop>>,
        output: &Path,
    ) -> Result<bool, String> {
        let app = ui.global::<App>();
        let edit = |key: &str, value: &str| app.invoke_field_edited(key.into(), value.into());
        match self.stage {
            0 => state.borrow_mut().navigate_now("bookings"),
            1 => {
                if app.get_page() != "office" || app.get_resource() != "rooms" {
                    return Err("会议室没有进入资源与登记工作区。".into());
                }
                app.invoke_office_action("new".into(), 0);
            }
            2 => {
                edit("name", "原生验收会议室");
                edit("location", "行政楼三层");
                edit("equipment", "投影、白板");
                app.invoke_save_form();
            }
            3 => {
                self.selected = app
                    .get_office_cards()
                    .iter()
                    .find(|card| card.title == "原生验收会议室")
                    .ok_or("会议室未进入目录。")?
                    .id;
                snapshot(ui, &output.join("28-meeting-directory.png"))?;
                app.invoke_office_action("schedule".into(), self.selected);
            }
            4 => {
                snapshot(ui, &output.join("29-meeting-schedule.png"))?;
                app.invoke_office_action("book".into(), 0);
            }
            5 => {
                edit("title", "原生会议与钥匙交接");
                edit("attendeeCount", "4");
                app.invoke_field_selected("employeeId".into(), 1);
                app.invoke_save_form();
            }
            6 => {
                let card = app
                    .get_office_cards()
                    .row_data(0)
                    .ok_or("预约未进入记录目录。")?;
                if !app.get_office_requests() || card.title != "原生会议与钥匙交接" {
                    return Err("预约保存没有定位登记记录。".into());
                }
                self.selected = card.id;
                app.invoke_office_action(ISSUE_MEETING_ROOM_KEY.id.into(), self.selected);
            }
            7 => {
                edit("note", "已当面交付钥匙");
                app.invoke_save_form();
            }
            8 => {
                if app
                    .get_office_cards()
                    .row_data(0)
                    .is_none_or(|row| row.state != "使用中")
                {
                    return Err("钥匙交接未更新状态。".into());
                }
                app.invoke_office_action(RETURN_MEETING_ROOM_KEY.id.into(), self.selected);
            }
            9 => {
                edit("note", "会议结束，钥匙归还");
                app.invoke_save_form();
            }
            10 => {
                if app
                    .get_office_cards()
                    .row_data(0)
                    .is_none_or(|row| row.state != "已完成")
                {
                    return Err("会议归还未更新状态。".into());
                }
                app.invoke_office_action("history".into(), self.selected);
            }
            11 => {
                if app.get_records().row_count() != 3 {
                    return Err("会议登记或交接历史缺失。".into());
                }
                snapshot(ui, &output.join("30-meeting-handover-history.png"))?;
                self.checks
                    .push("native-meeting-directory-schedule-register-key-handover-history".into());
                state.borrow_mut().navigate_now("supply-requests");
            }
            12 => app.invoke_office_action("new".into(), 0),
            13 => {
                edit("name", "原生验收演示设备");
                edit("unit", "台");
                edit("location", "行政柜");
                edit("isReturnable", "true");
                edit("minimumStock", "2");
                app.invoke_save_form();
            }
            14 => {
                self.selected = app
                    .get_office_cards()
                    .iter()
                    .find(|card| card.title == "原生验收演示设备")
                    .ok_or("物品未保存。")?
                    .id;
                app.invoke_office_action(RESTOCK_OFFICE_SUPPLY.id.into(), self.selected);
            }
            15 => {
                edit("quantity", "5");
                edit("note", "验收入库");
                app.invoke_save_form();
            }
            16 => {
                snapshot(ui, &output.join("31-supply-directory-stock.png"))?;
                app.invoke_office_action("request".into(), self.selected);
            }
            17 => {
                edit("quantity", "3");
                edit("purpose", "客户会议演示");
                app.invoke_field_selected("employeeId".into(), 1);
                app.invoke_save_form();
            }
            18 => {
                self.selected = app
                    .get_office_cards()
                    .row_data(0)
                    .ok_or("领用登记未保存。")?
                    .id;
                app.invoke_office_action(ISSUE_OFFICE_SUPPLY.id.into(), self.selected);
            }
            19 => {
                edit("note", "现场发放");
                app.invoke_save_form();
            }
            20 => app.invoke_office_action(RETURN_OFFICE_SUPPLY.id.into(), self.selected),
            21 => {
                edit("quantity", "1");
                edit("note", "先归还一台");
                app.invoke_save_form();
            }
            22 => {
                if app
                    .get_office_cards()
                    .row_data(0)
                    .is_none_or(|row| row.state != "部分归还")
                {
                    return Err("部分归还数量没有正确展示。".into());
                }
                snapshot(ui, &output.join("32-supply-partial-return.png"))?;
                app.invoke_office_action(RETURN_OFFICE_SUPPLY.id.into(), self.selected);
            }
            23 => {
                edit("note", "剩余两台已归还");
                app.invoke_save_form();
            }
            24 => {
                if app
                    .get_office_cards()
                    .row_data(0)
                    .is_none_or(|row| row.state != "已归还")
                {
                    return Err("完整归还没有结束登记。".into());
                }
                app.invoke_office_action("history".into(), self.selected);
            }
            25 => {
                if app.get_records().row_count() != 4 {
                    return Err("领用或部分归还处理历史缺失。".into());
                }
                snapshot(ui, &output.join("33-supply-return-history.png"))?;
                self.checks
                    .push("native-supply-restock-reserve-issue-partial-return-history".into());
                return Ok(true);
            }
            _ => return Ok(true),
        }
        self.stage += 1;
        Ok(false)
    }
}
