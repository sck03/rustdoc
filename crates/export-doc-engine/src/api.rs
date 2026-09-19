use crate::generated_api::*;
use serde::de::DeserializeOwned;
use serde_json::{Value, json};
use std::{fmt, sync::Arc};
#[cfg(feature = "reference-backend")]
use std::{io::Read, time::Duration};

#[derive(Debug)]
pub struct ApiError {
    pub status: Option<u16>,
    pub message: String,
}

impl ApiError {
    pub fn local(message: impl Into<String>) -> Self {
        Self {
            status: None,
            message: message.into(),
        }
    }
}

impl fmt::Display for ApiError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        if let Some(code) = self.status {
            write!(formatter, "HTTP {code}：{}", self.message)?;
            if code == 409 {
                write!(formatter, " 草稿已保留，请重新读取并核对版本。")?;
            }
            if code == 401 {
                write!(formatter, " 请重新登录。")?;
            }
            Ok(())
        } else {
            formatter.write_str(&self.message)
        }
    }
}
impl std::error::Error for ApiError {}

#[derive(Clone)]
pub struct ApiClient {
    native: Option<Arc<crate::engine::NativeService>>,
    #[cfg(feature = "reference-backend")]
    agent: ureq::Agent,
    base_url: String,
    #[cfg(feature = "reference-backend")]
    desktop_token: String,
    access_token: String,
}

impl ApiClient {
    pub fn native(paths: crate::paths::RuntimePaths) -> Result<Self, ApiError> {
        Self::native_with_retention(paths, Default::default())
    }
    pub fn native_with_retention(
        paths: crate::paths::RuntimePaths,
        retention: crate::engine::tasks::retention::Retention,
    ) -> Result<Self, ApiError> {
        Ok(Self {
            native: Some(crate::engine::NativeService::open_with_retention(
                paths, retention,
            )?),
            base_url: "native://local".into(),
            access_token: String::new(),
            #[cfg(feature = "reference-backend")]
            agent: ureq::Agent::new_with_defaults(),
            #[cfg(feature = "reference-backend")]
            desktop_token: String::new(),
        })
    }
    pub fn supports(&self, operation: Operation) -> bool {
        self.native.is_none() || crate::engine::NativeService::supports(operation)
    }
    pub fn is_native(&self) -> bool {
        self.native.is_some()
    }
    pub fn preview_report_pdf(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        body: &Value,
    ) -> Result<Vec<u8>, ApiError> {
        self.native
            .as_ref()
            .ok_or_else(|| ApiError::local("原生预览需要本地应用服务。"))?
            .preview_report_pdf(operation, parameters, body, &self.access_token)
    }
    pub fn preview_document_package_pdf(
        &self,
        invoice_id: i64,
        body: &Value,
    ) -> Result<Vec<u8>, ApiError> {
        let native = self
            .native
            .as_ref()
            .ok_or_else(|| ApiError::local("组合单据预览需要本地应用服务。"))?;
        let actor = native.session_actor(&self.access_token)?;
        native.document_package_preview_pdf(&actor, invoice_id, body)
    }
    pub fn upload(
        &self,
        operation: Operation,
        path: &[(&str, String)],
        metadata: Value,
        file_name: &str,
        content: &[u8],
    ) -> Result<Value, ApiError> {
        let native = self
            .native
            .as_ref()
            .ok_or_else(|| ApiError::local("该上传入口仅用于原生工作区。"))?;
        native.upload(
            operation,
            path,
            metadata,
            file_name,
            content,
            &self.access_token,
        )
    }
    #[cfg(feature = "reference-backend")]
    pub fn new(base_url: &str, desktop_token: String) -> Result<Self, ApiError> {
        validate_loopback_url(base_url)?;
        let config = ureq::Agent::config_builder()
            .http_status_as_error(false)
            .max_redirects(0)
            .proxy(None)
            .timeout_global(Some(Duration::from_secs(25)))
            .build();
        Ok(Self {
            native: None,
            agent: config.into(),
            base_url: base_url.trim_end_matches('/').into(),
            desktop_token,
            access_token: String::new(),
        })
    }

    pub fn origin(&self) -> &str {
        &self.base_url
    }

    pub fn login(
        &self,
        username: String,
        password: String,
    ) -> Result<(Self, ApiUserDto), ApiError> {
        let response: LoginResponse = self.json(
            LOGIN,
            &[],
            &[],
            Some(json!(ApiLoginRequest {
                username,
                password,
                ..Default::default()
            })),
        )?;
        if response.access_token.is_empty() || response.user.id <= 0 {
            return Err(ApiError::local("登录响应缺少有效会话。"));
        }
        let mut authenticated = self.clone();
        authenticated.access_token = response.access_token;
        Ok((authenticated, response.user))
    }

    pub fn json<T: DeserializeOwned>(
        &self,
        operation: Operation,
        path: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
    ) -> Result<T, ApiError> {
        let data = self.bytes(operation, path, query, body, 16 * 1024 * 1024)?;
        serde_json::from_slice(&data)
            .map_err(|error| ApiError::local(format!("后端响应不符合已生成的接口契约：{error}")))
    }

    pub fn bytes(
        &self,
        operation: Operation,
        path: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
        limit: u64,
    ) -> Result<Vec<u8>, ApiError> {
        if let Some(native) = &self.native {
            let bytes = native.dispatch(operation, path, query, body, &self.access_token)?;
            if bytes.len() as u64 > limit {
                return Err(ApiError::local("响应超过本次操作的容量上限。"));
            }
            return Ok(bytes);
        }
        #[cfg(not(feature = "reference-backend"))]
        return Err(ApiError::local("没有可用的原生服务。"));
        #[cfg(feature = "reference-backend")]
        {
            let target = route(operation.path, path, query)?;
            let mut builder = ureq::http::Request::builder()
                .method(operation.method)
                .uri(format!("{}{target}", self.base_url))
                .header("X-ExportDocManager-Desktop-Token", &self.desktop_token)
                .header("Accept", "application/json, application/pdf");
            if !self.access_token.is_empty() {
                builder = builder.header("Authorization", format!("Bearer {}", self.access_token));
            }
            let payload = match body {
                Some(value) => {
                    builder = builder.header("Content-Type", "application/json");
                    serde_json::to_vec(&value)
                        .map_err(|error| ApiError::local(error.to_string()))?
                }
                None => Vec::new(),
            };
            let request = builder
                .body(payload)
                .map_err(|error| ApiError::local(error.to_string()))?;
            let mut response = self.agent.run(request).map_err(|error| ApiError::local(format!("无法完成后端请求：{error}。草稿已保留；保存超时后请先重新读取，核实服务端状态。")))?;
            let status = response.status().as_u16();
            let cap = if (200..300).contains(&status) {
                limit
            } else {
                64 * 1024
            };
            let mut bytes = Vec::new();
            response
                .body_mut()
                .as_reader()
                .take(cap + 1)
                .read_to_end(&mut bytes)
                .map_err(|error| ApiError::local(format!("读取后端响应失败：{error}")))?;
            if bytes.len() as u64 > cap {
                return Err(ApiError::local("后端响应超过本次操作的容量上限。"));
            }
            if !(200..300).contains(&status) {
                let message = serde_json::from_slice::<Value>(&bytes)
                    .ok()
                    .and_then(|value| {
                        value
                            .get("message")
                            .or_else(|| value.get("title"))
                            .and_then(Value::as_str)
                            .map(str::to_owned)
                    })
                    .unwrap_or_else(|| "后端拒绝请求或依赖暂不可用。".into());
                return Err(ApiError {
                    status: Some(status),
                    message: format!(
                        "{} {}：{}",
                        operation.method,
                        operation.path,
                        message.chars().take(2000).collect::<String>()
                    ),
                });
            }
            Ok(bytes)
        }
    }

    pub fn save_invoice(
        &self,
        invoice: &ApiInvoiceDetailDto,
    ) -> Result<ApiInvoiceDetailDto, ApiError> {
        if invoice.id > 0 && invoice.row_version.is_empty() {
            return Err(ApiError::local("现有发票缺少版本号，已阻止覆盖。"));
        }
        let operation = if invoice.id == 0 {
            CREATE_INVOICE
        } else {
            UPDATE_INVOICE
        };
        let path = if invoice.id == 0 {
            vec![]
        } else {
            vec![("id", invoice.id.to_string())]
        };
        let saved: ApiInvoiceSaveResponse =
            self.json(operation, &path, &[], Some(json!(invoice)))?;
        if !saved.success || saved.invoice.id <= 0 || saved.invoice.row_version.is_empty() {
            return Err(ApiError::local(
                "保存响应缺少发票或版本号，请重新读取核对。",
            ));
        }
        Ok(saved.invoice)
    }

    pub fn get_invoice(&self, id: i64) -> Result<ApiInvoiceDetailDto, ApiError> {
        self.json(GET_INVOICE, &[("id", id.to_string())], &[], None)
    }

    pub fn save_template(
        &self,
        name: &str,
        content: &str,
        previous: Option<&ApiUserReportTemplateDto>,
    ) -> Result<ApiUserReportTemplateDto, ApiError> {
        if let Some(template) = previous {
            self.json(
                SAVE_USER_REPORT_TEMPLATE_DRAFT,
                &[("id", template.id.to_string())],
                &[],
                Some(json!(ApiUserReportTemplateDraftRequest {
                    name: Some(name.into()),
                    report_type: Some("ExportDocument".into()),
                    content_html: Some(content.into()),
                    expected_version: Some(template.version_number),
                    ..Default::default()
                })),
            )
        } else {
            self.json(
                CREATE_USER_REPORT_TEMPLATE,
                &[],
                &[],
                Some(json!(ApiUserReportTemplateCreateRequest {
                    name: Some(name.into()),
                    report_type: Some("ExportDocument".into()),
                    content_html: Some(content.into()),
                    ..Default::default()
                })),
            )
        }
    }

    pub fn publish_template(
        &self,
        template: &ApiUserReportTemplateDto,
    ) -> Result<ApiUserReportTemplateDto, ApiError> {
        self.json(
            PUBLISH_USER_REPORT_TEMPLATE,
            &[("id", template.id.to_string())],
            &[],
            Some(json!({"expectedVersion":template.version_number})),
        )
    }

    pub fn shutdown_maintenance(&self) -> Result<(), ApiError> {
        let result: Value = self.json(RUN_SHUTDOWN_MAINTENANCE, &[], &[], Some(json!({})))?;
        if result.get("success").and_then(Value::as_bool) == Some(true) {
            Ok(())
        } else {
            Err(ApiError::local(
                "后端关闭维护未完成，请查看隔离目录中的日志。",
            ))
        }
    }
}

#[cfg(feature = "reference-backend")]
pub fn validate_loopback_url(value: &str) -> Result<(), ApiError> {
    let uri: ureq::http::Uri = value
        .parse()
        .map_err(|_| ApiError::local("无效的后端地址。"))?;
    if uri.scheme_str() != Some("http")
        || !matches!(uri.host(), Some("127.0.0.1" | "[::1]"))
        || uri.port_u16().unwrap_or(0) == 0
        || uri.path() != "/"
        || uri.query().is_some()
        || uri
            .authority()
            .is_some_and(|authority| authority.as_str().contains('@'))
    {
        return Err(ApiError::local("验证版只连接自己启动的本机回环 API。"));
    }
    Ok(())
}

#[cfg(any(test, feature = "reference-backend"))]
fn encode(value: &str) -> String {
    value
        .bytes()
        .map(|byte| {
            if byte.is_ascii_alphanumeric() || b"-_.~".contains(&byte) {
                (byte as char).to_string()
            } else {
                format!("%{byte:02X}")
            }
        })
        .collect()
}

#[cfg(any(test, feature = "reference-backend"))]
fn route(
    template: &str,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
) -> Result<String, ApiError> {
    let mut path = template.to_owned();
    for (name, value) in parameters {
        let placeholder = format!("{{{name}}}");
        if !path.contains(&placeholder) {
            return Err(ApiError::local("请求包含未知路径参数。"));
        }
        path = path.replace(&placeholder, &encode(value));
    }
    if path.contains(['{', '}']) {
        return Err(ApiError::local("请求缺少路径参数。"));
    }
    if !query.is_empty() {
        path.push('?');
        path.push_str(
            &query
                .iter()
                .map(|(key, value)| format!("{}={}", encode(key), encode(value)))
                .collect::<Vec<_>>()
                .join("&"),
        );
    }
    Ok(path)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    #[cfg(feature = "reference-backend")]
    fn only_owned_loopback_origin_is_accepted() {
        for value in [
            "https://127.0.0.1:8",
            "http://evil.test:8",
            "http://localhost:8",
            "http://127.0.0.1:0",
            "http://127.0.0.1:8/api",
            "http://user@127.0.0.1:8",
        ] {
            assert!(validate_loopback_url(value).is_err(), "{value}");
        }
        assert!(validate_loopback_url("http://127.0.0.1:5188").is_ok());
    }
    #[test]
    fn paths_and_queries_are_encoded_and_required() {
        assert_eq!(
            route(
                "/x/{id}",
                &[("id", "a/b".into())],
                &[("q", "A&B 空格".into())]
            )
            .unwrap(),
            "/x/a%2Fb?q=A%26B%20%E7%A9%BA%E6%A0%BC"
        );
        assert!(route("/x/{id}", &[], &[]).is_err());
    }
}
