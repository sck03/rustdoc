use std::io::{self, Write};

/// A shared input/output buffer that refuses bytes before growing beyond its limit.
pub struct BoundedBytes {
    pub bytes: Vec<u8>,
    pub limit: usize,
}
impl BoundedBytes {
    pub fn capacity_for(&self, additional: usize) -> io::Result<usize> {
        let needed = self
            .bytes
            .len()
            .checked_add(additional)
            .filter(|size| *size <= self.limit)
            .ok_or_else(|| io::Error::other("输出超过容量上限。"))?;
        Ok(if needed > self.bytes.capacity() {
            needed
                .max(self.bytes.capacity().saturating_mul(2).max(1024))
                .min(self.limit)
        } else {
            self.bytes.capacity()
        })
    }
}
impl Write for BoundedBytes {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        let capacity = self.capacity_for(bytes.len())?;
        if capacity > self.bytes.capacity() {
            self.bytes
                .try_reserve_exact(capacity - self.bytes.len())
                .map_err(io::Error::other)?;
        }
        self.bytes.extend_from_slice(bytes);
        Ok(bytes.len())
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
