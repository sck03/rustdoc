use chrono::{Datelike, NaiveDateTime, NaiveTime};
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct BackupPlan {
    pub enabled: bool,
    pub frequency: String,
    pub time: String,
    pub weekday: u32,
    pub retention_count: usize,
    pub retention_days: u32,
    pub replica_mode: String,
    pub verify_replica: bool,
    pub s3: S3Settings,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct S3Settings {
    pub endpoint: String,
    pub region: String,
    pub bucket: String,
    pub prefix: String,
    pub access_key_id: String,
    pub path_style: bool,
}
impl Default for S3Settings {
    fn default() -> Self {
        Self {
            endpoint: String::new(),
            region: "us-east-1".into(),
            bucket: String::new(),
            prefix: "ExportDocManager".into(),
            access_key_id: String::new(),
            path_style: true,
        }
    }
}
impl Default for BackupPlan {
    fn default() -> Self {
        Self {
            enabled: false,
            frequency: "Daily".into(),
            time: "02:00".into(),
            weekday: 1,
            retention_count: 14,
            retention_days: 0,
            replica_mode: "None".into(),
            verify_replica: true,
            s3: Default::default(),
        }
    }
}
impl BackupPlan {
    pub fn validate(&self) -> Result<(), String> {
        if !matches!(self.frequency.as_str(), "Daily" | "Weekly")
            || self.weekday > 6
            || !(1..=365).contains(&self.retention_count)
            || self.retention_days > 3650
        {
            return Err("备份周期或保留策略无效；保留份数为 1–365，保留天数为 0–3650。".into());
        }
        let time =
            NaiveTime::parse_from_str(&self.time, "%H:%M").map_err(|_| "备份时间须为 HH:MM。")?;
        if time.format("%H:%M").to_string() != self.time {
            return Err("备份时间须为 HH:MM。".into());
        }
        if !matches!(self.replica_mode.as_str(), "None" | "WebDav" | "S3") {
            return Err("备份副本目标无效。".into());
        }
        if self.replica_mode == "S3"
            && (self.s3.endpoint.is_empty()
                || self.s3.region.is_empty()
                || self.s3.bucket.is_empty()
                || self.s3.access_key_id.is_empty())
        {
            return Err("请填写 S3 端点、区域、桶和访问密钥 ID。".into());
        }
        Ok(())
    }
    pub fn due_slot(&self, local: NaiveDateTime) -> Option<String> {
        if !self.enabled
            || (self.frequency == "Weekly"
                && local.weekday().num_days_from_sunday() != self.weekday)
            || local.time() < NaiveTime::parse_from_str(&self.time, "%H:%M").ok()?
        {
            return None;
        }
        Some(format!("{} {}", local.date(), self.time))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn schedules_are_opt_in_and_use_the_business_day() {
        let now = NaiveDateTime::parse_from_str("2026-10-14 03:00", "%Y-%m-%d %H:%M").unwrap();
        let mut plan = BackupPlan::default();
        assert_eq!(plan.due_slot(now), None);
        plan.enabled = true;
        assert_eq!(plan.due_slot(now).unwrap(), "2026-10-14 02:00");
        plan.frequency = "Weekly".into();
        assert_eq!(plan.due_slot(now), None);
        plan.weekday = 3;
        assert!(plan.due_slot(now).is_some());
        plan.time = "25:00".into();
        assert!(plan.validate().is_err());
    }
}
