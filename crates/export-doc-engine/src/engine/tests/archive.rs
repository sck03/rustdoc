use super::*;
use crate::operation::OperationScope;
use std::{io::Cursor, time::Duration};
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};

fn package(content: &[u8]) -> Vec<u8> {
    let mut writer = ZipWriter::new(Cursor::new(Vec::new()));
    writer
        .start_file(
            "data",
            SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated),
        )
        .unwrap();
    writer.write_all(content).unwrap();
    writer.finish().unwrap().into_inner()
}

#[test]
fn bounded_reads_check_actual_length_crc_and_limits() {
    let content = vec![b'x'; 200_000];
    let bytes = package(&content);
    for declared in [0u32, 2, content.len() as u32, content.len() as u32 + 1] {
        let mut modified = bytes.clone();
        let central = modified
            .windows(4)
            .position(|s| s == b"PK\x01\x02")
            .unwrap();
        modified[central + 24..central + 28].copy_from_slice(&declared.to_le_bytes());
        let mut archive = ZipArchive::new(Cursor::new(modified)).unwrap();
        let result = read_entry(&mut archive.by_index(0).unwrap(), content.len() as u64 + 1);
        if declared as usize == content.len() {
            assert_eq!(result.unwrap(), content);
        } else {
            assert_eq!(result.unwrap_err().status, Some(400));
        }
    }
    let mut archive = ZipArchive::new(Cursor::new(&bytes)).unwrap();
    assert!(read_entry(&mut archive.by_index(0).unwrap(), 10).is_err());
    verify_entry(&mut archive.by_index(0).unwrap(), content.len() as u64).unwrap();
    let mut corrupt = bytes;
    let central = corrupt.windows(4).position(|s| s == b"PK\x01\x02").unwrap();
    corrupt[central + 16] ^= 1;
    let mut archive = ZipArchive::new(Cursor::new(corrupt)).unwrap();
    assert!(verify_entry(&mut archive.by_index(0).unwrap(), content.len() as u64).is_err());
}

#[test]
fn cancellation_stops_between_chunks() {
    struct CancelAfterWrite(OperationScope, usize);
    impl Write for CancelAfterWrite {
        fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
            self.1 += bytes.len();
            self.0.cancel();
            Ok(bytes.len())
        }
        fn flush(&mut self) -> std::io::Result<()> {
            Ok(())
        }
    }
    let content = vec![b'x'; 200_000];
    let mut archive = ZipArchive::new(Cursor::new(package(&content))).unwrap();
    let scope = OperationScope::new(Duration::from_secs(10));
    let mut sink = CancelAfterWrite(scope.clone(), 0);
    let result = scope.run(|| {
        copy_entry(
            &mut archive.by_index(0).unwrap(),
            content.len() as u64,
            &mut sink,
        )
    });
    assert_eq!(result.unwrap_err().status, Some(504));
    assert!(sink.1 > 0 && sink.1 <= 64 * 1024);
}
