use super::*;
use crate::engine::test_support::replica_fixture::Fixture;
#[test]
fn webdav_snapshot_keeps_reader_checks_live_and_rejects_lost_ownership() {
    let work = super::super::super::tests::Workspace::new();
    let fixture = Fixture::start(work.0.clone(), false, false, None);
    let id = "11111111111111111111111111111111";
    let target = || {
        super::super::super::cloud::RecoveryTarget::from_credentials(
            &format!("http://{}/backups", fixture.address),
            "fixture",
            "fixture-secret",
            id,
        )
        .unwrap()
    };
    let remote = remote::Remote::open(
        remote::Target::WebDav(target()),
        "22222222222222222222222222222222",
        false,
    )
    .unwrap();
    // A valid writer-session must not make this brand new repository nonempty.
    let repo =
        Repository::open(Box::new(remote), id, "independent-recovery-password", true).unwrap();
    repo.io.create("blob-test-a", b"a").unwrap();
    repo.io.create("blob-test-b", b"b").unwrap();
    assert_eq!(repo.io.list("blob-", "", 1).unwrap(), ["blob-test-a"]);
    assert_eq!(
        repo.io.list("blob-", "blob-test-a", 1).unwrap(),
        ["blob-test-b"]
    );
    repo.io.delete("blob-test-a").unwrap();
    assert_eq!(repo.io.list("blob-", "", 10).unwrap(), ["blob-test-b"]);
    assert!(repo.io.list("reader-", "", 1).unwrap().is_empty());
    let reader = remote::Reader::open(remote::Target::WebDav(target())).unwrap();
    assert_eq!(reader.list("blob-", "", 1).unwrap(), ["blob-test-b"]);
    assert!(reader.list("blob-", "blob-test-b", 1).unwrap().is_empty());
    assert_eq!(repo.io.list("reader-", "", 1).unwrap().len(), 1);
    assert!(garbage::Exclusive::acquire(&repo).is_err());
    drop(reader);
    let gc = garbage::Exclusive::acquire(&repo).unwrap();
    assert!(remote::Reader::open(remote::Target::WebDav(target())).is_err());
    drop(gc);
    target().delete("writer-session").unwrap();
    target()
        .create_small("writer-session", b"different-session")
        .unwrap();
    assert!(repo.io.list("blob-", "", 10).is_err());
    drop(repo);
    let calls = fixture.finish();
    let root = format!("PROPFIND /backups/exportdoc-recovery-{id}/");
    assert_eq!(
        calls.iter().filter(|call| *call == &root).count(),
        2,
        "{calls:?}"
    );
    assert!(
        calls
            .iter()
            .filter(|call| call.starts_with("PROPFIND ") && call.contains("/readers/"))
            .count()
            >= 4
    );
}
