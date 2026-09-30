use export_doc_storage::{Connection, ErrorKind, RecordWrite, pool::Pool};
use serde_json::json;
use std::{
    path::PathBuf,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

pub fn exercise(app: &str, maintenance: &str) {
    let mut results = vec![];
    for instance in [true, false] {
        let pool = Pool::postgres(app, Default::default()).unwrap();
        assert!(matches!(Connection::postgres(app),Err(cause) if cause.kind==ErrorKind::Busy));
        let body = json!({"versionNumber":1,"ownerUserId":1,"companyScope":"TEST","departmentId":"RECOVERY","amount":"123.45"});
        let committed = {
            let c = pool.acquire(|| false).unwrap();
            c.begin().unwrap();
            let id = c
                .insert(&RecordWrite {
                    kind: "pool-recovery",
                    identity: None,
                    body: &body,
                })
                .unwrap();
            c.commit().unwrap();
            id
        };
        let held = pool.acquire(|| false).unwrap();
        held.begin().unwrap();
        let pending = held
            .insert(&RecordWrite {
                kind: "pool-recovery",
                identity: None,
                body: &body,
            })
            .unwrap();
        let mut admin = postgres::Client::connect(maintenance, postgres::NoTls).unwrap();
        let lock_type = if instance {
            "advisory"
        } else {
            "transactionid"
        };
        let rows=admin.query("SELECT DISTINCT l.pid FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE a.datname=current_database() AND a.usename='native_app' AND l.locktype=$1 AND l.granted AND l.mode='ExclusiveLock'",&[&lock_type]).unwrap();
        assert_eq!(
            rows.len(),
            1,
            "target exactly the isolated instance or transaction connection"
        );
        let pid: i32 = rows[0].get(0);
        let failure = Instant::now();
        assert!(
            admin
                .query_one("SELECT pg_terminate_backend($1)", &[&pid])
                .unwrap()
                .get::<_, bool>(0)
        );
        let deadline = Instant::now() + Duration::from_secs(5);
        while (if instance {
            pool.health()
        } else {
            held.health()
        })
        .is_ok()
            && Instant::now() < deadline
        {
            std::thread::sleep(Duration::from_millis(10));
        }
        assert_eq!(held.commit().unwrap_err().kind, ErrorKind::Unavailable);
        drop(held);
        assert_eq!(
            pool.acquire(|| false).err().unwrap().kind,
            ErrorKind::Unavailable
        );
        assert_eq!(pool.snapshot()["failed"], true);
        drop(pool);
        let restarted = Pool::postgres(app, Default::default()).unwrap();
        let c = restarted.acquire(|| false).unwrap();
        assert_eq!(
            c.get("pool-recovery", committed).unwrap().unwrap()["amount"],
            "123.45"
        );
        assert!(c.get("pool-recovery", pending).unwrap().is_none());
        results.push(json!({"scenario":if instance {"instance-lock-loss"}else{"business-connection-loss"},"lostCommittedRecords":0,"uncommittedWriteRolledBack":true,"recoveryMilliseconds":failure.elapsed().as_millis() as u64}));
    }
    let output =
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../.codex-runtime/pool-recovery");
    std::fs::create_dir_all(&output).unwrap();
    let file = output.join(format!(
        "{}-{}.json",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    std::fs::write(&file, serde_json::to_vec_pretty(&results).unwrap()).unwrap();
    println!(
        "PostgreSQL connection recovery evidence: {}",
        file.display()
    );
}
