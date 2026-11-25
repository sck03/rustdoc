//! Browser download tickets are short lived and bound to both the authenticated
//! session and an HttpOnly cookie. A copied URL alone cannot download a file.
use axum::http::HeaderMap;
use export_doc_contracts::generated_api::{
    DOWNLOAD_BUSINESS_ATTACHMENT, DOWNLOAD_BUSINESS_ATTACHMENT_WITH_TICKET,
    DOWNLOAD_JOB_RESULT_WITH_TICKET, DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
    DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET, Operation,
};
use export_doc_engine::{api::ApiError, engine::NativeService, paths::nonce};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::Mutex,
    time::{Duration, Instant},
};
use subtle::ConstantTimeEq;

const COOKIE: &str = "ExportDocManager.DownloadSession";
const LIFETIME: Duration = Duration::from_secs(300);
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Target {
    Job(String),
    PostgreSqlBackup(String),
    RecoveryArchive(String),
    BusinessAttachment { id: String, revision: String },
}
impl Target {
    fn operation(&self) -> Operation {
        match self {
            Self::Job(_) => DOWNLOAD_JOB_RESULT_WITH_TICKET,
            Self::PostgreSqlBackup(_) => DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
            Self::RecoveryArchive(_) => DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET,
            Self::BusinessAttachment { .. } => DOWNLOAD_BUSINESS_ATTACHMENT_WITH_TICKET,
        }
    }
    pub fn prepare(
        &self,
        service: &NativeService,
        session: &str,
    ) -> Result<export_doc_engine::engine::backup_io::Download, ApiError> {
        match self {
            Self::Job(id) => service.prepare_job_download(id, session),
            Self::PostgreSqlBackup(token) | Self::RecoveryArchive(token) => service
                .prepare_backup_download(self.operation(), &[("token", token.clone())], session),
            Self::BusinessAttachment { id, revision } => service.prepare_file_download(
                DOWNLOAD_BUSINESS_ATTACHMENT,
                &[("id", id.clone()), ("revision", revision.clone())],
                session,
            ),
        }
    }
}
pub fn is_ticket_download(operation: Operation) -> bool {
    matches!(
        operation,
        DOWNLOAD_JOB_RESULT_WITH_TICKET
            | DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET
            | DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET
            | DOWNLOAD_BUSINESS_ATTACHMENT_WITH_TICKET
    )
}
struct Ticket {
    target: Target,
    session: String,
    binding: String,
    expires: Instant,
}
#[derive(Default)]
pub struct Tickets(Mutex<HashMap<String, Ticket>>);
fn error(status: u16, message: &str) -> ApiError {
    ApiError {
        status: Some(status),
        message: message.into(),
        code: None,
        diagnostic: None,
    }
}
pub fn binding(headers: &HeaderMap) -> String {
    headers
        .get("cookie")
        .and_then(|v| v.to_str().ok())
        .unwrap_or("")
        .split(';')
        .filter_map(|part| part.trim().split_once('='))
        .find(|(key, _)| *key == COOKIE)
        .map(|(_, value)| value)
        .filter(|value| value.len() == 64 && value.bytes().all(|b| b.is_ascii_hexdigit()))
        .unwrap_or("")
        .to_owned()
}
impl Tickets {
    pub fn issue(
        &self,
        target: Target,
        session: &str,
        binding: &str,
        secure: bool,
    ) -> Result<(Value, String), ApiError> {
        let mut entries = self
            .0
            .lock()
            .map_err(|_| error(503, "下载票据状态异常。"))?;
        let now = Instant::now();
        entries.retain(|_, ticket| ticket.expires > now);
        if entries.len() >= 4096 {
            return Err(error(429, "下载请求过多，请稍后重试。"));
        }
        let token = format!(
            "{}{}",
            nonce().map_err(ApiError::local)?,
            nonce().map_err(ApiError::local)?
        );
        let binding = if binding.is_empty() {
            // Parallel initial requests have not received Set-Cookie yet.
            // Reuse this session's live binding under the bounded ticket lock.
            if let Some(ticket) = entries.values().find(|ticket| ticket.session == session) {
                ticket.binding.clone()
            } else {
                format!(
                    "{}{}",
                    nonce().map_err(ApiError::local)?,
                    nonce().map_err(ApiError::local)?
                )
            }
        } else {
            binding.into()
        };
        let url = target.operation().path.replace("{token}", &token);
        entries.insert(
            token.clone(),
            Ticket {
                target,
                session: session.into(),
                binding: binding.clone(),
                expires: now + LIFETIME,
            },
        );
        let expiry = (chrono::Utc::now() + chrono::Duration::seconds(300)).to_rfc3339();
        Ok((
            json!({"token":token,"downloadUrl":url,"expiresAtUtc":expiry}),
            format!(
                "{COOKIE}={binding}; Path=/; HttpOnly; SameSite=Strict; Max-Age=28800{}",
                if secure { "; Secure" } else { "" }
            ),
        ))
    }
    pub fn resolve(
        &self,
        operation: Operation,
        token: &str,
        binding: &str,
    ) -> Result<(Target, String), ApiError> {
        let mut entries = self
            .0
            .lock()
            .map_err(|_| error(503, "下载票据状态异常。"))?;
        // Downloads may resolve the same ticket repeatedly. Only issuance needs
        // to reclaim the whole bounded cache; reads inspect their own ticket.
        if entries
            .get(token)
            .is_some_and(|ticket| ticket.expires <= Instant::now())
        {
            entries.remove(token);
        }
        let ticket = entries
            .get(token)
            .filter(|ticket| {
                ticket.target.operation() == operation
                    && !binding.is_empty()
                    && ticket
                        .binding
                        .as_bytes()
                        .ct_eq(binding.as_bytes())
                        .unwrap_u8()
                        == 1
            })
            .ok_or_else(|| error(404, "下载票据已过期或不属于当前会话。"))?;
        Ok((ticket.target.clone(), ticket.session.clone()))
    }
    pub fn revoke(&self, session: &str) -> Result<(), ApiError> {
        self.0
            .lock()
            .map_err(|_| error(503, "下载票据状态异常。"))?
            .retain(|_, ticket| ticket.session != session);
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn concurrent_initial_tickets_share_a_cookie_without_crossing_sessions() {
        let tickets = Tickets::default();
        let barrier = std::sync::Barrier::new(8);
        let issued = std::thread::scope(|threads| {
            (0..8)
                .map(|id| {
                    let (tickets, barrier) = (&tickets, &barrier);
                    threads.spawn(move || {
                        barrier.wait();
                        tickets
                            .issue(Target::Job(id.to_string()), "first", "", true)
                            .unwrap()
                    })
                })
                .collect::<Vec<_>>()
                .into_iter()
                .map(|thread| thread.join().unwrap())
                .collect::<Vec<_>>()
        });
        let cookie = &issued[0].1;
        let mut headers = HeaderMap::new();
        headers.insert("cookie", cookie.parse().unwrap());
        let binding = binding(&headers);
        for (value, next_cookie) in &issued {
            assert_eq!(cookie, next_cookie);
            assert!(
                tickets
                    .resolve(
                        DOWNLOAD_JOB_RESULT_WITH_TICKET,
                        value["token"].as_str().unwrap(),
                        &binding
                    )
                    .is_ok()
            );
        }
        let (_, other_cookie) = tickets
            .issue(Target::Job("other".into()), "second", "", true)
            .unwrap();
        assert_ne!(cookie, &other_cookie);
        tickets.revoke("first").unwrap();
        let (_, renewed) = tickets
            .issue(Target::Job("new".into()), "first", "", true)
            .unwrap();
        assert_ne!(cookie, &renewed);
    }

    #[test]
    fn tickets_require_the_bound_browser_and_are_revoked_on_logout() {
        let tickets = Tickets::default();
        let (value, cookie) = tickets
            .issue(Target::Job("job-1".into()), "session-1", "", true)
            .unwrap();
        assert!(cookie.contains("HttpOnly; SameSite=Strict"));
        assert!(cookie.ends_with("; Secure"));
        let mut headers = HeaderMap::new();
        headers.insert("cookie", cookie.parse().unwrap());
        let binding = binding(&headers);
        let token = value["token"].as_str().unwrap();
        assert!(
            tickets
                .resolve(DOWNLOAD_JOB_RESULT_WITH_TICKET, token, "")
                .is_err()
        );
        assert!(
            tickets
                .resolve(DOWNLOAD_JOB_RESULT_WITH_TICKET, token, &"0".repeat(64))
                .is_err()
        );
        assert_eq!(
            tickets
                .resolve(DOWNLOAD_JOB_RESULT_WITH_TICKET, token, &binding)
                .unwrap(),
            (Target::Job("job-1".into()), "session-1".into())
        );
        tickets.revoke("session-1").unwrap();
        assert!(
            tickets
                .resolve(DOWNLOAD_JOB_RESULT_WITH_TICKET, token, &binding)
                .is_err()
        );
    }

    #[test]
    fn tickets_preserve_retry_and_operation_boundaries_and_reject_expiry() {
        let tickets = Tickets::default();
        let binding = "a".repeat(64);
        for target in [
            Target::Job("job".into()),
            Target::PostgreSqlBackup("backup-token".into()),
            Target::RecoveryArchive("archive-token".into()),
            Target::BusinessAttachment {
                id: "17".into(),
                revision: "3".into(),
            },
        ] {
            let operation = target.operation();
            let other = if operation == DOWNLOAD_JOB_RESULT_WITH_TICKET {
                DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET
            } else {
                DOWNLOAD_JOB_RESULT_WITH_TICKET
            };
            let (value, _) = tickets
                .issue(target.clone(), "session", &binding, false)
                .unwrap();
            let token = value["token"].as_str().unwrap();
            assert!(tickets.resolve(other, token, &binding).is_err());
            for _ in 0..2 {
                assert_eq!(
                    tickets.resolve(operation, token, &binding).unwrap(),
                    (target.clone(), "session".into())
                );
            }
            tickets.0.lock().unwrap().get_mut(token).unwrap().expires = Instant::now();
            assert!(tickets.resolve(operation, token, &binding).is_err());
            assert!(!tickets.0.lock().unwrap().contains_key(token));
            let (value, _) = tickets.issue(target, "session", &binding, false).unwrap();
            assert!(
                tickets
                    .resolve(operation, value["token"].as_str().unwrap(), &binding)
                    .is_ok()
            );
        }
    }
}
