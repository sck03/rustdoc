//! Conditional, bounded object access used by recovery repositories.
use super::*;
impl Target {
    pub(in crate::engine) fn read_small(
        &self,
        name: &str,
        limit: usize,
    ) -> Result<Option<Vec<u8>>> {
        let request = self.request("GET", &self.url(name)?, (), None, EMPTY_HASH)?;
        let mut response = match self.agent.run(request) {
            Ok(response) => response,
            Err(ureq::Error::StatusCode(404)) => return Ok(None),
            Err(_) => return Err(unavailable("S3 对象读取失败，请检查网络、凭据和读取权限。")),
        };
        if response.status().as_u16() == 404 {
            return Ok(None);
        }
        if response.status().as_u16() != 200 {
            return Err(unavailable(format!(
                "S3 对象读取被拒绝（HTTP {}）。",
                response.status().as_u16()
            )));
        }
        let mut bytes = Vec::new();
        response
            .body_mut()
            .as_reader()
            .take(limit as u64 + 1)
            .read_to_end(&mut bytes)?;
        if bytes.len() > limit {
            return Err(invalid("S3 恢复仓库对象超过长度上限。"));
        }
        Ok(Some(bytes))
    }
    pub(in crate::engine) fn create_small(&self, name: &str, bytes: &[u8]) -> Result<bool> {
        let request = self.request_headers(
            "PUT",
            &self.url(name)?,
            bytes,
            Some(bytes.len() as u64),
            &hex(&Sha256::digest(bytes)),
            &[("if-none-match", "*")],
        )?;
        let response = match self.agent.run(request) {
            Ok(response) => response,
            Err(ureq::Error::StatusCode(412)) => return Ok(false),
            Err(_) => {
                return Err(unavailable(
                    "S3 条件写入失败，结果待核验；请重新读取仓库状态。",
                ));
            }
        };
        match response.status().as_u16() {
            200 | 201 | 204 => Ok(true),
            412 => Ok(false),
            status => Err(unavailable(format!(
                "S3 不支持或拒绝仓库条件写入（HTTP {status}），请检查兼容性与权限。"
            ))),
        }
    }
    pub(in crate::engine) fn list_names(
        &self,
        prefix: &str,
        after: &str,
        limit: usize,
    ) -> Result<Vec<String>> {
        let base = format!(
            "{}{}/",
            if self.settings.prefix.is_empty() {
                String::new()
            } else {
                format!("{}/", self.settings.prefix.trim_matches('/'))
            },
            self.repository
        );
        let mut url = self.url("")?;
        {
            let mut path = url
                .path_segments_mut()
                .map_err(|_| invalid("S3 桶地址无效。"))?;
            path.pop();
            for _ in self.settings.prefix.split('/').filter(|p| !p.is_empty()) {
                path.pop();
            }
        }
        url.query_pairs_mut()
            .append_pair("list-type", "2")
            .append_pair("prefix", &format!("{base}{prefix}"))
            .append_pair("start-after", &format!("{base}{after}"))
            .append_pair("max-keys", &limit.min(100).to_string());
        let request = self.request("GET", &url, (), None, EMPTY_HASH)?;
        let response = self
            .agent
            .run(request)
            .map_err(|_| unavailable("无法列出 S3 恢复点，请检查桶枚举权限和连接。"))?;
        let xml = response_text(response)?;
        let mut reader = quick_xml::Reader::from_str(&xml);
        let mut names = Vec::new();
        loop {
            match reader
                .read_event()
                .map_err(|_| invalid("S3 对象列表格式无效。"))?
            {
                quick_xml::events::Event::Start(tag) if tag.name().as_ref() == "Key" => {
                    let key = reader
                        .read_text(tag.name())
                        .map_err(|_| invalid("S3 对象名称编码无效。"))?;
                    let key = quick_xml::escape::unescape(&key)
                        .map_err(|_| invalid("S3 对象名称编码无效。"))?;
                    let name = key
                        .strip_prefix(&base)
                        .ok_or_else(|| invalid("S3 返回了仓库范围外的对象。"))?;
                    if name.contains('/')
                        || !crate::paths::valid_file_name(name)
                        || !name.starts_with(prefix)
                        || name <= after
                    {
                        return Err(invalid("S3 对象分页或仓库范围无效。"));
                    }
                    names.push(name.to_owned());
                    if names.len() > limit {
                        return Err(invalid("S3 对象列表超过请求上限。"));
                    }
                }
                quick_xml::events::Event::Eof => break,
                _ => {}
            }
        }
        names.sort();
        names.dedup();
        Ok(names)
    }
}
