//! Probes have bounded independent admission and never materialize runtime diagnostics.
use super::{Cancellation, error};
use crate::{ServerState, response::Reply};
use export_doc_contracts::{
    generated_api::Operation,
    request_limits::{self, Probe},
};
use export_doc_engine::{api::ApiError, operation::OperationScope};
use std::time::Duration;

pub(super) async fn execute(
    state: &ServerState,
    operation: Operation,
    kind: Probe,
) -> Result<Reply, ApiError> {
    if kind == Probe::Liveness {
        return Ok(Reply::bytes(Vec::new()));
    }
    let permit = state
        .probes
        .clone()
        .try_acquire_owned()
        .map_err(|_| error(503, "就绪检查正在进行。"))?;
    let timeout = Duration::from_secs(request_limits::execution_seconds(operation.id));
    let scope = OperationScope::new(timeout);
    let registration = state.shutdown.register(scope.clone())?;
    let _cancel = Cancellation {
        scope: scope.clone(),
        _registration: registration.clone(),
    };
    let service = state.service.clone();
    let worker = tokio::task::spawn_blocking(move || {
        let _permit = permit;
        let _registration = registration;
        scope.run_result(|| service.health())
    });
    tokio::time::timeout(timeout, worker)
        .await
        .map_err(|_| error(503, "就绪检查超时。"))?
        .map_err(|_| error(503, "就绪检查中断。"))?
        .map_err(|_| error(503, "服务依赖尚未就绪。"))?;
    Ok(Reply::bytes(Vec::new()))
}
