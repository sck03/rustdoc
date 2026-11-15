use super::*;
use crate::engine::{file_store::FileStore, test_support::ScratchDirectory};

#[test]
fn cached_local_reads_have_independent_cursors_and_detect_same_size_tampering() {
    let root = ScratchDirectory::new();
    let store = FileStore::open(&root.paths(), false).unwrap();
    let prepared = store.prepare(b"original").unwrap();
    let mut first = store.open_object(&prepared.object).unwrap();
    first.seek(SeekFrom::Start(4)).unwrap();
    let mut second = store.open_object(&prepared.object).unwrap();
    let mut bytes = Vec::new();
    second.read_to_end(&mut bytes).unwrap();
    assert_eq!(bytes, b"original");
    drop((first, second));
    let path = store
        .object_path(&store.configuration().unwrap(), &prepared.object)
        .unwrap();
    let modified = fs::metadata(&path).unwrap().modified().unwrap();
    fs::write(&path, b"tampered").unwrap();
    fs::File::options()
        .write(true)
        .open(&path)
        .unwrap()
        .set_modified(modified)
        .unwrap();
    assert!(
        store.open_object(&prepared.object).is_err(),
        "restored mtime cannot hide a changed object"
    );
    assert!(store.verify_object(&prepared.object).is_err());
    fs::write(&path, b"original").unwrap();
    assert!(store.open_object(&prepared.object).is_ok());
    fs::remove_file(&path).unwrap();
    assert!(
        store.open_object(&prepared.object).is_err(),
        "metadata cache cannot resurrect missing files"
    );
}
