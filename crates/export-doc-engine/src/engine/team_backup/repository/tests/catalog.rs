use super::*;

#[test]
fn point_pages_do_not_offer_an_empty_page_at_exact_boundaries() {
    let work = super::super::super::tests::Workspace::new();
    let root = work.0.join("pages");
    fs::create_dir(&root).unwrap();
    let repo = Repository::open(
        Box::new(io::Directory::open(root, None).unwrap()),
        "11111111111111111111111111111111",
        "independent-recovery-password",
        true,
    )
    .unwrap();
    for n in 1..=3 {
        repo.writer(&format!("{n:032x}"), "SQLite", serde_json::json!({}))
            .unwrap()
            .finish()
            .unwrap();
    }
    let exact = repo.page("", 3).unwrap();
    assert_eq!(exact.items.len(), 3);
    assert!(exact.next_cursor.is_empty());
    let first = repo.page("", 2).unwrap();
    assert!(!first.next_cursor.is_empty());
    let last = repo.page(&first.next_cursor, 2).unwrap();
    assert_eq!(last.items.len(), 1);
    assert!(last.next_cursor.is_empty());
    assert_eq!(repo.page("", 2).unwrap().items[0].id, first.items[0].id);
}
#[test]
fn catalog_orders_instants_and_reuses_bounded_summaries_until_roots_change() {
    use std::sync::{
        Arc,
        atomic::{AtomicUsize, Ordering},
    };
    let work = super::super::super::tests::Workspace::new();
    let root = work.0.join("catalog-repository");
    fs::create_dir(&root).unwrap();
    let reads = Arc::new(AtomicUsize::new(0));
    let repo = Repository::open(
        Box::new(CountReads {
            inner: io::Directory::open(root.clone(), None).unwrap(),
            blobs: reads.clone(),
            prefix: "point-",
        }),
        "11111111111111111111111111111111",
        "independent-recovery-password",
        true,
    )
    .unwrap();
    let older = "ffffffffffffffffffffffffffffffff";
    let newer = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    for (id, at) in [
        (older, "2026-11-28T01:00:00+08:00"),
        (newer, "2026-11-27T20:00:00Z"),
    ] {
        let mut commit = repo
            .writer(id, "SQLite", serde_json::json!({}))
            .unwrap()
            .finish()
            .unwrap();
        commit.created_at = at.into();
        let name = format!("point-{id}.commit");
        fs::write(
            root.join(&name),
            repo.encrypted(&name, &serde_json::to_vec(&commit).unwrap())
                .unwrap(),
        )
        .unwrap();
    }
    reads.store(0, Ordering::Relaxed);
    let catalog = Catalog::default();
    assert_eq!(
        catalog.page(&repo, 1, 1, 1).unwrap()["items"][0]["id"],
        newer
    );
    assert_eq!(
        catalog.page(&repo, 1, 2, 1).unwrap()["items"][0]["id"],
        older
    );
    assert_eq!(
        reads.load(Ordering::Relaxed),
        2,
        "unchanged pagination must reuse authenticated summaries"
    );
    repo.io.delete(&format!("point-{newer}.commit")).unwrap();
    let page = catalog.page(&repo, 1, usize::MAX, 1).unwrap();
    assert_eq!(page["totalCount"], 1);
    assert_eq!(page["page"], 1);
    assert_eq!(page["items"][0]["id"], older);
    assert_eq!(reads.load(Ordering::Relaxed), 3);
    catalog.page(&repo, 2, 1, 1).unwrap();
    assert_eq!(
        reads.load(Ordering::Relaxed),
        4,
        "configuration changes refresh the catalog"
    );
}
