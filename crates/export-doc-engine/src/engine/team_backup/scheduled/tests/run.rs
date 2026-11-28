use super::*;
use crate::engine::team_backup::tests::{TestService, Workspace};
#[test]
fn unknown_commit_keeps_the_attempted_point_id() {
    let mut cause = unavailable("提交回执丢失。");
    cause.outcome_unknown = true;
    let patch = failure_state("point", &cause);
    assert_eq!(patch["lastResult"], "Unknown");
    assert_eq!(patch["lastPointId"], "point");
}
#[test]
fn cleanup_failure_keeps_the_committed_point_and_partial_status() {
    let work = Workspace::new();
    let service = TestService::open(work.paths());
    let id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    update_state(&service.store, None, json!({"archiveId": id})).unwrap();
    let commit = Commit {
        id: id.into(),
        repository: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb".into(),
        created_at: chrono::Utc::now().to_rfc3339(),
        provider: "SQLite".into(),
        database_schema: 19,
        pages: 1,
        manifest_digest: String::new(),
        files: 1,
        size_bytes: 10,
        new_bytes: 5,
        source_storage: json!({}),
    };
    let output = complete(
        &service.store,
        id,
        &commit,
        Err(std::io::Error::from(std::io::ErrorKind::PermissionDenied).into()),
        Ok(()),
    )
    .unwrap();
    assert!(output.committed);
    assert!(output.detail.contains(id));
    assert!(output.detail.contains("临时快照清理"));
    let status = state(&service.store).unwrap();
    assert_eq!(status["lastResult"], "Partial");
    assert_eq!(status["lastPointId"], id);
    assert_eq!(status["logicalBytes"], 10);
}
