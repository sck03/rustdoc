use crate::{Connection, ErrorKind, RecordWrite};
use serde_json::json;

fn exercise(db: &Connection) {
    db.begin().unwrap();
    let body = json!({"versionNumber":1,"ownerUserId":0,"companyScope":"","departmentId":""});
    let request = db
        .insert(&RecordWrite::unkeyed("oa-general", &body))
        .unwrap();
    let archive = db
        .insert(&RecordWrite::unkeyed("oa-submission", &body))
        .unwrap();
    let definition = db
        .insert(&RecordWrite::unkeyed("workflow-definitions", &body))
        .unwrap();
    let execute = |sql: &str| db.adapter.execute_statement(sql, &[]);
    execute(&format!("INSERT INTO request_revisions(id,request_id,revision,body) VALUES({archive},{request},1,'{{}}')")).unwrap();
    execute(&format!("INSERT INTO workflow_instances(id,request_id,revision,state,generation,definition) VALUES('constraint-fixture',{request},1,'Pending',1,'{{}}')")).unwrap();
    execute(&format!("INSERT INTO workflow_subinstances(id,parent_instance_id,definition_id,definition_version,submission_id,subflow_key,state,node_keys,input_snapshot) VALUES('sub-fixture','constraint-fixture',{definition},1,{archive},'child','Pending','[]','{{\"sources\":{{}}}}')")).unwrap();
    execute("UPDATE workflow_subinstances SET id=id,parent_instance_id=parent_instance_id,definition_id=definition_id,definition_version=definition_version,submission_id=submission_id,subflow_key=subflow_key,node_keys=node_keys,input_snapshot=input_snapshot,state='Active' WHERE id='sub-fixture'").unwrap();
    for statement in [
        "UPDATE workflow_subinstances SET id='changed' WHERE id='sub-fixture'",
        "UPDATE workflow_subinstances SET definition_version=2 WHERE id='sub-fixture'",
        "UPDATE workflow_subinstances SET subflow_key='other' WHERE id='sub-fixture'",
        "UPDATE workflow_subinstances SET node_keys='[\"other\"]' WHERE id='sub-fixture'",
        "UPDATE workflow_subinstances SET input_snapshot='{\"sources\":{\"other\":1}}' WHERE id='sub-fixture'",
        "UPDATE workflow_instances SET generation=0 WHERE id='constraint-fixture'",
    ] {
        execute("SAVEPOINT expected_conflict").unwrap();
        assert_eq!(
            execute(statement).unwrap_err().kind,
            ErrorKind::Conflict,
            "{statement}"
        );
        execute("ROLLBACK TO SAVEPOINT expected_conflict").unwrap();
        execute("RELEASE SAVEPOINT expected_conflict").unwrap();
    }
    db.rollback().unwrap();
}

#[test]
fn sqlite_enforces_subflow_facts_and_classifies_checks() {
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../.codex-runtime/workflow-constraints")
        .join(format!(
            "{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
    std::fs::create_dir_all(&root).unwrap();
    let db = Connection::sqlite(&root.join("test.db")).unwrap();
    exercise(&db);
    drop(db);
    std::fs::remove_dir_all(root).unwrap();
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires an isolated PostgreSQL 18 cluster and separate maintenance/app roles"]
fn postgres_enforces_subflow_facts_and_classifies_checks() {
    let maintenance = std::env::var("EXPORTDOC_TEST_POSTGRES_MAINTENANCE").unwrap();
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let db = Connection::postgres(&std::env::var("EXPORTDOC_TEST_POSTGRES_APP").unwrap()).unwrap();
    exercise(&db);
}
