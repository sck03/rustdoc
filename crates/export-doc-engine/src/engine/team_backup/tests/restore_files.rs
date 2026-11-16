use super::*;

#[test]
fn install_uses_snapshot_inventory_and_rolls_back_canceled_or_out_of_space_copy() {
    use crate::{engine::test_support::ScratchDirectory, operation::OperationScope};
    use std::{sync::Arc, time::Duration};
    for scenario in ["success", "cancel", "space"] {
        let root = ScratchDirectory::new();
        let mut paths = root.paths();
        let staging = paths.data_root.join("restore-stage");
        let object = export_doc_storage::FileObject {
            digest: "a".repeat(64),
            size: 3,
        };
        let relative = format!("objects/aa/{}", object.digest);
        fs::create_dir_all(staging.join("Files/objects/aa")).unwrap();
        fs::write(staging.join("Files").join(&relative), b"new").unwrap();
        fs::write(
            staging.join("Files/objects/aa").join("a".repeat(63) + "b"),
            b"unregistered",
        )
        .unwrap();
        fs::write(staging.join("Files/.store-id"), b"staged identity").unwrap();
        fs::create_dir_all(paths.data_root.join("Files")).unwrap();
        fs::write(paths.data_root.join("Files/original"), b"original data").unwrap();
        let scope = OperationScope::new(Duration::from_secs(5));
        if scenario == "space" {
            paths.disk_space = Arc::new(crate::temporary_space::Budget::with_available(
                paths.cache_root.clone(),
                0,
            ));
        }
        let result = scope.run_result(|| {
            install(&paths, &staging, false, |cursor| {
                if scenario == "cancel" {
                    scope.cancel();
                }
                Ok(if cursor.is_empty() {
                    vec![object.clone()]
                } else {
                    vec![]
                })
            })
        });
        if scenario == "success" {
            result.unwrap();
            assert_eq!(
                fs::read(paths.data_root.join("Files").join(&relative)).unwrap(),
                b"new"
            );
            assert_eq!(
                fs::read_dir(paths.data_root.join("Files/objects/aa"))
                    .unwrap()
                    .count(),
                1
            );
        } else {
            assert!(result.is_err());
            assert_eq!(
                fs::read(paths.data_root.join("Files/original")).unwrap(),
                b"original data"
            );
            assert!(!staging.join("install-journal.json").exists());
        }
        assert_eq!(
            fs::read(staging.join("Files").join(&relative)).unwrap(),
            b"new"
        );
    }
}
