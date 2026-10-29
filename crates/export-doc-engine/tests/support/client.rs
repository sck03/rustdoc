//! In-process client shared by native integration tests.
use export_doc_engine::{
    api::ApiError,
    engine::{NativeService, tasks::retention::Retention},
    generated_api::*,
    paths::RuntimePaths,
};
use serde::de::DeserializeOwned;
use serde_json::{Value, json};
use std::sync::Arc;
#[path = "approval_fixture.rs"]
mod workflow;

/// Typed in-process client for native service integration workflows.
#[derive(Clone)]
pub struct ApiClient {
    native: Arc<NativeService>,
    access_token: String,
}

impl ApiClient {
    pub fn configure_approval(&self, id: i64) {
        workflow::configure(&self.native, &self.access_token, id);
    }
    pub fn native(paths: RuntimePaths) -> Result<Self, ApiError> {
        Self::native_edition(paths, Default::default(), "Full")
    }
    pub fn native_edition(
        paths: RuntimePaths,
        retention: Retention,
        edition: &str,
    ) -> Result<Self, ApiError> {
        Ok(Self {
            native: NativeService::open_desktop(paths, retention, edition)?,
            access_token: String::new(),
        })
    }
    pub fn supports(&self, operation: Operation) -> bool {
        NativeService::supports(operation)
    }
    pub fn preview_report_pdf(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        body: &Value,
    ) -> Result<Vec<u8>, ApiError> {
        self.native
            .preview_report_pdf(operation, parameters, body, &self.access_token)
    }
    pub fn preview_document_package_pdf(
        &self,
        invoice_id: i64,
        body: &Value,
    ) -> Result<Vec<u8>, ApiError> {
        let actor = self.native.session_actor(&self.access_token)?;
        self.native
            .document_package_preview_pdf(&actor, invoice_id, body)
    }
    pub fn upload(
        &self,
        operation: Operation,
        path: &[(&str, String)],
        metadata: Value,
        file_name: &str,
        content: &[u8],
    ) -> Result<Value, ApiError> {
        self.native.upload(
            operation,
            path,
            metadata,
            file_name,
            content,
            &self.access_token,
        )
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
        let bytes = self
            .native
            .dispatch(operation, path, query, body, &self.access_token)?;
        if bytes.len() as u64 > limit {
            return Err(ApiError::local("响应超过本次操作的容量上限。"));
        }
        Ok(bytes)
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
}
