use super::*;
use crate::License;
use export_doc_engine::generated_api::{
    ApiLicenseRegisterResponse, ApiLicenseStatusResponse, ApiSupportPackageResponse,
};
use serde_json::{Value, json};

impl Desktop {
    fn support_package_body(&self, view: &License) -> Value {
        let include_backup = view.get_include_database_backup();
        let include_samples = view.get_include_sample_files();
        json!({
            "includeLatestDatabaseBackup": include_backup,
            "includeSampleFiles": include_samples,
            "confirmationText": if include_backup || include_samples {
                "INCLUDE OPTIONAL FILES"
            } else {
                ""
            }
        })
    }
    pub fn license_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<License>();
        match action {
            "status" => {
                self.request(GET_LICENSE_STATUS, 0, vec![], None, "license:status");
            }
            "register" => {
                if !self.can(REGISTER_LICENSE) {
                    self.error("当前账号没有授权管理权限。");
                    return;
                }
                let key = view.get_license_key().trim().to_string();
                if key.is_empty() {
                    self.error("请输入注册码。");
                    return;
                }
                self.license.license_key = key.clone();
                self.request(
                    REGISTER_LICENSE,
                    0,
                    vec![],
                    Some(json!({ "licenseKey": key })),
                    "license:registered",
                );
            }
            "copy" => {
                let machine_id = self.license.machine_id();
                if machine_id.is_empty() {
                    self.error("尚未读取到机器码，请先刷新授权状态。");
                    return;
                }
                match self.platform.copy_text(ui.window(), machine_id) {
                    Ok(()) => self.status("机器码已复制。"),
                    Err(error) => self.error(error),
                }
            }
            "support" => {
                if !self.can(SAVE_SUPPORT_PACKAGE_TO_RUNTIME) {
                    self.error("当前账号没有系统设置管理权限。");
                    return;
                }
                let body = self.support_package_body(&view);
                if body["confirmationText"].as_str().is_some_and(|text| !text.is_empty()) {
                    self.confirm(
                        Pending::SupportPackage(body),
                        "支持包将包含所选的数据库备份或样张文件。请确认其中不含不应交给技术支持的敏感业务资料。",
                    );
                } else {
                    self.request(
                        SAVE_SUPPORT_PACKAGE_TO_RUNTIME,
                        0,
                        vec![],
                        Some(body),
                        "license:support",
                    );
                }
            }
            "download-support" => {
                if !self.can(DOWNLOAD_SUPPORT_PACKAGE) {
                    self.error("当前账号没有系统设置管理权限。");
                    return;
                }
                let body = self.support_package_body(&view);
                if body["confirmationText"].as_str().is_some_and(|text| !text.is_empty()) {
                    self.confirm(
                        Pending::SupportPackageDownload(body),
                        "支持包将包含所选的数据库备份或样张文件。请确认其中不含不应交给技术支持的敏感业务资料。",
                    );
                } else {
                    self.start_support_package_download(body, &ui);
                }
            }
            "cleanup-logs" => {
                if !self.can(CLEANUP_SYSTEM_LOGS) {
                    self.error("当前账号没有系统设置管理权限。");
                    return;
                }
                self.confirm(
                    Pending::LogCleanup,
                    "将按已保存的日志保留设置清理审计日志与文本日志，此操作不可撤销。",
                );
            }
            _ => {}
        }
    }
    fn start_support_package_download(&mut self, body: Value, ui: &AppWindow) {
        let name = format!(
            "support-package-{}.zip",
            chrono::Local::now().format("%Y%m%d%H%M%S")
        );
        let Some(destination) = self.platform.choose_destination(ui.window(), &name, &["zip"])
        else {
            self.status("已取消下载");
            return;
        };
        self.start(Work::SupportPackageDownload { body, destination });
    }
    pub fn license_loaded(&mut self, reply: &str, value: Value) {
        match reply {
            "license:status" => match serde_json::from_value::<ApiLicenseStatusResponse>(value) {
                Ok(status) => {
                    self.license.status = Some(status);
                    self.sync_license();
                }
                Err(cause) => self.error(cause.to_string()),
            },
            "license:registered" => {
                match serde_json::from_value::<ApiLicenseRegisterResponse>(value) {
                    Ok(response) => {
                        if response.success {
                            self.license.status = Some(response.status);
                            self.license.license_key.clear();
                            self.sync_license();
                            self.status(response.message);
                        } else {
                            self.error(response.message);
                        }
                    }
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            "license:support" => match serde_json::from_value::<ApiSupportPackageResponse>(value) {
                Ok(package) => {
                    self.license.package = Some(package);
                    self.sync_license();
                    self.status("支持包已生成到运行目录。");
                }
                Err(cause) => self.error(cause.to_string()),
            },
            "license:logs" => {
                let message = value["message"]
                    .as_str()
                    .unwrap_or("系统日志清理完成")
                    .to_owned();
                self.status(message);
                self.request(GET_LICENSE_STATUS, 0, vec![], None, "license:status");
            }
            _ => {}
        }
    }
    pub fn sync_license(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<License>();
        let license = &self.license;
        view.set_loaded(license.status.is_some());
        view.set_status_text(license.status_text().into());
        view.set_registered(license.registered());
        view.set_machine_id(license.machine_id().into());
        view.set_trial_days(license.trial_days().into());
        view.set_trial_start(license.trial_start().into());
        view.set_days_remaining(license.days_remaining().into());
        view.set_expire_date(license.expire_date().into());
        view.set_message(license.message().into());
        view.set_license_key(license.license_key.clone().into());
        view.set_storage_path(license.storage_path().into());
        view.set_storage_policy(license.storage_policy().into());
        view.set_package_summary(license.package_summary().into());
        view.set_package_root(license.package_root().into());
        view.set_package_path(license.package_path().into());
        view.set_log_retention(self.log_retention_summary().into());
    }
    fn log_retention_summary(&self) -> String {
        let Some(form) = &self.form else {
            return "尚未读取设置".into();
        };
        let days = |key: &str| {
            form.value[key]
                .as_i64()
                .map(|number| number.to_string())
                .or_else(|| form.value[key].as_str().map(str::to_owned))
                .filter(|value| !value.is_empty())
                .unwrap_or_else(|| "未设置".into())
        };
        format!(
            "文本日志保留 {} 天（最多 {} 个文件），审计日志保留 {} 天",
            days("system.logRetentionDays"),
            days("system.logRetainedFileCount"),
            days("system.auditLogRetentionDays")
        )
    }
}