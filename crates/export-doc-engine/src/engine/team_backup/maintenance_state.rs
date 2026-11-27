use serde_json::Value;
#[cfg(any(test, feature = "postgres"))]
use serde_json::json;

pub(super) fn finished(state: &Value) -> bool {
    matches!(state["state"].as_str(), Some("Completed" | "Abandoned"))
}

/// Plans and unlocked repositories belong to the current process. Starting a
/// new session must neither revive an old plan nor hide an interrupted restore.
#[cfg(any(test, feature = "postgres"))]
pub(super) fn reopen(previous: Option<&Value>, pending_restore: bool) -> Value {
    let interrupted = pending_restore || previous.is_some_and(|state| !finished(state));
    json!({
        "state": if interrupted { "Interrupted" } else { "Locked" },
        "message": if interrupted {
            "上次维护尚未完成，请重新解锁仓库并核验；已暂存的恢复须继续完成。"
        } else {
            "请先停止并隔离旧业务实例，再解锁恢复仓库。"
        },
        "planId": "",
        "points": []
    })
}

#[cfg(test)]
#[path = "tests/maintenance_state.rs"]
mod tests;
