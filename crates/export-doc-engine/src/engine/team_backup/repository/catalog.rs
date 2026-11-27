//! Small display-only catalog. Every request rechecks ownership and the live
//! committed-name set; restore/export always authenticate the full point again.
use super::*;
use crate::engine::operation_lock::OperationLock;
use chrono::{DateTime, FixedOffset};
use serde_json::{Value, json};
use std::time::{Duration, Instant};

#[derive(Default)]
pub(in crate::engine) struct Catalog(OperationLock<Option<Snapshot>>);
struct Snapshot {
    identity: Vec<u8>,
    names: Vec<String>,
    loaded: Instant,
    rows: Vec<(DateTime<FixedOffset>, Value)>,
}
impl Catalog {
    pub(in crate::engine::team_backup) fn page(
        &self,
        repo: &Repository,
        revision: i64,
        number: usize,
        size: usize,
    ) -> Result<Value> {
        let mut snapshot = self.0.write(Duration::from_secs(30))?;
        let mut hash = Sha256::new();
        hash.update(repo.id.as_bytes());
        hash.update(repo.root.as_ref());
        hash.update(revision.to_le_bytes());
        let identity = hash.finalize().to_vec();
        let mut names = Vec::new();
        let mut cursor = String::new();
        loop {
            let page = repo.io.list("point-", &cursor, 1000)?;
            if page.is_empty() {
                break;
            }
            for name in page {
                cursor = name.clone();
                if name.ends_with(".commit") {
                    names.push(name);
                }
                if names.len() > 10_000 {
                    return Err(invalid("恢复点目录超过一万条，请先按保留策略整理仓库。"));
                }
            }
        }
        let fresh = snapshot.as_ref().is_some_and(|s| {
            s.identity == identity
                && s.names == names
                && s.loaded.elapsed() < Duration::from_secs(60)
        });
        if !fresh {
            let mut rows = Vec::with_capacity(names.len());
            for name in &names {
                let id = name
                    .strip_prefix("point-")
                    .and_then(|s| s.strip_suffix(".commit"))
                    .ok_or_else(|| invalid("恢复点名称无效。"))?;
                let point = repo.commit(id)?;
                let at = DateTime::parse_from_rfc3339(&point.created_at)
                    .map_err(|_| invalid("恢复点创建时间无效。"))?;
                rows.push((at, json!({"available":true,"integrityMessage":"","id":point.id,"fileName":format!("{}.edmpoint",point.id),"createdAt":point.created_at,"sizeBytes":point.size_bytes,"replicaStatus":"Committed","message":format!("新增 {} 字节",point.new_bytes)})));
            }
            rows.sort_by(|a, b| {
                b.0.cmp(&a.0)
                    .then_with(|| b.1["id"].as_str().cmp(&a.1["id"].as_str()))
            });
            *snapshot = Some(Snapshot {
                identity,
                names,
                loaded: Instant::now(),
                rows,
            });
        }
        repo.io.check()?;
        let rows = &snapshot.as_ref().unwrap().rows;
        let number = number.max(1).min(rows.len().div_ceil(size).max(1));
        let items: Vec<_> = rows
            .iter()
            .skip((number - 1) * size)
            .take(size)
            .map(|(_, row)| row.clone())
            .collect();
        Ok(json!({"items":items,"page":number,"pageSize":size,"totalCount":rows.len()}))
    }
}
