use export_doc_engine::{controlled_process, operation::OperationScope};
use std::{
    io::{Read, Write},
    process::Command,
    time::{Duration, Instant},
};

fn child(mode: &str) -> Command {
    let mut command = Command::new(std::env::current_exe().unwrap());
    command.args(["--exact", "controlled_child_fixture", "--nocapture"]);
    command.env("EXPORTDOC_CONTROLLED_PROCESS_TEST", mode);
    command
}

#[test]
fn controlled_child_fixture() {
    let Ok(mode) = std::env::var("EXPORTDOC_CONTROLLED_PROCESS_TEST") else {
        return;
    };
    match mode.as_str() {
        "echo" => {
            std::io::stderr().write_all(&vec![b'e'; 20000]).unwrap();
            let mut input = Vec::new();
            std::io::stdin().read_to_end(&mut input).unwrap();
            std::io::stdout().write_all(&input).unwrap();
            std::io::stdout().flush().unwrap();
        }
        "overflow" => {
            for _ in 0..200 {
                let _ = std::io::stdout().write_all(&[b'x'; 16384]);
            }
        }
        "idle" => std::thread::sleep(Duration::from_secs(60)),
        _ => panic!("unknown child fixture"),
    }
    std::process::exit(0);
}

#[test]
fn borrowed_input_and_concurrent_diagnostic_pipes_complete_without_losing_bytes() {
    let input = vec![0xab; 1024 * 1024];
    let output = controlled_process::run(
        &mut child("echo"),
        input.as_slice(),
        2 * 1024 * 1024,
        Duration::from_secs(10),
    )
    .unwrap();
    assert!(output.status.success());
    assert!(output.stdout.ends_with(&input));
    assert_eq!(output.stderr.len(), 8192);
}

#[test]
fn output_limit_stops_the_process_and_releases_all_pipes() {
    let started = Instant::now();
    let error =
        controlled_process::run(&mut child("overflow"), &[], 32768, Duration::from_secs(10))
            .err()
            .unwrap();
    assert_eq!(error.status, Some(503));
    assert!(started.elapsed() < Duration::from_secs(5));
}

#[test]
fn streamed_input_is_read_in_bounded_chunks_and_transferred_exactly() {
    struct Chunked(std::io::Take<std::io::Repeat>);
    impl Read for Chunked {
        fn read(&mut self, buffer: &mut [u8]) -> std::io::Result<usize> {
            assert!(buffer.len() <= 64 * 1024);
            self.0.read(buffer)
        }
    }
    let size = 4 * 1024 * 1024;
    let output = controlled_process::run_reader(
        &mut child("echo"),
        Chunked(std::io::repeat(0xab).take(size)),
        size as usize + 1024,
        Duration::from_secs(10),
    )
    .unwrap();
    assert!(output.status.success());
    assert!(output.stdout.ends_with(&vec![0xab; size as usize]));
}

#[test]
fn cancellation_and_timeout_release_a_blocked_input_writer() {
    let input = vec![0; 1024 * 1024];
    let scope = OperationScope::new(Duration::from_secs(10));
    let cancel = scope.clone();
    let started = Instant::now();
    std::thread::scope(|threads| {
        threads.spawn(move || {
            std::thread::sleep(Duration::from_millis(150));
            cancel.cancel();
        });
        let error = scope
            .run(|| {
                controlled_process::run_reader(
                    &mut child("idle"),
                    std::io::Cursor::new(input.as_slice()),
                    32768,
                    Duration::from_secs(10),
                )
            })
            .err()
            .unwrap();
        assert_eq!(error.status, Some(504));
    });
    assert!(started.elapsed() < Duration::from_secs(5));
    let error = controlled_process::run(
        &mut child("idle"),
        input.as_slice(),
        32768,
        Duration::from_millis(150),
    )
    .err()
    .unwrap();
    assert_eq!(error.status, Some(504));
    assert!(started.elapsed() < Duration::from_secs(5));
}

#[test]
fn streamed_output_is_not_retained_and_writer_failure_releases_the_child() {
    struct CountingWriter(usize);
    impl Write for CountingWriter {
        fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
            assert!(bytes.len() <= 64 * 1024);
            self.0 += bytes.len();
            Ok(bytes.len())
        }
        fn flush(&mut self) -> std::io::Result<()> {
            Ok(())
        }
    }
    let mut writer = CountingWriter(0);
    let output = controlled_process::run_to_writer(
        &mut child("overflow"),
        &mut writer,
        Duration::from_secs(10),
    )
    .unwrap();
    assert!(output.status.success());
    assert!(output.stdout.is_empty());
    assert!(writer.0 >= 200 * 16384);

    struct FullDisk;
    impl Write for FullDisk {
        fn write(&mut self, _: &[u8]) -> std::io::Result<usize> {
            Err(std::io::Error::other("simulated disk budget exhausted"))
        }
        fn flush(&mut self) -> std::io::Result<()> {
            Ok(())
        }
    }
    let started = Instant::now();
    let error = controlled_process::run_to_writer(
        &mut child("overflow"),
        FullDisk,
        Duration::from_secs(10),
    )
    .err()
    .unwrap();
    assert_eq!(error.status, Some(503));
    assert!(started.elapsed() < Duration::from_secs(5));
}
