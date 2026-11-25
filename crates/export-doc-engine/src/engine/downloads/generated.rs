//! Generated binary responses share the existing instance download budget.
use super::*;
use crate::generated_api::*;
use serde_json::Value;

fn description(operation: Operation) -> Option<(&'static str, &'static str)> {
    Some(match operation {
        PREVIEW_OCR_IMAGE => ("OCR预览.png", "image/png"),
        DOWNLOAD_CONTAINER_PACKING_PDF => ("装柜现场作业单.pdf", "application/pdf"),
        DOWNLOAD_INVOICE_TRANSFER_PACKAGE => ("单据.edpkg", "application/zip"),
        DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE | DOWNLOAD_AGENT_CONSIGNMENT_SUBMIT_PACKAGE => {
            ("单一窗口提交包.zip", "application/zip")
        }
        DOWNLOAD_AUDIT_LOGS | EXPORT_CRM_CUSTOMERS | EXPORT_SUPPLIERS => (
            match operation {
                EXPORT_CRM_CUSTOMERS => "CRM客户.xlsx",
                EXPORT_SUPPLIERS => "供应商.xlsx",
                _ => "AuditLogs.xlsx",
            },
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ),
        _ => return None,
    })
}

impl NativeService {
    #[cfg(feature = "ocr")]
    pub fn prepare_uploaded_ocr_preview(
        &self,
        bytes: &[u8],
        token: &str,
    ) -> Result<backup_io::Download> {
        self.with_download_access(PREVIEW_UPLOADED_OCR_IMAGE, token, |_| Ok(()))?;
        let mut reservation = self.jobs.reserve_download(64 * 1024 * 1024, true)?;
        let output = super::super::ocr::preview_upload(self, bytes)?;
        reservation.shrink_to(output.content.capacity());
        Ok(backup_io::Download::Memory {
            output,
            reservation,
        })
    }

    pub fn is_generated_download(operation: Operation) -> bool {
        description(operation).is_some()
    }

    pub fn prepare_generated_download(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body: Option<Value>,
        token: &str,
    ) -> Result<backup_io::Download> {
        let (name, media_type) =
            description(operation).ok_or_else(|| unsupported("此操作不是生成文件下载。"))?;
        #[cfg(feature = "excel")]
        if operation == DOWNLOAD_AUDIT_LOGS {
            return self.with_download_access(operation, token, |actor| {
                super::super::audit::download(self, actor, body.as_ref().unwrap_or(&Value::Null))
            });
        }
        self.with_download_access(operation, token, |_| Ok(()))?;
        // This includes encoding workspace as well as the outstanding response.
        let mut reservation = self.jobs.reserve_download(128 * 1024 * 1024, true)?;
        let output = match operation {
            #[cfg(feature = "ocr")]
            PREVIEW_OCR_IMAGE => self.preview_ocr_image(query, token)?,
            _ => super::super::tasks::FileOutput {
                file_name: name.into(),
                media_type: media_type.into(),
                content: self.dispatch(operation, parameters, query, body, token)?,
            },
        };
        if output.content.capacity() > 128 * 1024 * 1024 {
            return Err(super::super::error::unavailable("下载结果超过预留容量。"));
        }
        reservation.shrink_to(output.content.capacity());
        Ok(backup_io::Download::Memory {
            output,
            reservation,
        })
    }
}
