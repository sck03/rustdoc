//! Database snapshots own an inventory; immutable bytes are copied once into
//! their adjacent object repository. Snapshot publication follows object durability.
use super::super::{backup_io, store::Store};
use super::*;
use std::io::{BufRead, BufReader, BufWriter};
const MAGIC: &str = "EDM-SNAPSHOT-OBJECTS-1";
mod retention;
pub(in crate::engine) use retention::collect;

fn repository(snapshot: &Path) -> Result<PathBuf> {
    let root = snapshot
        .parent()
        .ok_or_else(|| invalid("备份目录无效。"))?
        .join("Objects");
    paths::ensure_safe_absolute(&root).map_err(unavailable)?;
    Ok(root)
}
fn object_path(root: &Path, object: &FileObject) -> Result<PathBuf> {
    validate(object)?;
    let path = root.join(&object.digest[..2]).join(&object.digest);
    paths::ensure_safe_absolute(&path).map_err(unavailable)?;
    Ok(path)
}
pub(in crate::engine) fn capture(
    store: &Store,
    snapshot: &Path,
    published: &Path,
    mut page: impl FnMut(&str) -> Result<Vec<FileObject>>,
) -> Result<()> {
    let root = repository(published)?;
    crate::secrets::private_directory(&root).map_err(unavailable)?;
    let (size, digest) = backup_io::digest_file(snapshot)?;
    let manifest = published.with_extension("objects.jsonl");
    paths::atomic_write_with(&manifest, |output| {
        let result: Result<()> = (|| {
            let mut output = BufWriter::new(output);
            writeln!(
                output,
                "{}",
                serde_json::json!({"format":MAGIC,"databaseSize":size,"databaseDigest":digest})
            )?;
            let mut cursor = String::new();
            loop {
                let objects = page(&cursor)?;
                if objects.is_empty() {
                    break;
                }
                for object in objects {
                    crate::operation::check()?;
                    let path = object_path(&root, &object)?;
                    if path.try_exists()? {
                        verify(&mut fs::File::open(&path)?, &object)?;
                    } else {
                        let mut source = store.files.open_object(&object)?;
                        crate::secrets::private_directory(path.parent().unwrap())
                            .map_err(unavailable)?;
                        paths::atomic_write_with(&path, |out| {
                            let mut out = store
                                .files
                                .space
                                .at(path.parent().unwrap().to_path_buf())
                                .writer(out)
                                .map_err(|e| e.to_string())?;
                            backup_io::copy_and_hash(&mut source, &mut out, object.size)
                                .map_err(|e| e.to_string())
                                .and_then(|actual| {
                                    if actual == (object.size, object.digest.clone()) {
                                        Ok(())
                                    } else {
                                        Err("备份对象摘要不符。".into())
                                    }
                                })
                        })
                        .map_err(unavailable)?;
                    }
                    writeln!(output, "{}", serde_json::to_string(&object)?)?;
                    cursor = object.digest;
                }
            }
            writeln!(output, "END")?;
            output.flush()?;
            Ok(())
        })();
        result.map_err(|e| e.to_string())
    })
    .map_err(unavailable)
}
pub(in crate::engine) fn restore(files: &FileStore, snapshot: &Path) -> Result<()> {
    let root = repository(snapshot)?;
    inventory(snapshot, |object| {
        let mut source = fs::File::open(object_path(&root, &object)?)
            .map_err(|_| unavailable("备份对象缺失。"))?;
        verify(&mut source, &object)?;
        source.seek(SeekFrom::Start(0))?;
        let prepared = files.prepare_reader(&mut source, object.size)?;
        if prepared.object != object {
            return Err(unavailable("恢复对象摘要不符。"));
        }
        Ok(())
    })
}
fn inventory(snapshot: &Path, mut visit: impl FnMut(FileObject) -> Result<()>) -> Result<()> {
    let manifest = snapshot.with_extension("objects.jsonl");
    paths::ensure_safe_absolute(&manifest).map_err(unavailable)?;
    let mut input = BufReader::new(
        fs::File::open(manifest)
            .map_err(|_| unavailable("备份文件对象清单缺失，请使用完整恢复包。"))?,
    );
    let header: serde_json::Value = serde_json::from_str(&line(&mut input)?)?;
    let (size, digest) = backup_io::digest_file(snapshot)?;
    if header["format"] != MAGIC
        || header["databaseSize"] != size
        || header["databaseDigest"] != digest
    {
        return Err(unavailable("数据库备份与文件清单不匹配。"));
    }
    let mut previous = String::new();
    loop {
        let entry = line(&mut input)?;
        if entry == "END" {
            break;
        }
        let object: FileObject = serde_json::from_str(&entry)?;
        if object.digest <= previous {
            return Err(unavailable("备份对象顺序无效。"));
        }
        crate::operation::check()?;
        validate(&object)?;
        previous = object.digest.clone();
        visit(object)?;
    }
    if !input.fill_buf()?.is_empty() {
        return Err(unavailable("备份对象清单存在额外数据。"));
    }
    Ok(())
}
fn line(reader: &mut impl BufRead) -> Result<String> {
    let mut bytes = vec![];
    reader.take(4097).read_until(b'\n', &mut bytes)?;
    if bytes.len() > 4096 || bytes.last() != Some(&b'\n') {
        return Err(unavailable("备份对象清单损坏或未完成。"));
    }
    bytes.pop();
    String::from_utf8(bytes).map_err(|_| unavailable("备份对象清单编码无效。"))
}
