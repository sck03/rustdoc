// SPDX-License-Identifier: MIT
//! Allocation checks for untrusted workbook metadata and dense worksheet ranges.
use std::io;

pub(crate) const MAX_ALLOCATION_BYTES: usize = 64 * 1024 * 1024;

pub(crate) fn invalid() -> io::Error {
    io::Error::new(
        io::ErrorKind::InvalidData,
        "workbook allocation limit exceeded",
    )
}

pub(crate) fn bytes<T>(count: usize) -> io::Result<usize> {
    let bytes = count
        .checked_mul(std::mem::size_of::<T>())
        .ok_or_else(invalid)?;
    if bytes > MAX_ALLOCATION_BYTES {
        return Err(invalid());
    }
    Ok(bytes)
}

pub(crate) fn push<T>(values: &mut Vec<T>, value: T) -> io::Result<()> {
    bytes::<T>(values.len().checked_add(1).ok_or_else(invalid)?)?;
    if values.len() == values.capacity() {
        let remaining = MAX_ALLOCATION_BYTES / std::mem::size_of::<T>().max(1) - values.len();
        let growth = values.capacity().max(64).min(remaining);
        values.try_reserve_exact(growth).map_err(|_| invalid())?;
    }
    values.push(value);
    Ok(())
}

pub(crate) fn insert_first<T>(values: &mut Vec<T>, value: T) -> io::Result<()> {
    push(values, value)?;
    values.rotate_right(1);
    Ok(())
}
