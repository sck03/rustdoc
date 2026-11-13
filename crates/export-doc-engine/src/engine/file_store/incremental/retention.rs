//! Merge sorted snapshot inventories on disk before reclaiming unreferenced bytes.
//! Memory and open handles grow logarithmically with the number of recovery points.
use super::*;

pub(in crate::engine) fn collect(directory: &Path, cache: &Path) -> Result<u64> {
    paths::ensure_safe_absolute(directory).map_err(unavailable)?;
    let mut levels: Vec<Option<fs::File>> = vec![];
    for entry in fs::read_dir(directory)? {
        let snapshot = entry?.path();
        if !matches!(
            snapshot.extension().and_then(|x| x.to_str()),
            Some("sqlite3" | "dump")
        ) {
            continue;
        }
        paths::ensure_safe_absolute(&snapshot).map_err(unavailable)?;
        let mut index = paths::anonymous_file(cache).map_err(unavailable)?;
        {
            let mut out = BufWriter::new(&mut index);
            inventory(&snapshot, |object| {
                writeln!(out, "{}", object.digest)?;
                Ok(())
            })?;
            out.flush()?;
        }
        let mut level = 0;
        while level < levels.len() {
            let Some(previous) = levels[level].take() else {
                break;
            };
            index = merge(previous, index, cache)?;
            level += 1;
        }
        if level == levels.len() {
            levels.push(None);
        }
        levels[level] = Some(index);
    }
    let mut index = paths::anonymous_file(cache).map_err(unavailable)?;
    for level in levels.into_iter().flatten() {
        index = merge(index, level, cache)?;
    }
    let count = index.metadata()?.len() / 65;
    let root = directory.join("Objects");
    paths::ensure_safe_absolute(&root).map_err(unavailable)?;
    if !root.try_exists()? {
        return Ok(0);
    }
    let mut reclaimed = 0;
    for prefix in fs::read_dir(root)? {
        let prefix = prefix?;
        paths::ensure_safe_absolute(&prefix.path()).map_err(unavailable)?;
        let name = prefix.file_name();
        let Some(name) = name.to_str().filter(|v| hex(v, 2)) else {
            continue;
        };
        if !prefix.file_type()?.is_dir() {
            continue;
        }
        for entry in fs::read_dir(prefix.path())? {
            crate::operation::check()?;
            let entry = entry?;
            let path = entry.path();
            paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            let file_name = entry.file_name();
            let Some(digest) = file_name
                .to_str()
                .filter(|v| hex(v, 64) && v.starts_with(name))
            else {
                continue;
            };
            if entry.file_type()?.is_file() && !contains(&mut index, count, digest.as_bytes())? {
                let size = entry.metadata()?.len();
                fs::remove_file(path)?;
                reclaimed += size;
            }
        }
    }
    Ok(reclaimed)
}
fn hex(value: &str, length: usize) -> bool {
    value.len() == length
        && value
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
}
fn next(reader: &mut impl BufRead) -> Result<Option<[u8; 65]>> {
    if reader.fill_buf()?.is_empty() {
        return Ok(None);
    }
    let mut digest = [0; 65];
    reader.read_exact(&mut digest)?;
    Ok(Some(digest))
}
fn merge(mut left: fs::File, mut right: fs::File, cache: &Path) -> Result<fs::File> {
    left.rewind()?;
    right.rewind()?;
    let mut left = BufReader::new(left);
    let mut right = BufReader::new(right);
    let mut output = paths::anonymous_file(cache).map_err(unavailable)?;
    {
        let mut writer = BufWriter::new(&mut output);
        let (mut a, mut b) = (next(&mut left)?, next(&mut right)?);
        while a.is_some() || b.is_some() {
            crate::operation::check()?;
            let take_left = a.is_some() && (b.is_none() || a <= b);
            let selected = if take_left { a.unwrap() } else { b.unwrap() };
            writer.write_all(&selected)?;
            if a == Some(selected) {
                a = next(&mut left)?;
            }
            if b == Some(selected) {
                b = next(&mut right)?;
            }
        }
        writer.flush()?;
    }
    Ok(output)
}
fn contains(index: &mut fs::File, count: u64, digest: &[u8]) -> Result<bool> {
    let (mut low, mut high) = (0, count);
    while low < high {
        let mid = low + (high - low) / 2;
        index.seek(SeekFrom::Start(mid * 65))?;
        let mut entry = [0; 64];
        index.read_exact(&mut entry)?;
        match entry.as_slice().cmp(digest) {
            std::cmp::Ordering::Equal => return Ok(true),
            std::cmp::Ordering::Less => low = mid + 1,
            std::cmp::Ordering::Greater => high = mid,
        }
    }
    Ok(false)
}
