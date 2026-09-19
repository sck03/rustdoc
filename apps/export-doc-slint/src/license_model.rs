//! License status, registration and diagnostic support package view model.
use export_doc_engine::generated_api::{ApiLicenseStatusResponse, ApiSupportPackageResponse};

#[derive(Clone, Default)]
pub struct LicenseModel {
    pub status: Option<ApiLicenseStatusResponse>,
    pub license_key: String,
    pub package: Option<ApiSupportPackageResponse>,
}

impl LicenseModel {
    pub fn status_text(&self) -> String {
        match &self.status {
            Some(status) if status.is_registered => "已注册",
            Some(status) if status.is_trial_expired => "已过期",
            Some(_) => "试用中",
            None => "读取中",
        }
        .into()
    }
    pub fn registered(&self) -> bool {
        self.status
            .as_ref()
            .is_some_and(|status| status.is_registered)
    }
    fn raw(&self, key: &str) -> String {
        self.status
            .as_ref()
            .and_then(|status| match key {
                "machineId" => Some(status.machine_id.clone()),
                "message" => Some(status.message.clone()),
                "licenseStoragePath" => Some(status.license_storage_path.clone()),
                "storagePolicy" => Some(status.storage_policy.clone()),
                _ => None,
            })
            .unwrap_or_default()
    }
    pub fn machine_id(&self) -> String {
        self.raw("machineId")
    }
    pub fn message(&self) -> String {
        self.raw("message")
    }
    pub fn trial_days(&self) -> String {
        self.status
            .as_ref()
            .map(|status| status.trial_days.to_string())
            .unwrap_or_default()
    }
    pub fn days_remaining(&self) -> String {
        match &self.status {
            Some(status) if status.is_registered && status.days_remaining == i64::MAX => {
                "终身授权".into()
            }
            Some(status) => status.days_remaining.to_string(),
            None => String::new(),
        }
    }
    /// The contract seals the trial start date in managed settings; during an
    /// active trial it is derived from the trial window so users can see it.
    pub fn trial_start(&self) -> String {
        let Some(status) = &self.status else {
            return String::new();
        };
        if status.is_registered
            || status.is_trial_expired
            || status.trial_days <= 0
            || status.days_remaining < 0
            || status.days_remaining > status.trial_days
        {
            return "—".into();
        }
        let elapsed = status.trial_days - status.days_remaining;
        (chrono::Local::now().date_naive() - chrono::Duration::days(elapsed)).to_string()
    }
    pub fn expire_date(&self) -> String {
        let Some(status) = &self.status else {
            return String::new();
        };
        if !status.is_registered {
            return "—".into();
        }
        if status.expire_date.starts_with("9999") {
            "终身授权".into()
        } else {
            status.expire_date.clone()
        }
    }
    pub fn storage_path(&self) -> String {
        self.raw("licenseStoragePath")
    }
    pub fn storage_policy(&self) -> String {
        self.raw("storagePolicy")
    }
    pub fn package_summary(&self) -> String {
        let Some(package) = &self.package else {
            return String::new();
        };
        format!(
            "{}（{:.2} MiB）",
            package.file_name,
            package.size_bytes as f64 / 1024.0 / 1024.0
        )
    }
    pub fn package_root(&self) -> String {
        self.package
            .as_ref()
            .map(|package| package.support_package_root.clone())
            .unwrap_or_default()
    }
    pub fn package_path(&self) -> String {
        self.package
            .as_ref()
            .map(|package| package.full_path.clone())
            .unwrap_or_default()
    }
}
