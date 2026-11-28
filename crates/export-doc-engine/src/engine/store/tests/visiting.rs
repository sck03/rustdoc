use super::*;
use crate::engine::error::invalid;
use crate::engine::test_support::ScratchDirectory;

#[test]
fn changing_a_filter_during_paged_updates_keeps_all_rows_and_rolls_back_on_failure() {
    let root = ScratchDirectory::new();
    let store = Store::open(&root.paths()).unwrap();
    store.transaction(|tx| {
        for _ in 0..405 {
            let mut row = serde_json::json!({
                "versionNumber":1,"companyScope":"ACME","ownerUserId":1,"departmentId":"D","pending":true
            });
            let id = tx.insert(&export_doc_storage::RecordWrite::unkeyed("visit-test", &row))?;
            row["id"] = serde_json::json!(id);
            tx.set_body(id, &row)?;
        }
        Ok(())
    }).unwrap();
    for fail in [true, false] {
        let mut visited = 0;
        let result = store.transaction(|tx| {
            visit_matching(
                tx,
                "visit-test",
                &[("pending", "true".into())],
                &[],
                |mut row| {
                    visited += 1;
                    row["pending"] = serde_json::json!(false);
                    tx.set_body(row["id"].as_i64().unwrap(), &row)?;
                    if fail && visited == 201 {
                        return Err(invalid("模拟第二批写入失败。"));
                    }
                    Ok(())
                },
            )
        });
        assert_eq!(result.is_err(), fail);
        assert_eq!(visited, if fail { 201 } else { 405 });
        store
            .read(|tx| {
                assert_eq!(
                    matching(
                        tx,
                        "visit-test",
                        &[("pending", "true".into())],
                        &[("id", "id")]
                    )?
                    .len(),
                    if fail { 405 } else { 0 }
                );
                Ok(())
            })
            .unwrap();
    }
}
