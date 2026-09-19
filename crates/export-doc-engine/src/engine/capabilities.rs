use super::*;

impl NativeService {
    /// Local filesystem/process adapters are absent from the network host.
    /// The contract's desktop-token flag is a mode-specific authentication
    /// requirement and also applies to ordinary reads in desktop mode.
    pub fn requires_local_transport(operation: Operation) -> bool {
        #[cfg(feature = "invoice-transfer")]
        if invoice_transfer::LOCAL.contains(&operation) {
            return true;
        }
        #[cfg(feature = "single-window")]
        if single_window::requires_local(operation) {
            return true;
        }
        if team_backup::LOCAL.contains(&operation) {
            return true;
        }
        #[cfg(feature = "excel")]
        if excel::LOCAL_OPERATIONS.contains(&operation) || hs_files::LOCAL.contains(&operation) {
            return true;
        }
        reports::LOCAL_OPERATIONS.contains(&operation)
            || report_template_files::LOCAL.contains(&operation)
            || operation == SAVE_SUPPORT_PACKAGE_TO_RUNTIME
            || operation == START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB
            || operation == SAVE_CONTAINER_PACKING_PDF_TO_PATH
            || matches!(
                operation,
                RUN_SHUTDOWN_MAINTENANCE
                    | START_PDF_MERGE_SAVE_TO_PATH_JOB
                    | SAVE_QUERIED_INVOICES_TO_PATH
                    | SAVE_BUSINESS_ATTACHMENT_TO_PATH
                    | IMPORT_LETTER_OF_CREDIT_DOCUMENT
                    | RECOGNIZE_OCR_IMAGE
                    | SAVE_AUDIT_LOGS_TO_PATH
            )
    }
    pub fn supports(operation: Operation) -> bool {
        #[cfg(feature = "invoice-transfer")]
        if invoice_transfer::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "single-window")]
        if single_window::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "ai")]
        if operation == REVIEW_LETTER_OF_CREDIT_COMPLIANCE {
            return true;
        }
        if letter_of_credit::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(all(feature = "single-window", feature = "excel"))]
        if operation == PREVIEW_SINGLE_WINDOW_REFERENCE_CATALOG_EXCEL_IMPORT {
            return true;
        }
        if invoice_maintenance::OPERATIONS.contains(&operation)
            || product_options::OPERATIONS.contains(&operation)
            || [CREATE_JOB_DOWNLOAD_TICKET, DOWNLOAD_JOB_RESULT_WITH_TICKET].contains(&operation)
        {
            return true;
        }
        #[cfg(feature = "ocr")]
        if ocr::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "mail")]
        if email::OPERATIONS.contains(&operation)
            || email_templates::OPERATIONS.contains(&operation)
        {
            return true;
        }
        #[cfg(not(feature = "mail"))]
        if [
            LIST_EMAIL_TEMPLATES,
            CREATE_EMAIL_TEMPLATE,
            SAVE_EMAIL_TEMPLATE_DRAFT,
        ]
        .contains(&operation)
        {
            return false;
        }
        #[cfg(feature = "hs-remote")]
        if hs_remote::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "exchange-rates")]
        if exchange::OPERATIONS.contains(&operation) {
            return true;
        }
        if audit::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "excel")]
        if invoice_query::EXPORTS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "excel")]
        if audit::EXPORTS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "excel")]
        if excel::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "excel")]
        if party_files::OPERATIONS.contains(&operation) {
            return true;
        }
        #[cfg(feature = "excel")]
        if hs_files::OPERATIONS.contains(&operation)
            || [EXPORT_HS_CODE_KNOWLEDGE, IMPORT_HS_CODE_KNOWLEDGE].contains(&operation)
        {
            return true;
        }
        catalog::find_resource_operation(operation)
            || pdf_merge::OPERATIONS.contains(&operation)
            || packing::OPERATIONS.contains(&operation)
            || hs::OPERATIONS.contains(&operation)
            || SPECIAL_OPERATIONS.contains(&operation)
            || workflows::ACTIONS.contains(&operation)
            || OFFICE_ACTIONS.contains(&operation)
            || attachments::OPERATIONS.contains(&operation)
            || attachment_categories::OPERATIONS.contains(&operation)
            || personnel::OPERATIONS.contains(&operation)
            || report_assets::OPERATIONS.contains(&operation)
            || document_packages::OPERATIONS.contains(&operation)
            || reports::OPERATIONS.contains(&operation)
            || report_templates::OPERATIONS.contains(&operation)
            || report_template_files::OPERATIONS.contains(&operation)
            || licensing::OPERATIONS.contains(&operation)
            || team_backup::OPERATIONS.contains(&operation)
            || sales::OPERATIONS.contains(&operation)
            || crm::OPERATIONS.contains(&operation)
            || related_records::OPERATIONS.contains(&operation)
            || [
                LIST_DATABASE_BACKUPS,
                CREATE_DATABASE_BACKUP,
                RESTORE_DATABASE_BACKUP,
                CLEANUP_DATABASE_BACKUPS,
                ANALYZE_INVOICE_PROFIT,
            ]
            .contains(&operation)
    }
    pub fn upload_pdf_merge(&self, files: Vec<tasks::FileOutput>, token: &str) -> Result<Value> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        auth::authorize_operation(&actor, UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB, &[])?;
        pdf_merge::upload(self, &actor, files)
    }
    pub fn upload(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        metadata: Value,
        file_name: &str,
        content: &[u8],
        token: &str,
    ) -> Result<Value> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        auth::authorize_operation(&actor, operation, &[])?;
        #[cfg(feature = "invoice-transfer")]
        if invoice_transfer::UPLOADS.contains(&operation) {
            return invoice_transfer::upload(
                self, &actor, operation, &metadata, file_name, content,
            );
        }
        if team_backup::UPLOADS.contains(&operation) {
            return team_backup::upload(
                self, &actor, operation, parameters, &metadata, file_name, content,
            );
        }
        #[cfg(feature = "single-window")]
        if single_window::accepts_upload(operation) {
            return single_window::upload(self, &actor, operation, &metadata, file_name, content);
        }
        if operation == UPLOAD_LETTER_OF_CREDIT_DOCUMENT {
            return letter_of_credit::import(self, file_name, content);
        }
        #[cfg(feature = "ocr")]
        if operation == UPLOAD_OCR_IMAGE {
            return ocr::recognize(self, content, file_name);
        }
        #[cfg(feature = "excel")]
        if operation == IMPORT_HS_CODE_KNOWLEDGE {
            return hs_package::import(self, &actor, file_name, content);
        }
        #[cfg(feature = "excel")]
        if hs_files::UPLOADS.contains(&operation) {
            return hs_files::upload(self, &actor, operation, &metadata, file_name, content);
        }
        if report_template_files::UPLOADS.contains(&operation) {
            return report_template_files::upload(
                self, &actor, operation, parameters, &metadata, file_name, content,
            );
        }
        if report_assets::UPLOADS.contains(&operation) {
            return report_assets::upload(
                &self.store,
                &actor,
                operation,
                parameters,
                file_name,
                content,
            );
        }
        #[cfg(feature = "excel")]
        if excel::UPLOADS.contains(&operation) {
            return excel::upload(self, &actor, operation, file_name, content);
        }
        #[cfg(feature = "excel")]
        if party_files::UPLOADS.contains(&operation) {
            return party_files::upload(self, &actor, operation, file_name, content);
        }
        match operation {
            UPLOAD_PERSONNEL_IMAGE => {
                personnel::upload(&self.store, &actor, parameters, metadata, content)
            }
            UPLOAD_BUSINESS_ATTACHMENT => attachments::upload(
                &self.store,
                &actor,
                records::id(parameters)?,
                metadata,
                file_name,
                content,
            ),
            _ => Err(unsupported("此文件上传尚未完成原生迁移。")),
        }
    }
}
