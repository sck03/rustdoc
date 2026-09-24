//! Browser download tickets are short lived and bound to both the authenticated
//! session and an HttpOnly cookie. A copied URL alone cannot download a file.
use axum::http::HeaderMap;
use export_doc_engine::{api::ApiError, paths::nonce};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::Mutex,
    time::{Duration, Instant},
};
use subtle::ConstantTimeEq;

const COOKIE: &str = "ExportDocManager.DownloadSession";
const LIFETIME: Duration = Duration::from_secs(300);
#[derive(Clone)]
struct Ticket {
    operation: &'static str,
    job_id: String,
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
        job_id: &str,
        session: &str,
        binding: &str,
        secure: bool,
    ) -> Result<(Value, String), ApiError> {
        self.issue_for(
            export_doc_contracts::generated_api::DOWNLOAD_JOB_RESULT_WITH_TICKET,
            job_id,
            session,
            binding,
            secure,
        )
    }
    pub fn issue_for(
        &self,
        operation: export_doc_contracts::generated_api::Operation,
        job_id: &str,
        session: &str,
        binding: &str,
        secure: bool,
    ) -> Result<(Value, String), ApiError> {
        let mut entries = self
            .0
            .lock()
            .map_err(|_| error(503, "下载票据状态异常。"))?;
        entries.retain(|_, ticket| ticket.expires > Instant::now());
        if entries.len() >= 4096 {
            return Err(error(429, "下载请求过多，请稍后重试。"));
        }
        let token = format!(
            "{}{}",
            nonce().map_err(ApiError::local)?,
            nonce().map_err(ApiError::local)?
        );
        let binding = if binding.is_empty() {
            format!(
                "{}{}",
                nonce().map_err(ApiError::local)?,
                nonce().map_err(ApiError::local)?
            )
        } else {
            binding.into()
        };
        entries.insert(
            token.clone(),
            Ticket {
                operation: operation.id,
                job_id: job_id.into(),
                session: session.into(),
                binding: binding.clone(),
                expires: Instant::now() + LIFETIME,
            },
        );
        let expiry = (chrono::Utc::now() + chrono::Duration::seconds(300)).to_rfc3339();
        let url = operation.path.replace("{token}", &token);
        Ok((
            json!({"token":token,"downloadUrl":url,"expiresAtUtc":expiry}),
            format!(
                "{COOKIE}={binding}; Path=/; HttpOnly; SameSite=Strict; Max-Age=28800{}",
                if secure { "; Secure" } else { "" }
            ),
        ))
    }
    pub fn resolve(&self, token: &str, binding: &str) -> Result<(String, String), ApiError> {
        self.resolve_for(
            export_doc_contracts::generated_api::DOWNLOAD_JOB_RESULT_WITH_TICKET,
            token,
            binding,
        )
    }
    pub fn resolve_for(
        &self,
        operation: export_doc_contracts::generated_api::Operation,
        token: &str,
        binding: &str,
    ) -> Result<(String, String), ApiError> {
        let mut entries = self
            .0
            .lock()
            .map_err(|_| error(503, "下载票据状态异常。"))?;
        entries.retain(|_, ticket| ticket.expires > Instant::now());
        let ticket = entries
            .get(token)
            .filter(|ticket| {
                ticket.operation == operation.id
                    && !binding.is_empty()
                    && ticket
                        .binding
                        .as_bytes()
                        .ct_eq(binding.as_bytes())
                        .unwrap_u8()
                        == 1
            })
            .ok_or_else(|| error(404, "下载票据已过期或不属于当前会话。"))?;
        Ok((ticket.job_id.clone(), ticket.session.clone()))
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
    fn tickets_require_the_bound_browser_and_are_revoked_on_logout() {
        let tickets = Tickets::default();
        let (value, cookie) = tickets.issue("job-1", "session-1", "", true).unwrap();
        assert!(cookie.contains("HttpOnly; SameSite=Strict"));
        assert!(cookie.ends_with("; Secure"));
        let mut headers = HeaderMap::new();
        headers.insert("cookie", cookie.parse().unwrap());
        let binding = binding(&headers);
        let token = value["token"].as_str().unwrap();
        assert!(tickets.resolve(token, "").is_err());
        assert!(tickets.resolve(token, &"0".repeat(64)).is_err());
        assert_eq!(
            tickets.resolve(token, &binding).unwrap(),
            ("job-1".into(), "session-1".into())
        );
        tickets.revoke("session-1").unwrap();
        assert!(tickets.resolve(token, &binding).is_err());
    }
}
