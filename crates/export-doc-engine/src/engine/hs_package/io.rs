//! Incremental JSON processing; no expanded package or parsed collection is retained.
use super::*;
use serde::de::{DeserializeSeed, SeqAccess, Visitor};
use sha2::{Digest, Sha256};

pub(super) struct EntryWriter<'a, W: Write> {
    pub output: &'a mut W,
    pub remaining: u64,
    pub digest: Sha256,
}
impl<W: Write> Write for EntryWriter<'_, W> {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        if bytes.len() as u64 > self.remaining {
            return Err(std::io::Error::other(
                "知识包导出超过容量上限，请使用增量导出。",
            ));
        }
        self.output.write_all(bytes)?;
        self.digest.update(bytes);
        self.remaining -= bytes.len() as u64;
        Ok(bytes.len())
    }
    fn flush(&mut self) -> std::io::Result<()> {
        self.output.flush()
    }
}

pub(super) fn open(bytes: &[u8]) -> Result<(ZipArchive<Cursor<&[u8]>>, Value)> {
    if bytes.is_empty() || bytes.len() as u64 > MAX_PACKAGE {
        return Err(invalid("知识包为空或超过 100 MiB。"));
    }
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|e| invalid(format!("知识包无效：{e}")))?;
    if archive.len() != 5 {
        return Err(invalid("知识包须包含清单和四类知识文件。"));
    }
    let mut names = BTreeSet::new();
    let mut total = 0_u64;
    for index in 0..archive.len() {
        operation::check()?;
        let entry = archive
            .by_index(index)
            .map_err(|e| invalid(e.to_string()))?;
        let name = entry
            .name()
            .map_err(|e| invalid(e.to_string()))?
            .into_owned();
        if !(name == "manifest.json" || CONTENTS.iter().any(|(key, _)| *key == name))
            || !names.insert(name.clone())
            || entry.is_dir()
            || entry
                .unix_mode()
                .is_some_and(|mode| mode & 0o170000 == 0o120000)
        {
            return Err(invalid("知识包包含不允许、重复或链接文件。"));
        }
        let maximum = if name == "manifest.json" {
            256 * 1024
        } else {
            MAX_PACKAGE
        };
        total = total
            .checked_add(entry.size())
            .ok_or_else(|| invalid("知识包容量无效。"))?;
        if entry.size() > maximum || total > MAX_EXPANDED {
            return Err(invalid("知识包解压容量超过上限。"));
        }
    }
    let manifest: Value = {
        let mut entry = archive
            .by_name("manifest.json")
            .map_err(|e| invalid(e.to_string()))?;
        serde_json::from_slice(&super::super::archive::read_entry(&mut entry, 256 * 1024)?)
            .map_err(|e| invalid(e.to_string()))?
    };
    if manifest["schemaVersion"] != "1.0" {
        return Err(invalid("知识包版本无效。"));
    }
    chrono::DateTime::parse_from_rfc3339(manifest["exportedAt"].as_str().unwrap_or(""))
        .map_err(|_| invalid("知识包导出时间无效。"))?;
    Ok((archive, manifest))
}

pub(super) fn visit(
    archive: &mut ZipArchive<Cursor<&[u8]>>,
    manifest: &Value,
    kind: &str,
    mut accept: impl FnMut(Value) -> Result<()>,
) -> Result<usize> {
    let name = CONTENTS
        .iter()
        .find(|(_, k)| *k == kind)
        .ok_or_else(|| invalid("知识文件类型无效。"))?
        .0;
    let mut entry = archive.by_name(name).map_err(|e| invalid(e.to_string()))?;
    let bytes = super::super::archive::read_entry(&mut entry, MAX_PACKAGE)?;
    if !manifest["checksums"][name]
        .as_str()
        .is_some_and(|d| d.eq_ignore_ascii_case(&media::digest(&bytes)))
    {
        return Err(invalid("知识包校验和不匹配。"));
    }
    struct Rows<'a, F> {
        accept: &'a mut F,
        failure: &'a mut Option<crate::api::ApiError>,
        limit: usize,
    }
    impl<'de, F: FnMut(Value) -> Result<()>> Visitor<'de> for Rows<'_, F> {
        type Value = usize;
        fn expecting(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
            f.write_str("知识记录数组")
        }
        fn visit_seq<A: SeqAccess<'de>>(self, mut seq: A) -> std::result::Result<usize, A::Error> {
            use serde::de::Error;
            let mut count = 0;
            while count < self.limit {
                if let Err(error) = operation::check() {
                    *self.failure = Some(error);
                    return Err(A::Error::custom("操作已取消"));
                }
                let Some(row) = seq.next_element::<Value>()? else {
                    return Ok(count);
                };
                if let Err(error) = (self.accept)(row) {
                    *self.failure = Some(error);
                    return Err(A::Error::custom("知识记录处理失败"));
                }
                count += 1;
            }
            if seq.next_element::<serde::de::IgnoredAny>()?.is_some() {
                return Err(A::Error::custom("知识包记录数量超过上限"));
            }
            Ok(count)
        }
    }
    impl<'de, F: FnMut(Value) -> Result<()>> DeserializeSeed<'de> for Rows<'_, F> {
        type Value = usize;
        fn deserialize<D: serde::Deserializer<'de>>(
            self,
            d: D,
        ) -> std::result::Result<usize, D::Error> {
            d.deserialize_seq(self)
        }
    }
    let mut failure = None;
    let mut deserializer = serde_json::Deserializer::from_slice(&bytes);
    let parsed = Rows {
        accept: &mut accept,
        failure: &mut failure,
        limit: if kind == hs::CODES {
            500_000
        } else {
            1_000_000
        },
    }
    .deserialize(&mut deserializer);
    if let Some(error) = failure {
        return Err(error);
    }
    let count = parsed.map_err(|e| invalid(format!("知识记录无效：{e}")))?;
    deserializer
        .end()
        .map_err(|e| invalid(format!("知识记录无效：{e}")))?;
    Ok(count)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn entry_budget_rejects_before_writing_or_hashing_extra_bytes() {
        let mut output = Vec::new();
        let mut writer = EntryWriter {
            output: &mut output,
            remaining: 3,
            digest: Sha256::new(),
        };
        writer.write_all(b"abc").unwrap();
        assert!(writer.write_all(b"d").is_err());
        assert_eq!(writer.remaining, 0);
        assert_eq!(
            writer.digest.finalize().as_slice(),
            Sha256::digest(b"abc").as_slice()
        );
        assert_eq!(output, b"abc");
    }
}
