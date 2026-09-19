//! Disaster recovery package and cloud backup view model.
use export_doc_engine::generated_api::{
    ApiCloudBackupItemDto, ApiCloudBackupStatusResponse, ApiDisasterRecoveryStatusResponse,
    ApiPostgreSqlPhysicalBackupListResponse, ApiServerMigrationStatusResponse,
    ApiSharedDatabaseOwnershipSummaryResponse,
};
#[derive(Clone, Default)]
pub struct RecoveryModel {
    pub status: Option<ApiDisasterRecoveryStatusResponse>,
    pub cloud: Option<ApiCloudBackupStatusResponse>,
    pub cloud_backups: Vec<ApiCloudBackupItemDto>,
    pub team: Option<ApiPostgreSqlPhysicalBackupListResponse>,
    pub migration: Option<ApiServerMigrationStatusResponse>,
    pub ownership: Option<ApiSharedDatabaseOwnershipSummaryResponse>,
    pub team_error: String,
    pub migration_error: String,
    pub ownership_error: String,
    pub cloud_backup_index: usize,
    pub team_backup_index: usize,
    pub cloud_error: String,
    pub cloud_message: String,
    pub password: String,
    pub password_confirm: String,
    pub package_path: String,
    pub restore_password: String,
    pub restore_confirmation: String,
    pub migration_password: String,
    pub migration_confirmation: String,
    pub migration_admin_password: String,
    pub ownership_from_user_id: i64,
    pub ownership_to_user_id: i64,
    pub ownership_include_invoices: bool,
    pub ownership_include_payments: bool,
    pub ownership_include_other: bool,
    pub ownership_only_unassigned: bool,
    pub ownership_department_id: String,
    pub ownership_company_scope: String,
    pub ownership_confirmation: String,
}

impl RecoveryModel {
    pub fn can_create(&self) -> bool {
        self.status.as_ref().is_some_and(|status| status.supported)
            && self.password.len() >= 12
            && self.password == self.password_confirm
    }
    pub fn can_restore(&self) -> bool {
        self.status.as_ref().is_some_and(|status| status.supported)
            && !self.package_path.is_empty()
            && !self.restore_password.is_empty()
            && self.restore_confirmation == "RECOVER"
    }
    pub fn recovery_root(&self) -> String {
        self.status
            .as_ref()
            .map(|status| status.recovery_root.clone())
            .unwrap_or_default()
    }
    pub fn storage_policy(&self) -> String {
        self.status
            .as_ref()
            .map(|status| status.storage_policy.clone())
            .unwrap_or_default()
    }
    pub fn message(&self) -> String {
        self.status
            .as_ref()
            .map(|status| status.message.clone())
            .unwrap_or_default()
    }
    pub fn cloud_url(&self) -> String {
        self.cloud
            .as_ref()
            .map(|cloud| cloud.url.clone())
            .unwrap_or_default()
    }
    pub fn cloud_user(&self) -> String {
        self.cloud
            .as_ref()
            .map(|cloud| cloud.user_name.clone())
            .unwrap_or_default()
    }
    pub fn cloud_latest(&self) -> String {
        let Some(cloud) = &self.cloud else {
            return String::new();
        };
        if cloud.latest_backup_file_name.is_empty() {
            return "尚未上传".into();
        }
        format!(
            "{}（{:.2} MiB）",
            cloud.latest_backup_file_name,
            cloud.latest_backup_size_bytes as f64 / 1024.0 / 1024.0
        )
    }
    pub fn cloud_summary(&self) -> String {
        let Some(cloud) = &self.cloud else {
            return "未配置云端备份".into();
        };
        if !cloud.is_configured {
            return "尚未配置云端备份（WebDAV），可在“备份与恢复”设置中填写。".into();
        }
        format!(
            "已配置：{}{}",
            cloud.url,
            if cloud.user_name.is_empty() {
                String::new()
            } else {
                format!("（账号 {}）", cloud.user_name)
            }
        )
    }
    pub fn team_summary(&self) -> String {
        match &self.team {
            None if !self.team_error.is_empty() => {
                format!("团队库备份状态不可用:{}", self.team_error)
            }
            None => "当前不是 PostgreSQL 团队库模式。".into(),
            Some(team) if !team.status.postgre_sql_selected => {
                "当前为 SQLite 单机模式,团队库物理备份不可用。".into()
            }
            Some(team) if !team.status.postgre_sql_configured => {
                "PostgreSQL 连接未完整配置。".into()
            }
            Some(team) if !team.status.tools_ready => {
                "PostgreSQL 客户端工具未就绪,请部署 pg_dump/pg_restore。".into()
            }
            Some(team) => format!(
                "{}:{}({} 份物理备份)",
                team.status.host,
                team.status.database,
                team.backups.len()
            ),
        }
    }
    pub fn team_backup_names(&self) -> Vec<String> {
        self.team
            .as_ref()
            .map(|team| {
                team.backups
                    .iter()
                    .map(|backup| backup.file_name.clone())
                    .collect()
            })
            .unwrap_or_default()
    }
    pub fn migration_summary(&self) -> String {
        match &self.migration {
            None if !self.migration_error.is_empty() => {
                format!("服务器迁移状态不可用:{}", self.migration_error)
            }
            None => "尚未读取服务器迁移状态。".into(),
            Some(status) => status.message.clone(),
        }
    }
    pub fn ownership_summary(&self) -> String {
        match &self.ownership {
            None if !self.ownership_error.is_empty() => {
                format!("归属摘要不可用:{}", self.ownership_error)
            }
            None => "尚未读取共享库归属摘要。".into(),
            Some(summary) => format!(
                "发票 {} 条(未归属 {}),付款 {} 条(未归属 {}),其它 {} 条(未归属 {})",
                summary.total_invoices,
                summary.unassigned_invoices,
                summary.total_payments,
                summary.unassigned_payments,
                summary.total_other_business_data,
                summary.unassigned_other_business_data
            ),
        }
    }
    pub fn ownership_users(&self) -> Vec<(i64, String)> {
        self.ownership
            .as_ref()
            .map(|summary| {
                summary
                    .owners
                    .iter()
                    .map(|owner| {
                        (
                            owner.user_id,
                            if owner.full_name.is_empty() {
                                owner.username.clone()
                            } else {
                                format!("{} ({})", owner.full_name, owner.username)
                            },
                        )
                    })
                    .collect()
            })
            .unwrap_or_default()
    }
}
