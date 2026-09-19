use super::*;
use crate::Audit;
use export_doc_engine::{clock::BusinessClock, engine::audit::ENTITIES};
use serde_json::{Value, json};

impl Desktop {
    pub fn setup_audit(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let audit = ui.global::<Audit>();
        audit.set_entities(model(
            std::iter::once("全部实体".into())
                .chain(ENTITIES.iter().map(|(_, _, label)| (*label).into()))
                .collect(),
        ));
        audit.set_can_manage(self.can(DELETE_AUDIT_LOGS_BY_CRITERIA));
        audit.set_can_export(self.can(SAVE_AUDIT_LOGS_TO_PATH));
        if let Some(user) = &self.user {
            audit.set_zone(user.business_time_zone.clone().into());
            if let Ok(clock) = BusinessClock::new(&user.business_time_zone) {
                let now = chrono::Utc::now();
                audit.set_start(
                    clock
                        .local_input(&(now - chrono::Duration::days(7)).to_rfc3339())
                        .unwrap_or_default()
                        .into(),
                );
                audit.set_end(
                    clock
                        .local_input(&now.to_rfc3339())
                        .unwrap_or_default()
                        .into(),
                );
            }
        }
    }
    fn audit_filters(&self) -> Result<Value, String> {
        let ui = self.ui.upgrade().ok_or("窗口已关闭。")?;
        let audit = ui.global::<Audit>();
        let entity = audit
            .get_entity_index()
            .checked_sub(1)
            .and_then(|index| ENTITIES.get(index as usize))
            .map(|(_, name, _)| *name)
            .unwrap_or("");
        let action = ["", "Added", "Modified", "Deleted"]
            .get(audit.get_action_index() as usize)
            .copied()
            .unwrap_or("");
        let mut body = json!({"invoiceKeyword":audit.get_invoice().to_string(),"entityName":entity,
            "action":action,
            "userId":audit.get_user().to_string(),"keyword":ui.global::<App>().get_search().to_string(),"maxCount":50000});
        if audit.get_date_range() {
            let user = self.user.as_ref().ok_or("请先登录。")?;
            let clock = BusinessClock::new(&user.business_time_zone)?;
            let start = clock.parse_local_input(&audit.get_start())?;
            let end = clock.parse_local_input(&audit.get_end())?;
            if start > end {
                return Err("结束时间不能早于开始时间。".into());
            }
            body["startTime"] = json!(start.to_rfc3339());
            body["endTime"] = json!(end.to_rfc3339());
        }
        Ok(body)
    }
    pub fn refresh_audit(&mut self) {
        match self.audit_filters() {
            Ok(filters) => {
                let mut query = vec![
                    ("pageNumber", self.workspace.page.to_string()),
                    ("pageSize", "50".into()),
                ];
                for key in [
                    "invoiceKeyword",
                    "entityName",
                    "action",
                    "userId",
                    "keyword",
                    "startTime",
                    "endTime",
                ] {
                    if let Some(value) = filters[key].as_str().filter(|value| !value.is_empty()) {
                        query.push((key, value.into()));
                    }
                }
                self.request(LIST_AUDIT_LOGS, 0, query, None, "list:audit");
            }
            Err(error) => self.error(error),
        }
    }
    pub fn audit_select(&self, record: &Value) {
        let pretty = |key: &str| {
            record[key]
                .as_str()
                .map(|value| {
                    serde_json::from_str::<Value>(value)
                        .and_then(|value| serde_json::to_string_pretty(&value))
                        .unwrap_or_else(|_| value.into())
                })
                .unwrap_or_default()
        };
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<Audit>().set_detail(
                format!(
                    "{} · {} · 操作人 {}\n原值：{}\n新值：{}",
                    record["entityName"].as_str().unwrap_or(""),
                    record["entityId"].as_str().unwrap_or(""),
                    record["userId"].as_str().unwrap_or(""),
                    pretty("oldValues"),
                    pretty("newValues")
                )
                .into(),
            );
        }
    }
    pub fn audit_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let audit = ui.global::<Audit>();
        if action == "reset" {
            ui.global::<App>().set_search("".into());
            audit.set_entity_index(0);
            audit.set_action_index(0);
            audit.set_invoice("".into());
            audit.set_user("".into());
            audit.set_date_range(false);
            audit.set_detail("".into());
        }
        if action == "search" || action == "reset" {
            self.workspace.page = 1;
            self.refresh_audit();
            return;
        }
        let mut body = match self.audit_filters() {
            Ok(body) => body,
            Err(error) => {
                self.error(error);
                return;
            }
        };
        match action {
            "export" => {
                if let Some(path) =
                    self.platform
                        .choose_destination(ui.window(), "AuditLogs.xlsx", &["xlsx"])
                {
                    body["destinationPath"] = json!(path);
                    self.request(
                        SAVE_AUDIT_LOGS_TO_PATH,
                        0,
                        vec![],
                        Some(body),
                        "audit-exported",
                    );
                }
            }
            "delete" => {
                if [
                    "invoiceKeyword",
                    "entityName",
                    "action",
                    "userId",
                    "keyword",
                    "startTime",
                    "endTime",
                ]
                .iter()
                .all(|key| {
                    body[*key]
                        .as_str()
                        .is_none_or(|value| value.trim().is_empty())
                }) {
                    self.error("请先设置筛选条件；清理较早的日志请使用保留天数。");
                    return;
                }
                body["confirmed"] = json!(true);
                self.confirm(
                    Pending::AuditMaintenance(DELETE_AUDIT_LOGS_BY_CRITERIA, body),
                    "确认删除当前筛选条件下的审计记录？此操作不可撤销，业务数据及处理历史会保留。",
                );
            }
            "cleanup" => {
                let Ok(days) = audit.get_days().parse::<u32>() else {
                    self.error("请输入有效的保留天数。");
                    return;
                };
                if days == 0 {
                    self.error("保留天数必须大于零。");
                    return;
                }
                self.confirm(
                    Pending::AuditMaintenance(
                        CLEANUP_AUDIT_LOGS,
                        json!({"daysToKeep":days,"confirmed":true,"maxCount":200000}),
                    ),
                    &format!("确认清理 {days} 天以前的审计日志？业务数据及处理历史会保留。"),
                );
            }
            _ => {}
        }
    }
}
