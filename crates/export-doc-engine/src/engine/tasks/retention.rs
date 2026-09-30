//! Bounded history policy, injected by the host; only terminal jobs are pruned.
use super::persistence::{owner, remove};
use crate::engine::{
    error::{Result, invalid},
    store::Store,
};
use chrono::{DateTime, Duration, Utc};

#[derive(Clone, Copy, Debug)]
pub struct Retention {
    pub days: u32,
    pub global_limit: usize,
    pub per_user_limit: usize,
}
impl Default for Retention {
    fn default() -> Self {
        Self {
            days: 30,
            global_limit: 2000,
            per_user_limit: 200,
        }
    }
}
impl Retention {
    pub fn from_lookup(mut lookup: impl FnMut(&str) -> Option<String>) -> Result<Self> {
        let mut read = |key: &str, default: usize, min: usize, max: usize| match lookup(key)
            .filter(|value| !value.trim().is_empty())
        {
            None => Ok(default),
            Some(value) => value
                .trim()
                .parse::<usize>()
                .ok()
                .filter(|value| (min..=max).contains(value))
                .ok_or_else(|| invalid(format!("{key} 必须为 {min}–{max} 之间的整数。"))),
        };
        Ok(Self {
            days: read("EXPORTDOCMANAGER_JOB_HISTORY_DAYS", 30, 1, 3650)? as u32,
            global_limit: read("EXPORTDOCMANAGER_JOB_HISTORY_LIMIT", 2000, 100, 100_000)?,
            per_user_limit: read("EXPORTDOCMANAGER_JOB_USER_HISTORY_LIMIT", 200, 20, 10_000)?,
        })
    }
    pub(super) fn validate(self) -> Result<Self> {
        if self.days == 0
            || self.days > 3650
            || self.global_limit == 0
            || self.global_limit > 100_000
            || self.per_user_limit == 0
            || self.per_user_limit > 10_000
        {
            return Err(invalid("文件任务保留策略超出有效范围。"));
        }
        Ok(self)
    }
}

pub(super) fn prune(store: &Store, policy: Retention, now: DateTime<Utc>) -> Result<usize> {
    store.transaction(|tx| {
        let cutoff = (now - Duration::days(i64::from(policy.days))).to_rfc3339();
        let fields = super::queries::metadata_fields();
        let mut removed = 0;
        loop {
            crate::operation::check()?;
            let mut query = super::queries::batch(None, false, &fields);
            query.retention = Some(export_doc_storage::JobRetention {
                cutoff: &cutoff,
                per_user: policy.per_user_limit,
                global: policy.global_limit,
            });
            let (_, jobs) = tx.query_jobs(&query)?;
            if jobs.is_empty() {
                break;
            }
            for job in jobs {
                remove(tx, &owner(&job)?, job)?;
                removed += 1;
            }
        }
        Ok(removed)
    })
}
