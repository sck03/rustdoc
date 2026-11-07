//! Bounded synchronous storage boundary over the asynchronous wire protocol.
//! One driver per connection; cancellation drains the response or closes the
//! connection. Uncertain writes are never replayed.
use crate::{Error, ErrorKind, Result, operation};
use postgres_native_tls::MakeTlsConnector;
use std::{
    cell::Cell,
    future::Future,
    ops::{Deref, DerefMut},
    time::Duration,
};
use tokio::{
    runtime::{Builder, Runtime},
    task::JoinHandle,
};
use tokio_postgres::{Config, Row, types::ToSql};

pub(crate) struct Client {
    raw: tokio_postgres::Client,
    driver: JoinHandle<()>,
    runtime: Option<Runtime>,
    tls: MakeTlsConnector,
    failed: Cell<bool>,
}

async fn interrupted(limit: Duration) -> Error {
    let deadline = tokio::time::Instant::now() + limit;
    loop {
        if let Err(error) = operation::check() {
            return error;
        }
        if tokio::time::Instant::now() >= deadline {
            return Error::new(ErrorKind::Timeout, "PostgreSQL 网络操作超过时限。");
        }
        tokio::time::sleep_until(
            deadline.min(tokio::time::Instant::now() + Duration::from_millis(10)),
        )
        .await;
    }
}

impl Client {
    pub(super) fn connect(config: Config, tls: MakeTlsConnector) -> Result<Self> {
        operation::check()?;
        let runtime = Builder::new_current_thread()
            .enable_all()
            .build()
            .map_err(|_| Error::unavailable("无法初始化 PostgreSQL 网络运行时。"))?;
        let connected = runtime.block_on(async {
            tokio::select! {
                result = config.connect(tls.clone()) => result.map_err(Error::from),
                error = interrupted(Duration::from_secs(10)) => Err(error),
            }
        });
        let (raw, connection) = match connected {
            Ok(value) => value,
            Err(error) => {
                runtime.shutdown_background();
                return Err(error);
            }
        };
        let driver = runtime.spawn(async move {
            let _ = connection.await;
        });
        Ok(Self {
            raw,
            driver,
            runtime: Some(runtime),
            tls,
            failed: Cell::new(false),
        })
    }

    fn run<T>(
        &self,
        future: impl Future<Output = std::result::Result<T, tokio_postgres::Error>>,
    ) -> Result<T> {
        operation::check()?;
        if self.is_closed() {
            return Err(Error::unavailable(
                "PostgreSQL 连接已关闭，必须重新启动服务。",
            ));
        }
        self.runtime
            .as_ref()
            .expect("live runtime")
            .block_on(async {
                tokio::pin!(future);
                let error = tokio::select! {
                    result = &mut future => return result.map_err(Error::from),
                    error = interrupted(Duration::from_secs(25)) => error,
                };
                // A single bounded cleanup budget covers both cancellation and the
                // original response. Failure to establish the outcome closes the wire.
                let drained = tokio::time::timeout(Duration::from_secs(1), async {
                    self.raw
                        .cancel_token()
                        .cancel_query(self.tls.clone())
                        .await?;
                    match future.await {
                        Ok(_) => Ok(()),
                        Err(error) if error.as_db_error().is_some() => Ok(()),
                        Err(error) => Err(error),
                    }
                })
                .await;
                if !matches!(drained, Ok(Ok(()))) {
                    self.failed.set(true);
                    self.driver.abort();
                    tokio::task::yield_now().await;
                }
                Err(error)
            })
    }

    pub fn is_closed(&self) -> bool {
        self.failed.get() || self.raw.is_closed() || self.driver.is_finished()
    }
    pub fn query(&self, sql: &str, params: &[&(dyn ToSql + Sync)]) -> Result<Vec<Row>> {
        self.run(self.raw.query(sql, params))
    }
    pub fn query_one(&self, sql: &str, params: &[&(dyn ToSql + Sync)]) -> Result<Row> {
        self.run(self.raw.query_one(sql, params))
    }
    pub fn query_opt(&self, sql: &str, params: &[&(dyn ToSql + Sync)]) -> Result<Option<Row>> {
        self.run(self.raw.query_opt(sql, params))
    }
    pub fn execute(&self, sql: &str, params: &[&(dyn ToSql + Sync)]) -> Result<u64> {
        self.run(self.raw.execute(sql, params))
    }
    pub fn batch_execute(&self, sql: &str) -> Result<()> {
        self.run(self.raw.batch_execute(sql))
    }
    pub(super) fn transaction(&mut self) -> Result<Transaction<'_>> {
        self.batch_execute("BEGIN")?;
        Ok(Transaction {
            client: self,
            complete: false,
        })
    }
}

#[cfg(test)]
mod tests;
impl Drop for Client {
    fn drop(&mut self) {
        self.driver.abort();
        if let Some(runtime) = self.runtime.take() {
            runtime.shutdown_background();
        }
    }
}
pub(super) struct Transaction<'a> {
    client: &'a mut Client,
    complete: bool,
}
impl Transaction<'_> {
    pub fn commit(mut self) -> Result<()> {
        let result = self.client.batch_execute("COMMIT");
        self.complete = result.is_ok();
        result
    }
}
impl Deref for Transaction<'_> {
    type Target = Client;
    fn deref(&self) -> &Client {
        self.client
    }
}
impl DerefMut for Transaction<'_> {
    fn deref_mut(&mut self) -> &mut Client {
        self.client
    }
}
impl Drop for Transaction<'_> {
    fn drop(&mut self) {
        if !self.complete
            && operation::without_control(|| self.client.batch_execute("ROLLBACK")).is_err()
        {
            self.client.failed.set(true);
            self.client.driver.abort();
        }
    }
}
