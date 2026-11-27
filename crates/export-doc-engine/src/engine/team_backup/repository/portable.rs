//! Sequential portable transport of the exact repository objects. Only a fully
//! verified final COMMIT is made visible on import; scratch use stays bounded.
use super::*;
use std::collections::{BTreeSet, VecDeque};
use std::io::Write;
pub(super) const MAGIC: &[u8] = b"EDM-RECOVERY-POINT-1\n";
fn frame(output: &mut impl Write, name: &str, bytes: &[u8]) -> Result<()> {
    output.write_all(&(name.len() as u16).to_le_bytes())?;
    output.write_all(name.as_bytes())?;
    output.write_all(&(bytes.len() as u32).to_le_bytes())?;
    output.write_all(bytes)?;
    Ok(())
}
pub(super) fn export(repo: &Repository, id: &str, output: &mut impl Write) -> Result<()> {
    valid_id(id)?;
    let commit_name = format!("point-{id}.commit");
    let (commit_bytes, plain) = repo.read_object(&commit_name)?;
    let commit = repo.decode_commit(id, &plain)?;
    output.write_all(MAGIC)?;
    let header = match current_header(repo.io.as_ref())?.1 {
        Some(header) => header,
        None => repo
            .io
            .read(HEADER, 64 * 1024)?
            .ok_or_else(|| invalid("仓库头缺失。"))?,
    };
    frame(output, HEADER, &header)?;
    // The shared walker validates file digests, manifest order and totals using
    // the very ciphertext emitted here. COMMIT is written only after it succeeds.
    // Names are bounded by the import index budget; plaintext cache is capped at
    // 16 MiB and zeroized. Repeated chunks outside the cache are read again for
    // whole-file hashing, but never emitted twice.
    let mut emitted = BTreeSet::new();
    let mut cache: VecDeque<(String, zeroize::Zeroizing<Vec<u8>>)> = VecDeque::new();
    let mut cached_bytes = 0;
    points::visit_with(repo, commit, &mut Verify, &mut |name| {
        crate::operation::check()?;
        if let Some(index) = cache.iter().position(|(key, _)| key == name) {
            let entry = cache.remove(index).unwrap();
            let plain = entry.1.clone();
            cache.push_back(entry);
            return Ok(plain);
        }
        let (bytes, plain) = repo.read_object(name)?;
        if emitted.insert(name.to_owned()) {
            if emitted.len() > 999_998 {
                return Err(invalid("便携恢复点超过一百万对象预算，请拆分仓库。"));
            }
            frame(output, name, &bytes)?;
        }
        if name.starts_with("blob-") {
            while cached_bytes + plain.len() > 16 * 1024 * 1024 || cache.len() >= 64 {
                cached_bytes -= cache.pop_front().unwrap().1.len();
            }
            cached_bytes += plain.len();
            cache.push_back((name.to_owned(), plain.clone()));
        }
        Ok(plain)
    })?;
    frame(output, &commit_name, &commit_bytes)?;
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
