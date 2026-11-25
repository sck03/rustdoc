use export_doc_storage::Connection;
#[path = "support/workspace.rs"]
mod workspace;

const OLDEST: &str = "2026-01-01T01:00:00.000000001+02:00";
fn seed(pg: bool) -> String {
    let body = if pg { "'{}'::jsonb" } else { "'{}'" };
    format!(
        "INSERT INTO audit_logs(kind,record_id,version,action,actor_id,occurred_at,body) VALUES ('test',0,1,'create',1,'{OLDEST}',{body}), ('test',0,1,'create',1,'2026-01-01T00:00:00Z',{body}); WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<60000) INSERT INTO audit_logs(kind,record_id,version,action,actor_id,occurred_at,body) SELECT 'test',x,1,'create',1,'2026-11-26T00:00:00Z',{body} FROM n;"
    )
}
fn assert_statistics(connection: &Connection) {
    connection.begin_read().unwrap();
    let statistics = connection.audit_statistics().unwrap();
    assert_eq!(statistics.total_events, 60002);
    assert_eq!(statistics.oldest_event.as_deref(), Some(OLDEST));
    connection.commit().unwrap();
}

#[test]
fn sqlite_audit_statistics_are_exact_and_reject_invalid_instants() {
    let root = workspace::create("audit-statistics");
    let file = root.join("test.db");
    {
        let connection = Connection::sqlite(&file).unwrap();
        let empty = connection.audit_statistics().unwrap();
        assert_eq!(empty.total_events, 0);
        assert!(empty.oldest_event.is_none());
        let raw = rusqlite::Connection::open(&file).unwrap();
        raw.execute_batch(&seed(false)).unwrap();
        assert_statistics(&connection);
        raw.execute("UPDATE audit_logs SET occurred_at='invalid' WHERE id=1", [])
            .unwrap();
        assert!(connection.audit_statistics().is_err());
    }
    workspace::remove(&root).unwrap();
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires the isolated PostgreSQL 18 AuditStatistics scope"]
fn postgres_audit_statistics_match_sqlite() {
    let maintenance = std::env::var("EXPORTDOC_TEST_POSTGRES_MAINTENANCE").unwrap();
    let app = std::env::var("EXPORTDOC_TEST_POSTGRES_APP").unwrap();
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let connection = Connection::postgres(&app).unwrap();
    assert_eq!(connection.audit_statistics().unwrap().total_events, 0);
    let mut raw = postgres::Client::connect(&maintenance, postgres::NoTls).unwrap();
    raw.batch_execute("SET ROLE native_owner").unwrap();
    raw.batch_execute(&seed(true)).unwrap();
    assert_statistics(&connection);
    raw.batch_execute("UPDATE audit_logs SET occurred_at='invalid' WHERE id=1")
        .unwrap();
    assert!(connection.audit_statistics().is_err());
}
