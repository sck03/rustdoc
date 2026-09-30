//! Bounded connections sharing one database-instance lease. A failed lease or
//! indeterminate transaction stops the pool; requests are never replayed.
use crate::{Connection, Error, ErrorKind, Result, metrics::Metrics};
#[cfg(test)]
mod tests;
use serde_json::{Value, json};
use std::{
    ops::Deref,
    sync::{
        Arc, Condvar, Mutex,
        atomic::{AtomicU64, Ordering::Relaxed},
    },
    time::{Duration, Instant},
};

#[derive(Clone, Copy)]
pub struct PoolOptions {
    pub size: usize,
    pub acquire_timeout: Duration,
}
impl Default for PoolOptions {
    fn default() -> Self {
        Self {
            size: 4,
            acquire_timeout: Duration::from_secs(5),
        }
    }
}
impl PoolOptions {
    pub fn validate(self) -> Result<Self> {
        if !(1..=16).contains(&self.size)
            || self.acquire_timeout < Duration::from_millis(1)
            || self.acquire_timeout > Duration::from_secs(30)
        {
            return Err(Error::unavailable(
                "连接池须为 1–16 个连接，等待时限须为 1–30000 毫秒。",
            ));
        }
        Ok(self)
    }
}
struct State {
    idle: Vec<Connection>,
    failed: bool,
}
pub struct Pool {
    health_check: Option<Arc<dyn Fn() -> Result<()> + Send + Sync>>,
    state: Mutex<State>,
    ready: Condvar,
    options: PoolOptions,
    operations: Arc<Metrics>,
    waits: Metrics,
    leased: AtomicU64,
    timeouts: AtomicU64,
}
pub struct Lease<'a> {
    pool: &'a Pool,
    connection: Option<Connection>,
}
impl Pool {
    pub fn single(connection: Connection) -> Self {
        Self::new(
            vec![connection],
            PoolOptions {
                size: 1,
                ..Default::default()
            },
        )
    }
    fn new(mut connections: Vec<Connection>, options: PoolOptions) -> Self {
        let operations = Arc::new(Metrics::default());
        for connection in &mut connections {
            connection.metrics = operations.clone();
        }
        Self {
            health_check: None,
            state: Mutex::new(State {
                idle: connections,
                failed: false,
            }),
            ready: Condvar::new(),
            options,
            operations,
            waits: Metrics::default(),
            leased: AtomicU64::new(0),
            timeouts: AtomicU64::new(0),
        }
    }
    #[cfg(feature = "postgres")]
    pub fn postgres(connection_string: &str, options: PoolOptions) -> Result<Self> {
        let options = options.validate()?;
        let adapters = crate::postgres::Postgres::open_pool(connection_string, options.size)?;
        let health_check = adapters[0].health_check();
        let mut pool = Self::new(
            adapters
                .into_iter()
                .map(|adapter| Connection::from_adapter(Box::new(adapter)))
                .collect(),
            options,
        );
        pool.health_check = Some(health_check);
        Ok(pool)
    }
    pub fn health(&self) -> Result<()> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| Error::unavailable("连接池状态异常。"))?;
        state.failed |= state
            .idle
            .iter()
            .any(|connection| connection.failed.get() || connection.adapter.is_closed());
        if state.failed {
            return Err(Error::unavailable("连接池已停止服务。"));
        }
        drop(state);
        if let Some(check) = &self.health_check {
            if let Err(cause) = check() {
                if let Ok(mut state) = self.state.lock() {
                    state.failed = true;
                }
                return Err(cause);
            }
        }
        Ok(())
    }
    pub fn acquire(&self, mut canceled: impl FnMut() -> bool) -> Result<Lease<'_>> {
        let observation = self.waits.start();
        let deadline = Instant::now() + self.options.acquire_timeout;
        let mut state = self
            .state
            .lock()
            .map_err(|_| Error::unavailable("连接池状态异常。"))?;
        loop {
            if state.failed {
                return Err(Error::unavailable("连接池已停止服务，请检查数据库并重启。"));
            }
            if canceled() {
                return Err(Error::new(ErrorKind::Timeout, "连接等待已取消。"));
            }
            if let Some(connection) = state.idle.pop() {
                self.leased.fetch_add(1, Relaxed);
                drop(state);
                let lease = Lease {
                    pool: self,
                    connection: Some(connection),
                };
                lease.health()?;
                observation.finish(true);
                return Ok(lease);
            }
            let now = Instant::now();
            if now >= deadline {
                self.timeouts.fetch_add(1, Relaxed);
                return Err(Error::new(ErrorKind::Busy, "数据库连接繁忙，请稍后重试。"));
            }
            state = self
                .ready
                .wait_timeout(state, (deadline - now).min(Duration::from_millis(25)))
                .map_err(|_| Error::unavailable("连接池等待状态异常。"))?
                .0;
        }
    }
    pub fn snapshot(&self) -> Value {
        let failed = self.state.lock().map(|state| state.failed).unwrap_or(true);
        json!({"capacity":self.options.size,"leased":self.leased.load(Relaxed),"failed":failed,
            "acquireTimeouts":self.timeouts.load(Relaxed),"acquireTimeoutMilliseconds":self.options.acquire_timeout.as_millis() as u64,
            "wait":self.waits.snapshot(),"operations":self.operations.snapshot()})
    }
}
impl Deref for Lease<'_> {
    type Target = Connection;
    fn deref(&self) -> &Connection {
        self.connection.as_ref().expect("active connection lease")
    }
}
impl Drop for Lease<'_> {
    fn drop(&mut self) {
        if let Some(connection) = self.connection.take() {
            if connection.in_transaction.get() {
                let _ = connection.rollback();
            }
            if let Ok(mut state) = self.pool.state.lock() {
                state.failed |= connection.failed.get() || connection.adapter.is_closed();
                state.idle.push(connection);
            }
            self.pool.leased.fetch_sub(1, Relaxed);
            self.pool.ready.notify_all();
        }
    }
}
