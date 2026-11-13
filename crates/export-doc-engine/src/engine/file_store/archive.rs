//! Streamed object inventory inside the authenticated recovery package. Object
//! count does not consume ZIP entries or require a whole-library JSON manifest.
use super::*;
const MAGIC: &[u8] = b"EDM-FILE-OBJECTS-1\n";
pub(in crate::engine) fn verify_local_inventory(
    root: &Path,
    mut page: impl FnMut(&str) -> Result<Vec<FileObject>>,
) -> Result<()> {
    let mut cursor = String::new();
    loop {
        let objects = page(&cursor)?;
        if objects.is_empty() {
            return Ok(());
        }
        for object in objects {
            crate::operation::check()?;
            validate(&object)?;
            let path = root
                .join("Files")
                .join("objects")
                .join(&object.digest[..2])
                .join(&object.digest);
            paths::ensure_safe_absolute(&path).map_err(invalid)?;
            verify(
                &mut fs::File::open(&path)
                    .map_err(|_| unavailable("恢复包缺少数据库引用的文件。"))?,
                &object,
            )?;
            cursor = object.digest;
        }
    }
}

pub(in crate::engine) fn export(store: &super::super::store::Store, path: &Path) -> Result<()> {
    let mut output = fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(path)?;
    output.write_all(MAGIC)?;
    let policy = serde_json::to_vec(&store.files.configuration()?.policy)?;
    output.write_all(&(policy.len() as u32).to_le_bytes())?;
    output.write_all(&policy)?;
    let mut cursor = String::new();
    loop {
        let objects = store.connection()?.file_objects(&cursor, 200, false)?;
        if objects.is_empty() {
            break;
        }
        for object in objects {
            crate::operation::check()?;
            let mut file = store.files.open_object(&object)?;
            output.write_all(object.digest.as_bytes())?;
            output.write_all(&object.size.to_le_bytes())?;
            if super::super::backup_io::copy_and_hash(&mut file, &mut output, object.size)?
                != (object.size, object.digest.clone())
            {
                return Err(unavailable("文件对象在备份时发生变化。"));
            }
            cursor = object.digest;
        }
    }
    output.write_all(&[0; 64])?;
    output.sync_all()?;
    Ok(())
}
pub(in crate::engine) fn restore_archive(archive: &Path, staging: &Path) -> Result<()> {
    let mut input = fs::File::open(archive)?;
    let mut magic = vec![0; MAGIC.len()];
    input.read_exact(&mut magic)?;
    if magic != MAGIC {
        return Err(invalid("文件对象归档格式无效。"));
    }
    let mut length = [0; 4];
    input.read_exact(&mut length)?;
    let length = u32::from_le_bytes(length) as usize;
    if length > 16 * 1024 {
        return Err(invalid("文件策略清单过大。"));
    }
    let mut bytes = vec![0; length];
    input.read_exact(&mut bytes)?;
    let policy: Policy =
        serde_json::from_slice(&bytes).map_err(|_| invalid("文件策略清单无效。"))?;
    policy.validate()?;
    let root = staging.join("Files");
    crate::secrets::private_directory(&root).map_err(unavailable)?;
    let mut previous = String::new();
    loop {
        crate::operation::check()?;
        let mut digest = [0; 64];
        input.read_exact(&mut digest)?;
        if digest == [0; 64] {
            break;
        }
        let digest =
            String::from_utf8(digest.to_vec()).map_err(|_| invalid("文件归档摘要无效。"))?;
        let mut size = [0; 8];
        input.read_exact(&mut size)?;
        let object = FileObject {
            digest,
            size: u64::from_le_bytes(size),
        };
        validate(&object)?;
        if object.digest <= previous {
            return Err(invalid("文件对象清单重复或排序无效。"));
        }
        let path = root
            .join("objects")
            .join(&object.digest[..2])
            .join(&object.digest);
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        crate::secrets::private_directory(path.parent().unwrap()).map_err(unavailable)?;
        paths::atomic_write_with(&path, |output| {
            let actual = super::super::backup_io::copy_and_hash(
                &mut Read::by_ref(&mut input).take(object.size),
                output,
                object.size,
            )
            .map_err(|e| e.to_string())?;
            if actual != (object.size, object.digest.clone()) {
                return Err("文件对象归档不完整或摘要不符。".into());
            }
            Ok(())
        })
        .map_err(invalid)?;
        previous = object.digest;
    }
    let mut extra = [0];
    if input.read(&mut extra)? != 0 {
        return Err(invalid("文件对象归档存在额外数据。"));
    }
    let identity = paths::nonce().map_err(unavailable)?;
    paths::atomic_write(&root.join(".store-id"), identity.as_bytes()).map_err(unavailable)?;
    let config = Configuration {
        version: 1,
        revision: 1,
        identity,
        backend: Backend::Local {
            directory: "Files".into(),
        },
        policy,
    };
    let config_path = staging.join("Configuration").join("file-storage.json");
    crate::secrets::private_directory(config_path.parent().unwrap()).map_err(unavailable)?;
    paths::atomic_write(&config_path, &serde_json::to_vec(&config)?).map_err(unavailable)?;
    Ok(())
}
