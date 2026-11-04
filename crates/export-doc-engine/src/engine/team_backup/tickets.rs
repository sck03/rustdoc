//! Bounded, single-use backup authorizations owned by one application instance.
use super::{Actor, Result, TICKET_HEADER, contracts, error, header, nonce, unavailable};
use crate::generated_api::DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET;
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::Mutex,
    time::{Duration, Instant},
};

const LIFETIME: u64 = 300;
struct Ticket<T> {
    value: T,
    expires: Instant,
}
struct Pool<T>(Mutex<HashMap<String, Ticket<T>>>);
impl<T> Default for Pool<T> {
    fn default() -> Self {
        Self(Mutex::new(HashMap::new()))
    }
}
impl<T> Pool<T> {
    fn issue(&self, value: T, limit: usize) -> Result<(String, String)> {
        let mut tickets = self
            .0
            .lock()
            .map_err(|_| unavailable("备份票据状态异常。"))?;
        let now = Instant::now();
        tickets.retain(|_, ticket| ticket.expires >= now);
        if tickets.len() >= limit {
            return Err(error(429, "备份授权请求过多，请稍后重试。"));
        }
        let token = format!(
            "{}{}",
            nonce().map_err(unavailable)?,
            nonce().map_err(unavailable)?
        );
        tickets.insert(
            token.clone(),
            Ticket {
                value,
                expires: now + Duration::from_secs(LIFETIME),
            },
        );
        Ok((
            token,
            (chrono::Utc::now() + chrono::Duration::seconds(LIFETIME as i64)).to_rfc3339(),
        ))
    }
    fn take(&self, token: &str) -> Result<Option<T>> {
        let mut tickets = self
            .0
            .lock()
            .map_err(|_| unavailable("备份票据状态异常。"))?;
        Ok(tickets
            .remove(token)
            .filter(|ticket| ticket.expires >= Instant::now())
            .map(|ticket| ticket.value))
    }
}
#[derive(Default)]
pub(in crate::engine) struct Tickets {
    sensitive: Pool<(i64, &'static str)>,
    downloads: Pool<String>,
}
impl Tickets {
    pub(super) fn issue_sensitive(
        &self,
        actor: &Actor,
        action: &'static str,
    ) -> Result<(String, String)> {
        self.sensitive.issue((actor.id, action), 1024)
    }
    pub(super) fn authorize(
        &self,
        parameters: &[(&str, String)],
        actor: &Actor,
        action: &'static str,
    ) -> Result<()> {
        let token = header(parameters, TICKET_HEADER);
        if token.is_empty() {
            return Err(error(401, "敏感操作缺少一次性票据。"));
        }
        if self.sensitive.take(token)? == Some((actor.id, action)) {
            Ok(())
        } else {
            Err(error(401, "敏感操作票据已过期或不属于当前管理员。"))
        }
    }
    pub(super) fn issue_download(&self, file_name: String) -> Result<Value> {
        let (token, expiry) = self.downloads.issue(file_name, 4096)?;
        let mut response = contracts::initial(contracts::schema("ApiDownloadTicket"));
        response["downloadUrl"] = json!(
            DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET
                .path
                .replace("{token}", &token)
        );
        response["token"] = json!(token);
        response["expiresAtUtc"] = json!(expiry);
        Ok(response)
    }
    pub(super) fn consume_download(&self, token: &str) -> Result<String> {
        self.downloads
            .take(token)?
            .ok_or_else(|| error(404, "下载票据已过期或不存在。"))
    }
}
