use super::*;
use std::io::{Seek, SeekFrom};

const MIB: u64 = 1024 * 1024;
const MAX_PARTS: u64 = 10_000;
const MAX_PART_SIZE: u64 = 5 * 1024 * MIB;
fn part_size(size: u64) -> Result<u64> {
    let needed = size
        .div_ceil(MAX_PARTS)
        .div_ceil(MIB)
        .checked_mul(MIB)
        .ok_or_else(|| invalid("S3 对象大小超出分段协议范围。"))?
        .max(64 * MIB);
    if needed > MAX_PART_SIZE {
        return Err(invalid(
            "备份超出 S3 单对象分段范围，请使用其他存储或由管理员分卷搬运。",
        ));
    }
    Ok(needed)
}

impl Target {
    pub(super) fn put_file(
        &self,
        file: &mut (impl Read + Seek),
        name: &str,
        size: u64,
        digest: &str,
    ) -> Result<()> {
        if size == 0 || file.seek(SeekFrom::End(0))? != size {
            return Err(invalid("上传文件为空或大小已变化。"));
        }
        if size > 8 * MIB {
            return self.multipart_file(file, name, size);
        }
        file.seek(SeekFrom::Start(0))?;
        let mut body = Read::by_ref(file).take(size);
        let request = self.request(
            "PUT",
            &self.url(name)?,
            ureq::SendBody::from_reader(&mut body),
            Some(size),
            digest,
        )?;
        let response = self
            .agent
            .run(request)
            .map_err(|_| unavailable("S3 上传失败或超时。"))?;
        response_text(response).map(|_| ())
    }
    pub(super) fn multipart_file(
        &self,
        file: &mut (impl Read + Seek),
        name: &str,
        size: u64,
    ) -> Result<()> {
        let part_size = part_size(size)?;
        if size == 0 || file.seek(SeekFrom::End(0))? != size {
            return Err(invalid("本地灾备包为空或大小已变化。"));
        }
        let object = self.url(name)?;
        let mut initiate = object.clone();
        initiate.query_pairs_mut().append_pair("uploads", "");
        let response = self.control(&self.agent, "POST", &initiate, &[])?;
        let id = xml_value(&response, "UploadId")?;
        if id.is_empty() || id.len() > 4096 {
            return Err(unavailable("S3 分段上传编号无效。"));
        }
        let mut session = object.clone();
        session.query_pairs_mut().append_pair("uploadId", &id);
        let journal = match self.remember_upload(name, &id) {
            Ok(path) => path,
            Err(cause) => {
                self.abort_upload(&session)?;
                return Err(cause);
            }
        };
        let result = (|| {
            let mut parts = Vec::new();
            let mut offset = 0;
            while offset < size {
                crate::operation::check()?;
                let length = part_size.min(size - offset);
                file.seek(SeekFrom::Start(offset))?;
                let (actual, hash) = backup_io::copy_and_hash(
                    &mut Read::by_ref(file).take(length),
                    &mut std::io::sink(),
                    length,
                )?;
                if actual != length {
                    return Err(unavailable("本地灾备包在上传时发生变化。"));
                }
                let mut url = session.clone();
                url.query_pairs_mut()
                    .append_pair("partNumber", &(parts.len() + 1).to_string());
                let mut etag = None;
                for attempt in 0..3 {
                    crate::operation::check()?;
                    file.seek(SeekFrom::Start(offset))?;
                    let mut part = Read::by_ref(file).take(length);
                    let request = self.request(
                        "PUT",
                        &url,
                        ureq::SendBody::from_reader(&mut part),
                        Some(length),
                        &hash,
                    )?;
                    match self.agent.run(request) {
                        Ok(response) if response.status().is_success() => {
                            let value = response
                                .headers()
                                .get("etag")
                                .and_then(|value| value.to_str().ok())
                                .map(str::to_owned);
                            response_text(response)?;
                            etag = value;
                            break;
                        }
                        Ok(response)
                            if attempt < 2
                                && (response.status().is_server_error()
                                    || response.status().as_u16() == 429) => {}
                        Ok(response) => {
                            response_text(response)?;
                        }
                        Err(_) if attempt < 2 => {}
                        Err(_) => return Err(unavailable("S3 分段上传失败或超时。")),
                    }
                    std::thread::sleep(Duration::from_millis(250 * (attempt + 1)));
                }
                let etag = etag
                    .filter(|value| !value.is_empty() && value.len() <= 1024)
                    .ok_or_else(|| unavailable("S3 上传未返回有效 ETag，不能完成备份。"))?;
                parts.push(etag);
                offset += length;
            }
            let mut xml = String::from("<CompleteMultipartUpload>");
            for (index, etag) in parts.iter().enumerate() {
                xml.push_str(&format!(
                    "<Part><PartNumber>{}</PartNumber><ETag>{}</ETag></Part>",
                    index + 1,
                    quick_xml::escape::escape(etag)
                ));
            }
            xml.push_str("</CompleteMultipartUpload>");
            let response = self.control(&self.agent, "POST", &session, xml.as_bytes())?;
            if xml_root(&response)? != "CompleteMultipartUploadResult" {
                return Err(unavailable("S3 未确认分段上传完成。"));
            }
            Ok(())
        })();
        if result.is_err() {
            let cleanup = self.abort_upload(&session);
            if cleanup.is_err() {
                return Err(unavailable(
                    "S3 分段上传失败且未能中止；本地备份已保留，请检查存储连接及未完成分段。",
                ));
            }
        }
        if let Some(path) = journal {
            std::fs::remove_file(path)?;
        }
        result
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn part_count_scales_without_allocating_the_part() {
        for size in [
            1,
            64 * 1024 * MIB,
            6 * 1024 * 1024 * MIB,
            MAX_PARTS * MAX_PART_SIZE,
        ] {
            let part = part_size(size).unwrap();
            assert!(part >= 5 * MIB && part <= MAX_PART_SIZE);
            assert!(size.div_ceil(part) <= MAX_PARTS);
        }
        assert!(part_size(u64::MAX).is_err());
    }
    #[test]
    fn complete_response_may_report_an_error_with_http_200() {
        let response = Response::builder()
            .status(200)
            .body(ureq::Body::builder().data("<Error><Code>InvalidPart</Code></Error>".as_bytes()))
            .unwrap();
        assert!(response_text(response).is_err());
        assert_eq!(
            xml_value(
                "<Response><UploadId>a&amp;b</UploadId></Response>",
                "UploadId"
            )
            .unwrap(),
            "a&b"
        );
    }
}
