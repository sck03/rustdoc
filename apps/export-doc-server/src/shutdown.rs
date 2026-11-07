//! Request cancellation and bounded draining shared by desktop and server hosts.
use export_doc_engine::{api::ApiError, operation::OperationScope};
use std::{
    collections::HashMap,
    future::Future,
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    time::Duration,
};
use tokio::sync::Notify;
use tower::Layer;

#[derive(Default)]
struct Inner {
    stopping: AtomicBool,
    scopes: Mutex<(u64, HashMap<u64, OperationScope>)>,
    stopped: Notify,
    changed: Notify,
}
#[derive(Clone, Default)]
pub struct Shutdown(Arc<Inner>);
struct Lease {
    owner: Shutdown,
    id: u64,
}
#[derive(Clone)]
pub(crate) struct Registration {
    _lease: Arc<Lease>,
}

impl Shutdown {
    pub fn stop(&self) {
        self.0.stopping.store(true, Ordering::Release);
        let scopes = self
            .0
            .scopes
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        for scope in scopes.1.values() {
            scope.cancel();
        }
        self.0.stopped.notify_waiters();
    }
    pub async fn wait(&self) {
        let ready = self.0.stopped.notified();
        if !self.0.stopping.load(Ordering::Acquire) {
            ready.await;
        }
    }
    pub(crate) fn register(&self, scope: OperationScope) -> Result<Registration, ApiError> {
        let mut state = self
            .0
            .scopes
            .lock()
            .map_err(|_| super::request::error(503, "请求生命周期状态异常。"))?;
        if self.0.stopping.load(Ordering::Acquire) {
            return Err(super::request::error(503, "服务正在关闭。"));
        }
        let id = state.0;
        state.0 = id
            .checked_add(1)
            .ok_or_else(|| super::request::error(503, "请求编号已耗尽。"))?;
        state.1.insert(id, scope);
        Ok(Registration {
            _lease: Arc::new(Lease {
                owner: self.clone(),
                id,
            }),
        })
    }
    async fn drained(&self) {
        loop {
            let ready = self.0.changed.notified();
            if self
                .0
                .scopes
                .lock()
                .unwrap_or_else(|poison| poison.into_inner())
                .1
                .is_empty()
            {
                return;
            }
            ready.await;
        }
    }
}
impl Drop for Lease {
    fn drop(&mut self) {
        self.owner
            .0
            .scopes
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .1
            .remove(&self.id);
        self.owner.0.changed.notify_waiters();
    }
}

pub async fn serve(
    listener: tokio::net::TcpListener,
    router: axum::Router,
    shutdown: Shutdown,
    signal: impl Future<Output = ()> + Send + 'static,
) -> std::io::Result<()> {
    use hyper_util::{
        rt::{TokioIo, TokioTimer},
        service::TowerToHyperService,
    };
    let mut connections = tokio::task::JoinSet::new();
    let mut accept_failures = 0u32;
    tokio::pin!(signal);
    let accepted = loop {
        tokio::select! {
            _ = &mut signal => { shutdown.stop(); break Ok(()); },
            _ = shutdown.wait() => break Ok(()),
            _ = connections.join_next(), if !connections.is_empty() => {},
            incoming = listener.accept(), if connections.len() < 256 => {
                let (stream, peer) = match incoming {
                    Ok(value) => { accept_failures = 0; value },
                    Err(cause) if retryable_accept(&cause) && accept_failures < 8 => {
                        accept_failures += 1;
                        eprintln!("HTTP accept temporarily unavailable (attempt {accept_failures}, kind {:?})", cause.kind());
                        tokio::select! {
                            _ = &mut signal => { shutdown.stop(); break Ok(()); },
                            _ = shutdown.wait() => break Ok(()),
                            _ = tokio::time::sleep(Duration::from_millis(100 * u64::from(accept_failures))) => {},
                        }
                        continue;
                    },
                    Err(cause) => { shutdown.stop(); break Err(cause); }
                };
                // Wrap the shared router once per connection instead of cloning
                // and layering every endpoint in its routing table.
                let route = axum::Extension(axum::extract::ConnectInfo(peer)).layer(router.clone());
                let control = shutdown.clone();
                connections.spawn(async move {
                    let mut builder = hyper::server::conn::http1::Builder::new();
                    builder.timer(TokioTimer::new()).header_read_timeout(Duration::from_secs(15));
                    let connection = builder.serve_connection(TokioIo::new(stream), TowerToHyperService::new(route));
                    tokio::pin!(connection);
                    tokio::select! {
                        _ = &mut connection => {},
                        _ = control.wait() => {
                            connection.as_mut().graceful_shutdown();
                            let _ = connection.await;
                        }
                    }
                });
            }
        }
    };
    drop(listener);
    let drain = async {
        while connections.join_next().await.is_some() {}
        shutdown.drained().await;
    };
    if tokio::time::timeout(Duration::from_secs(35), drain)
        .await
        .is_err()
    {
        connections.abort_all();
        return Err(std::io::Error::new(
            std::io::ErrorKind::TimedOut,
            "HTTP 关闭超过 35 秒时限。",
        ));
    }
    accepted
}

fn retryable_accept(cause: &std::io::Error) -> bool {
    use std::io::ErrorKind::*;
    matches!(
        cause.kind(),
        Interrupted | WouldBlock | ConnectionAborted | ConnectionReset
    ) || matches!(cause.raw_os_error(), Some(23 | 24 | 10024))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    async fn stop_cancels_work_and_tracks_a_detached_worker_until_it_releases_its_lease() {
        let shutdown = Shutdown::default();
        let scope = OperationScope::new(Duration::from_secs(30));
        let owner = shutdown.register(scope.clone()).unwrap();
        let worker = owner.clone();
        shutdown.stop();
        assert!(scope.run_result(|| Ok(())).is_err());
        assert!(shutdown.register(scope).is_err());
        drop(owner);
        assert!(
            tokio::time::timeout(Duration::from_millis(10), shutdown.drained())
                .await
                .is_err()
        );
        drop(worker);
        shutdown.drained().await;
    }
}
