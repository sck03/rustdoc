//! Reviewed schema 18 delta from 9dee6868: explicit before/after values preserve
//! all unrelated schemas, authentication, permissions, errors and resource metadata.
use serde_json::Value;

pub(super) fn apply(baseline: &mut Value) {
    for line in include_str!("../tests/fixtures/file-storage-contract-changes.jsonl")
        .lines()
        .chain(
            include_str!("../tests/fixtures/file-storage-stability-contract-changes.jsonl").lines(),
        )
    {
        let change: Value = serde_json::from_str(line).unwrap();
        let path = change["path"].as_array().unwrap();
        let mut parent = &mut *baseline;
        for key in &path[..path.len() - 1] {
            parent = parent
                .get_mut(key.as_str().unwrap())
                .expect("reviewed parent exists");
        }
        let key = path.last().unwrap().as_str().unwrap();
        let parent = parent.as_object_mut().unwrap();
        assert_eq!(
            parent.get(key),
            change.get("before"),
            "unreviewed baseline drift at {path:?}"
        );
        match change.get("after") {
            Some(value) => {
                parent.insert(key.into(), value.clone());
            }
            None => {
                parent.remove(key);
            }
        }
    }
}
