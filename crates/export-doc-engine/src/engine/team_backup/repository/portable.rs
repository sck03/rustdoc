//! Sequential portable transport of the exact repository objects. Only a fully
//! verified final COMMIT is made visible on import; scratch use stays bounded.
use super::*;
use std::io::Write;
pub(super) const MAGIC: &[u8] = b"EDM-RECOVERY-POINT-1\n";
fn frame(output: &mut impl Write, name: &str, bytes: &[u8]) -> Result<()> {
    output.write_all(&(name.len() as u16).to_le_bytes())?;
    output.write_all(name.as_bytes())?;
    output.write_all(&(bytes.len() as u32).to_le_bytes())?;
    output.write_all(bytes)?;
    Ok(())
}
fn object(repo: &Repository, output: &mut impl Write, name: &str) -> Result<()> {
    let bytes = repo
        .io
        .read(name, sealed::object::MAX_PLAIN + sealed::object::OVERHEAD)?
        .ok_or_else(|| invalid("便携导出缺少仓库对象。"))?;
    repo.read(name)?;
    frame(output, name, &bytes)
}
pub(super) fn export(repo: &Repository, id: &str, output: &mut impl Write) -> Result<()> {
    let commit = repo.commit(id)?;
    // Full verification is explicit; export never turns a stale index into a valid copy.
    repo.verify(id, &mut Verify)?;
    output.write_all(MAGIC)?;
    frame(
        output,
        HEADER,
        &current_header(repo.io.as_ref())?
            .1
            .or(repo.io.read(HEADER, 64 * 1024)?)
            .ok_or_else(|| invalid("仓库头缺失。"))?,
    )?;
    for page in 0..commit.pages {
        let name = points::page_name(id, page);
        let plain = repo.read(&name)?;
        for line in plain.split(|c| *c == b'\n').filter(|line| !line.is_empty()) {
            if let points::Record::Chunk { key, .. } = serde_json::from_slice(line)? {
                object(repo, output, &key)?;
            }
        }
        object(repo, output, &name)?;
    }
    object(repo, output, &format!("point-{id}.commit"))?;
    output.write_all(&0u16.to_le_bytes())?;
    output.flush()?;
    Ok(())
}
struct Verify;
impl Visitor for Verify {
    fn begin(&mut self, _: &str, _: &str, _: u64, _: &str) -> Result<()> {
        Ok(())
    }
    fn data(&mut self, _: &[u8]) -> Result<()> {
        Ok(())
    }
    fn end(&mut self) -> Result<()> {
        Ok(())
    }
}
