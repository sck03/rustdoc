use super::*;
use std::{fs, io::Write};
#[path = "tests/catalog.rs"]
mod catalog_tests;
#[path = "tests/portable.rs"]
mod portable_tests;
#[path = "tests/remote.rs"]
mod remote_tests;
struct CountReads {
    inner: io::Directory,
    blobs: std::sync::Arc<std::sync::atomic::AtomicUsize>,
    prefix: &'static str,
}
impl io::ObjectStore for CountReads {
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        if name.starts_with(self.prefix) {
            self.blobs
                .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        }
        self.inner.read(name, limit)
    }
    fn create(&self, name: &str, bytes: &[u8]) -> Result<bool> {
        self.inner.create(name, bytes)
    }
    fn delete(&self, name: &str) -> Result<()> {
        self.inner.delete(name)
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        self.inner.list(prefix, after, limit)
    }
    fn check(&self) -> Result<()> {
        self.inner.check()
    }
}

#[test]
fn garbage_collection_verifies_shared_chunks_once_and_rejects_corruption() {
    use std::sync::{
        Arc,
        atomic::{AtomicUsize, Ordering},
    };
    let work = super::super::tests::Workspace::new();
    let root = work.0.join("shared-repository");
    fs::create_dir(&root).unwrap();
    let reads = Arc::new(AtomicUsize::new(0));
    let repo = Repository::open(
        Box::new(CountReads {
            inner: io::Directory::open(root.clone(), None).unwrap(),
            blobs: reads.clone(),
            prefix: "blob-",
        }),
        "11111111111111111111111111111111",
        "independent-recovery-password",
        true,
    )
    .unwrap();
    let bytes = b"shared archive";
    let digest = super::super::hex(&Sha256::digest(bytes));
    for n in 1..=3 {
        let mut writer = repo
            .writer(&format!("{n:032x}"), "SQLite", serde_json::json!({}))
            .unwrap();
        writer
            .file("Attachment", &digest, bytes.len() as u64, &digest, |out| {
                out.write_all(bytes)?;
                Ok(())
            })
            .unwrap();
        writer.finish().unwrap();
    }
    reads.store(0, Ordering::Relaxed);
    let plan = repo.preview_gc().unwrap();
    assert_eq!(plan["protectedObjects"], 4);
    assert_eq!(reads.load(Ordering::Relaxed), 1);
    let key = repo.blob_name(&digest).unwrap();
    fs::write(root.join(key), b"corrupt").unwrap();
    assert!(repo.preview_gc().is_err());
}
struct Readback(Vec<u8>);
#[test]
fn initialization_rejects_objects_after_all_allowed_session_markers() {
    let work = super::super::tests::Workspace::new();
    let root = work.0.join("nonempty-repository");
    fs::create_dir(&root).unwrap();
    let io = io::Directory::open(root, None).unwrap();
    for name in ["writer-lock", "writer-session", "zz-unregistered"] {
        io.create(name, b"existing").unwrap();
    }
    assert!(
        Repository::open(
            Box::new(io),
            "11111111111111111111111111111111",
            "independent-recovery-password",
            true
        )
        .is_err()
    );
}
impl Visitor for Readback {
    fn begin(&mut self, _: &str, _: &str, _: u64, _: &str) -> Result<()> {
        Ok(())
    }
    fn data(&mut self, bytes: &[u8]) -> Result<()> {
        self.0.extend_from_slice(bytes);
        Ok(())
    }
    fn end(&mut self) -> Result<()> {
        Ok(())
    }
}
#[test]
fn recovery_points_are_discoverable_independent_deduplicated_and_authenticated() {
    let work = super::super::tests::Workspace::new();
    let root = work.0.join("repository");
    fs::create_dir(&root).unwrap();
    let id = "1234567890abcdef1234567890abcdef";
    let point = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    let second = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    let io = io::Directory::open(root.clone(), None).unwrap();
    let repo = Repository::open(Box::new(io), id, "independent-recovery-password", true).unwrap();
    let bytes = vec![42; sealed::object::MAX_PLAIN + 17];
    let digest = super::super::hex(&Sha256::digest(&bytes));
    let mut writer = repo.writer(point, "SQLite", serde_json::json!({})).unwrap();
    writer
        .file("Attachment", &digest, bytes.len() as u64, &digest, |out| {
            out.write_all(&bytes)?;
            Ok(())
        })
        .unwrap();
    writer.finish().unwrap();
    let mut writer = repo
        .writer(second, "SQLite", serde_json::json!({}))
        .unwrap();
    writer
        .file("Attachment", &digest, bytes.len() as u64, &digest, |out| {
            out.write_all(&bytes)?;
            Ok(())
        })
        .unwrap();
    assert_eq!(writer.finish().unwrap().new_bytes, 0);
    assert_eq!(repo.list("", 10).unwrap().len(), 2);
    let mut output = Readback(Vec::new());
    repo.verify(second, &mut output).unwrap();
    assert_eq!(output.0, bytes);
    drop(repo);
    let io = io::Directory::open(root.clone(), None).unwrap();
    assert!(Repository::open(Box::new(io), id, "wrong-recovery-password", false).is_err());
    let io = io::Directory::open(root.clone(), None).unwrap();
    let repo = Repository::open(Box::new(io), id, "independent-recovery-password", false).unwrap();
    let page = root.join(format!("manifest-{second}-0000000000"));
    let mut corrupt = fs::read(&page).unwrap();
    let last = corrupt.len() - 1;
    corrupt[last] ^= 1;
    fs::write(&page, corrupt).unwrap();
    assert!(repo.verify(second, &mut Readback(Vec::new())).is_err());
    assert!(repo.verify(point, &mut Readback(Vec::new())).is_ok());
}

#[test]
fn garbage_collection_rechecks_roots_holds_and_readers_and_rotation_keeps_objects() {
    use serde_json::json;
    let work = super::super::tests::Workspace::new();
    let root = work.0.join("gc-repository");
    fs::create_dir(&root).unwrap();
    let id = "11111111111111111111111111111111";
    let repo = Repository::open(
        Box::new(io::Directory::open(root.clone(), None).unwrap()),
        id,
        "first-recovery-password",
        true,
    )
    .unwrap();
    let point = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    let content = b"permanent business data";
    let digest = super::super::hex(&Sha256::digest(content));
    let mut writer = repo.writer(point, "SQLite", json!({})).unwrap();
    writer
        .file(
            "Attachment",
            &digest,
            content.len() as u64,
            &digest,
            |out| {
                out.write_all(content)?;
                Ok(())
            },
        )
        .unwrap();
    writer.finish().unwrap();
    repo.hold(point, "audit evidence", true).unwrap();
    let orphan = repo
        .blob_name(&super::super::hex(&Sha256::digest(b"orphan")))
        .unwrap();
    repo.put(&orphan, b"orphan").unwrap();
    let first = repo.preview_gc().unwrap();
    assert_eq!(first["count"], 0);
    assert_eq!(first["waitingObjects"], 1);
    let mark = format!("mark-{orphan}");
    repo.io.delete(&mark).unwrap();
    repo.put(&mark,&serde_json::to_vec(&json!({"name":orphan,"markedAt":(chrono::Utc::now()-chrono::Duration::hours(25)).to_rfc3339()})).unwrap()).unwrap();
    let ready = repo.preview_gc().unwrap();
    assert_eq!(ready["count"], 1);
    repo.hold(point, "release", false).unwrap();
    assert!(repo.execute_gc(ready["token"].as_str().unwrap()).is_err());
    repo.io.create("reader-test", b"active").unwrap();
    assert!(repo.preview_gc().is_err());
    repo.io.delete("reader-test").unwrap();
    let ready = repo.preview_gc().unwrap();
    let token = ready["token"].as_str().unwrap();
    let done = repo.execute_gc(token).unwrap();
    assert_eq!(done["count"], 1);
    assert_eq!(repo.execute_gc(token).unwrap(), done);
    repo.verify(point, &mut Readback(Vec::new())).unwrap();
    let object = repo.blob_name(&digest).unwrap();
    let original = repo.io.read(&object, 10000).unwrap().unwrap();
    repo.rotate_password("second-recovery-password").unwrap();
    assert_eq!(repo.io.read(&object, 10000).unwrap().unwrap(), original);
    let portable = work.0.join("portable.edmpoint");
    let mut output = fs::File::create(&portable).unwrap();
    repo.export(point, &mut output).unwrap();
    drop(output);
    drop(repo);
    assert!(
        Repository::open(
            Box::new(io::Directory::open(root.clone(), None).unwrap()),
            id,
            "first-recovery-password",
            false
        )
        .is_err()
    );
    let repo = Repository::open(
        Box::new(io::Directory::open(root.clone(), None).unwrap()),
        id,
        "second-recovery-password",
        false,
    )
    .unwrap();
    repo.verify(point, &mut Readback(Vec::new())).unwrap();
    let (direct, p) = Repository::from_portable(
        fs::File::open(&portable).unwrap(),
        "second-recovery-password",
    )
    .unwrap();
    assert_eq!(p, point);
    let mut out = Readback(Vec::new());
    direct.verify(point, &mut out).unwrap();
    assert_eq!(out.0, content);
    drop(direct);
    let mut bytes = fs::read(&portable).unwrap();
    bytes.push(1);
    fs::write(&portable, bytes).unwrap();
    assert!(
        Repository::from_portable(
            fs::File::open(&portable).unwrap(),
            "second-recovery-password"
        )
        .is_err()
    );
    let page = root.join(points::page_name(point, 0));
    let mut bytes = fs::read(&page).unwrap();
    let end = bytes.len() - 1;
    bytes[end] ^= 1;
    fs::write(page, bytes).unwrap();
    assert!(repo.preview_gc().is_err());
}
