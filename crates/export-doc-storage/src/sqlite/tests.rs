use super::*;
use std::sync::{
    Arc,
    atomic::{AtomicBool, Ordering},
};

fn memory() -> Sqlite {
    let connection = SqliteConnection::open_in_memory().unwrap();
    connection
        .progress_handler(1000, Some(operation::cancelled as fn() -> bool))
        .unwrap();
    Sqlite {
        connection: RefCell::new(connection),
    }
}
#[test]
fn running_query_observes_cross_thread_cancellation_and_connection_is_reusable() {
    let sqlite = memory();
    let flag = Arc::new(AtomicBool::new(false));
    let cancel = flag.clone();
    let worker = std::thread::spawn(move || {
        std::thread::sleep(Duration::from_millis(20));
        cancel.store(true, Ordering::Release);
    });
    let start = Instant::now();
    let result: Result<i64> = operation::run(flag, start + Duration::from_secs(5), || {
        sqlite.connection.borrow().query_row("WITH RECURSIVE numbers(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM numbers WHERE n<100000000) SELECT sum(n) FROM numbers", [], |row| row.get(0)).map_err(Into::into)
    });
    worker.join().unwrap();
    assert_eq!(result.unwrap_err().kind, ErrorKind::Timeout);
    assert!(start.elapsed() < Duration::from_secs(2));
    sqlite.begin_read().unwrap();
    sqlite.rollback().unwrap();
    sqlite.begin().unwrap();
    sqlite.commit().unwrap();
}
#[test]
fn interrupted_write_rolls_back_and_cleanup_ignores_the_cancelled_scope() {
    let sqlite = memory();
    sqlite
        .connection
        .borrow()
        .execute_batch("CREATE TABLE sample(n INTEGER);")
        .unwrap();
    sqlite.begin().unwrap();
    let flag = Arc::new(AtomicBool::new(false));
    let result: Result<usize> = operation::run(
        flag,
        Instant::now() + Duration::from_millis(10),
        || {
            sqlite.connection.borrow().execute("WITH RECURSIVE numbers(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM numbers WHERE n<100000000) INSERT INTO sample SELECT n FROM numbers", []).map_err(Into::into)
        },
    );
    assert_eq!(result.unwrap_err().kind, ErrorKind::Timeout);
    operation::run(Arc::new(AtomicBool::new(true)), Instant::now(), || {
        operation::without_control(|| sqlite.rollback())
    })
    .unwrap();
    assert_eq!(
        sqlite
            .connection
            .borrow()
            .query_row("SELECT COUNT(*) FROM sample", [], |row| row
                .get::<_, i64>(0))
            .unwrap(),
        0
    );
    sqlite.begin().unwrap();
    sqlite.commit().unwrap();
}
#[test]
fn backup_copy_checks_cancellation_before_copying_pages() {
    let source = SqliteConnection::open_in_memory().unwrap();
    source
        .execute_batch("CREATE TABLE sample(n INTEGER);")
        .unwrap();
    let mut destination = SqliteConnection::open_in_memory().unwrap();
    let result = operation::run(
        Arc::new(AtomicBool::new(true)),
        Instant::now() + Duration::from_secs(5),
        || copy(&source, &mut destination),
    );
    assert_eq!(result.unwrap_err().kind, ErrorKind::Timeout);
    copy(&source, &mut destination).unwrap();
}
