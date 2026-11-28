use super::*;
#[test]
fn frame_read_stops_before_the_next_chunk_on_cancel() {
    struct CancelAfterRead(crate::operation::OperationScope, usize);
    impl Read for CancelAfterRead {
        fn read(&mut self, bytes: &mut [u8]) -> std::io::Result<usize> {
            self.1 += bytes.len();
            bytes.fill(0);
            self.0.cancel();
            Ok(bytes.len())
        }
    }
    let scope = crate::operation::OperationScope::new(std::time::Duration::from_secs(10));
    let mut reader = CancelAfterRead(scope.clone(), 0);
    let result = scope.run(|| read_exact(&mut reader, &mut vec![0; 256 * 1024]));
    assert_eq!(result.unwrap_err().diagnostic, Some("cancelled-or-timeout"));
    assert_eq!(reader.1, 64 * 1024);
}
