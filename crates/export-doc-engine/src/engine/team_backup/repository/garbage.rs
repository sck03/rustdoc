//! Bounded, authenticated mark/recheck/sweep. COMMIT and explicit holds are roots;
//! object timestamps never decide whether business data remains recoverable.
use super::*;
use chrono::{DateTime, Utc};
use serde_json::{Value, json};
use std::collections::BTreeMap;
const MAX_MARKS: usize = 1_000_000;
const MAX_DELETE: usize = 10_000;
const GRACE_HOURS: i64 = 24;
pub(super) struct Exclusive<'a> {
    repo: &'a Repository,
    value: Vec<u8>,
}
impl<'a> Exclusive<'a> {
    pub(super) fn acquire(repo: &'a Repository) -> Result<Self> {
        let value = paths::nonce().map_err(unavailable)?.into_bytes();
        if !repo.io.create("gc-lock", &value)? {
            return Err(invalid(
                "仓库存在未完成的回收锁，请在隔离旧写者后处理维护记录。",
            ));
        }
        let guard = Self { repo, value };
        if !repo.io.list("reader-", "", 1)?.is_empty() {
            return Err(super::super::error(
                429,
                "仓库存在恢复/导出读取保护，已停止清理。",
            ));
        }
        Ok(guard)
    }
    fn check(&self) -> Result<()> {
        if self.repo.io.read("gc-lock", 128)?.as_deref() != Some(self.value.as_slice())
            || !self.repo.io.list("reader-", "", 1)?.is_empty()
        {
            return Err(invalid("仓库回收锁或读取保护发生变化，已停止清理。"));
        }
        self.repo.io.check()
    }
}
impl Drop for Exclusive<'_> {
    fn drop(&mut self) {
        let _ = crate::operation::OperationScope::detached(std::time::Duration::from_secs(15)).run(
            || {
                if self.repo.io.read("gc-lock", 128)?.as_deref() == Some(self.value.as_slice()) {
                    self.repo.io.delete("gc-lock")?;
                }
                Ok::<_, crate::api::ApiError>(())
            },
        );
    }
}
impl Repository {
    pub fn rotate_password(&self, password: &str) -> Result<()> {
        if password.chars().count() < 12 || password.len() > 1024 {
            return Err(invalid("新恢复凭证须为 12–1024 个字符。"));
        }
        let _guard = Exclusive::acquire(self)?;
        let (generation, _) = current_header(self.io.as_ref())?;
        let mut wrapped = Vec::new();
        let mut writer = sealed::SealingWriter::new(KEY_MAGIC, password, &mut wrapped)?;
        writer.write_all(self.root.as_ref())?;
        writer.finish()?;
        let header = Header {
            format: FORMAT,
            id: self.id.clone(),
            wrapped_key: wrapped,
        };
        let name = format!(
            "credential-{:020}",
            generation
                .checked_add(1)
                .ok_or_else(|| invalid("凭证世代溢出。"))?
        );
        if !self.io.create(&name, &serde_json::to_vec(&header)?)? {
            return Err(invalid("恢复凭证已被并发更换。"));
        }
        let header: Header = serde_json::from_slice(
            &self
                .io
                .read(&name, 64 * 1024)?
                .ok_or_else(|| invalid("新凭证未持久保存。"))?,
        )?;
        let mut key = zeroize::Zeroizing::new(Vec::new());
        sealed::open_to(
            KEY_MAGIC,
            password,
            header.wrapped_key.as_slice(),
            &mut *key,
        )?;
        if key.as_slice() != self.root.as_ref() {
            return Err(invalid("新恢复凭证核验失败。"));
        }
        Ok(())
    }
    pub fn release_stale_protection(&self) -> Result<()> {
        // Called only after deployment isolation was explicitly confirmed and the
        // current writer acquired its host/package lock and repository ownership.
        let mut cursor = String::new();
        loop {
            let names = self.io.list("reader-", &cursor, 100)?;
            if names.is_empty() {
                break;
            }
            for name in names {
                self.io.delete(&name)?;
                cursor = name;
            }
        }
        self.io.delete("gc-lock")
    }
    fn roots(&self) -> Result<(BTreeMap<String, u64>, String)> {
        let mut roots = BTreeMap::new();
        let mut fingerprint = Sha256::new();
        let mut cursor = String::new();
        let mut count = 0;
        loop {
            let points = self.list(&cursor, 100)?;
            if points.is_empty() {
                break;
            }
            for point in points {
                count += 1;
                if count > 10_000 {
                    return Err(invalid("仓库超过一万恢复点预算，已停止回收。"));
                }
                cursor = format!("point-{}.commit", point.id);
                fingerprint.update(serde_json::to_vec(&point)?);
                if let Some(hold) = self.io.read(&format!("hold-{}", point.id), 64 * 1024)? {
                    self.read(&format!("hold-{}", point.id))?;
                    fingerprint.update(hold);
                }
                for page in 0..point.pages {
                    roots.insert(points::page_name(&point.id, page), 0);
                    if roots.len() > MAX_MARKS {
                        return Err(invalid("仓库标记超过本轮一百万对象预算，未删除任何对象。"));
                    }
                }
                points::references(self, point, &mut |key, size, digest| {
                    if let Some(previous) = roots.get(key) {
                        if *previous != size {
                            return Err(invalid("共享恢复块的声明长度不一致。"));
                        }
                    } else {
                        points::verify_chunk(&self.read(key)?, size, digest)?;
                        roots.insert(key.to_owned(), size);
                    }
                    if roots.len() > MAX_MARKS {
                        return Err(invalid("仓库标记超过本轮一百万对象预算，未删除任何对象。"));
                    }
                    Ok(())
                })?;
            }
        }
        Ok((roots, super::super::hex(&fingerprint.finalize())))
    }
    pub fn hold(&self, id: &str, reason: &str, enabled: bool) -> Result<()> {
        let _guard = Exclusive::acquire(self)?;
        self.commit(id)?;
        let name = format!("hold-{id}");
        if enabled {
            if reason.trim().is_empty() || reason.chars().count() > 500 {
                return Err(invalid("请填写 1–500 字暂停保留原因。"));
            }
            if self.io.read(&name, 64 * 1024)?.is_none() {
                self.put(
                    &name,
                    &serde_json::to_vec(
                        &json!({"pointId":id,"reason":reason,"at":Utc::now().to_rfc3339()}),
                    )?,
                )?;
            }
        } else if self.io.read(&name, 64 * 1024)?.is_some() {
            self.read(&name)?;
            let audit = format!("hold-release-{}", paths::nonce().map_err(unavailable)?);
            self.put(
                &audit,
                &serde_json::to_vec(
                    &json!({"pointId":id,"reason":reason,"at":Utc::now().to_rfc3339()}),
                )?,
            )?;
            self.io.delete(&name)?;
        }
        Ok(())
    }
    pub fn preview_gc(&self) -> Result<Value> {
        let guard = Exclusive::acquire(self)?;
        let (roots, fingerprint) = self.roots()?;
        let mut candidates = Vec::<(String, u64)>::new();
        let mut waiting = 0u64;
        let mut examined = 0usize;
        'scan: for prefix in ["blob-", "manifest-"] {
            let mut cursor = String::new();
            loop {
                let page = self.io.list(prefix, &cursor, 200)?;
                if page.is_empty() {
                    break;
                }
                guard.check()?;
                for name in page {
                    crate::operation::check()?;
                    cursor = name.clone();
                    examined += 1;
                    if examined > MAX_MARKS {
                        return Err(invalid("仓库扫描超过本轮对象预算，未执行删除。"));
                    }
                    if roots.contains_key(&name) {
                        continue;
                    }
                    let mark = format!("mark-{name}");
                    if self.io.read(&mark, 64 * 1024)?.is_none() {
                        self.put(
                            &mark,
                            &serde_json::to_vec(
                                &json!({"name":name,"markedAt":Utc::now().to_rfc3339()}),
                            )?,
                        )?;
                        waiting += 1;
                        continue;
                    }
                    let record: Value = serde_json::from_slice(&self.read(&mark)?)?;
                    let at = record["markedAt"]
                        .as_str()
                        .and_then(|v| DateTime::parse_from_rfc3339(v).ok())
                        .ok_or_else(|| invalid("仓库回收标记损坏，已停止清理。"))?;
                    if record["name"] != name {
                        return Err(invalid("仓库回收标记身份不符。"));
                    }
                    if at > Utc::now() - chrono::Duration::hours(GRACE_HOURS) {
                        waiting += 1;
                        continue;
                    }
                    let size = self
                        .io
                        .read(&name, sealed::object::MAX_PLAIN + sealed::object::OVERHEAD)?
                        .ok_or_else(|| invalid("候选对象在预检时消失，请重新核对仓库。"))?
                        .len() as u64;
                    candidates.push((name, size));
                    if candidates.len() >= MAX_DELETE {
                        break 'scan;
                    }
                }
            }
        }
        let token = paths::nonce().map_err(unavailable)?;
        let expires = Utc::now() + chrono::Duration::minutes(15);
        let bytes = candidates.iter().map(|c| c.1).sum::<u64>();
        let plan = json!({"token":token,"fingerprint":fingerprint,"expiresAt":expires.to_rfc3339(),"candidates":candidates});
        self.put(&format!("gc-plan-{token}"), &serde_json::to_vec(&plan)?)?;
        Ok(
            json!({"token":token,"expiresAt":expires.to_rfc3339(),"count":candidates.len(),"bytes":bytes,"protectedObjects":roots.len(),"waitingObjects":waiting}),
        )
    }
    pub fn execute_gc(&self, token: &str) -> Result<Value> {
        valid_id(token)?;
        let guard = Exclusive::acquire(self)?;
        let done = format!("gc-completed-{token}");
        if self.io.read(&done, 64 * 1024)?.is_some() {
            return Ok(serde_json::from_slice(&self.read(&done)?)?);
        }
        let plan: Value = serde_json::from_slice(&self.read(&format!("gc-plan-{token}"))?)?;
        let expires = plan["expiresAt"]
            .as_str()
            .and_then(|v| DateTime::parse_from_rfc3339(v).ok())
            .ok_or_else(|| invalid("回收计划期限无效。"))?;
        if expires < Utc::now() {
            return Err(invalid("回收预检已过期，请重新预览。"));
        }
        let (roots, fingerprint) = self.roots()?;
        if plan["fingerprint"] != fingerprint {
            return Err(invalid("恢复点或暂停保留已变化，请重新预览。"));
        }
        let candidates: Vec<(String, u64)> = serde_json::from_value(plan["candidates"].clone())?;
        if candidates.len() > MAX_DELETE {
            return Err(invalid("回收计划超出预算。"));
        }
        for (name, _) in &candidates {
            if roots.contains_key(name)
                || !(name.starts_with("blob-") || name.starts_with("manifest-"))
            {
                return Err(invalid("回收计划包含受保护对象，已拒绝执行。"));
            }
        }
        let mut bytes = 0u64;
        for (name, size) in &candidates {
            guard.check()?;
            self.io.delete(name)?;
            self.io.delete(&format!("mark-{name}"))?;
            bytes += size;
        }
        let result = json!({"token":token,"count":candidates.len(),"bytes":bytes,"completedAt":Utc::now().to_rfc3339()});
        self.put(&done, &serde_json::to_vec(&result)?)?;
        Ok(result)
    }
}
