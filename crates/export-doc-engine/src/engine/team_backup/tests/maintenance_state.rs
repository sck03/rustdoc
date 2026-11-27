use super::*;

#[test]
fn maintenance_reopen_starts_new_session_after_completion_or_abandon() {
    for state in ["Completed", "Abandoned"] {
        let next = reopen(
            Some(&json!({"state":state,"planId":"old","points":[1]})),
            false,
        );
        assert_eq!(next["state"], "Locked");
        assert_eq!(next["planId"], "");
        assert_eq!(next["points"], json!([]));
    }
    assert_eq!(reopen(None, false)["state"], "Locked");
}

#[test]
fn maintenance_reopen_never_hides_pending_or_interrupted_restore() {
    for state in ["Ready", "Failed", "Committing", "Completed", "Abandoned"] {
        assert_eq!(
            reopen(Some(&json!({"state":state})), true)["state"],
            "Interrupted"
        );
    }
    assert_eq!(
        reopen(Some(&json!({"state":"ApplyingFiles"})), false)["state"],
        "Interrupted"
    );
    assert_eq!(reopen(None, true)["state"], "Interrupted");
}
