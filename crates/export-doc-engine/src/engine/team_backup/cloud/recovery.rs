//! Conditional immutable WebDAV objects for the shared recovery repository.
use super::*;

pub(in crate::engine::team_backup) fn validate_recovery_settings(value: &Value) -> Result<()> {
    if value["enabled"] != true || value["userName"].as_str().is_none_or(str::is_empty) {
        return Err(invalid("请启用并填写 WebDAV 地址和用户名。"));
    }
    if value["userName"]
        .as_str()
        .is_some_and(|user| user.contains(':'))
    {
        return Err(invalid("WebDAV 用户名不能包含冒号。"));
    }
    parse_url(value["url"].as_str().unwrap_or(""))?;
    Ok(())
}

pub(in crate::engine::team_backup) struct RecoveryTarget {
    config: Config,
    endpoint: Endpoint,
}
impl RecoveryTarget {
    fn reader_endpoint(&self) -> Result<Endpoint> {
        parse_url(&self.endpoint.request_url("readers"))
    }
    fn object_url(&self, name: &str) -> Result<String> {
        if name.starts_with("reader-") {
            remote_path(&self.reader_endpoint()?, name)
        } else {
            remote_path(&self.object_endpoint(name)?, name)
        }
    }
    fn object_endpoint(&self, name: &str) -> Result<Endpoint> {
        let directory = super::super::repository::names::Namespace::of(name).directory();
        if directory.is_empty() {
            parse_url(&self.endpoint.propfind_url())
        } else {
            parse_url(&self.endpoint.request_url(directory))
        }
    }
    pub(in crate::engine::team_backup) fn from_credentials(
        url: &str,
        user: &str,
        password: &str,
        repository: &str,
    ) -> Result<Self> {
        validate_recovery_settings(&serde_json::json!({"enabled":true,"url":url,"userName":user}))?;
        let config = Config {
            enabled: true,
            url: url.into(),
            user: user.into(),
            password: zeroize::Zeroizing::new(password.into()),
        };
        let base = parse_url(url)?;
        let endpoint = parse_url(&base.request_url(&format!("exportdoc-recovery-{repository}")))?;
        Ok(Self { config, endpoint })
    }
    pub(in crate::engine::team_backup) fn read_small(
        &self,
        name: &str,
        limit: usize,
    ) -> Result<Option<Vec<u8>>> {
        let (_, auth) = authorization(&self.config);
        let response = request(
            &self.endpoint,
            "GET",
            &self.object_url(name)?,
            &[("Authorization", auth)],
            &[],
            limit,
            TRANSFER_TIMEOUT,
        )?;
        match response.status {
            200 => Ok(Some(response.body)),
            404 => Ok(None),
            status => Err(failure(status, &response.body)),
        }
    }
    pub(in crate::engine::team_backup) fn create_small(
        &self,
        name: &str,
        bytes: &[u8],
    ) -> Result<bool> {
        let collection = self.call("MKCOL", &self.endpoint.propfind_url(), &[])?;
        if !matches!(collection.status, 201 | 405) {
            return Err(failure(collection.status, &collection.body));
        }
        if name.starts_with("reader-") {
            let collection = self.call("MKCOL", &self.reader_endpoint()?.propfind_url(), &[])?;
            if !matches!(collection.status, 201 | 405) {
                return Err(failure(collection.status, &collection.body));
            }
        }
        if !super::super::repository::names::Namespace::of(name)
            .directory()
            .is_empty()
        {
            let collection =
                self.call("MKCOL", &self.object_endpoint(name)?.propfind_url(), &[])?;
            if !matches!(collection.status, 201 | 405) {
                return Err(failure(collection.status, &collection.body));
            }
        }
        let (_, auth) = authorization(&self.config);
        let response = request(
            &self.endpoint,
            "PUT",
            &self.object_url(name)?,
            &[
                ("Authorization", auth),
                ("If-None-Match", "*".into()),
                ("Content-Type", "application/octet-stream".into()),
            ],
            bytes,
            ERROR_LIMIT,
            TRANSFER_TIMEOUT,
        )?;
        match response.status {
            200 | 201 | 204 => Ok(true),
            412 => Ok(false),
            status => Err(failure(status, &response.body)),
        }
    }
    pub(in crate::engine::team_backup) fn list_names(
        &self,
        prefix: &str,
        after: &str,
        limit: usize,
    ) -> Result<Vec<String>> {
        // Reader admission remains live during every GC check. Keeping these
        // markers in a small collection avoids enumerating all encrypted blobs.
        if prefix.starts_with("reader-") {
            return self.list_collection(&self.reader_endpoint()?, prefix, after, limit);
        }
        let mut names =
            self.list_collection(&self.object_endpoint(prefix)?, prefix, after, limit)?;
        if prefix.is_empty() {
            names.extend(self.list_collection(
                &self.object_endpoint("blob-")?,
                prefix,
                after,
                limit,
            )?);
            names.extend(self.list_collection(
                &self.reader_endpoint()?,
                "reader-",
                after,
                limit,
            )?);
            names.sort();
            names.truncate(limit);
        }
        Ok(names)
    }
    pub(in crate::engine::team_backup) fn object_names(
        &self,
        directory: &str,
    ) -> Result<Vec<String>> {
        let endpoint = if directory.is_empty() {
            parse_url(&self.endpoint.propfind_url())?
        } else {
            parse_url(&self.endpoint.request_url(directory))?
        };
        self.list_collection(&endpoint, "", "", 1_000_001)
    }
    fn list_collection(
        &self,
        endpoint: &Endpoint,
        prefix: &str,
        after: &str,
        limit: usize,
    ) -> Result<Vec<String>> {
        let (_, auth) = authorization(&self.config);
        let request = ureq::http::Request::builder()
            .method("PROPFIND")
            .uri(endpoint.propfind_url())
            .header("Authorization", auth)
            .header("Depth", "1")
            .body(Vec::<u8>::new())
            .map_err(|_| invalid("WebDAV 枚举请求无效。"))?;
        let mut response = agent(TRANSFER_TIMEOUT)
            .run(request)
            .map_err(|_| unavailable("无法枚举 WebDAV 恢复点，请检查连接和目录读取权限。"))?;
        if response.status().as_u16() == 404 {
            return Ok(Vec::new());
        }
        if response.status().as_u16() != 207 {
            return Err(unavailable("WebDAV 目标未提供所需目录枚举能力。"));
        }
        let mut reader = quick_xml::Reader::from_reader(std::io::BufReader::new(
            response.body_mut().as_reader().take(64 * 1024 * 1024),
        ));
        let mut buffer = Vec::new();
        let mut names = std::collections::BTreeSet::new();
        let mut in_href = false;
        let mut count = 0usize;
        let mut completed = false;
        loop {
            crate::operation::check()?;
            match reader
                .read_event_into(&mut buffer)
                .map_err(|_| invalid("WebDAV 恢复仓库枚举响应无效或超过预算。"))?
            {
                quick_xml::events::Event::Start(tag) if tag.local_name().as_ref() == "href" => {
                    in_href = true
                }
                quick_xml::events::Event::End(tag) => {
                    if tag.local_name().as_ref() == "href" {
                        in_href = false;
                    }
                    if tag.local_name().as_ref() == "multistatus" {
                        completed = true;
                    }
                }
                quick_xml::events::Event::Text(text) if in_href => {
                    let href = text.xml10_content();
                    let href = quick_xml::escape::unescape(&href)
                        .map_err(|_| invalid("WebDAV 对象名称编码无效。"))?;
                    if let Some(name) = href.rsplit('/').next().filter(|n| !n.is_empty()) {
                        if !matches!(name, "readers" | "objects")
                            && name.starts_with(prefix)
                            && name > after
                            && crate::paths::valid_file_name(name)
                        {
                            names.insert(name.to_owned());
                            if names.len() > limit {
                                names.pop_last();
                            }
                        }
                    }
                    count += 1;
                    if count > 1_000_000 {
                        return Err(invalid("WebDAV 单仓库枚举超过预算，请拆分仓库或使用 S3。"));
                    }
                }
                quick_xml::events::Event::Eof => break,
                _ => {}
            }
            if buffer.len() > 8192 {
                return Err(invalid("WebDAV 枚举条目过大。"));
            }
            buffer.clear();
        }
        if !completed {
            return Err(invalid("WebDAV 枚举响应未完整结束。"));
        }
        Ok(names.into_iter().collect())
    }
    pub(in crate::engine::team_backup) fn read(
        store: &Store,
        protector: &crate::secrets::Protector,
        repository: &str,
    ) -> Result<Self> {
        let config = Config::read_parts(store, protector)?;
        require_enabled(&config)?;
        let base = parse_url(&config.url)?;
        let endpoint = parse_url(&base.request_url(&format!("exportdoc-recovery-{repository}")))?;
        Ok(Self { config, endpoint })
    }
    fn call(&self, method: &str, url: &str, extra: &[(&str, String)]) -> Result<Response> {
        let (_, auth) = authorization(&self.config);
        let mut headers = vec![("Authorization", auth)];
        headers.extend_from_slice(extra);
        request(
            &self.endpoint,
            method,
            url,
            &headers,
            &[],
            ERROR_LIMIT,
            REQUEST_TIMEOUT,
        )
    }
    fn delete_url(&self, url: &str) -> Result<()> {
        let response = self.call("DELETE", url, &[])?;
        if !matches!(response.status, 200 | 202 | 204 | 404) {
            return Err(failure(response.status, &response.body));
        }
        Ok(())
    }
    pub(in crate::engine::team_backup) fn delete(&self, name: &str) -> Result<()> {
        self.delete_url(&self.object_url(name)?)
    }
}
