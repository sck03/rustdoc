use super::*;

#[test]
fn expired_sessions_never_resolve_and_do_not_disturb_other_logins() {
    let sessions = Sessions::default();
    let now = Utc::now();
    sessions.values.lock().unwrap().extend([
        (
            "expired".into(),
            Session {
                user_id: 1,
                expires: now - Duration::seconds(1),
                version: (2, 3),
                context: String::new(),
            },
        ),
        (
            "active".into(),
            Session {
                user_id: 4,
                expires: now + Duration::hours(1),
                version: (5, 6),
                context: String::new(),
            },
        ),
    ]);
    assert_eq!(sessions.identity("expired").unwrap_err().status, Some(401));
    assert_eq!(sessions.identity("missing").unwrap_err().status, Some(401));
    let active = sessions.identity("active").unwrap();
    assert_eq!((active.user_id, active.version), (4, (5, 6)));
    assert!(!sessions.values.lock().unwrap().contains_key("expired"));
    sessions.logout("active").unwrap();
    assert_eq!(sessions.identity("active").unwrap_err().status, Some(401));
}

#[test]
fn session_capacity_preserves_existing_logins_and_recovers_after_logout_or_expiry() {
    let sessions = Sessions::default();
    let expires = Utc::now() + Duration::hours(12);
    let tokens: Vec<_> = (0..MAX_USER_SESSIONS)
        .map(|_| sessions.issue(7, expires, (1, 2)).unwrap())
        .collect();
    assert_eq!(
        sessions.issue(7, expires, (1, 2)).unwrap_err().status,
        Some(429)
    );
    let active = sessions.identity(&tokens[0]).unwrap();
    assert_eq!((active.user_id, active.version), (7, (1, 2)));
    sessions.issue(8, expires, (1, 2)).unwrap();
    sessions.logout(&tokens[0]).unwrap();
    sessions.issue(7, expires, (1, 2)).unwrap();
    sessions
        .values
        .lock()
        .unwrap()
        .get_mut(&tokens[1])
        .unwrap()
        .expires = Utc::now() - Duration::seconds(1);
    sessions.issue(7, expires, (1, 2)).unwrap();
    let mut values = sessions.values.lock().unwrap();
    while values.len() < MAX_SESSIONS {
        let index = values.len();
        values.insert(
            format!("capacity-{index}"),
            Session {
                user_id: 100 + index as i64,
                expires,
                version: (1, 2),
                context: String::new(),
            },
        );
    }
    drop(values);
    assert_eq!(
        sessions.issue(9, expires, (1, 2)).unwrap_err().status,
        Some(429)
    );
}
