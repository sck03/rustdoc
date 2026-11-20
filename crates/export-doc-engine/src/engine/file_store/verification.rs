//! Each independent reader verifies the content before serving an object.
//! File timestamps can collide or be restored and are not integrity evidence.
use super::*;
#[cfg(test)]
#[path = "tests/verification.rs"]
mod tests;

pub(super) fn open(path: &Path, object: &FileObject) -> Result<fs::File> {
    let mut file = fs::File::open(path).map_err(|_| unavailable("已登记文件对象缺失或不可读。"))?;
    check(&mut file, object)?;
    Ok(file)
}

pub(super) fn check(file: &mut fs::File, object: &FileObject) -> Result<()> {
    verify(file, object)?;
    file.seek(SeekFrom::Start(0))?;
    Ok(())
}
