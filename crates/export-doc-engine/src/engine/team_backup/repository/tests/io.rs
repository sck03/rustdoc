use super::*;
#[test]
fn repository_directory_pages_share_one_bounded_index_and_track_mutations() {
    let work = crate::engine::test_support::ScratchDirectory::new();
    let root = work.0.join("repository");
    fs::create_dir(&root).unwrap();
    for i in 0..501 {
        fs::write(root.join(format!("blob-{i:04}")), b"x").unwrap();
    }
    let io = Directory::open(root, None).unwrap();
    let mut after = String::new();
    let mut count = 0;
    loop {
        let page = io.list("blob-", &after, 17).unwrap();
        if page.is_empty() {
            break;
        }
        count += page.len();
        after = page.last().unwrap().clone();
        assert!(io.list("reader-", "", 1).unwrap().is_empty());
    }
    assert_eq!(count, 501);
    assert_eq!(io.scans.load(std::sync::atomic::Ordering::Relaxed), 1);
    io.create("reader-current", b"x").unwrap();
    assert_eq!(io.list("reader-", "", 1).unwrap(), ["reader-current"]);
    io.delete("reader-current").unwrap();
    assert!(io.list("reader-", "", 1).unwrap().is_empty());
    assert_eq!(io.scans.load(std::sync::atomic::Ordering::Relaxed), 1);
}
