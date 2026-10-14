//! Version 2: authenticated 1 MiB records and a mandatory authenticated end record.
//! Every record binds the complete header, length and monotonic nonce counter.
use super::*;
const CHUNK: usize = 1024 * 1024;
const PREFIX: usize = 8;
const TAG: usize = 16;

fn version_two(magic: &[u8]) -> Result<Vec<u8>> {
    if magic.last() != Some(&b'1') || magic.len() > 64 {
        return Err(invalid("包类型标记无效。"));
    }
    let mut magic = magic.to_vec();
    *magic.last_mut().unwrap() = b'2';
    Ok(magic)
}
fn cipher(password: &str, salt: &[u8]) -> Result<Aes256Gcm> {
    let key = zeroize::Zeroizing::new(pbkdf2_hmac_array::<Sha256, 32>(
        password.as_bytes(),
        salt,
        PBKDF2_ITERATIONS,
    ));
    Aes256Gcm::new_from_slice(key.as_ref()).map_err(|_| unavailable("包密钥长度无效。"))
}
fn nonce(prefix: &[u8], index: u32) -> [u8; NONCE_LENGTH] {
    let mut value = [0; NONCE_LENGTH];
    value[..PREFIX].copy_from_slice(prefix);
    value[PREFIX..].copy_from_slice(&index.to_be_bytes());
    value
}
fn exact(input: &mut impl Read, bytes: &mut [u8]) -> Result<()> {
    input.read_exact(bytes).map_err(|cause| {
        if cause.kind() == std::io::ErrorKind::UnexpectedEof {
            invalid("迁移包被截断或缺少完整结束标记。")
        } else {
            cause.into()
        }
    })
}
pub(in crate::engine::team_backup) fn seal(
    magic: &[u8],
    password: &str,
    mut input: impl Read,
    output: &mut impl Write,
) -> Result<()> {
    let mut header = version_two(magic)?;
    let mut random = [0; SALT_LENGTH + PREFIX];
    getrandom::fill(&mut random).map_err(|_| unavailable("无法生成包随机数。"))?;
    header.extend_from_slice(&random);
    let cipher = cipher(password, &random[..SALT_LENGTH])?;
    output.write_all(&header)?;
    let mut aad = header.clone();
    aad.extend_from_slice(&[0; 4]);
    let mut written = header.len() as u64;
    let mut buffer = zeroize::Zeroizing::new(Vec::with_capacity(CHUNK));
    for index in 0..=u32::MAX {
        crate::operation::check()?;
        buffer.clear();
        input.by_ref().take(CHUNK as u64).read_to_end(&mut buffer)?;
        let size = buffer.len();
        written += (4 + size + TAG) as u64;
        if written > MAX_PACKAGE_BYTES {
            return Err(invalid("迁移包文件超过可表示的文件大小。"));
        }
        let length = (size as u32).to_le_bytes();
        aad[header.len()..].copy_from_slice(&length);
        let tag = cipher
            .encrypt_inout_detached(
                &Nonce::from(nonce(&random[SALT_LENGTH..], index)),
                &aad,
                buffer.as_mut_slice().into(),
            )
            .map_err(|_| unavailable("迁移包分块加密失败。"))?;
        output.write_all(&length)?;
        output.write_all(&buffer)?;
        output.write_all(&tag)?;
        if size == 0 {
            output.flush()?;
            return Ok(());
        }
    }
    Err(invalid("迁移包块数超过上限。"))
}
pub(in crate::engine::team_backup) fn open(
    magic: &[u8],
    password: &str,
    mut input: impl Read,
    output: &mut impl Write,
) -> Result<()> {
    let mut prefix = vec![0; magic.len()];
    exact(&mut input, &mut prefix)?;
    if prefix == magic {
        let payload = super::open(magic, password, std::io::Cursor::new(prefix).chain(input))?;
        output.write_all(&payload)?;
        return Ok(());
    }
    if prefix != version_two(magic)? {
        return Err(invalid("包类型或格式不匹配。"));
    }
    let mut random = [0; SALT_LENGTH + PREFIX];
    exact(&mut input, &mut random)?;
    let cipher = cipher(password, &random[..SALT_LENGTH])?;
    prefix.extend_from_slice(&random);
    let mut aad = prefix.clone();
    aad.extend_from_slice(&[0; 4]);
    let mut consumed = prefix.len() as u64;
    let mut partial = false;
    let mut buffer = zeroize::Zeroizing::new(Vec::with_capacity(CHUNK));
    for index in 0..=u32::MAX {
        crate::operation::check()?;
        let mut length = [0; 4];
        exact(&mut input, &mut length)?;
        let size = u32::from_le_bytes(length) as usize;
        consumed += (4 + size + TAG) as u64;
        if size > CHUNK || consumed > MAX_PACKAGE_BYTES || (partial && size != 0) {
            return Err(invalid("迁移包块长度或容量无效。"));
        }
        buffer.resize(size, 0);
        exact(&mut input, &mut buffer)?;
        let mut tag = [0; TAG];
        exact(&mut input, &mut tag)?;
        aad[prefix.len()..].copy_from_slice(&length);
        cipher
            .decrypt_inout_detached(
                &Nonce::from(nonce(&random[SALT_LENGTH..], index)),
                &aad,
                buffer.as_mut_slice().into(),
                &Tag::from(tag),
            )
            .map_err(|_| invalid("包密码错误或内容已损坏。"))?;
        if size == 0 {
            let mut extra = [0; 1];
            if input.read(&mut extra)? != 0 {
                return Err(invalid("迁移包结束标记之后含多余内容。"));
            }
            output.flush()?;
            return Ok(());
        }
        output.write_all(&buffer)?;
        partial = size < CHUNK;
    }
    Err(invalid("迁移包块数超过上限。"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use sha2::Digest;

    #[test]
    #[ignore = "explicit large-file disk and streaming verification"]
    fn large_backup_stream_and_zip64_exceed_the_old_limits() {
        let workspace = crate::engine::team_backup::tests::Workspace::new();
        let source = workspace.0.join("large.bin");
        let size = 1024 * 1024 * 1024 + 17;
        fs::File::create(&source).unwrap().set_len(size).unwrap();
        let expected =
            super::super::ManifestFile::from_path("Templates/large.bin", &source).unwrap();
        let encrypted = workspace.0.join("large.sealed");
        seal(
            b"EDM-TEST-1",
            "large-file-test-password",
            fs::File::open(&source).unwrap(),
            &mut fs::File::create(&encrypted).unwrap(),
        )
        .unwrap();
        struct Sink {
            bytes: u64,
            hash: Sha256,
        }
        impl Write for Sink {
            fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
                self.bytes += bytes.len() as u64;
                self.hash.update(bytes);
                Ok(bytes.len())
            }
            fn flush(&mut self) -> std::io::Result<()> {
                Ok(())
            }
        }
        let mut output = Sink {
            bytes: 0,
            hash: Sha256::new(),
        };
        open(
            b"EDM-TEST-1",
            "large-file-test-password",
            fs::File::open(&encrypted).unwrap(),
            &mut output,
        )
        .unwrap();
        assert_eq!(output.bytes, size);
        assert_eq!(
            crate::engine::team_backup::hex(&output.hash.finalize()),
            expected.sha256
        );
        let archive = workspace.0.join("large.zip");
        super::super::zip_payload(
            &[super::super::Entry {
                source: &source,
                name: "Templates/large.bin",
            }],
            fs::File::create(&archive).unwrap(),
        )
        .unwrap();
        let entries = super::super::unpack(
            fs::File::open(archive).unwrap(),
            &workspace.0.join("unpacked"),
        )
        .unwrap();
        assert_eq!(entries[0].size_bytes, size);
        assert_eq!(entries[0].sha256, expected.sha256);
    }
    #[test]
    fn records_bind_order_length_type_password_and_authenticated_end() {
        let payload = vec![42; CHUNK * 2 + 7];
        let mut encrypted = Vec::new();
        seal(
            b"EDM-TEST-1",
            "correct-password",
            payload.as_slice(),
            &mut encrypted,
        )
        .unwrap();
        let decode = |bytes: &[u8], password| {
            let mut output = Vec::new();
            open(b"EDM-TEST-1", password, bytes, &mut output).map(|_| output)
        };
        assert_eq!(decode(&encrypted, "correct-password").unwrap(), payload);
        assert!(decode(&encrypted, "wrong-password").is_err());
        for index in [0, 15, encrypted.len() / 2, encrypted.len() - 1] {
            let mut broken = encrypted.clone();
            broken[index] ^= 1;
            assert!(decode(&broken, "correct-password").is_err());
        }
        for length in [1, encrypted.len() - 1, encrypted.len() - 20] {
            assert!(decode(&encrypted[..length], "correct-password").is_err());
        }
        let header = b"EDM-TEST-1".len() + SALT_LENGTH + PREFIX;
        let record = CHUNK + 4 + TAG;
        let mut reordered = encrypted.clone();
        reordered[header..header + record]
            .copy_from_slice(&encrypted[header + record..header + record * 2]);
        assert!(decode(&reordered, "correct-password").is_err());
        encrypted.push(0);
        assert!(decode(&encrypted, "correct-password").is_err());
    }
}
