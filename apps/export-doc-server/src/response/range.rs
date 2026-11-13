//! One range per request bounds response work. Unknown units and multipart
//! ranges are ignored; malformed or unsatisfiable byte intervals return 416.
#[derive(Debug, PartialEq, Eq)]
pub(super) struct Selection {
    pub start: u64,
    pub length: u64,
}
pub(super) fn select(header: Option<&str>, size: u64) -> Result<Option<Selection>, ()> {
    let Some(value) = header.and_then(|h| h.strip_prefix("bytes=")) else {
        return Ok(None);
    };
    if value.contains(',') {
        return Ok(None);
    }
    let (start, end) = value.split_once('-').ok_or(())?;
    let number = |value: &str| {
        if value.is_empty() || !value.bytes().all(|b| b.is_ascii_digit()) {
            return Err(());
        }
        value.parse::<u64>().map_err(|_| ())
    };
    if size == 0 {
        return Err(());
    }
    let (start, end) = if start.is_empty() {
        let suffix = number(end)?;
        if suffix == 0 {
            return Err(());
        }
        (size.saturating_sub(suffix), size - 1)
    } else {
        let start = number(start)?;
        let end = if end.is_empty() {
            size - 1
        } else {
            number(end)?.min(size - 1)
        };
        if start >= size || end < start {
            return Err(());
        }
        (start, end)
    };
    Ok(Some(Selection {
        start,
        length: end - start + 1,
    }))
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn ranges_cover_pdf_seeks_suffixes_and_large_files() {
        assert_eq!(
            select(Some("bytes=3-5"), 10),
            Ok(Some(Selection {
                start: 3,
                length: 3
            }))
        );
        assert_eq!(
            select(Some("bytes=3-"), 10),
            Ok(Some(Selection {
                start: 3,
                length: 7
            }))
        );
        assert_eq!(
            select(Some("bytes=-50"), 10),
            Ok(Some(Selection {
                start: 0,
                length: 10
            }))
        );
        assert_eq!(
            select(Some("bytes=4294967296-"), 4294967300),
            Ok(Some(Selection {
                start: 4294967296,
                length: 4
            }))
        );
        for invalid in [
            "bytes=-0",
            "bytes=10-",
            "bytes=4-2",
            "bytes=1--",
            "bytes=+1-2",
            "bytes=18446744073709551616-",
        ] {
            assert_eq!(select(Some(invalid), 10), Err(()), "{invalid}");
        }
        assert_eq!(select(Some("bytes=0-0"), 0), Err(()));
        assert_eq!(select(Some("bytes=0-1,4-5"), 10), Ok(None));
        assert_eq!(select(Some("items=0-1"), 10), Ok(None));
    }
}
