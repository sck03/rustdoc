//! Complete encrypted recovery replicas, isolated from the historical SQLite-only cloud API.
use super::*;
use crate::engine::backup_io;

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
    pub(in crate::engine::team_backup) fn identity(&self) -> String {
        format!(
            "WebDav:{}:{}",
            self.endpoint.propfind_url(),
            self.config.user
        )
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
        self.delete_url(&remote_path(&self.endpoint, name)?)
    }
    pub(in crate::engine::team_backup) fn upload(
        &self,
        path: &Path,
        name: &str,
        size: u64,
        digest: &str,
        verify: bool,
    ) -> Result<()> {
        let collection = self.endpoint.propfind_url();
        let response = self.call("MKCOL", &collection, &[])?;
        if !matches!(response.status, 201 | 405) {
            return Err(failure(response.status, &response.body));
        }
        let remote = remote_path(&self.endpoint, name)?;
        let pending = remote_path(
            &self.endpoint,
            &format!(
                "{name}.{}.partial",
                crate::paths::nonce().map_err(unavailable)?
            ),
        )?;
        let result = (|| {
            let (_, auth) = authorization(&self.config);
            let mut source = fs::File::open(path)?;
            if source.metadata()?.len() != size {
                return Err(unavailable("本地灾备包大小已改变。"));
            }
            let mut response = agent(TRANSFER_TIMEOUT)
                .put(&pending)
                .header("Authorization", auth)
                .header("Content-Type", "application/octet-stream")
                .header("Content-Length", size.to_string())
                .header("If-None-Match", "*")
                .send(ureq::SendBody::from_reader(&mut source))
                .map_err(|_| unavailable("WebDAV 灾备上传失败或超时。"))?;
            let status = response.status().as_u16();
            let body = read_limited(
                &mut response.body_mut().as_reader(),
                ERROR_LIMIT,
                "WebDAV 响应过大。",
            )?;
            if !(200..300).contains(&status) {
                return Err(failure(status, &body));
            }
            if verify {
                self.verify(&pending, size, digest)?;
            }
            crate::operation::check()?;
            let response = self.call(
                "MOVE",
                &pending,
                &[("Destination", remote), ("Overwrite", "F".into())],
            )?;
            if !matches!(response.status, 201 | 204) {
                return Err(failure(response.status, &response.body));
            }
            Ok(())
        })();
        if result.is_err() {
            // A canceled job must still remove only its own incomplete object.
            let cleanup = crate::operation::OperationScope::new(REQUEST_TIMEOUT)
                .run(|| self.delete_url(&pending));
            if cleanup.is_err() {
                return Err(unavailable(
                    "WebDAV 上传未完成，临时对象清理失败；本地备份已保留，请检查存储连接。",
                ));
            }
        }
        result
    }
    fn verify(&self, url: &str, size: u64, digest: &str) -> Result<()> {
        let (_, auth) = authorization(&self.config);
        let mut response = agent(TRANSFER_TIMEOUT)
            .get(url)
            .header("Authorization", auth)
            .call()
            .map_err(|_| unavailable("WebDAV 副本回读失败或超时。"))?;
        if response.status().as_u16() != 200 {
            return Err(unavailable("WebDAV 副本无法完整回读。"));
        }
        let actual = backup_io::copy_and_hash(
            &mut response.body_mut().as_reader(),
            &mut std::io::sink(),
            size,
        )?;
        if actual != (size, digest.into()) {
            return Err(unavailable("WebDAV 副本大小或 SHA-256 校验失败。"));
        }
        Ok(())
    }
}
