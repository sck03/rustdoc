//! Bounded repository objects. Random data keys are wrapped by the independent
//! repository key; neither encryption keys nor nonces derive from file contents.
use super::*;
pub(in crate::engine::team_backup) const MAX_PLAIN: usize = 4 * 1024 * 1024;
const MAGIC: &[u8] = b"EDM-REPOSITORY-OBJECT-1\0";
pub(in crate::engine::team_backup) const OVERHEAD: usize = MAGIC.len() + 12 + 48 + 12 + 16;

fn encrypt(key: &[u8; 32], nonce: &[u8; 12], aad: &[u8], bytes: &mut Vec<u8>) -> Result<()> {
    let cipher = Aes256Gcm::new_from_slice(key).map_err(|_| invalid("仓库密钥长度无效。"))?;
    let tag = cipher
        .encrypt_inout_detached(&Nonce::from(*nonce), aad, bytes.as_mut_slice().into())
        .map_err(|_| unavailable("恢复点对象加密失败。"))?;
    bytes.extend_from_slice(&tag);
    Ok(())
}
fn decrypt(
    key: &[u8; 32],
    nonce: &[u8; 12],
    aad: &[u8],
    bytes: &[u8],
) -> Result<zeroize::Zeroizing<Vec<u8>>> {
    if bytes.len() < 16 {
        return Err(invalid("恢复点对象已截断。"));
    }
    let split = bytes.len() - 16;
    let tag: [u8; 16] = bytes[split..].try_into().unwrap();
    let mut output = zeroize::Zeroizing::new(bytes[..split].to_vec());
    Aes256Gcm::new_from_slice(key)
        .map_err(|_| invalid("仓库密钥长度无效。"))?
        .decrypt_inout_detached(
            &Nonce::from(*nonce),
            aad,
            output.as_mut_slice().into(),
            &Tag::from(tag),
        )
        .map_err(|_| {
            invalid("恢复点密钥不匹配或对象内容被修改，请核对仓库凭证并执行完整性检查。")
        })?;
    Ok(output)
}
pub(in crate::engine::team_backup) fn seal(
    root: &[u8; 32],
    binding: &[u8],
    bytes: &[u8],
) -> Result<Vec<u8>> {
    if bytes.len() > MAX_PLAIN {
        return Err(invalid("恢复点对象超过传输窗口。"));
    }
    let mut key = zeroize::Zeroizing::new([0u8; 32]);
    let mut nonces = [0u8; 24];
    getrandom::fill(key.as_mut()).map_err(|_| unavailable("无法生成恢复点对象密钥。"))?;
    getrandom::fill(&mut nonces).map_err(|_| unavailable("无法生成恢复点对象随机数。"))?;
    let mut aad = MAGIC.to_vec();
    aad.extend_from_slice(binding);
    let mut wrapped = key.to_vec();
    encrypt(root, nonces[..12].try_into().unwrap(), &aad, &mut wrapped)?;
    aad.extend_from_slice(&wrapped);
    let mut payload = bytes.to_vec();
    encrypt(&key, nonces[12..].try_into().unwrap(), &aad, &mut payload)?;
    let mut output = MAGIC.to_vec();
    output.extend_from_slice(&nonces[..12]);
    output.extend_from_slice(&wrapped);
    output.extend_from_slice(&nonces[12..]);
    output.extend_from_slice(&payload);
    Ok(output)
}
pub(in crate::engine::team_backup) fn open(
    root: &[u8; 32],
    binding: &[u8],
    bytes: &[u8],
) -> Result<zeroize::Zeroizing<Vec<u8>>> {
    let start = MAGIC.len();
    if bytes.len() < start + 88 || bytes.len() > MAX_PLAIN + start + 88 || !bytes.starts_with(MAGIC)
    {
        return Err(invalid("恢复点对象格式、长度或结束认证无效。"));
    }
    let mut aad = MAGIC.to_vec();
    aad.extend_from_slice(binding);
    let wrapped = &bytes[start + 12..start + 60];
    let key = decrypt(
        root,
        bytes[start..start + 12].try_into().unwrap(),
        &aad,
        wrapped,
    )?;
    let key: &[u8; 32] = key
        .as_slice()
        .try_into()
        .map_err(|_| invalid("恢复点对象密钥格式无效。"))?;
    aad.extend_from_slice(wrapped);
    decrypt(
        key,
        bytes[start + 60..start + 72].try_into().unwrap(),
        &aad,
        &bytes[start + 72..],
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn object_authentication_binds_repository_name_key_and_complete_content() {
        let root = [7; 32];
        let value = b"repository secret";
        let blob = seal(&root, b"repository/object", value).unwrap();
        assert_eq!(&**open(&root, b"repository/object", &blob).unwrap(), value);
        assert_ne!(blob, seal(&root, b"repository/object", value).unwrap());
        assert!(open(&[8; 32], b"repository/object", &blob).is_err());
        assert!(open(&root, b"other/object", &blob).is_err());
        for position in [0, 24, 48, blob.len() - 1] {
            let mut bad = blob.clone();
            bad[position] ^= 1;
            assert!(open(&root, b"repository/object", &bad).is_err());
        }
        assert!(open(&root, b"repository/object", &blob[..blob.len() - 1]).is_err());
    }
}
