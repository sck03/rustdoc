use std::{
    fs, io,
    path::{Path, PathBuf},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

pub fn create(namespace: &str) -> PathBuf {
    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_nanos();
    create_stamped(namespace, stamp)
}

fn create_stamped(namespace: &str, stamp: u128) -> PathBuf {
    let parent = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../.codex-runtime")
        .join(namespace);
    fs::create_dir_all(&parent).unwrap();
    // A clock value is not a unique identity. Never reuse another test's files.
    for sequence in 0_u64.. {
        let root = parent.join(format!("{}-{stamp}-{sequence}", std::process::id()));
        match fs::create_dir(&root) {
            Ok(()) => return root,
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => continue,
            Err(error) => panic!("cannot create test workspace {}: {error}", root.display()),
        }
    }
    unreachable!("test workspace sequence exhausted")
}

#[test]
fn equal_clock_values_cannot_share_or_overwrite_a_test_directory() {
    let roots = std::thread::scope(|scope| {
        let workers: Vec<_> = (0..8)
            .map(|_| scope.spawn(|| create_stamped("storage-workspace-tests", 0)))
            .collect();
        workers
            .into_iter()
            .map(|worker| worker.join().unwrap())
            .collect::<Vec<_>>()
    });
    assert_eq!(
        roots.iter().collect::<std::collections::HashSet<_>>().len(),
        roots.len()
    );
    for (index, root) in roots.iter().enumerate() {
        fs::write(root.join("owner.txt"), index.to_string()).unwrap();
    }
    for (index, root) in roots.iter().enumerate() {
        assert_eq!(
            fs::read_to_string(root.join("owner.txt")).unwrap(),
            index.to_string()
        );
        remove(root).unwrap();
    }
}

/// Call after database handles have left scope. Windows scanners can briefly
/// retain a file without delete sharing; persistent or unrelated errors fail.
pub fn remove(root: &Path) -> io::Result<()> {
    remove_with_timeout(root, Duration::from_secs(10))
}

pub fn remove_with_timeout(root: &Path, timeout: Duration) -> io::Result<()> {
    let started = Instant::now();
    loop {
        match fs::remove_dir_all(root) {
            Ok(()) => return Ok(()),
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
            Err(error)
                if cfg!(windows)
                    && matches!(error.raw_os_error(), Some(32 | 33))
                    && started.elapsed() < timeout =>
            {
                std::thread::sleep(Duration::from_millis(25));
            }
            Err(error) => return Err(error),
        }
    }
}
