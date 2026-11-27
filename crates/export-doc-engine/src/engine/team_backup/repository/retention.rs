//! Retire whole recovery points before reclaiming objects. Active COMMIT records
//! remain the sole root set; damaged manifests stop the entire collection pass.
use super::*;
use chrono::{DateTime, Utc};
pub(super) fn retain(repo: &Repository, count: usize, days: u32) -> Result<u64> {
    let _guard = super::garbage::Exclusive::acquire(repo)?;
    if !(1..=365).contains(&count) || days > 3650 {
        return Err(invalid("恢复点保留策略超出允许范围。"));
    }
    // A fixed upper bound protects maintenance memory even if an unmanaged target
    // contains an excessive number of commits. No deletion occurs on overflow.
    let mut all = Vec::new();
    let mut cursor = String::new();
    loop {
        let points = repo.list(&cursor, 100)?;
        if points.is_empty() {
            break;
        }
        for point in points {
            cursor = format!("point-{}.commit", point.id);
            all.push(point);
            if all.len() > 10_000 {
                return Err(invalid(
                    "恢复仓库超过单次保留策略预算，请先分批整理恢复点；本次未执行清理。",
                ));
            }
        }
    }
    if all.len() <= count {
        return Ok(0);
    }
    all.sort_by(|a, b| {
        b.created_at
            .cmp(&a.created_at)
            .then_with(|| b.id.cmp(&a.id))
    });
    let cutoff = Utc::now() - chrono::Duration::days(days as i64);
    let mut retire = Vec::new();
    for point in all.iter().skip(count) {
        let created = DateTime::parse_from_rfc3339(&point.created_at)
            .map_err(|_| invalid("恢复点创建时间无效，已停止保留策略。"))?;
        if days > 0 && created >= cutoff {
            continue;
        }
        if repo
            .io
            .read(&format!("hold-{}", point.id), 64 * 1024)?
            .is_some()
        {
            continue;
        }
        retire.push(point.clone());
    }
    // Authenticated tombstones preserve each retired manifest for review and a
    // future GC approval. A failed retirement never removes its COMMIT first.
    for point in &retire {
        let name = format!("retired-{}", point.id);
        if repo.io.read(&name, 64 * 1024)?.is_none() {
            repo.put(
                &name,
                &serde_json::to_vec(
                    &serde_json::json!({"retiredAt":Utc::now().to_rfc3339(),"commit":point}),
                )?,
            )?;
        }
        repo.read(&name)?;
        repo.io.delete(&format!("point-{}.commit", point.id))?;
    }
    Ok(retire.len() as u64)
}
