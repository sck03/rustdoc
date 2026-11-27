//! Version 2: authenticated 1 MiB records and a mandatory authenticated end record.
//! Every record binds the complete header, length and monotonic nonce counter.
use super::*;
const CHUNK: usize = 1024 * 1024;
const PREFIX: usize = 8;
const TAG: usize = 16;
const NONCE_LENGTH: usize = 12;

fn cipher(password: &str, salt: &[u8]) -> Result<Aes256Gcm> {
    let key = zeroize::Zeroizing::new(pbkdf2_hmac_array::<Sha256, 32>(
        password.as_bytes(),
        salt,
        PBKDF2_ITERATIONS,
    ));
    Aes256Gcm::new_from_slice(key.as_ref()).map_err(|_| {
        unavailable("包密钥长度无效。").coded(crate::api::ErrorCode::DependencyUnavailable)
    })
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
#[cfg(test)]
pub(in crate::engine::team_backup) fn seal(
    magic: &[u8],
    password: &str,
    mut input: impl Read,
    output: &mut impl Write,
) -> Result<()> {
    let mut writer = SealingWriter::new(magic, password, output)?;
    crate::engine::backup_io::copy_chunks(
        &mut input,
        &mut writer,
        crate::engine::backup_io::MAX_FILE_BYTES,
        |_| {},
    )?;
    writer.finish()?;
    Ok(())
}

pub(in crate::engine::team_backup) struct SealingWriter<W: Write> {
    output: W,
    cipher: Aes256Gcm,
    header: Vec<u8>,
    prefix: [u8; PREFIX],
    index: u64,
    buffer: zeroize::Zeroizing<Vec<u8>>,
}
impl<W: Write> SealingWriter<W> {
    pub(in crate::engine::team_backup) fn new(
        magic: &[u8],
        password: &str,
        mut output: W,
    ) -> Result<Self> {
        let mut header = magic.to_vec();
        let mut random = [0; SALT_LENGTH + PREFIX];
        getrandom::fill(&mut random).map_err(|_| {
            unavailable("无法生成包随机数。").coded(crate::api::ErrorCode::DependencyUnavailable)
        })?;
        header.extend_from_slice(&random);
        let cipher = cipher(password, &random[..SALT_LENGTH])?;
        output.write_all(&header)?;
        let prefix = random[SALT_LENGTH..].try_into().unwrap();
        Ok(Self {
            output,
            cipher,
            header,
            prefix,
            index: 0,
            buffer: zeroize::Zeroizing::new(Vec::with_capacity(CHUNK)),
        })
    }
    fn record(&mut self) -> Result<()> {
        crate::operation::check()?;
        let index = u32::try_from(self.index).map_err(|_| invalid("恢复包分块数量超过上限。"))?;
        let mut aad = self.header.clone();
        aad.extend_from_slice(&[0; 4]);
        let size = self.buffer.len();
        let length = (size as u32).to_le_bytes();
        aad[self.header.len()..].copy_from_slice(&length);
        let tag = self
            .cipher
            .encrypt_inout_detached(
                &Nonce::from(nonce(&self.prefix, index)),
                &aad,
                self.buffer.as_mut_slice().into(),
            )
            .map_err(|_| {
                unavailable("迁移包分块加密失败。")
                    .coded(crate::api::ErrorCode::DependencyUnavailable)
            })?;
        self.output.write_all(&length)?;
        self.output.write_all(&self.buffer)?;
        self.output.write_all(&tag)?;
        self.buffer.clear();
        self.index += 1;
        Ok(())
    }
    pub(in crate::engine::team_backup) fn finish(mut self) -> Result<W> {
        if !self.buffer.is_empty() {
            self.record()?;
        }
        self.record()?;
        self.output.flush()?;
        Ok(self.output)
    }
}
impl<W: Write> Write for SealingWriter<W> {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        let size = bytes.len().min(CHUNK - self.buffer.len());
        self.buffer.extend_from_slice(&bytes[..size]);
        if self.buffer.len() == CHUNK {
            self.record().map_err(std::io::Error::other)?;
        }
        Ok(size)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        // A short encrypted frame may only be followed by the authenticated end.
        self.output.flush()
    }
}
pub(in crate::engine::team_backup) fn open(
    magic: &[u8],
    password: &str,
    mut input: impl Read,
    output: &mut impl Write,
) -> Result<()> {
    let mut prefix = vec![0; magic.len()];
    exact(&mut input, &mut prefix)?;
    if prefix != magic {
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
        consumed = consumed
            .checked_add((4 + size + TAG) as u64)
            .ok_or_else(|| invalid("迁移包大小计数溢出。"))?;
        if size > CHUNK || (partial && size != 0) {
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

    #[test]
    fn records_bind_order_length_type_password_and_authenticated_end() {
        let payload = vec![42; CHUNK * 2 + 7];
        let mut encrypted = Vec::new();
        seal(
            b"EDM-TEST-2",
            "correct-password",
            payload.as_slice(),
            &mut encrypted,
        )
        .unwrap();
        let decode = |bytes: &[u8], password| {
            let mut output = Vec::new();
            open(b"EDM-TEST-2", password, bytes, &mut output).map(|_| output)
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
        let header = b"EDM-TEST-2".len() + SALT_LENGTH + PREFIX;
        let record = CHUNK + 4 + TAG;
        let mut reordered = encrypted.clone();
        reordered[header..header + record]
            .copy_from_slice(&encrypted[header + record..header + record * 2]);
        assert!(decode(&reordered, "correct-password").is_err());
        encrypted.push(0);
        assert!(decode(&encrypted, "correct-password").is_err());
    }
}
