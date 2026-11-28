use super::*;
#[test]
fn portable_export_emits_shared_objects_once_and_rejects_inconsistent_manifests() {
    use std::sync::{
        Arc,
        atomic::{AtomicUsize, Ordering},
    };
    let work = super::super::super::tests::Workspace::new();
    let root = work.0.join("portable-repository");
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
    let id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    let bytes = b"shared file content";
    let digest = super::super::super::hex(&Sha256::digest(bytes));
    let mut writer = repo.writer(id, "SQLite", serde_json::json!({})).unwrap();
    for name in ["a.bin", "b.bin"] {
        writer
            .file("Attachment", name, bytes.len() as u64, &digest, |out| {
                out.write_all(bytes)?;
                Ok(())
            })
            .unwrap();
    }
    let mut commit = writer.finish().unwrap();
    reads.store(0, Ordering::Relaxed);
    let mut exported = Vec::new();
    repo.export(id, &mut exported).unwrap();
    assert_eq!(reads.load(Ordering::Relaxed), 1);
    let mut offset = portable::MAGIC.len();
    let mut names = std::collections::BTreeSet::new();
    loop {
        let len = u16::from_le_bytes(exported[offset..offset + 2].try_into().unwrap()) as usize;
        offset += 2;
        if len == 0 {
            break;
        }
        let name = std::str::from_utf8(&exported[offset..offset + len])
            .unwrap()
            .to_owned();
        offset += len;
        assert!(names.insert(name));
        let size = u32::from_le_bytes(exported[offset..offset + 4].try_into().unwrap()) as usize;
        offset += 4 + size;
    }
    assert_eq!(offset, exported.len());
    assert_eq!(names.len(), 4);
    let file = work.0.join("point.edmpoint");
    fs::write(&file, &exported).unwrap();
    let (transport, point) = Repository::from_portable(
        fs::File::open(file).unwrap(),
        "independent-recovery-password",
    )
    .unwrap();
    let mut data = Readback(Vec::new());
    transport.verify(&point, &mut data).unwrap();
    assert_eq!(data.0, bytes.repeat(2));
    // Valid encryption must not hide an inconsistent whole-file digest.
    let page = points::page_name(id, 0);
    let plain = repo.read(&page).unwrap();
    let changed = String::from_utf8(plain.to_vec())
        .unwrap()
        .replacen(&digest, &"0".repeat(64), 1);
    fs::write(
        root.join("objects").join(&page),
        repo.encrypted(&page, changed.as_bytes()).unwrap(),
    )
    .unwrap();
    commit.manifest_digest = super::super::super::hex(&Sha256::digest(changed.as_bytes()));
    let name = format!("point-{id}.commit");
    fs::write(
        root.join(&name),
        repo.encrypted(&name, &serde_json::to_vec(&commit).unwrap())
            .unwrap(),
    )
    .unwrap();
    assert!(repo.export(id, &mut Vec::new()).is_err());
}
