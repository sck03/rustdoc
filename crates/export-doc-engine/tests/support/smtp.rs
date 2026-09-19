//! A loopback SMTP peer; tests never use a configured external mail server.
use std::{
    io::{BufRead, BufReader, Write},
    net::{TcpListener, TcpStream},
    sync::Arc,
    sync::atomic::{AtomicBool, Ordering},
    sync::mpsc::{self, Receiver},
    thread::JoinHandle,
    time::{Duration, Instant},
};
pub struct Smtp {
    pub port: u16,
    pub body: Receiver<String>,
    handle: Option<JoinHandle<()>>,
    stop: Arc<AtomicBool>,
}
impl Smtp {
    pub fn start(acknowledge: bool) -> Self {
        let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let port = listener.local_addr().unwrap().port();
        listener.set_nonblocking(true).unwrap();
        let (sender, body) = mpsc::channel();
        let stop = Arc::new(AtomicBool::new(false));
        let stop_thread = Arc::clone(&stop);
        let handle = std::thread::spawn(move || {
            // A test may open several sequential sessions; stay available while the
            // peer is alive, and stop promptly when it is dropped.
            let deadline = Instant::now() + Duration::from_secs(60);
            while !stop_thread.load(Ordering::Acquire) && Instant::now() < deadline {
                match listener.accept() {
                    Ok((stream, _)) => {
                        if let Some(body) = session(stream, acknowledge) {
                            let _ = sender.send(body);
                        }
                    }
                    Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        std::thread::sleep(Duration::from_millis(10))
                    }
                    Err(e) => panic!("SMTP test listener: {e}"),
                }
            }
        });
        Self {
            port,
            body,
            handle: Some(handle),
            stop,
        }
    }
}
impl Drop for Smtp {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Release);
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}
fn session(mut stream: TcpStream, acknowledge: bool) -> Option<String> {
    // The listener is non-blocking for the accept loop; the session itself is
    // blocking with a read timeout, otherwise reads fail with WSAEWOULDBLOCK.
    stream.set_nonblocking(false).ok()?;
    stream
        .set_read_timeout(Some(Duration::from_secs(5)))
        .unwrap();
    stream.write_all(b"220 localhost ESMTP test\r\n").ok()?;
    let mut reader = BufReader::new(stream.try_clone().ok()?);
    let mut data = false;
    let mut message = String::new();
    loop {
        let mut line = String::new();
        if reader.read_line(&mut line).ok()? == 0 {
            return Some(message);
        }
        if data {
            if line == ".\r\n" {
                if !acknowledge {
                    return Some(message);
                }
                stream.write_all(b"250 2.0.0 queued\r\n").ok()?;
                data = false;
            } else {
                message.push_str(&line);
            }
            continue;
        }
        let command = line.split_whitespace().next().unwrap_or("");
        let response = match command {
            "EHLO" => "250-localhost\r\n250-AUTH PLAIN\r\n250 8BITMIME\r\n",
            "HELO" | "MAIL" | "RCPT" | "RSET" => "250 OK\r\n",
            "AUTH" => "235 authenticated\r\n",
            "DATA" => {
                data = true;
                "354 end with dot\r\n"
            }
            "QUIT" => {
                stream.write_all(b"221 Bye\r\n").ok()?;
                return Some(message);
            }
            _ => "250 OK\r\n",
        };
        stream.write_all(response.as_bytes()).ok()?;
    }
}
