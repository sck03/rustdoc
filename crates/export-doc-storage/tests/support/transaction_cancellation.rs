use export_doc_storage::{Connection, ErrorKind, RecordWrite, operation};
use serde_json::json;
use std::{
    sync::{Arc, atomic::AtomicBool},
    time::{Duration, Instant},
};

pub fn before_commit(connection: &Connection) {
    connection.begin().unwrap();
    let id = connection
        .insert(&RecordWrite::unkeyed(
            "cancelled-commit",
            &json!({"versionNumber": 1, "ownerUserId": 1}),
        ))
        .unwrap();
    let result = operation::run(
        Arc::new(AtomicBool::new(true)),
        Instant::now() + Duration::from_secs(1),
        || connection.commit(),
    );
    assert_eq!(result.unwrap_err().kind, ErrorKind::Timeout);
    connection.health().unwrap();
    assert!(connection.get("cancelled-commit", id).unwrap().is_none());
}

#[cfg(feature = "postgres")]
pub fn during_postgres_commit(app: &str, maintenance: &str) {
    let pool = export_doc_storage::pool::Pool::postgres(app, Default::default()).unwrap();
    let mut admin = postgres::Client::connect(maintenance, postgres::NoTls).unwrap();
    admin
        .batch_execute(
            "SET ROLE native_owner;
         CREATE FUNCTION delay_test_commit() RETURNS trigger LANGUAGE plpgsql AS $$
             BEGIN PERFORM pg_sleep(1); RETURN NEW; END $$;
         CREATE CONSTRAINT TRIGGER delay_test_commit AFTER INSERT ON records
             DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
             WHEN (NEW.kind = 'delayed-commit') EXECUTE FUNCTION delay_test_commit();",
        )
        .unwrap();
    let connection = pool.acquire(|| false).unwrap();
    connection.begin().unwrap();
    let id = connection
        .insert(&RecordWrite::unkeyed(
            "delayed-commit",
            &json!({"versionNumber": 1, "ownerUserId": 1}),
        ))
        .unwrap();
    let started = Instant::now();
    let result = operation::run(
        Arc::new(AtomicBool::new(false)),
        started + Duration::from_millis(500),
        || connection.commit(),
    );
    drop(connection);
    admin
        .batch_execute(
            "DROP TRIGGER delay_test_commit ON records; DROP FUNCTION delay_test_commit();",
        )
        .unwrap();
    result.expect("a dispatched commit must finish within its own bounded budget");
    assert!(started.elapsed() < Duration::from_secs(3));
    pool.health().unwrap();
    assert_eq!(pool.snapshot()["failed"], false);
    assert!(
        pool.acquire(|| false)
            .unwrap()
            .get("delayed-commit", id)
            .unwrap()
            .is_some()
    );
}
