use super::*;
use crate::{BackupRow, model};
use serde_json::{Value, json};

impl Desktop {
    pub fn backups_loaded(&mut self, value: Value) {
        let response = match serde_json::from_value::<ApiBackupListResponse>(value) {
            Ok(response) => response,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let selected = self
            .backups
            .get(app.get_backup_index().max(0) as usize)
            .map(|backup| backup.file_name.clone());
        self.backups = response.backups;
        app.set_backup_root(response.backup_root.into());
        app.set_backup_policy(response.storage_policy.into());
        app.set_backup_names(model(
            self.backups
                .iter()
                .map(|backup| backup.file_name.clone().into())
                .collect(),
        ));
        app.set_backup_index(
            selected
                .and_then(|name| {
                    self.backups
                        .iter()
                        .position(|backup| backup.file_name == name)
                })
                .map_or(0, |index| index as i32),
        );
        app.set_backup_rows(model(
            self.backups
                .iter()
                .enumerate()
                .map(|(index, backup)| BackupRow {
                    index: index as i32,
                    name: backup.file_name.clone().into(),
                    size: format!("{:.2} MiB", backup.size_bytes as f64 / 1024.0 / 1024.0).into(),
                    created: backup.created_at.clone().into(),
                    modified: backup.last_write_time.clone().into(),
                })
                .collect(),
        ));
        app.set_backup_confirmation("".into());
    }
    pub fn maintenance_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        match action {
            "create" => self.request(CREATE_DATABASE_BACKUP, 0, vec![], None, "backup-created"),
            "restore" => {
                let Some(backup) = self.backups.get(app.get_backup_index().max(0) as usize) else {
                    self.error("请先选择备份。");
                    return;
                };
                if app.get_backup_confirmation() != "RESTORE" {
                    self.error("请输入 RESTORE 确认还原数据库。");
                    return;
                }
                let body = json!({"backupFileName":backup.file_name,"confirmationText":"RESTORE"});
                self.confirm(Pending::Maintenance(RESTORE_DATABASE_BACKUP,body),&format!("将使用 {} 还原当前数据。程序会先保留当前数据库的备份，还原后需要重新登录。",backup.file_name));
            }
            "cleanup" => {
                let days = match app.get_backup_days().parse::<i64>() {
                    Ok(days) if (1..=3650).contains(&days) => days,
                    _ => {
                        self.error("保留天数须为 1–3650 的整数。");
                        return;
                    }
                };
                self.confirm(
                    Pending::Maintenance(CLEANUP_DATABASE_BACKUPS, json!({"daysToKeep":days})),
                    &format!("清理 {days} 天以前的旧备份？最新一份备份会保留。"),
                );
            }
            "copy" => {
                if !self.can(LIST_DATABASE_BACKUPS) {
                    self.error("当前账号没有备份管理权限。");
                    return;
                }
                let Some(backup) = self.backups.get(app.get_backup_index().max(0) as usize) else {
                    self.error("请先选择备份。");
                    return;
                };
                if let Some(destination) =
                    self.platform
                        .choose_destination(ui.window(), &backup.file_name, &["sqlite3"])
                {
                    self.start(Work::SaveBackupCopy {
                        file_name: backup.file_name.clone(),
                        destination,
                    });
                }
            }
            _ => self.refresh(),
        }
    }
    pub fn mutate_maintenance(&mut self, operation: Operation, body: Value) {
        if !self.can(operation) {
            self.error("当前账号没有备份管理权限。");
            return;
        }
        self.request(
            operation,
            0,
            vec![],
            Some(body),
            if operation == RESTORE_DATABASE_BACKUP {
                "backup-restored"
            } else {
                "backup-cleaned"
            },
        );
    }
}
