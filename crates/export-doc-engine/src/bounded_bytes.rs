use std::io::{self, Write};

/// A serialization/output sink that refuses bytes before growing beyond its budget.
pub(crate) struct BoundedBytes {
    pub bytes: Vec<u8>,
    pub limit: usize,
}
impl Write for BoundedBytes {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        if bytes.len() > self.limit.saturating_sub(self.bytes.len()) {
            return Err(io::Error::other("输出超过容量上限。"));
        }
        self.bytes.extend_from_slice(bytes);
        Ok(bytes.len())
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
