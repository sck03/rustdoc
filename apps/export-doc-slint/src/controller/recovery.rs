use super::*;
use crate::Recovery;
use export_doc_engine::generated_api::{
    ApiCloudBackupCommandResponse, ApiCloudBackupListResponse, ApiCloudBackupStatusResponse,
    ApiDisasterRecoveryStatusResponse, ApiPostgreSqlPhysicalBackupListResponse,
    ApiServerMigrationStatusResponse, ApiSharedDatabaseOwnershipSummaryResponse,
    BackgroundJobSnapshot,
};
use serde_json::{Value, json};

impl Desktop {
    pub fn refresh_recovery(&mut self) {
        if self.task.is_some() {
            return;
        }
        self.start(Work::RecoveryStatus);
        if self.can(LIST_DATABASE_BACKUPS) {
            self.request(LIST_DATABASE_BACKUPS, 0, vec![], None, "backup-list");
        }
    }
    fn recovery_request(&mut self, operation: Operation, body: Value, reply: &str) {
        if !self.can(operation) {
            self.error("当前账号没有此维护操作的权限或能力。");
            return;
        }
        self.request(operation, 0, vec![], Some(body), reply);
    }
    pub fn recovery_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Recovery>();
        self.recovery.cloud_backup_index = view.get_cloud_backup_index().max(0) as usize;
        self.recovery.team_backup_index = view.get_team_backup_index().max(0) as usize;
        match action {
            "status" => self.refresh_recovery(),
            "create" => {
                let password = view.get_password().to_string();
                let confirm = view.get_password_confirm().to_string();
                self.recovery.password = password.clone();
                self.recovery.password_confirm = confirm.clone();
                if password.len() < 12 {
                    self.error("恢复包密码至少需要 12 位。");
                    return;
                }
                if password != confirm {
                    self.error("两次输入的密码不一致。");
                    return;
                }
                self.request(
                    CREATE_DISASTER_RECOVERY_PACKAGE,
                    0,
                    vec![],
                    Some(json!({ "password": password })),
                    "recovery:created",
                );
            }
            "choose" => {
                if let Some(path) =
                    self.platform
                        .choose_source(ui.window(), "灾备恢复包", &["edmrecovery", "zip"])
                {
                    self.recovery.package_path = path.to_string_lossy().into_owned();
                    ui.global::<Recovery>()
                        .set_package_path(self.recovery.package_path.clone().into());
                }
            }
            "restore" => {
                let package_path = view.get_package_path().trim().to_string();
                let password = view.get_restore_password().to_string();
                let confirmation = view.get_restore_confirmation().trim().to_string();
                self.recovery.package_path = package_path.clone();
                self.recovery.restore_password = password.clone();
                self.recovery.restore_confirmation = confirmation.clone();
                if package_path.is_empty() {
                    self.error("请先选择待恢复的灾备包。");
                    return;
                }
                if password.is_empty() {
                    self.error("请输入恢复包密码。");
                    return;
                }
                if confirmation != "RECOVER" {
                    self.error("请输入 RECOVER 确认恢复。");
                    return;
                }
                let body = json!({
                    "packagePath": package_path,
                    "password": password,
                    "confirmationText": confirmation
                });
                self.confirm(
                    Pending::RecoveryRestore(body),
                    "将使用所选灾备包替换当前数据。恢复完成后需要重新登录，并按当前机器码重新激活授权。",
                );
            }
            "test-cloud" => {
                self.request(
                    TEST_CLOUD_BACKUP_CONNECTION,
                    0,
                    vec![],
                    None,
                    "recovery:cloud-test",
                );
            }
            "cloud-upload" => {
                self.recovery_request(
                    UPLOAD_LATEST_DATABASE_BACKUP_TO_CLOUD,
                    json!({}),
                    "recovery:job",
                );
            }
            "cloud-list" => {
                self.recovery_request(
                    LIST_CLOUD_DATABASE_BACKUPS,
                    json!({}),
                    "recovery:cloud-list",
                );
            }
            "cloud-download" => {
                let names = self.recovery.cloud_backups.clone();
                let Some(item) = names.get(view.get_cloud_backup_index().max(0) as usize) else {
                    self.error("请先选择云端备份。");
                    return;
                };
                if !self.can(DOWNLOAD_CLOUD_DATABASE_BACKUP) {
                    self.error("当前账号没有下载云端备份的权限。");
                    return;
                }
                self.confirm(
                    Pending::RecoveryAction(
                        DOWNLOAD_CLOUD_DATABASE_BACKUP,
                        json!({
                            "remoteFileName": item.file_name,
                            "confirmationText": "DOWNLOAD"
                        }),
                        "recovery:job".into(),
                    ),
                    "云端备份会先下载到运行目录并校验,不会自动替换当前数据库。",
                );
            }
            "team-list" => {
                self.recovery_request(
                    LIST_POSTGRE_SQL_PHYSICAL_BACKUPS,
                    json!({}),
                    "recovery:team-list",
                );
            }
            "team-create" => {
                self.recovery_request(
                    CREATE_POSTGRE_SQL_PHYSICAL_BACKUP,
                    json!({}),
                    "recovery:job",
                );
            }
            "team-download" => {
                let names = self.recovery.team_backup_names();
                let Some(name) = names.get(view.get_team_backup_index().max(0) as usize) else {
                    self.error("请先选择团队库备份。");
                    return;
                };
                if !self.can(CREATE_POSTGRE_SQL_PHYSICAL_BACKUP_DOWNLOAD_TICKET) {
                    self.error("当前账号没有下载团队库备份的权限。");
                    return;
                }
                if let Some(destination) =
                    self.platform
                        .choose_destination(ui.window(), name, &["dump"])
                {
                    self.start(Work::BinarySave {
                        operation: CREATE_POSTGRE_SQL_PHYSICAL_BACKUP_DOWNLOAD_TICKET,
                        parameters: vec![],
                        query: vec![],
                        destination,
                        limit: 4 * 1024 * 1024 * 1024,
                    });
                }
            }
            "migration-status" => {
                self.recovery_request(
                    GET_SERVER_MIGRATION_STATUS,
                    json!({}),
                    "recovery:migration-status",
                );
            }
            "migration-create" => {
                let password = self.recovery.migration_password.clone();
                let admin = self.recovery.migration_admin_password.clone();
                if password.len() < 8 {
                    self.error("迁移包密码至少需要 8 位。");
                    return;
                }
                if self.recovery.migration_confirmation != "MIGRATE" {
                    self.error("请输入 MIGRATE 确认创建迁移包。");
                    return;
                }
                self.confirm(
                    Pending::RecoveryAction(
                        CREATE_SERVER_MIGRATION_PACKAGE,
                        json!({
                            "password": password,
                            "adminPassword": admin,
                            "confirmationText": "MIGRATE"
                        }),
                        "recovery:job".into(),
                    ),
                    "将创建包含当前 PostgreSQL 物理备份的加密迁移包。请确认管理员密码和目标环境。",
                );
            }
            "ownership" => {
                self.recovery_request(
                    GET_SHARED_DATABASE_OWNERSHIP_SUMMARY,
                    json!({}),
                    "recovery:ownership",
                );
            }
            "ownership-transfer" => {
                let body = json!({
                    "fromUserId": self.recovery.ownership_from_user_id,
                    "toUserId": self.recovery.ownership_to_user_id,
                    "includeInvoices": self.recovery.ownership_include_invoices,
                    "includePayments": self.recovery.ownership_include_payments,
                    "includeOtherBusinessData": self.recovery.ownership_include_other,
                    "onlyUnassigned": self.recovery.ownership_only_unassigned,
                    "departmentId": self.recovery.ownership_department_id,
                    "companyScope": self.recovery.ownership_company_scope,
                    "confirmationText": self.recovery.ownership_confirmation
                });
                if self.recovery.ownership_confirmation != "TRANSFER OWNERSHIP" {
                    self.error("请输入 TRANSFER OWNERSHIP 确认归属改派。");
                    return;
                }
                self.confirm(
                    Pending::RecoveryAction(
                        TRANSFER_SHARED_DATABASE_OWNERSHIP,
                        body,
                        "recovery:ownership".into(),
                    ),
                    "归属改派会更新业务数据的所属用户、部门和公司范围。请确认来源与目标用户。",
                );
            }
            _ => {}
        }
    }
    pub fn recovery_loaded(&mut self, reply: &str, value: Value) {
        match reply {
            "recovery:status" => {
                if let Ok(status) = serde_json::from_value::<ApiDisasterRecoveryStatusResponse>(
                    value["status"].clone(),
                ) {
                    self.recovery.status = Some(status);
                } else {
                    self.error("灾备状态响应不符合接口契约。");
                }
                match serde_json::from_value::<ApiCloudBackupStatusResponse>(value["cloud"].clone())
                {
                    Ok(cloud) => {
                        self.recovery.cloud = Some(cloud);
                        self.recovery.cloud_error.clear();
                    }
                    Err(_) => {
                        self.recovery.cloud = None;
                        self.recovery.cloud_error = value["cloudError"]
                            .as_str()
                            .unwrap_or("云端备份状态不可用。")
                            .into();
                    }
                }
                self.sync_recovery();
            }
            "recovery:created" | "recovery:restored" => {
                match serde_json::from_value::<BackgroundJobSnapshot>(value) {
                    Ok(job) => {
                        self.recovery.cloud_message = String::new();
                        self.sync_recovery();
                        self.status(format!(
                            "已提交后台任务：{}（{}）",
                            job.title,
                            if job.status_text.is_empty() {
                                job.status
                            } else {
                                job.status_text
                            }
                        ));
                    }
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            "recovery:job" => match serde_json::from_value::<BackgroundJobSnapshot>(value) {
                Ok(job) => self.status(format!("已提交后台任务:{}({})", job.title, job.status)),
                Err(cause) => self.error(cause.to_string()),
            },
            "recovery:cloud-list" => {
                match serde_json::from_value::<ApiCloudBackupListResponse>(value) {
                    Ok(response) => {
                        self.recovery.cloud_backups = response.backups;
                        self.recovery.cloud_backup_index = 0;
                        self.recovery.cloud_message = format!(
                            "云端备份 {} 份,保存位置 {}",
                            self.recovery.cloud_backups.len(),
                            response.backup_root
                        );
                        self.sync_recovery();
                    }
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            "recovery:team-list" => {
                match serde_json::from_value::<ApiPostgreSqlPhysicalBackupListResponse>(value) {
                    Ok(response) => {
                        self.recovery.team = Some(response);
                        self.recovery.team_error.clear();
                        self.recovery.team_backup_index = 0;
                        self.sync_recovery();
                    }
                    Err(cause) => {
                        self.recovery.team = None;
                        self.recovery.team_error = cause.to_string();
                        self.sync_recovery();
                    }
                }
            }
            "recovery:migration-status" => {
                match serde_json::from_value::<ApiServerMigrationStatusResponse>(value) {
                    Ok(response) => {
                        self.recovery.migration = Some(response);
                        self.recovery.migration_error.clear();
                        self.sync_recovery();
                    }
                    Err(cause) => {
                        self.recovery.migration = None;
                        self.recovery.migration_error = cause.to_string();
                        self.sync_recovery();
                    }
                }
            }
            "recovery:ownership" => {
                match serde_json::from_value::<ApiSharedDatabaseOwnershipSummaryResponse>(value) {
                    Ok(response) => {
                        self.recovery.ownership = Some(response);
                        self.recovery.ownership_error.clear();
                        self.sync_recovery();
                    }
                    Err(cause) => {
                        self.recovery.ownership = None;
                        self.recovery.ownership_error = cause.to_string();
                        self.sync_recovery();
                    }
                }
            }
            "recovery:cloud-test" => {
                match serde_json::from_value::<ApiCloudBackupCommandResponse>(value) {
                    Ok(response) => {
                        self.recovery.cloud_message = response.message.clone();
                        self.sync_recovery();
                        if response.success {
                            self.status(response.message);
                        } else {
                            self.error(response.message);
                        }
                    }
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            _ => {}
        }
    }
    pub fn sync_recovery(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Recovery>();
        let recovery = &self.recovery;
        view.set_loaded(recovery.status.is_some());
        view.set_supported(
            recovery
                .status
                .as_ref()
                .is_some_and(|status| status.supported),
        );
        view.set_can_create(recovery.can_create());
        view.set_can_restore(recovery.can_restore());
        view.set_pending_restore(
            recovery
                .status
                .as_ref()
                .is_some_and(|status| status.pending_restore),
        );
        view.set_recovery_root(recovery.recovery_root().into());
        view.set_message(recovery.message().into());
        view.set_storage_policy(recovery.storage_policy().into());
        view.set_password(recovery.password.clone().into());
        view.set_password_confirm(recovery.password_confirm.clone().into());
        view.set_package_path(recovery.package_path.clone().into());
        view.set_restore_password(recovery.restore_password.clone().into());
        view.set_restore_confirmation(recovery.restore_confirmation.clone().into());
        view.set_cloud_configured(
            recovery
                .cloud
                .as_ref()
                .is_some_and(|cloud| cloud.is_configured),
        );
        view.set_cloud_summary(
            if !recovery.cloud_error.is_empty() {
                format!("无法读取云端备份状态：{}", recovery.cloud_error)
            } else {
                recovery.cloud_summary()
            }
            .into(),
        );
        view.set_cloud_url(recovery.cloud_url().into());
        view.set_cloud_user(recovery.cloud_user().into());
        view.set_cloud_latest(recovery.cloud_latest().into());
        view.set_cloud_message(recovery.cloud_message.clone().into());
        view.set_team_summary(recovery.team_summary().into());
        view.set_team_names(model(
            recovery
                .team_backup_names()
                .into_iter()
                .map(Into::into)
                .collect(),
        ));
        view.set_team_backup_index(recovery.team_backup_index as i32);
        view.set_migration_summary(recovery.migration_summary().into());
        view.set_ownership_summary(recovery.ownership_summary().into());
        view.set_ownership_users(model(
            recovery
                .ownership_users()
                .into_iter()
                .map(|(_, name)| name.into())
                .collect(),
        ));
        view.set_ownership_to_index(
            recovery
                .ownership_users()
                .iter()
                .position(|(id, _)| *id == recovery.ownership_to_user_id)
                .map_or(0, |index| index as i32),
        );
        view.set_ownership_from_index(
            recovery
                .ownership_users()
                .iter()
                .position(|(id, _)| *id == recovery.ownership_from_user_id)
                .map_or(0, |index| index as i32),
        );
    }
}
