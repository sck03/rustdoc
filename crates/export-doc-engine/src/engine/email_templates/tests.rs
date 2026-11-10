use super::*;
use crate::engine::test_support::{ScratchDirectory, administrator};

#[test]
fn mail_reads_revalidate_the_account_inside_the_snapshot() {
    let root = ScratchDirectory::new();
    let service = root.service();
    let actor = administrator();
    let saved=handle(&service,&actor,CREATE_EMAIL_TEMPLATE,&[],&[],&json!({"name":"snapshot","category":"mail","subject":"subject","bodyHtml":"<p>body</p>","expectedVersion":0})).unwrap();
    let id = saved["id"].as_i64().unwrap().to_string();
    let paths = [
        ("id", id),
        ("versionNumber", "1".into()),
        ("customerId", "1".into()),
    ];
    service
        .store
        .transaction(|tx| {
            let mut user = store::get(tx, "users", actor.id)?;
            user["isActive"] = json!(false);
            store::save(tx, "users", actor.id, user, None, &actor, "deactivate")?;
            Ok(())
        })
        .unwrap();
    for op in [
        LIST_EMAIL_TEMPLATES,
        LIST_EMAIL_TEMPLATE_VERSIONS,
        GET_EMAIL_TEMPLATE_VERSION,
        GET_CRM_EMAIL_VARIABLE_DRAFT,
    ] {
        let error = handle(&service, &actor, op, &paths, &[], &Value::Null).unwrap_err();
        assert!(
            matches!(error.status, Some(401 | 403)),
            "{}: {error}",
            op.id
        );
    }
}
